// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using LedgerNamespaces = Daml.Ledger.Abstractions.LedgerNamespaces;
using RuntimeNamespaces = Daml.Runtime.RuntimeNamespaces;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Spells every imported runtime/BCL simple type name <c>global::</c>-rooted. C# binds an
/// unqualified simple name by walking the enclosing member and namespace scopes before
/// consulting <c>using</c> directives, so a generated namespace segment, a package-declared
/// type, or a same-named property would capture it. Rooting the name at <c>global::</c>
/// closes that whole family by construction instead of detecting each collision.
/// </summary>
internal static class TypeReferenceQualifier
{
    private static readonly IReadOnlyDictionary<string, string> ImportedSimpleNames =
        WithGeneratedStdlibNames(new Dictionary<string, string>(StringComparer.Ordinal)
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
            [RuntimeTypeNames.Month] = RuntimeNamespaces.Stdlib,
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
            ["Version"] = "System",
        });

    private static Dictionary<string, string> WithGeneratedStdlibNames(Dictionary<string, string> names)
    {
        foreach (var (_, typeName) in StdlibRuntimeInventory.Generated)
        {
            names[Identifiers.Sanitize(typeName)] = RuntimeNamespaces.Stdlib;
        }
        return names;
    }

    /// <summary>
    /// Qualifies the head symbol of a C# type reference. Returns
    /// <c>global::Owning.Namespace.<paramref name="simpleName"/></c> for an imported runtime/BCL
    /// type; a name already <c>global::</c>-qualified or namespace-qualified is returned
    /// unchanged. Generic arguments are composed by the caller, e.g.
    /// <c>$"{Qualify("ContractId")}&lt;{inner}&gt;"</c>.
    /// </summary>
    /// <exception cref="CodegenException">
    /// <paramref name="simpleName"/> is a bare name no imported namespace owns, so the
    /// reference could not be rooted.
    /// </exception>
    public static string Qualify(string simpleName)
    {
        if (simpleName.StartsWith(Identifiers.GlobalPrefix, StringComparison.Ordinal) || simpleName.Contains('.'))
        {
            return simpleName;
        }

        return ImportedSimpleNames.TryGetValue(simpleName, out var owningNamespace)
            ? Identifiers.GlobalQualified(owningNamespace, simpleName)
            : throw new CodegenException(
                $"Runtime type '{simpleName}' has no owning namespace. Register it in TypeReferenceQualifier before emitting a reference to it.");
    }
}
