// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using LedgerNamespaces = Daml.Ledger.Abstractions.LedgerNamespaces;
using RuntimeNamespaces = Daml.Runtime.RuntimeNamespaces;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Resolves whether an imported runtime/BCL simple type name is shadowed —
/// either by a generated namespace segment, or by a top-level type the target
/// Daml package itself declares — and qualifies it with <c>global::</c> when it
/// is. C# binds an unqualified simple name by walking enclosing namespace
/// scopes and sibling type declarations before consulting <c>using</c>
/// directives, so either a generated namespace segment or a package-declared
/// type equal to an imported type name (e.g. a Daml <c>enum Unit</c>) produces
/// CS0118 / CS0117 unless qualified.
/// </summary>
internal sealed class TypeReferenceQualifier
{
    /// <remarks>
    /// Implicit usings import <c>System</c> into every generated file, so a stdlib type sharing a
    /// BCL simple name — <c>DayOfWeek</c> against <c>System.DayOfWeek</c> — is an ambiguous
    /// reference (CS0104) no matter how the generated namespace shadows it. These names are
    /// always emitted <c>global::</c>-qualified.
    /// </remarks>
    private static readonly IReadOnlySet<string> NamesCollidingWithImplicitBclImports =
        new HashSet<string>(StringComparer.Ordinal) { RuntimeTypeNames.DayOfWeek };

    private static readonly IReadOnlyDictionary<string, string> ImportedSimpleNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeTypeNames.Party] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlRecord] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlField] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlFieldAttribute] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlFieldCollections] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlValue] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.IDamlValue] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.IDamlRecord] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.IDamlVariant] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlVariant] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlEnum] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlOptional] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlOptionalChain] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlList] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlTextMap] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlGenMap] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlInt64] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlNumeric] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlText] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlBool] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlUnit] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlDate] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlTimestamp] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlParty] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.Identifier] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlContractId] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.EquatableArray] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.ContractId] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.Contract] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.ITemplate] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.IHasKey] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.IHasChoices] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.IUpgradeable] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.IContract] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.TransactionResult] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.CreatedContract] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.Choice] = RuntimeNamespaces.Commands,
            [RuntimeTypeNames.SubmitterInfo] = RuntimeNamespaces.Commands,
            [RuntimeTypeNames.ExerciseCommand] = RuntimeNamespaces.Commands,
            [RuntimeTypeNames.ExerciseByKeyCommand] = RuntimeNamespaces.Commands,
            [RuntimeTypeNames.CommandsSubmission] = RuntimeNamespaces.Commands,
            [RuntimeTypeNames.WorkflowId] = RuntimeNamespaces.Commands,
            [RuntimeTypeNames.CommandId] = RuntimeNamespaces.Commands,
            [RuntimeTypeNames.ChoiceName] = RuntimeNamespaces.Commands,
            [RuntimeTypeNames.IChoice] = RuntimeNamespaces.Commands,
            [RuntimeTypeNames.IDamlInterface] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.IHasView] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.ViewDescriptor] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.KeyDescriptor] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.IImplements] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.CreatedEvent] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.DamlTypeDescriptor] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.DamlTypeKind] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.ExerciseOutcome] = RuntimeNamespaces.Outcomes,
            [RuntimeTypeNames.RelTime] = RuntimeNamespaces.Stdlib,
            [RuntimeTypeNames.DayOfWeek] = RuntimeNamespaces.Stdlib,
            [RuntimeTypeNames.Tuple2] = RuntimeNamespaces.Stdlib,
            [RuntimeTypeNames.Tuple3] = RuntimeNamespaces.Stdlib,
            [RuntimeTypeNames.Either] = RuntimeNamespaces.Stdlib,
            [RuntimeTypeNames.Set] = RuntimeNamespaces.Stdlib,
            [RuntimeTypeNames.NonEmpty] = RuntimeNamespaces.Stdlib,
            [RuntimeTypeNames.Map] = RuntimeNamespaces.Stdlib,
            [RuntimeTypeNames.Optional] = RuntimeNamespaces.Stdlib,
            [RuntimeTypeNames.GenericStub] = RuntimeNamespaces.Stdlib,
            [RuntimeTypeNames.ILedgerClient] = LedgerNamespaces.Abstractions,
            [RuntimeTypeNames.ILedgerWriter] = LedgerNamespaces.Abstractions,
            ["IReadOnlyList"] = "System.Collections.Generic",
            ["IReadOnlyDictionary"] = "System.Collections.Generic",
            ["HashSet"] = "System.Collections.Generic",
            ["EqualityComparer"] = "System.Collections.Generic",
            ["HashCode"] = "System",
            ["Func"] = "System",
        };

    private readonly string _generatedNamespace;

    /// <summary>The emitting module's namespace plus all its ancestor prefixes, used for shadowing checks.</summary>
    public IReadOnlySet<string> AllNamespaces { get; }

    /// <summary>
    /// Sanitised C# names of the top-level types that shadow an imported runtime/BCL name
    /// inside the emitting module's namespace — every type the Daml package declares in that
    /// namespace or in one of its ancestor namespaces — used for shadowing checks alongside
    /// <see cref="AllNamespaces"/>. C# binds a simple name by walking the enclosing
    /// namespaces outward before consulting <c>using</c> directives, so a type declared in a
    /// sibling module's namespace does not belong here.
    /// </summary>
    public IReadOnlySet<string> DeclaredTypeNames { get; }

    /// <summary>
    /// Creates a qualifier scoped to the emitting module's namespace (ancestors are derived
    /// automatically) and the top-level type names visible from it.
    /// </summary>
    public TypeReferenceQualifier(
        string generatedNamespace, IEnumerable<string>? declaredTypeNames = null)
    {
        _generatedNamespace = generatedNamespace;
        AllNamespaces = new HashSet<string>(Identifiers.NamespaceWithAncestors(generatedNamespace), StringComparer.Ordinal);
        DeclaredTypeNames = new HashSet<string>(declaredTypeNames ?? [], StringComparer.Ordinal);
    }

    /// <summary>
    /// Qualifies the head symbol of a C# type reference. Returns
    /// <c>global::Owning.Namespace.<paramref name="simpleName"/></c> when
    /// <paramref name="simpleName"/> is an imported runtime/BCL type shadowed by a segment
    /// of the emitting module's namespace, or by a top-level type visible from it;
    /// returns the name unchanged otherwise. Generic arguments are composed by the
    /// caller, e.g. <c>$"{Qualify("ContractId")}&lt;{inner}&gt;"</c>. Names already
    /// <c>global::</c>-qualified or namespace-qualified are returned unchanged.
    /// </summary>
    public string Qualify(string simpleName)
    {
        if (simpleName.StartsWith(Identifiers.GlobalPrefix, StringComparison.Ordinal)
            || simpleName.Contains('.')
            || !ImportedSimpleNames.TryGetValue(simpleName, out var owningNamespace))
        {
            return simpleName;
        }

        if (!NamesCollidingWithImplicitBclImports.Contains(simpleName)
            && !IsShadowed(simpleName))
        {
            return simpleName;
        }

        return Identifiers.GlobalQualified(owningNamespace, simpleName);
    }

    private bool IsShadowed(string simpleName)
    {
        if (DeclaredTypeNames.Contains(simpleName))
        {
            return true;
        }

        if (AllNamespaces.Contains(simpleName))
        {
            return true;
        }

        return Identifiers.NamespaceWithAncestors(_generatedNamespace)
            .Any(prefix => AllNamespaces.Contains($"{prefix}.{simpleName}"));
    }
}
