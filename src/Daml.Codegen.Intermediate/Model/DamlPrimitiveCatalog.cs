// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;

namespace Daml.Codegen.Intermediate.Model;

/// <summary>
/// What the codegen pipeline is allowed to do with a proto
/// <see cref="BuiltinType"/> value. Every proto value's disposition is recorded in
/// <see cref="DamlPrimitiveCatalog"/>.
/// </summary>
public enum DamlPrimitiveDisposition
{
    /// <summary>
    /// The builtin is a serializable Daml value type with full end-to-end backing: a
    /// <see cref="DamlPrimitive"/> identity, a <c>Daml.Runtime</c> value type, an emitter
    /// type-mapping arm, and a JSON codec.
    /// </summary>
    SupportedValue,

    /// <summary>
    /// The builtin is a structural type-former that is legal in type signatures (function
    /// arrows, the update monad, exception/interface contexts) but is never a serializable
    /// data-field type. The model carries it with its real identity — a
    /// <see cref="DamlPrimitiveType"/> over this row's <see cref="DamlPrimitiveCatalogRow.Primitive"/>
    /// — instead of substituting <see cref="DamlPrimitive.Unit"/>, and the emitter fails
    /// loudly if one reaches a data position.
    /// </summary>
    SignatureOnly,

    /// <summary>
    /// The builtin has no mapping and encountering it throws: a value-level type with no
    /// correct serialization yet (<c>BUILTIN_TYPE_BIGNUMERIC</c>,
    /// <c>BUILTIN_TYPE_ROUNDING_MODE</c>), a value that cannot legitimately appear on the
    /// wire (<c>BUILTIN_TYPE_UNSPECIFIED</c>, <c>BUILTIN_TYPE_SCENARIO</c>), or any other
    /// value the pipeline does not accept.
    /// </summary>
    Unsupported
}

/// <summary>
/// One immutable <see cref="DamlPrimitiveCatalog"/> row: the recorded mapping, arity, and
/// disposition of a single proto <see cref="BuiltinType"/> value.
/// </summary>
/// <param name="Builtin">
/// The proto <see cref="BuiltinType"/> value this row describes. The catalog carries
/// exactly one row per defined proto value.
/// </param>
/// <param name="Primitive">
/// The <see cref="DamlPrimitive"/> model identity, or <c>null</c> when the disposition is
/// <see cref="DamlPrimitiveDisposition.Unsupported"/> (encountering the builtin throws, so
/// an unsupported row carries no model identity to map to).
/// </param>
/// <param name="Arity">
/// The number of type arguments the Daml-LF builtin takes. Note
/// <see cref="DamlPrimitive.Numeric"/> has arity 1: its scale argument is the
/// <c>Nat</c> pun (a <see cref="DamlTypeVar"/> carrying the scale), which is why applied
/// <c>Numeric</c> stays a generic <see cref="DamlTypeApp"/>.
/// </param>
/// <param name="Disposition">
/// What the pipeline does when it meets this builtin; see
/// <see cref="DamlPrimitiveDisposition"/>.
/// </param>
/// <param name="Rationale">
/// A short human-readable record of why the row carries this disposition, citing evidence
/// (fixtures, Daml-LF spec facts) where the reason is not self-evident.
/// </param>
public sealed record DamlPrimitiveCatalogRow(
    BuiltinType Builtin,
    DamlPrimitive? Primitive,
    int Arity,
    DamlPrimitiveDisposition Disposition,
    string Rationale);

