// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

internal sealed class VariantEmitter(
    PackageEmitContext context,
    ICrossPackageResolver resolver,
    CodeGenOptions options,
    DamlTypeMapper mapper)
{
    private readonly CollectionValueSemanticsEmitter _valueSemantics = new(context, options);

    internal void WriteVariantType(IndentWriter indent, DamlDataType dataType, DamlVariantDefinition variant)
    {
        indent.Require("System");
        var className = EmitterHelpers.SanitizeIdentifier(dataType.Name);
        var typeParams = EmitterHelpers.GetTypeParametersDeclaration(dataType.TypeParams);
        var typeParamConstraints = EmitterHelpers.GetTypeParameterConstraints(dataType.TypeParams);
        var fullClassName = $"{className}{typeParams}";

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Generated from Daml variant {dataType.Name}");
            if (dataType.TypeParams.Count > 0)
            {
                indent.AppendLine($"/// Type parameters: {string.Join(", ", dataType.TypeParams)}");
            }
            indent.AppendLine("/// </summary>");
            EmitterHelpers.WriteTypeParamDocs(indent, dataType.TypeParams);
        }

        var qualifiedDamlValue = context.Qualifier.Qualify(RuntimeTypeNames.DamlValue);
        var toVariantParameters = EmitterHelpers.SerializeConverterParameters(dataType.TypeParams, qualifiedDamlValue);
        var fromVariantConverters = EmitterHelpers.DeserializeConverterParameters(dataType.TypeParams, qualifiedDamlValue);
        var fromVariantParameters = $"{context.Qualifier.Qualify(RuntimeTypeNames.DamlVariant)} variant{Prefixed(fromVariantConverters)}";
        var delegates = EmitterHelpers.ConverterNameMap(dataType.TypeParams);

        var variantInterface = InterfaceDeclaration(dataType.TypeParams, className);
        indent.AppendLine("[global::System.Text.Json.Serialization.JsonConverter(typeof(global::Daml.Runtime.Serialization.DamlVariantJsonConverterFactory))]");
        indent.AppendLine($"public abstract record {fullClassName}{variantInterface}{typeParamConstraints}");
        indent.AppendLine("{");
        indent.Indent();

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Gets the variant constructor name.</summary>");
        }
        indent.AppendLine("public abstract string Tag { get; }");
        indent.AppendLine();

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Converts to a DamlVariant.</summary>");
        }
        indent.AppendLine($"public abstract {context.Qualifier.Qualify(RuntimeTypeNames.DamlVariant)} ToVariant({toVariantParameters});");
        indent.AppendLine();

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine($"/// <summary>Reconstructs {IndefiniteArticleFor(className)} {className} by dispatching on the DamlVariant constructor tag.</summary>");
        }
        indent.AppendLine($"public static {fullClassName} FromVariant({fromVariantParameters}) =>");
        indent.Indent();
        indent.AppendLine("variant.Constructor switch");
        indent.AppendLine("{");
        indent.Indent();
        foreach (var ctor in variant.Constructors)
        {
            var ctorName = VariantConstructorName(ctor.Name, className);
            if (HasVariantPayload(ctor))
            {
                indent.AppendLine($"\"{ctor.Name}\" => new {ctorName}({mapper.FromValue(ctor.ArgumentType!, "variant.Value", delegates)}),");
            }
            else
            {
                indent.AppendLine($"\"{ctor.Name}\" => new {ctorName}(),");
            }
        }
        indent.AppendLine($"_ => throw new ArgumentOutOfRangeException(nameof(variant), variant.Constructor, \"Unknown {className} constructor\")");
        indent.Dedent();
        indent.AppendLine("};");
        indent.Dedent();
        indent.AppendLine();

        WriteReadDamlLfJsonMethod(indent, dataType, variant);

        foreach (var ctor in variant.Constructors)
        {
            var ctorName = VariantConstructorName(ctor.Name, className);
            var hasArg = HasVariantPayload(ctor);
            var argType = hasArg ? mapper.MapType(ctor.ArgumentType!) : null;

            if (argType is not null)
            {
                StdlibPackages.RequireForFieldType(resolver, context.Package, indent, ctor.ArgumentType!);
                if (options.GenerateXmlDocs)
                {
                    indent.AppendLine($"/// <summary>{ctor.Name} constructor.</summary>");
                }
                indent.AppendLine($"public sealed record {ctorName}({argType} Value) : {fullClassName}");
            }
            else
            {
                if (options.GenerateXmlDocs)
                {
                    indent.AppendLine($"/// <summary>{ctor.Name} constructor (no arguments).</summary>");
                }
                indent.AppendLine($"public sealed record {ctorName}() : {fullClassName}");
            }

            indent.AppendLine("{");
            indent.Indent();

            if (argType is not null)
            {
                _valueSemantics.Write(
                    indent,
                    ctorName,
                    [new ValueMember(
                        "Value",
                        argType,
                        mapper.ClassifyCollection(ctor.ArgumentType!),
                        $"The payload the {ctor.Name} constructor carries.",
                        DamlFieldName: null)],
                    derivesFromRecord: true);
            }

            if (options.GenerateXmlDocs)
            {
                indent.AppendLine("/// <inheritdoc />");
            }
            indent.AppendLine($"public override string Tag => \"{ctor.Name}\";");
            indent.AppendLine();
            var payload = hasArg
                ? mapper.ToValue(ctor.ArgumentType!, "Value", delegates)
                : $"{context.Qualifier.Qualify(RuntimeTypeNames.DamlUnit)}.Instance";
            if (options.GenerateXmlDocs)
            {
                indent.AppendLine("/// <inheritdoc />");
            }
            indent.AppendLine($"public override {context.Qualifier.Qualify(RuntimeTypeNames.DamlVariant)} ToVariant({toVariantParameters}) => {context.Qualifier.Qualify(RuntimeTypeNames.DamlVariant)}.Create(\"{ctor.Name}\", {payload});");

            indent.Dedent();
            indent.AppendLine("}");
            indent.AppendLine();
        }

        indent.Dedent();
        indent.AppendLine("}");
    }

    private string InterfaceDeclaration(IReadOnlyList<string> typeParams, string className) =>
        typeParams.Count == 0
            ? $" : {context.Qualifier.Qualify(RuntimeTypeNames.IDamlVariant)}<{className}>"
            : string.Empty;

    private void WriteReadDamlLfJsonMethod(IndentWriter indent, DamlDataType dataType, DamlVariantDefinition variant)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Decodes a Daml-LF JSON variant directly into a DamlVariant, without going through reflection.</summary>");
        }

        var className = EmitterHelpers.SanitizeIdentifier(dataType.Name);
        var reservedNames = variant.Constructors
            .Select(ctor => VariantConstructorName(ctor.Name, className))
            .ToHashSet(StringComparer.Ordinal);
        reservedNames.Add(className);
        reservedNames.UnionWith(context.Qualifier.DeclaredTypeNames);
        var constructorsFieldName = "ExpectedConstructors";
        while (reservedNames.Contains(constructorsFieldName))
            constructorsFieldName += "_";

        var readerParameters = dataType.TypeParams.Count == 0
            ? string.Empty
            : EmitterHelpers.JsonReaderParameters(dataType.TypeParams, DamlTypeMapper.DamlLfElementReaderQualifiedName);
        var typeVarReaders = EmitterHelpers.ReaderNameMap(dataType.TypeParams);
        var damlVariantRef = context.Qualifier.Qualify(RuntimeTypeNames.DamlVariant);
        var parameters = "global::System.Text.Json.JsonElement json, "
            + $"{DamlTypeMapper.DamlLfJsonDecodeContextQualifiedName} context{Prefixed(readerParameters)}";

        indent.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        indent.AppendLine($"public static {damlVariantRef} __ReadDamlLfJson({parameters})");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"var tag = {DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.ReadVariantTag(json, context);");
        indent.AppendLine("return tag switch");
        indent.AppendLine("{");
        indent.Indent();

        var valueExpr = $"{DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.RequireVariantValue(json, context)";
        var valueContextExpr = "context.Field(\"value\")";

        foreach (var ctor in variant.Constructors)
        {
            if (HasVariantPayload(ctor))
            {
                StdlibPackages.RequireForFieldType(resolver, context.Package, indent, ctor.ArgumentType!);
            }
            var payload = HasVariantPayload(ctor)
                ? mapper.FromJson(ctor.ArgumentType!, valueExpr, valueContextExpr, typeVarReaders: typeVarReaders)
                : $"{DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.ReadUnit({valueExpr}, {valueContextExpr})";
            indent.AppendLine($"\"{ctor.Name}\" => {damlVariantRef}.Create(\"{ctor.Name}\", {payload}),");
        }

        indent.AppendLine($"_ => throw {DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.UnknownConstructor(\"variant constructor\", tag, context, {constructorsFieldName})");
        indent.Dedent();
        indent.AppendLine("};");
        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();

        var expectedConstructors = string.Join(", ", variant.Constructors.Select(ctor => $"\"{ctor.Name}\""));
        indent.AppendLine($"private static readonly string[] {constructorsFieldName} = [{expectedConstructors}];");
        indent.AppendLine();
    }

    private static string Prefixed(string parameters) =>
        string.IsNullOrEmpty(parameters) ? string.Empty : $", {parameters}";

    private static bool HasVariantPayload(DamlVariantConstructor ctor) =>
        ctor.ArgumentType is not null
        && ctor.ArgumentType is not DamlPrimitiveType { Primitive: DamlPrimitive.Unit };

    private static string IndefiniteArticleFor(string name) =>
        name.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(name[0])) ? "an" : "a";

    private static string VariantConstructorName(string ctorName, string enclosingTypeName) =>
        Identifiers.Disambiguate(EmitterHelpers.SanitizeIdentifier(ctorName), enclosingTypeName);
}
