// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;

namespace Daml.Codegen.Intermediate.Model;

/// <summary>
/// The applied-type normalization the two reader boundaries share: applications of the
/// five folding builtins — <see cref="DamlPrimitive.List"/>, <see cref="DamlPrimitive.Optional"/>,
/// <see cref="DamlPrimitive.TextMap"/>, <see cref="DamlPrimitive.GenMap"/>,
/// <see cref="DamlPrimitive.ContractId"/>, the typed-node set recorded in
/// <see cref="DamlPrimitiveCatalog"/> — fold into their first-class typed nodes, with the
/// catalog's arity table as the oracle. A boundary converts a complete type expression to
/// its generic application shapes first (flattening curried applications), then folds the
/// finished tree once: folding at the complete-type boundary is what lets a curried chain
/// accumulate its arguments before the arity is checked, so <c>TypeApp(GenMap, [K])</c> at a
/// complete type position spells a one-argument application of a two-argument builtin and
/// is rejected as malformed, while the curried chain that completes the same GenMap
/// application folds into its node.
/// <para>
/// Eligibility is the five-node set, not "arity &gt; 0": <see cref="DamlPrimitive.Numeric"/>
/// has catalog arity 1 but its argument is the <c>Nat</c> pun, so applied Numeric stays a
/// generic <see cref="DamlTypeApp"/>, as do applications of every non-folding builtin and
/// of user-defined type constructors. Already-folded nodes pass through unchanged — a
/// boundary that folds each interned-pool entry at construction hands later entries
/// pre-folded subtrees, and the fold is idempotent over them.
/// </para>
/// <para>
/// A partially applied builtin is a legal subexpression on the wire: the Daml-LF interner
/// deduplicates the <c>GenMap k</c> subterm of an application into its own interned-pool
/// entry, completed by the referencing entry — the shipped stdlib carries one. So
/// <see cref="FoldComplete"/> enforces the arity only at the root of the type it is given,
/// and every nested application folds through <see cref="FoldSubexpression"/>: a folding
/// builtin whose argument count matches its catalog arity folds into its node, and any
/// other application keeps its generic shape for the referencing context to complete (or,
/// at a genuine data position, for the emitter to reject as it always has).
/// </para>
/// </summary>
public static class AppliedTypeFolding
{
    private static readonly FrozenDictionary<DamlPrimitive, DamlPrimitiveCatalogRow> FoldingRows =
        DamlPrimitiveCatalog.Rows
            .Where(row => row.Primitive is DamlPrimitive.List or DamlPrimitive.Optional
                or DamlPrimitive.TextMap or DamlPrimitive.GenMap or DamlPrimitive.ContractId)
            .ToFrozenDictionary(row => row.Primitive!.Value);

    /// <summary>
    /// Folds a complete type: an application of a folding builtin at the root whose argument
    /// count disagrees with the catalog arity throws <see cref="InvalidDataException"/> (the
    /// input is malformed), the root folds into its typed node when the arity matches, and
    /// nested applications fold through <see cref="FoldSubexpression"/>. Everything else —
    /// primitives, references, type variables, applications of non-folding builtins and of
    /// type constructors, and already-folded nodes — passes through structurally unchanged.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// A folding builtin is applied, at the root, to a number of arguments other than its
    /// catalog arity.
    /// </exception>
    public static DamlType FoldComplete(DamlType type) => type switch
    {
        DamlTypeApp application => FoldApplication(application, enforceArity: true),
        not null => type,
        null => throw new ArgumentNullException(nameof(type)),
    };

    /// <summary>
    /// Folds a subexpression — an interned-pool entry, or an application nested inside
    /// another: applications of folding builtins whose arity matches fold into their nodes,
    /// and any other application keeps its generic shape, because a subexpression can be a
    /// legal partial application the referencing context completes.
    /// </summary>
    public static DamlType FoldSubexpression(DamlType type) => type switch
    {
        DamlTypeApp application => FoldApplication(application, enforceArity: false),
        not null => type,
        null => throw new ArgumentNullException(nameof(type)),
    };

    private static DamlType FoldApplication(DamlTypeApp application, bool enforceArity)
    {
        if (application.Base is not DamlPrimitiveType { Primitive: var primitive }
            || !FoldingRows.TryGetValue(primitive, out var row))
        {
            return new DamlTypeApp(
                application.Base,
                [.. application.Arguments.Select(FoldSubexpression)]);
        }

        if (application.Arguments.Count != row.Arity)
        {
            if (enforceArity)
            {
                throw new InvalidDataException(
                    $"Daml type application applies '{primitive}' to {application.Arguments.Count} argument(s), " +
                    $"but the catalog arity is {row.Arity} — the applied builtin names no Daml type and the input is malformed.");
            }
            return new DamlTypeApp(
                application.Base,
                [.. application.Arguments.Select(FoldSubexpression)]);
        }

        return primitive switch
        {
            DamlPrimitive.List => new DamlListType(FoldSubexpression(application.Arguments[0])),
            DamlPrimitive.Optional => new DamlOptionalType(FoldSubexpression(application.Arguments[0])),
            DamlPrimitive.TextMap => new DamlTextMapType(FoldSubexpression(application.Arguments[0])),
            DamlPrimitive.ContractId => new DamlContractIdType(FoldSubexpression(application.Arguments[0])),
            DamlPrimitive.GenMap => new DamlGenMapType(
                FoldSubexpression(application.Arguments[0]),
                FoldSubexpression(application.Arguments[1])),
            _ => throw new NotSupportedException(
                $"DamlPrimitive '{primitive}' is recorded in DamlPrimitiveCatalog as one of the five folding " +
                "builtins but has no typed node. Add the node mapping in AppliedTypeFolding.FoldApplication " +
                "alongside the model's typed nodes."),
        };
    }
}