/// <summary>
/// The builtin catalog: one immutable <see cref="DamlPrimitiveCatalogRow"/> per proto
/// <see cref="BuiltinType"/> value (all 23 defined values,
/// <c>BUILTIN_TYPE_UNSPECIFIED</c> = 0 through <c>BUILTIN_TYPE_FAILURE_CATEGORY</c> = 22
/// in <c>proto/intermediate_dar.proto</c>), recording each builtin's model mapping, Daml-LF
/// arity, disposition, and rationale. This is the "adding a Daml primitive" checklist living
/// as queryable code instead of prose: the reader, the writer, and the DAR type
/// converter consult the catalog instead of mirroring independent switch tables, and
/// reflection completeness tests assert the row set stays in bijection with the proto enum
/// and that every <see cref="DamlPrimitiveDisposition.SupportedValue"/> row keeps its
/// runtime, emitter, and value backing.
/// <para>
/// Policy (user-approved): the 13 serializable value types are
/// <see cref="DamlPrimitiveDisposition.SupportedValue"/>; the six structural
/// type-formers (<c>ANY</c>, <c>TYPE_REP</c>, <c>ANY_EXCEPTION</c>, <c>UPDATE</c>,
/// <c>ARROW</c>, <c>FAILURE_CATEGORY</c>) are
/// <see cref="DamlPrimitiveDisposition.SignatureOnly"/> with real model identity — nothing
/// is silently erased to <see cref="DamlPrimitive.Unit"/>; <c>BIGNUMERIC</c>,
/// <c>ROUNDING_MODE</c>, <c>UNSPECIFIED</c>, and <c>SCENARIO</c> are
/// <see cref="DamlPrimitiveDisposition.Unsupported"/>.
/// </para>
/// <para>
/// Applied-type folding eligibility (the applied-type-nodes milestone,
/// <see cref="AppliedTypeFolding"/>) is the
/// five-node set <see cref="DamlPrimitive.List"/>, <see cref="DamlPrimitive.Optional"/>,
/// <see cref="DamlPrimitive.TextMap"/>, <see cref="DamlPrimitive.GenMap"/>,
/// <see cref="DamlPrimitive.ContractId"/> — NOT "arity &gt; 0":
/// <see cref="DamlPrimitive.Numeric"/> has arity 1 but its argument is the
/// <c>Nat</c> pun, so applied <c>Numeric</c> must stay a generic
/// <see cref="DamlTypeApp"/>.
/// </para>
/// </summary>
public static class DamlPrimitiveCatalog
{
    /// <summary>
    /// The catalog rows: exactly one per defined proto <see cref="BuiltinType"/> value,
    /// ordered by proto value (<c>BUILTIN_TYPE_UNSPECIFIED</c> = 0 first).
    /// </summary>
    public static IReadOnlyList<DamlPrimitiveCatalogRow> Rows { get; } =
    [
        Row(BuiltinType.Unspecified, primitive: null, arity: 0, DamlPrimitiveDisposition.Unsupported,
            "Proto zero-value: a Type that leaves `builtin` unset carries no builtin identity, and the "
            + "reader rejects it with InvalidDataException rather than guessing a mapping."),

        Row(BuiltinType.Unit, DamlPrimitive.Unit, arity: 0, DamlPrimitiveDisposition.SupportedValue,
            "Daml (): serializable value type with full backing — DamlUnit runtime value, emitter arm, JSON codec."),

        Row(BuiltinType.Bool, DamlPrimitive.Bool, arity: 0, DamlPrimitiveDisposition.SupportedValue,
            "Daml Bool: serializable value type with full backing — DamlBool runtime value, emitter arm, JSON codec."),

        Row(BuiltinType.Int64, DamlPrimitive.Int64, arity: 0, DamlPrimitiveDisposition.SupportedValue,
            "Daml Int: serializable value type with full backing — DamlInt64 runtime value, emitter arm, JSON codec."),

        Row(BuiltinType.Text, DamlPrimitive.Text, arity: 0, DamlPrimitiveDisposition.SupportedValue,
            "Daml Text: serializable value type with full backing — DamlText runtime value, emitter arm, JSON codec."),

        Row(BuiltinType.Numeric, DamlPrimitive.Numeric, arity: 1, DamlPrimitiveDisposition.SupportedValue,
            "Daml Numeric n: serializable value type with full backing — DamlNumeric runtime value, emitter arm, "
            + "JSON codec. Arity 1 is the Nat pun: the scale arrives as a DamlTypeVar, so applied Numeric stays a "
            + "generic DamlTypeApp and is deliberately NOT one of the five M4 folding nodes."),

        Row(BuiltinType.Party, DamlPrimitive.Party, arity: 0, DamlPrimitiveDisposition.SupportedValue,
            "Daml Party: serializable value type with full backing — DamlParty runtime value, emitter arm, JSON codec."),

        Row(BuiltinType.Date, DamlPrimitive.Date, arity: 0, DamlPrimitiveDisposition.SupportedValue,
            "Daml Date: serializable value type with full backing — DamlDate runtime value, emitter arm, JSON codec."),

        Row(BuiltinType.Timestamp, DamlPrimitive.Timestamp, arity: 0, DamlPrimitiveDisposition.SupportedValue,
            "Daml Time: serializable value type with full backing — DamlTimestamp runtime value, emitter arm, JSON codec."),

        Row(BuiltinType.List, DamlPrimitive.List, arity: 1, DamlPrimitiveDisposition.SupportedValue,
            "Daml [a]: serializable value type with full backing — DamlList runtime value, emitter arm, JSON codec. "
            + "One of the five M4 folding nodes."),

        Row(BuiltinType.Optional, DamlPrimitive.Optional, arity: 1, DamlPrimitiveDisposition.SupportedValue,
            "Daml Optional a: serializable value type with full backing — DamlOptional runtime value, emitter arm, "
            + "JSON codec. One of the five M4 folding nodes."),

        Row(BuiltinType.TextMap, DamlPrimitive.TextMap, arity: 1, DamlPrimitiveDisposition.SupportedValue,
            "Daml TextMap a: serializable value type with full backing — DamlTextMap runtime value, emitter arm, "
            + "JSON codec. One of the five M4 folding nodes."),

        Row(BuiltinType.GenMap, DamlPrimitive.GenMap, arity: 2, DamlPrimitiveDisposition.SupportedValue,
            "Daml GenMap k v: serializable value type with full backing — DamlGenMap runtime value, emitter arm, "
            + "JSON codec. One of the five M4 folding nodes."),

        Row(BuiltinType.ContractId, DamlPrimitive.ContractId, arity: 1, DamlPrimitiveDisposition.SupportedValue,
            "Daml ContractId t: serializable value type with full backing — DamlContractId runtime value, emitter "
            + "arm, JSON codec. One of the five M4 folding nodes."),

        Row(BuiltinType.Any, DamlPrimitive.Any, arity: 0, DamlPrimitiveDisposition.SignatureOnly,
            "Structural type-former: the existential builtin appears only in signature positions and the "
            + "splice-api-token-holding-v1 interned type pool; real identity, no data-position mapping."),

        Row(BuiltinType.TypeRep, DamlPrimitive.TypeRep, arity: 0, DamlPrimitiveDisposition.SignatureOnly,
            "Structural type-former: runtime type representations appear only in signature positions and the "
            + "splice-api-token-holding-v1 interned type pool; real identity, no data-position mapping."),

        Row(BuiltinType.RoundingMode, primitive: null, arity: 0, DamlPrimitiveDisposition.Unsupported,
            "Daml-LF 2.dev-only value type with no correct serialization mapping; encountering it throws "
            + "NotSupportedException rather than silently serializing wrongly."),

        Row(BuiltinType.Bignumeric, primitive: null, arity: 0, DamlPrimitiveDisposition.Unsupported,
            "Daml-LF 2.dev-only value type with no correct serialization mapping; encountering it throws "
            + "NotSupportedException rather than silently serializing wrongly."),

        Row(BuiltinType.AnyException, DamlPrimitive.AnyException, arity: 0, DamlPrimitiveDisposition.SignatureOnly,
            "Structural type-former: the catch-all exception context appears only in signature positions and the "
            + "splice-api-token-holding-v1 interned type pool; real identity, no data-position mapping."),

        Row(BuiltinType.Update, DamlPrimitive.Update, arity: 1, DamlPrimitiveDisposition.SignatureOnly,
            "Daml Update a: the contract-update monad, legal only in signature positions (interface-method return "
            + "types are typically Update X); real identity, no data-position mapping."),

        Row(BuiltinType.Scenario, primitive: null, arity: 1, DamlPrimitiveDisposition.Unsupported,
            "Reserved and removed in Daml-LF 2.x (field 1004 is `reserved` in the Daml-LF proto), so no valid "
            + "archive can carry it; encountering it throws rather than guessing a mapping."),

        Row(BuiltinType.Arrow, DamlPrimitive.Arrow, arity: 2, DamlPrimitiveDisposition.SignatureOnly,
            "Daml a -> b: the function type appears only in signature positions; real identity, no "
            + "data-position mapping."),

        Row(BuiltinType.FailureCategory, DamlPrimitive.FailureCategory, arity: 0, DamlPrimitiveDisposition.SignatureOnly,
            "Characterized 2026-09-15 on the splice fixtures (FailureCategoryCharacterizationTests): "
            + "FAILURE_CATEGORY appears only in interned type "
            + "pools and value-definition signatures — never a serializable data position, the sole data-field "
            + "occurrence being the non-serializable DA.Internal.Fail.Types:FailureStatus.category — and is absent "
            + "from the JVM-produced intermediate, so SignatureOnly with real identity even though Canton 3.5 "
            + "stdlib documents `instance Serializable FailureCategory`."),
    ];

    private static readonly FrozenDictionary<BuiltinType, DamlPrimitiveCatalogRow> RowsByBuiltin =
        Rows.ToFrozenDictionary(row => row.Builtin);

    /// <summary>
    /// Returns the catalog row for <paramref name="builtin"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="builtin"/> is not a defined proto
    /// <see cref="BuiltinType"/> value.
    /// </exception>
    public static DamlPrimitiveCatalogRow Get(BuiltinType builtin) =>
        RowsByBuiltin.TryGetValue(builtin, out var row)
            ? row
            : throw new ArgumentException(
                $"'{builtin}' is not a defined proto BuiltinType value.", nameof(builtin));

    private static DamlPrimitiveCatalogRow Row(
        BuiltinType builtin,
        DamlPrimitive? primitive,
        int arity,
        DamlPrimitiveDisposition disposition,
        string rationale) =>
        new(builtin, primitive, arity, disposition, rationale);
}
