// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Cardinality of an expected created contract slot in a choice's return type.
/// </summary>
internal enum CreatedCardinality
{
    /// <summary>Single <c>ContractId T</c> — exactly one created contract of <c>T</c> is expected.</summary>
    Single,
    /// <summary>Optional <c>ContractId T</c> — zero or one created contracts of <c>T</c> is expected.</summary>
    Optional,
    /// <summary>List <c>[ContractId T]</c> — any number of created contracts of <c>T</c> is expected.</summary>
    List,
}

/// <summary>
/// The Daml interface a <c>ContractId I</c> slot targets, identified by the
/// <c>(module, entity)</c> pair carried in a created contract's interface ids. Present
/// only when the slot's target is an interface marker; <see langword="null"/> for
/// concrete-template slots.
/// </summary>
/// <remarks>
/// <see cref="ChoiceEmitter"/> checks this record for presence (a non-<see
/// langword="null"/> slot is an interface slot) and then reads
/// <c>{marker}.InterfaceId.ModuleName</c>/<c>EntityName</c> off the generated marker at
/// runtime, mirroring how the template branch reads <c>{template}.TemplateId</c>. Local
/// and foreign interface refs alike resolve to a marker carrying that static, so both
/// take the same branch. <see cref="ModuleName"/> and <see cref="EntityName"/> remain
/// here as the slot's resolved identity for testing.
/// </remarks>
/// <param name="ModuleName">The interface's declaring Daml module name.</param>
/// <param name="EntityName">The interface's entity (declaration) name.</param>
internal sealed record InterfaceMatcher(string ModuleName, string EntityName);

/// <summary>
/// One step on the way from a choice's exercise result down to one of its <c>ContractId</c>
/// slots, mirroring the return-type wrapper the step passes through.
/// </summary>
internal abstract record ExerciseResultStep
{
    /// <summary>Into the value an <c>Optional</c> carries, when it carries one.</summary>
    public sealed record OptionalValue : ExerciseResultStep;

    /// <summary>Into every element of a list, in order.</summary>
    public sealed record ListElements : ExerciseResultStep;

    /// <summary>Into one component of a <c>DA.Types</c> tuple, by its zero-based position.</summary>
    /// <param name="Index">The component's position in the tuple.</param>
    public sealed record TupleComponent(int Index) : ExerciseResultStep;
}

/// <summary>
/// One declared <c>ContractId T</c>-bearing slot in a choice's return type.
/// </summary>
/// <param name="FieldName">PascalCase C# field name on the emitted <c>&lt;Choice&gt;Result</c> record.</param>
/// <param name="CSharpTemplateType">C# name of the template or interface-marker type (e.g. <c>Agreement</c>, <c>IFactory</c>).</param>
/// <param name="Cardinality">How many created contracts of this template the choice should produce.</param>
/// <param name="ResultPath">
/// The steps from the choice's exercise result down to this slot's <c>ContractId</c> values,
/// outermost first; empty when the return type is the bare <c>ContractId</c> itself.
/// </param>
/// <param name="Interface">
/// Set when the slot targets a Daml interface marker — generated interface markers expose
/// no <c>TemplateId</c>, so the projector matches an interface slot against the created
/// contract's interface ids rather than its template id.
/// </param>
internal sealed record ChoiceCreatedSlot(
    string FieldName,
    string CSharpTemplateType,
    CreatedCardinality Cardinality,
    IReadOnlyList<ExerciseResultStep> ResultPath,
    InterfaceMatcher? Interface = null);

/// <summary>
/// Walks a choice's return type for embedded <c>ContractId T</c> references and returns
/// one slot per reference. Pure: a return type and the per-package resolution inputs go
/// in, a list of slots comes out, with no emitter state. Unit-testable directly without
/// emitting any source.
/// </summary>
internal static class ChoiceCreatedSlots
{
    /// <summary>
    /// Walks the choice's return type for embedded <c>ContractId T</c> references and
    /// returns one slot per reference (preserving declaration order). Returns an empty
    /// list when the return type carries no contract IDs — those choices don't get a
    /// <c>&lt;Choice&gt;Result</c> emitted.
    /// </summary>
    /// <remarks>
    /// <para>
    ///   Recognised return-type shapes:
    ///   <list type="bullet">
    ///     <item><c>ContractId T</c> — single-create.</item>
    ///     <item><c>Optional (ContractId T)</c> — optional-create.</item>
    ///     <item><c>[ContractId T]</c> — list-create.</item>
    ///     <item><c>(ContractId A, ContractId B, ...)</c> — Daml tuples (LF: <c>DA.Types:Tuple{N}</c>) — flattened across components.</item>
    ///   </list>
    /// </para>
    /// <para>
    ///   Anything else (records, primitives, plain <c>Unit</c>) yields an empty list —
    ///   the choice is treated as non-creating from the codegen's perspective. This
    ///   intentionally undershoots: a choice whose body creates contracts but returns
    ///   <c>Unit</c> won't get a typed projector. Consumers can fall back to walking
    ///   <c>tx.CreatedContracts</c> manually for those cases.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ChoiceCreatedSlot> Extract(PackageEmitContext context, ICrossPackageResolver resolver, DamlTypeMapper mapper, DamlType returnType)
    {
        var slots = new List<ChoiceCreatedSlot>();
        Walk(context, resolver, mapper, returnType, slots, parentCardinality: CreatedCardinality.Single, resultPath: []);
        return Disambiguate(slots);
    }

