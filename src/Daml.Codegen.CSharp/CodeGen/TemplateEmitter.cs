// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RuntimeNamespaces = Daml.Runtime.RuntimeNamespaces;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Emits the C# for a Daml template: the sealed template record with its
/// <see cref="Daml.Runtime.Contracts.ITemplate"/> facet (plus the optional
/// <c>IUpgradeable</c> facet, plus one <c>IImplements</c> per implemented
/// interface, plus <c>IHasKey</c> and its static <c>Key</c> witness when the
/// template declares a contract key), the static template metadata,
/// and the namespace-level choice / submission extension surface. The
/// field-bearing serialization surface (constructor parameters, <c>ToRecord</c> /
/// <c>FromRecord</c>) is delegated to the shared
/// <see cref="RecordSerializationEmitter"/>, the choice descriptors / exercisers
/// to the shared <see cref="ChoiceEmitter"/>, and the typed-submitter surface to
/// the shared <see cref="SubmissionExtensionsEmitter"/> — the same per-package
/// instances the sibling emitters use, so record, template, and choice output stay
/// byte-identical. Constructed once per package over the package's
/// <see cref="PackageEmitContext"/>, the DAR-scoped <see cref="DarCrossPackageResolver"/>,
/// those three composed emitters, and the shared <see cref="CodeGenOptions"/>. The caller owns the file scaffold and the
/// common usings; this emitter writes the template body into the provided
/// <see cref="IndentWriter"/>.
/// </summary>
internal sealed partial class TemplateEmitter(
    PackageEmitContext context,
    DarCrossPackageResolver resolver,
    RecordSerializationEmitter recordSerialization,
    ChoiceEmitter choiceEmitter,
    SubmissionExtensionsEmitter submissionExtensions,
    CodeGenOptions options,
    ILogger? logger = null)
{
    private readonly DamlTypeMapper mapper = new(context, resolver);

    private const string KeyMemberName = "Key";
    private const string ChoicesMemberName = "Choices";
    private const string KeyEncoderMemberName = "KeyEncoder";
    private const string KeyEncoderParameterName = "key";
    private const string KeyDecoderMemberName = "KeyDecoder";
    private const string KeyDecoderParameterName = "value";
    private const string KeyJsonReaderMemberName = "KeyJsonReader";
    private const string KeyJsonReaderJsonParameterName = "json";
    private const string KeyJsonReaderContextParameterName = "context";

    private readonly ILogger _log = logger ?? NullLogger.Instance;

    /// <summary>
    /// Writes the template record, its static metadata,
    /// the serialization round-trip, and the sibling choice / submission extension classes for
    /// <paramref name="template"/> into <paramref name="indent"/>.
    /// </summary>
    internal void WriteTemplateType(
        IndentWriter indent,
        DamlPackage package,
        DamlModule module,
        DamlTemplate template,
        IReadOnlyList<DamlFieldDefinition> fields)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Generated from Daml template {module.Name}:{template.Name}");
            indent.AppendLine("/// </summary>");
        }

        var className = context.EmittedTypeName(module.Name, template.Name);
        indent.CurrentTypeName = className;
        indent.CurrentReservedMemberNames = context.TemplateFieldReservedNames(template);

        var keyWitness = DescribeKeyWitness(className, template, fields);

        var interfacesList = new List<string> { TypeReferenceQualifier.Qualify(RuntimeTypeNames.ITemplate) };
        if (package.UpgradedPackageId is not null)
            interfacesList.Add(TypeReferenceQualifier.Qualify(RuntimeTypeNames.IUpgradeable));
        foreach (var implemented in template.Implements)
            interfacesList.Add($"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.IImplements)}<{resolver.Resolve(implemented, context)}>");
        if (keyWitness is not null)
            interfacesList.Add(keyWitness.FacetType);
        interfacesList.Add($"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.IHasChoices)}<{className}>");
        interfacesList.Add($"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.IDamlRecord)}<{className}>");
        var interfaces = string.Join(", ", interfacesList);

        if (fields.Count > 0)
        {
            indent.Append($"public sealed partial record {className}(");
            recordSerialization.WriteRecordParameters(indent, fields);
            indent.AppendLine($") : {interfaces}");
        }
        else
        {
            indent.AppendLine($"public sealed partial record {className} : {interfaces}");
        }

        indent.AppendLine("{");
        indent.Indent();

        recordSerialization.WriteCollectionValueSemantics(indent, className, fields);
        WriteTemplateMetadata(indent, package, module, template);

        if (keyWitness is not null)
        {
            WriteKeyWitness(indent, module, template, keyWitness);
        }

        recordSerialization.WriteToRecordMethod(indent, fields, []);
        recordSerialization.WriteFromRecordMethod(indent, className, fields, []);
        recordSerialization.WriteReadDamlLfJsonMethod(indent, fields, []);

        choiceEmitter.WriteChoiceDescriptors(indent, template);

        var choicesFieldTakingTheMemberName = fields.FirstOrDefault(
            field => Identifiers.MemberName(field.Name, className, context.TemplateFieldReservedNames(template)) == ChoicesMemberName);
        var templateNameTakesTheChoicesMemberName = className == ChoicesMemberName;
        var nestedChoiceArgChoice = template.Choices.FirstOrDefault(c =>
            EmitterHelpers.SanitizeIdentifier(c.Name) == ChoicesMemberName &&
            choiceEmitter.GetChoiceArgumentInfo(c).IsNestedTemplateArg);
        var choicesMemberNameIsTaken = templateNameTakesTheChoicesMemberName
            || choicesFieldTakingTheMemberName is not null
            || nestedChoiceArgChoice is not null;
        if (choicesFieldTakingTheMemberName is not null)
        {
            LogChoicesFieldTakesTheWitnessName(_log, module.Name, template.Name, choicesFieldTakingTheMemberName.Name);
        }
        else if (templateNameTakesTheChoicesMemberName)
        {
            LogTemplateNameTakesTheChoicesWitnessName(_log, module.Name, template.Name);
        }
        else if (nestedChoiceArgChoice is not null)
        {
            LogNestedChoiceArgTakesTheChoicesWitnessName(_log, module.Name, template.Name, nestedChoiceArgChoice.Name);
        }
        choiceEmitter.WriteChoicesAggregateProperty(
            indent,
            template.Choices,
            explicitInterfaceOwner: choicesMemberNameIsTaken ? className : null);

        choiceEmitter.WriteChoiceByKeyCommandBuilders(indent, template, className);

        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();

        choiceEmitter.WriteChoiceAsyncExercisersClass(indent, template, className, fields);
        submissionExtensions.TryWriteSubmissionExtensions(indent, template, className, context.TemplateFieldReservedNames(template), fields);
        choiceEmitter.TryWriteNonContractChoiceExtensions(indent, template);
    }

    /// <summary>
    /// Writes a partial template record body whose sole member is the nested
    /// choice-argument record for <paramref name="choice"/>, mirroring the
    /// serialization surface the standalone record would have carried.
    /// </summary>
    internal void WriteNestedChoiceArgumentType(
        IndentWriter indent,
        DamlTemplate template,
        DamlChoice choice,
        DamlRecordDefinition record)
    {
        var templateClassName = context.EmittedTypeName(context.Module.Name, template.Name);
        indent.AppendLine($"public sealed partial record {templateClassName}");
        indent.AppendLine("{");
        indent.Indent();

        var choiceTypeName = EmitterHelpers.SanitizeIdentifier(choice.Name);
        indent.CurrentTypeName = choiceTypeName;
        indent.CurrentReservedMemberNames = ReservedMemberNames.OfRecordField();

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Choice argument type for {choice.Name}.");
            indent.AppendLine("/// </summary>");
        }

        if (record.Fields.Count > 0)
        {
            indent.Append($"public sealed record {choiceTypeName}(");
            recordSerialization.WriteRecordParameters(indent, record.Fields);
            indent.AppendLine($") : {TypeReferenceQualifier.Qualify(RuntimeTypeNames.IDamlRecord)}<{choiceTypeName}>");
        }
        else
        {
            indent.AppendLine($"public sealed record {choiceTypeName} : {TypeReferenceQualifier.Qualify(RuntimeTypeNames.IDamlRecord)}<{choiceTypeName}>");
        }

        indent.AppendLine("{");
        indent.Indent();

        recordSerialization.WriteCollectionValueSemantics(indent, choiceTypeName, record.Fields);
        recordSerialization.WriteToRecordMethod(indent, record.Fields, []);
        recordSerialization.WriteFromRecordMethod(indent, choiceTypeName, record.Fields, []);
        recordSerialization.WriteReadDamlLfJsonMethod(indent, record.Fields, []);

        indent.Dedent();
        indent.AppendLine("}");

        indent.Dedent();
        indent.AppendLine("}");
    }

    private void WriteTemplateMetadata(
        IndentWriter indent,
        DamlPackage package,
        DamlModule module,
        DamlTemplate template)
    {
        indent.Require("System");

        if (options.GenerateXmlDocs)
            indent.AppendLine("/// <summary>Gets the template identifier.</summary>");
        indent.AppendLine($"public static {TypeReferenceQualifier.Qualify(RuntimeTypeNames.Identifier)} TemplateId {{ get; }} = new(\"{package.PackageId}\", \"{module.Name}\", \"{template.Name}\");");
        indent.AppendLine();

        if (options.GenerateXmlDocs)
            indent.AppendLine("/// <summary>Gets the package ID.</summary>");
        indent.AppendLine($"public static string {nameof(Daml.Runtime.Contracts.ITemplate.PackageId)} => \"{package.PackageId}\";");
        indent.AppendLine();

        if (options.GenerateXmlDocs)
            indent.AppendLine("/// <summary>Gets the package name.</summary>");
        indent.AppendLine($"public static string {nameof(Daml.Runtime.Contracts.ITemplate.PackageName)} => \"{package.Name}\";");
        indent.AppendLine();

        if (options.GenerateXmlDocs)
            indent.AppendLine("/// <summary>Gets the package version.</summary>");
        indent.AppendLine($"public static {TypeReferenceQualifier.Qualify("Version")} {nameof(Daml.Runtime.Contracts.ITemplate.PackageVersion)} {{ get; }} = new({package.Version.Major}, {package.Version.Minor}, {package.Version.Build});");
        indent.AppendLine();

        if (options.GenerateXmlDocs)
            indent.AppendLine("/// <summary>Gets the compile-time Daml type descriptor.</summary>");
        indent.AppendLine($"public static {TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlTypeDescriptor)} DamlTypeId {{ get; }} = new(TemplateId, {TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlTypeKind)}.Template, PackageName);");
        indent.AppendLine();

        if (package.UpgradedPackageId is not null)
        {
            if (options.GenerateXmlDocs)
                indent.AppendLine("/// <summary>Gets the package ID that this package upgrades.</summary>");
            indent.AppendLine($"public static string? UpgradedPackageId => \"{package.UpgradedPackageId}\";");
            indent.AppendLine();
        }
    }

    /// <summary>
    /// The resolved pieces of a keyed template's <c>IHasKey</c> facet: the facet named in the
    /// base list, the descriptor type the witness is declared as, the encode expression over a
    /// lambda parameter named <c>key</c>, the decode expression over a lambda parameter named
    /// <c>value</c>, the JSON-read expression over lambda parameters named <c>json</c> and
    /// <c>context</c> — read straight to a <c>DamlValue</c>, with no <c>Decoder</c> composed in,
    /// matching <see cref="Daml.Runtime.Contracts.KeyDescriptor{TTemplate, TKey}.KeyJsonReader"/>
    /// — and whichever declaration already takes the C# member name <c>Key</c> on the template
    /// record.
    /// </summary>
    private sealed record KeyWitness(
        string FacetType,
        string DescriptorType,
        string Encoder,
        string Decoder,
        string JsonReadExpr,
        bool TemplateNameTakesTheMemberName,
        string? FieldTakingTheMemberName)
    {
        public bool MemberNameIsTaken =>
            TemplateNameTakesTheMemberName || FieldTakingTheMemberName is not null;
    }

    private KeyWitness? DescribeKeyWitness(
        string className,
        DamlTemplate template,
        IReadOnlyList<DamlFieldDefinition> fields)
    {
        if (template.Key is not { } keyType)
        {
            return null;
        }

        var typeArguments = $"<{className}, {mapper.MapType(keyType)}>";
        var fieldTakingTheMemberName = fields.FirstOrDefault(
            field => Identifiers.MemberName(field.Name, className, context.TemplateFieldReservedNames(template)) == KeyMemberName);

        return new KeyWitness(
            $"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.IHasKey)}{typeArguments}",
            $"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.KeyDescriptor)}{typeArguments}",
            mapper.ToValue(keyType, KeyEncoderParameterName),
            mapper.FromValue(keyType, KeyDecoderParameterName),
            mapper.FromJson(keyType, KeyJsonReaderJsonParameterName, KeyJsonReaderContextParameterName),
            className == KeyMemberName,
            fieldTakingTheMemberName?.Name);
    }

    /// <summary>
    /// Writes the keyed template's static <c>Key</c> witness. When the member name is already
    /// taken — by a payload field mapping to it (CS0102), or by the template record itself
    /// being named <c>Key</c> (CS0542) — only the explicit interface implementation is written.
    /// It declares no member name of its own, so the facet stays reachable through a generic
    /// constraint while the template keeps the name it had.
    /// </summary>
    private void WriteKeyWitness(
        IndentWriter indent,
        DamlModule module,
        DamlTemplate template,
        KeyWitness witness)
    {
        indent.Require(RuntimeNamespaces.Contracts);
        StdlibPackages.RequireForFieldType(resolver, context.Package, indent, template.Key!);

        if (witness.MemberNameIsTaken)
        {
            if (witness.FieldTakingTheMemberName is { } fieldName)
            {
                LogKeyFieldTakesTheWitnessName(_log, module.Name, template.Name, fieldName);
            }
            else
            {
                LogTemplateNameTakesTheWitnessName(_log, module.Name, template.Name);
            }

            indent.AppendLine($"static {witness.DescriptorType} {witness.FacetType}.{KeyMemberName} {{ get; }} =");
            WriteKeyWitnessInitializer(indent, witness);
            return;
        }

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Gets the witness pairing this template with its contract key type and carrying the key codec; passing it to a generic method infers both type parameters from one argument.</summary>");
        }
        indent.AppendLine($"public static {witness.DescriptorType} {KeyMemberName} {{ get; }} =");
        WriteKeyWitnessInitializer(indent, witness);
    }

    private static void WriteKeyWitnessInitializer(IndentWriter indent, KeyWitness witness)
    {
        indent.Indent();
        indent.AppendLine("new()");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"{KeyEncoderMemberName} = {KeyEncoderParameterName} => {witness.Encoder},");
        indent.AppendLine($"{KeyDecoderMemberName} = {KeyDecoderParameterName} => {witness.Decoder},");
        indent.AppendLine($"{KeyJsonReaderMemberName} = ({KeyJsonReaderJsonParameterName}, {KeyJsonReaderContextParameterName}) => {witness.JsonReadExpr},");
        indent.Dedent();
        indent.AppendLine("};");
        indent.Dedent();
        indent.AppendLine();
    }

    [LoggerMessage(
        EventId = 1300,
        Level = LogLevel.Warning,
        Message = "Template {ModuleName}:{TemplateName} has a field {FieldName} whose C# member is named Key, so the contract-key witness is emitted as an explicit IHasKey implementation only. Reach it through a generic constraint on IHasKey, not through the template type directly.")]
    private static partial void LogKeyFieldTakesTheWitnessName(ILogger logger, string moduleName, string templateName, string fieldName);

    [LoggerMessage(
        EventId = 1301,
        Level = LogLevel.Warning,
        Message = "Template {ModuleName}:{TemplateName} maps to a C# type named Key, which a member may not share, so the contract-key witness is emitted as an explicit IHasKey implementation only. Reach it through a generic constraint on IHasKey, not through the template type directly.")]
    private static partial void LogTemplateNameTakesTheWitnessName(ILogger logger, string moduleName, string templateName);

    [LoggerMessage(
        EventId = 1302,
        Level = LogLevel.Warning,
        Message = "Template {ModuleName}:{TemplateName} has a field {FieldName} whose C# member is named Choices, so the choices witness is emitted as an explicit IHasChoices implementation only. Reach it through a generic constraint on IHasChoices, not through the template type directly.")]
    private static partial void LogChoicesFieldTakesTheWitnessName(ILogger logger, string moduleName, string templateName, string fieldName);

    [LoggerMessage(
        EventId = 1303,
        Level = LogLevel.Warning,
        Message = "Template {ModuleName}:{TemplateName} maps to a C# type named Choices, which a member may not share, so the choices witness is emitted as an explicit IHasChoices implementation only. Reach it through a generic constraint on IHasChoices, not through the template type directly.")]
    private static partial void LogTemplateNameTakesTheChoicesWitnessName(ILogger logger, string moduleName, string templateName);

    [LoggerMessage(
        EventId = 1304,
        Level = LogLevel.Warning,
        Message = "Template {ModuleName}:{TemplateName} has a non-Unit choice named {ChoiceName} whose nested argument type emits a record named Choices inside the template partial, so the choices witness is emitted as an explicit IHasChoices implementation only. Reach it through a generic constraint on IHasChoices, not through the template type directly.")]
    private static partial void LogNestedChoiceArgTakesTheChoicesWitnessName(ILogger logger, string moduleName, string templateName, string choiceName);
}
