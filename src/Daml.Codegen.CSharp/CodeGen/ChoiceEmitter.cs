// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using RuntimeNamespaces = Daml.Runtime.RuntimeNamespaces;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Emits the C# that <em>exercises</em> a choice: the
/// <c>Choice&lt;Template, Arg, Result&gt;</c> descriptor with its
/// result decoder, the typed <c>Try&lt;Choice&gt;Async</c> exercisers (both the
/// contract-id-returning and the value-returning flavour), and the interface-choice
/// extensions. Constructed once per package over the package's
/// <see cref="PackageEmitContext"/>, the DAR-scoped <see cref="DarCrossPackageResolver"/>,
/// the package's <see cref="DamlTypeMapper"/>, and the shared <see cref="PartyAnalysis"/>
/// module. Calls the mapper for every type fragment and reads — but does not own — the
/// resolved choice-argument metadata; a choice argument it cannot map throws
/// <see cref="CodegenException"/> instead of emitting a stub. Distinct from the
/// create/submission path: creating a contract is not exercising a choice.
/// </summary>
internal sealed partial class ChoiceEmitter(
    PackageEmitContext context,
    DarCrossPackageResolver resolver,
    CodeGenOptions options,
    DamlTypeMapper mapper,
    PartyAnalysis party)
{
    /// <summary>
    /// Emits the choice descriptor surface nested inside the template record: the
    /// <c>Choice&lt;...&gt;</c> property (with its argument encoder and result decoder)
    /// for every choice on <paramref name="template"/>.
    /// </summary>
    internal void WriteChoiceDescriptors(IndentWriter indent, DamlTemplate template)
    {
        foreach (var choice in template.Choices)
        {
            WriteChoiceMethod(indent, choice);
        }
    }

    /// <summary>
    /// Emits the static <c>IHasChoices&lt;TSelf&gt;.Choices</c> witness aggregating the
    /// <c>Choice{X}</c> descriptor properties already written for <paramref name="choices"/> —
    /// shared between templates (see <see cref="WriteChoiceDescriptors"/>) and interface
    /// markers (see <see cref="WriteInterfaceChoiceDescriptors"/>), since both emit the same
    /// per-choice descriptor shape and therefore the same aggregate.
    /// </summary>
    /// <param name="indent">The writer to append to.</param>
    /// <param name="choices">The choices to aggregate, in declaration order.</param>
    /// <param name="explicitInterfaceOwner">
    /// When this witness is emitted inside an interface marker declaring
    /// <c>IHasChoices&lt;TSelf&gt;</c> for itself, the marker's own name (<c>TSelf</c>); the
    /// property is then emitted as an explicit interface implementation, because a plain
    /// <c>public static</c> member on the interface only <em>hides</em> the inherited static
    /// abstract requirement (CS0108) rather than implementing it, which leaves the interface
    /// without a most-specific implementation and makes it unusable as a type argument
    /// (CS8920) — concrete types (templates) implementing <c>IHasChoices&lt;TSelf&gt;</c> for
    /// themselves are unaffected and keep the plain <c>public static</c> form (pass
    /// <see langword="null"/>, the default).
    /// </param>
    internal void WriteChoicesAggregateProperty(
        IndentWriter indent,
        IReadOnlyList<DamlChoice> choices,
        string? explicitInterfaceOwner = null)
    {
        indent.Require("System.Collections.Generic");
        indent.Require(RuntimeNamespaces.Commands);

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Gets the choice descriptors declared by this type.</summary>");
        }

        var choiceRefs = string.Join(", ", choices.Select(choice => $"Choice{SanitizeIdentifier(choice.Name)}"));
        var choiceListHead = TypeReferenceQualifier.Qualify("IReadOnlyList");
        var choiceListType = TypeReferenceQualifier.Qualify(RuntimeTypeNames.IChoice);
        var hasChoicesRef = TypeReferenceQualifier.Qualify(RuntimeTypeNames.IHasChoices);
        var modifier = explicitInterfaceOwner is null ? "public static" : "static";
        var memberName = explicitInterfaceOwner is null
            ? "Choices"
            : $"{hasChoicesRef}<{explicitInterfaceOwner}>.Choices";
        indent.AppendLine($"{modifier} {choiceListHead}<{choiceListType}> {memberName} {{ get; }} = [{choiceRefs}];");
        indent.AppendLine();
    }

    /// <summary>
    /// Resolves the C# argument shape of <paramref name="choice"/>: a same-package
    /// record reference becomes the nested argument record, <c>Unit</c> and the
    /// synthetic stdlib <c>Archive</c> become the argument-less <c>DamlUnit</c> shape,
    /// and any other type reference resolves through the cross-package resolver.
    /// </summary>
    internal ChoiceArgumentInfo GetChoiceArgumentInfo(DamlChoice choice)
    {
        if (choice.ArgumentType is DamlTypeRef typeRef
            && context.IsLocalRef(typeRef)
            && context.NameTable.NestedChoiceArgumentRecord(choice) is { } argumentRecord)
        {
            return new ChoiceArgumentInfo(SanitizeIdentifier(choice.Name), argumentRecord.Fields, IsNestedTemplateArg: true);
        }

        if (choice.ArgumentType is DamlPrimitiveType { Primitive: DamlPrimitive.Unit })
        {
            return new ChoiceArgumentInfo(RuntimeTypeNames.DamlUnit, Fields: null, IsNestedTemplateArg: false);
        }

        if (choice.ArgumentType is DamlTypeRef externalRef)
        {
            if (IsSyntheticArchive(choice))
            {
                return new ChoiceArgumentInfo(RuntimeTypeNames.DamlUnit, Fields: null, IsNestedTemplateArg: false);
            }
            return new ChoiceArgumentInfo(resolver.Resolve(externalRef, context), Fields: null, IsNestedTemplateArg: false);
        }

        throw new CodegenException(
            $"Cannot emit choice '{choice.Name}': its argument type '{choice.ArgumentType}' does not map "
            + "to an exercisable C# argument record (expected a same-package record reference, Unit, or a "
            + "resolvable external type reference). Generation fails here instead of emitting an empty "
            + $"'{SanitizeIdentifier(choice.Name)}Arg' stub record into generated code.");
    }

    private void WriteChoiceMethod(IndentWriter indent, DamlChoice choice)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var returnType = mapper.MapType(choice.ReturnType);
        var argument = GetChoiceArgumentInfo(choice);

        indent.Require(RuntimeNamespaces.Commands);
        StdlibPackages.RequireForFieldType(resolver, context.Package, indent, choice.ReturnType);

        indent.AppendLine("/// <summary>");
        indent.AppendLine($"/// Exercise the {choice.Name} choice.");
        if (choice.Consuming)
        {
            indent.AppendLine("/// This choice is consuming and will archive the contract.");
        }
        indent.AppendLine("/// </summary>");

        var argTypeRef = argument.HasArgument
            ? argument.TypeName
            : TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlUnit);
        indent.AppendLine($"public static {TypeReferenceQualifier.Qualify(RuntimeTypeNames.Choice)}<{indent.CurrentTypeName}, {argTypeRef}, {returnType}> Choice{choiceName} {{ get; }} = new()");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"Name = new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ChoiceName)}(\"{choice.Name}\"),");
        indent.AppendLine($"Consuming = {(choice.Consuming ? "true" : "false")},");

        if (argument.HasArgument)
        {
            var damlRecordRef = TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlRecord);
            indent.AppendLine("ArgumentEncoder = arg => arg.ToRecord(),");
            indent.AppendLine($"ArgumentDecoder = val => {argTypeRef}.FromRecord(val.As<{damlRecordRef}>()),");
            WriteResultDecoder(indent, choice.ReturnType, returnType);
            WriteJsonReaderProperty(indent, "ArgumentJsonReader", choice.ArgumentType);
        }
        else
        {
            indent.AppendLine($"ArgumentEncoder = _ => {EmptyArgumentExpression(choice)},");
            WriteEmptyArgumentDecoder(indent, choice);
            WriteResultDecoder(indent, choice.ReturnType, returnType);
            WriteEmptyArgumentJsonReader(indent, choice);
        }

        WriteJsonReaderProperty(indent, "ResultJsonReader", choice.ReturnType);

        indent.Dedent();
        indent.AppendLine("};");
        indent.AppendLine();
    }

    /// <remarks>
    /// Only <c>Unit</c> and the primitive <c>ContractId</c> form keep a hand-written short form,
    /// where the call site reads better than the helper's output. Everything else — type refs for
    /// records, variants and enums included — delegates to the shared from-value conversion, so
    /// the decoder inherits the same module-qualified enum dispatch and map, optional and list
    /// handling that field deserialization uses. A hand-rolled enum check here matched on the
    /// simple name and would route an enum return through a record cast whenever a same-named
    /// record existed in another module of the same package.
    /// </remarks>
    private void WriteResultDecoder(
        IndentWriter indent,
        DamlType returnType,
        string csharpReturnType,
        DamlTypeMapper? mapperOverride = null)
    {
        var activeMapper = mapperOverride ?? mapper;
        switch (returnType)
        {
            case DamlPrimitiveType { Primitive: DamlPrimitive.Unit }:
                indent.AppendLine($"ResultDecoder = _ => {TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlUnit)}.Instance,");
                return;
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId }, Arguments: [var arg] }:
                var contractType = activeMapper.MapType(arg);
                indent.AppendLine($"ResultDecoder = val => new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ContractId)}<{contractType}>(val.As<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlContractId)}>().Value),");
                return;
            case DamlContractIdType typedContractId:
                var typedContractType = activeMapper.MapType(typedContractId.Payload);
                indent.AppendLine($"ResultDecoder = val => new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ContractId)}<{typedContractType}>(val.As<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlContractId)}>().Value),");
                return;
        }

        var expr = activeMapper.FromValue(returnType, "val");
        indent.AppendLine($"ResultDecoder = val => {expr},");
    }

    private void WriteJsonReaderProperty(
        IndentWriter indent,
        string memberName,
        DamlType type,
        DamlTypeMapper? mapperOverride = null)
    {
        var activeMapper = mapperOverride ?? mapper;
        var jsonExpr = activeMapper.FromJson(type, "json", "context");
        indent.AppendLine($"{memberName} = (json, context) => {jsonExpr},");
    }

    private static ChoiceSubmitterParameter SubmitterInfoParameter() => new(
        TypeReferenceQualifier.Qualify(RuntimeTypeNames.SubmitterInfo),
        "submitter",
        "The submitter party set (<c>actAs</c> + optional <c>readAs</c>), so a submitter that must read contracts it does not act as stays expressible.");

    /// <summary>
    /// True for the built-in stdlib <c>DA.Internal.Template:Archive</c> choice, whose
    /// argument type is the empty record <c>Archive {}</c>, which a template never binds to the
    /// <c>Daml.Runtime.Stdlib.Archive</c> record type. Distinguishes it from a genuine
    /// <c>Unit</c>-argument choice so the argument encodes as an empty record — Canton's
    /// gRPC command preprocessor type-checks the argument and rejects <c>Unit</c> against
    /// the <c>Archive</c> choice signature.
    /// </summary>
    private bool IsSyntheticArchive(DamlChoice choice) =>
        choice.ArgumentType is DamlTypeRef { Name: "Archive", Module: "DA.Internal.Template" } archiveRef
        && !string.IsNullOrEmpty(archiveRef.PackageId)
        && resolver.LookupPackage(archiveRef.PackageId) is { } archivePkg
        && (IsStdlibPackage(archivePkg.Name) || IsPlaceholderPackageName(archivePkg.Name));

    private string EmptyArgumentExpression(DamlChoice choice) =>
        IsSyntheticArchive(choice)
            ? $"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlRecord)}.Create()"
            : $"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlUnit)}.Instance";

    /// <summary>
    /// Emits the <c>ArgumentDecoder</c> for an argument-less choice, validating the
    /// decoded <see cref="global::Daml.Runtime.Data.DamlValue"/> against the shape
    /// <see cref="EmptyArgumentExpression"/> encodes instead of unconditionally
    /// returning the <c>DamlUnit</c> singleton: a genuine <c>Unit</c>-argument choice
    /// must decode from a <c>DamlUnit</c>, while <see cref="IsSyntheticArchive"/>'s
    /// empty-record shape must decode from a fields-empty <c>DamlRecord</c>. Guards
    /// <see cref="global::Daml.Runtime.Commands.IChoice.DecodeArgument"/> against
    /// silently accepting a <c>DamlValue</c> that does not match the choice's actual
    /// wire shape. Both branches throw <c>InvalidOperationException</c> on a mismatch,
    /// rather than letting the <c>DamlUnit</c> branch fall through to <c>DamlValue.As</c>'s
    /// own <c>InvalidCastException</c>, so callers of <c>IChoice.DecodeArgument</c> have a
    /// single exception type to catch regardless of which branch a choice takes.
    /// </summary>
    private void WriteEmptyArgumentDecoder(IndentWriter indent, DamlChoice choice)
    {
        var damlUnitRef = TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlUnit);
        if (IsSyntheticArchive(choice))
        {
            var damlRecordRef = TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlRecord);
            indent.AppendLine(
                $"ArgumentDecoder = val => val is {damlRecordRef} {{ Fields.Count: 0 }} ? {damlUnitRef}.Instance : "
                + $"throw new global::System.InvalidOperationException(\"Choice '{choice.Name}' argument must decode to an empty record.\"),");
        }
        else
        {
            indent.AppendLine(
                $"ArgumentDecoder = val => val is {damlUnitRef} u ? u : "
                + $"throw new global::System.InvalidOperationException(\"Choice '{choice.Name}' argument must decode to DamlUnit.\"),");
        }
    }

    private void WriteEmptyArgumentJsonReader(IndentWriter indent, DamlChoice choice)
    {
        if (IsSyntheticArchive(choice))
        {
            WriteSyntheticArchiveArgumentJsonReader(indent, choice);
        }
        else
        {
            WriteUnitArgumentJsonReader(indent);
        }
    }

    private void WriteSyntheticArchiveArgumentJsonReader(IndentWriter indent, DamlChoice choice)
    {
        var requireObject = $"{DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.RequireObject(json, context)";
        indent.AppendLine("ArgumentJsonReader = (json, context) =>");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"{requireObject};");
        indent.AppendLine($"return {EmptyArgumentExpression(choice)};");
        indent.Dedent();
        indent.AppendLine("},");
    }

    private static void WriteUnitArgumentJsonReader(IndentWriter indent)
    {
        var readUnit = $"{DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.ReadUnit(json, context)";
        indent.AppendLine($"ArgumentJsonReader = (json, context) => {readUnit},");
    }

    /// <summary>
    /// Emits the <c>&lt;Choice&gt;Command(this ContractId&lt;TemplateName&gt; contractId, ...)</c>
    /// builder that constructs the choice's <see cref="global::Daml.Runtime.Commands.ExerciseCommand"/>
    /// without submitting it. The single command builder shared by every generated
    /// <c>Try&lt;Choice&gt;Async</c> exerciser — the create-projecting ContractId overloads (see
    /// <c>WriteSingleChoiceAsyncExerciser</c> / <c>WriteSubmitterInfoChoiceAsyncExerciser</c>) and the
    /// non-contract exerciser (see <c>WriteSingleNonContractChoiceAsyncExerciser</c>) alike — they
    /// exercise the identical choice on the identical <c>ContractId&lt;T&gt;</c> type and therefore
    /// build the identical command. Argument-less choices encode via
    /// <see cref="EmptyArgumentExpression"/>: the synthetic stdlib Archive argument becomes the
    /// empty record Canton's command preprocessor accepts, a genuine <c>Unit</c> argument stays
    /// <c>DamlUnit.Instance</c>.
    /// </summary>
    private void WriteChoiceCommandBuilder(
        IndentWriter indent,
        DamlChoice choice,
        string templateClassName)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var commandMethodName = $"{choiceName}Command";
        var argument = GetChoiceArgumentInfo(choice);
        var hasArg = argument.HasArgument;

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Builds the <see cref=\"global::Daml.Runtime.Commands.ExerciseCommand\"/> for the {choice.Name} choice on this contract id.");
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <param name=\"contractId\">The contract on which to exercise the choice.</param>");
            if (hasArg)
            {
                indent.AppendLine("/// <param name=\"argument\">The choice argument.</param>");
            }
        }

        indent.AppendLine($"public static {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ExerciseCommand)} {commandMethodName}(");
        indent.Indent();
        if (hasArg)
        {
            indent.AppendLine($"this {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ContractId)}<{templateClassName}> contractId,");
            indent.AppendLine($"{argument.ParameterType(templateClassName)} argument)");
        }
        else
        {
            indent.AppendLine($"this {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ContractId)}<{templateClassName}> contractId)");
        }
        indent.Dedent();
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("global::System.ArgumentNullException.ThrowIfNull(contractId);");
        if (hasArg)
        {
            indent.AppendLine("global::System.ArgumentNullException.ThrowIfNull(argument);");
        }

        var argExpr = hasArg ? "argument.ToRecord()" : EmptyArgumentExpression(choice);
        indent.AppendLine($"return new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ExerciseCommand)}(");
        indent.Indent();
        indent.AppendLine($"{templateClassName}.TemplateId,");
        indent.AppendLine("contractId,");
        indent.AppendLine($"new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ChoiceName)}(\"{choice.Name}\"),");
        indent.AppendLine($"{argExpr});");
        indent.Dedent();

        indent.Dedent();
        indent.AppendLine("}");
    }

    /// <summary>
    /// Emits one <c>&lt;Choice&gt;ByKeyCommand</c> builder per choice on
    /// <paramref name="template"/>, into the template record body. A key-less template
    /// emits nothing.
    /// </summary>
    internal void WriteChoiceByKeyCommandBuilders(
        IndentWriter indent,
        DamlTemplate template,
        string emittedTemplateName)
    {
        if (template.Key is null)
        {
            return;
        }

        var templateClassName = context.QualifyInModule(emittedTemplateName);

        foreach (var choice in template.Choices)
        {
            WriteChoiceByKeyCommandBuilder(indent, choice, templateClassName, template.Key);
            indent.AppendLine();
        }
    }

    /// <summary>
    /// Emits the <c>&lt;Choice&gt;ByKeyCommand(TKey key, ...)</c> builder that constructs the
    /// choice's <see cref="global::Daml.Runtime.Commands.ExerciseByKeyCommand"/> without
    /// submitting it, the key-addressed twin of <see cref="WriteChoiceCommandBuilder"/>.
    /// Emitted into the template record itself rather than the sibling extensions classes, and
    /// as a plain static rather than an extension method: the key's C# type is whatever the Daml
    /// key type maps to — <c>string</c> and <c>Party</c> included — and extending those would put
    /// the method on every value of that type in the consuming project.
    /// </summary>
    private void WriteChoiceByKeyCommandBuilder(
        IndentWriter indent,
        DamlChoice choice,
        string templateClassName,
        DamlType keyType)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var argument = GetChoiceArgumentInfo(choice);
        var hasArg = argument.HasArgument;
        var csharpKeyType = mapper.MapType(keyType);

        indent.Require(RuntimeNamespaces.Commands);
        StdlibPackages.RequireForFieldType(resolver, context.Package, indent, keyType);

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Builds the <see cref=\"global::Daml.Runtime.Commands.ExerciseByKeyCommand\"/> for the {choice.Name} choice on the contract carrying this key.");
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <param name=\"key\">The contract key to exercise the choice against.</param>");
            if (hasArg)
            {
                indent.AppendLine("/// <param name=\"argument\">The choice argument.</param>");
            }
            indent.AppendLine("/// <remarks>");
            indent.AppendLine("/// Contract keys are not unique: several active contracts may carry the same key, and");
            indent.AppendLine("/// the ledger resolves this command against a first match by an order it only partly");
            indent.AppendLine("/// guarantees. Keeping a key unique is the application's responsibility.");
            indent.AppendLine("/// </remarks>");
        }

        indent.AppendLine($"public static {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ExerciseByKeyCommand)} {choiceName}ByKeyCommand(");
        indent.Indent();
        if (hasArg)
        {
            indent.AppendLine($"{csharpKeyType} key,");
            indent.AppendLine($"{argument.ParameterType(templateClassName)} argument)");
        }
        else
        {
            indent.AppendLine($"{csharpKeyType} key)");
        }
        indent.Dedent();
        indent.AppendLine("{");
        indent.Indent();

        if (mapper.MapsToReferenceType(keyType))
        {
            indent.AppendLine("global::System.ArgumentNullException.ThrowIfNull(key);");
        }
        if (hasArg)
        {
            indent.AppendLine("global::System.ArgumentNullException.ThrowIfNull(argument);");
        }

        var argExpr = hasArg ? "argument.ToRecord()" : EmptyArgumentExpression(choice);
        indent.AppendLine($"return new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ExerciseByKeyCommand)}(");
        indent.Indent();
        indent.AppendLine($"{templateClassName}.TemplateId,");
        indent.AppendLine($"{mapper.ToValue(keyType, "key")},");
        indent.AppendLine($"new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ChoiceName)}(\"{choice.Name}\"),");
        indent.AppendLine($"{argExpr});");
        indent.Dedent();

        indent.Dedent();
        indent.AppendLine("}");
    }

    private static bool IsStdlibPackage(string packageName) => StdlibPackages.IsStdlibPackage(packageName);

    private static bool IsPlaceholderPackageName(string packageName) => StdlibPackages.IsPlaceholderPackageName(packageName);

    private static string SanitizeIdentifier(string name) => Identifiers.Sanitize(name);

    private static string MemberName(string damlFieldName, string enclosingTypeName, IReadOnlySet<string> reservedMemberNames) =>
        Identifiers.MemberName(damlFieldName, enclosingTypeName, reservedMemberNames);
}