    private static IReadOnlyList<ChoiceCreatedSlot> Disambiguate(List<ChoiceCreatedSlot> slots)
    {
        var taken = new HashSet<string>(slots.Select(slot => slot.FieldName), StringComparer.Ordinal);
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < slots.Count; i++)
        {
            var baseName = slots[i].FieldName;
            if (assigned.Add(baseName))
            {
                continue;
            }
            var suffix = 2;
            string candidate;
            do
            {
                candidate = $"{baseName}{suffix}";
                suffix++;
            }
            while (taken.Contains(candidate) || !assigned.Add(candidate));
            slots[i] = slots[i] with { FieldName = candidate };
        }
        return slots;
    }

    /// <remarks>
    /// Recognizes <c>ContractId T</c> as a created slot, <c>Optional</c> and list wrappers as
    /// cardinality carriers, and <c>DA.Types</c> tuples as products to flatten over. A slot
    /// inherits the cardinality of the wrapper it sits under, so <c>[ContractId T]</c> yields a
    /// list slot rather than a single one. Records, variants, primitives, type variables and
    /// <c>Unit</c> contribute nothing — codegen treats them as non-creating return types.
    /// </remarks>
    private static void Walk(
        PackageEmitContext context, ICrossPackageResolver resolver, DamlTypeMapper mapper,
        DamlType type,
        List<ChoiceCreatedSlot> slots,
        CreatedCardinality parentCardinality,
        IReadOnlyList<ExerciseResultStep> resultPath)
    {
        switch (type)
        {
            case DamlContractIdType contractId:
            {
                var (templateName, csharpName, interfaceMatcher) =
                    ResolveContractIdTarget(context, resolver, mapper, contractId.Payload);
                slots.Add(new ChoiceCreatedSlot(
                    FieldName: templateName,
                    CSharpTemplateType: csharpName,
                    Cardinality: parentCardinality,
                    ResultPath: resultPath,
                    Interface: interfaceMatcher));
                return;
            }
            case DamlOptionalType optional:
                Walk(context, resolver, mapper, optional.Value, slots, CreatedCardinality.Optional, [.. resultPath, new ExerciseResultStep.OptionalValue()]);
                return;
            case DamlListType list:
                Walk(context, resolver, mapper, list.Element, slots, CreatedCardinality.List, [.. resultPath, new ExerciseResultStep.ListElements()]);
                return;
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId }, Arguments: [var arg] }:
            {
                var (templateName, csharpName, interfaceMatcher) = ResolveContractIdTarget(context, resolver, mapper, arg);
                slots.Add(new ChoiceCreatedSlot(
                    FieldName: templateName,
                    CSharpTemplateType: csharpName,
                    Cardinality: parentCardinality,
                    ResultPath: resultPath,
                    Interface: interfaceMatcher));
                return;
            }
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional }, Arguments: [var inner] }:
                Walk(context, resolver, mapper, inner, slots, CreatedCardinality.Optional, [.. resultPath, new ExerciseResultStep.OptionalValue()]);
                return;
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List }, Arguments: [var inner] }:
                Walk(context, resolver, mapper, inner, slots, CreatedCardinality.List, [.. resultPath, new ExerciseResultStep.ListElements()]);
                return;
            case DamlTypeApp { Base: DamlTypeRef { Module: "DA.Types", Name: var tupleName } } app
                when tupleName.StartsWith("Tuple", StringComparison.Ordinal):
                for (var i = 0; i < app.Arguments.Count; i++)
                {
                    Walk(context, resolver, mapper, app.Arguments[i], slots, parentCardinality, [.. resultPath, new ExerciseResultStep.TupleComponent(i)]);
                }
                return;
            default:
                return;
        }
    }

    private static (string FieldName, string CSharpTemplateType, InterfaceMatcher? Interface) ResolveContractIdTarget(PackageEmitContext context, ICrossPackageResolver resolver, DamlTypeMapper mapper, DamlType arg)
    {
        switch (arg)
        {
            case DamlTypeRef typeRef:
            {
                var fieldName = Identifiers.Sanitize(typeRef.Name);
                var csharpName = resolver.Resolve(typeRef, context);
                return (fieldName, csharpName, ResolveInterfaceMatcher(context, resolver, typeRef));
            }
            case DamlTypeApp { Base: DamlTypeRef typeRef }:
            {
                var fieldName = Identifiers.Sanitize(typeRef.Name);
                var csharpName = mapper.MapType(arg);
                return (fieldName, csharpName, ResolveInterfaceMatcher(context, resolver, typeRef));
            }
            default:
                var mapped = mapper.MapType(arg);
                return ("Created", mapped, null);
        }
    }

    /// <remarks>
    /// Mirrors the interface-marker branches of <see cref="DarCrossPackageResolver"/>: a slot
    /// targets a Daml interface exactly when the resolver would emit an interface-marker name for
    /// it. Local interfaces live in <see cref="PackageEmitContext.LocalInterfaceQualifiedNames"/>;
    /// foreign ones are read from the referenced package's interface declarations.
    /// </remarks>
    private static InterfaceMatcher? ResolveInterfaceMatcher(PackageEmitContext context, ICrossPackageResolver resolver, DamlTypeRef typeRef)
    {
        if (context.IsLocalRef(typeRef))
        {
            var isLocalInterface = context.LocalInterfaceQualifiedNames.Contains($"{typeRef.Module}:{typeRef.Name}");
            return isLocalInterface ? new InterfaceMatcher(typeRef.Module, typeRef.Name) : null;
        }

        var isForeignInterface = resolver.LookupPackage(typeRef.PackageId) is { } pkg
            && !StdlibPackages.IsStdlibPackage(pkg.Name)
            && !StdlibPackages.IsPlaceholderPackageName(pkg.Name)
            && pkg.Modules.Any(module => module.Name == typeRef.Module
                && module.Interfaces.Any(iface => iface.Name == typeRef.Name));

        return isForeignInterface ? new InterfaceMatcher(typeRef.Module, typeRef.Name) : null;
    }
}
