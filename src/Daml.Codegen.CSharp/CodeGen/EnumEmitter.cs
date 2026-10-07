// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Emits the C# for a Daml enum: the <c>enum</c> declaration itself plus the
/// <c>&lt;Enum&gt;Extensions</c> static class carrying the <c>ToDamlEnum</c> /
/// <c>FromDamlEnum</c> serialization round-trip. Constructed once per package over
/// the package's <see cref="PackageEmitContext"/> and the shared
/// <see cref="CodeGenOptions"/>. The caller owns the file scaffold and the common
/// usings; this emitter writes the enum body into the provided
/// <see cref="IndentWriter"/>.
/// </summary>
internal sealed class EnumEmitter(CodeGenOptions options)
{
    /// <summary>
    /// Writes the enum declaration and its serialization extension class for
    /// <paramref name="dataType"/> into <paramref name="indent"/>.
    /// </summary>
    internal void WriteEnumType(IndentWriter indent, PackageEmitContext context, DamlDataType dataType, DamlEnumDefinition enumDef)
    {
        indent.Require("System");
        var enumName = EmitterHelpers.SanitizeIdentifier(dataType.Name);
        var enumReference = context.QualifyInModule(enumName);

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Generated from Daml enum {dataType.Name}");
            indent.AppendLine("/// </summary>");
        }

        indent.AppendLine($"public enum {enumName}");
        indent.AppendLine("{");
        indent.Indent();

        foreach (var ctor in enumDef.Constructors)
        {
            if (options.GenerateXmlDocs)
            {
                indent.AppendLine($"/// <summary>{ctor} enum constructor.</summary>");
            }
            indent.AppendLine($"{EmitterHelpers.SanitizeIdentifier(ctor)},");
        }

        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Extension methods for {enumName} serialization.");
            indent.AppendLine("/// </summary>");
        }
        indent.AppendLine($"public static class {enumName}Extensions");
        indent.AppendLine("{");
        indent.Indent();

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Converts to a DamlEnum value.</summary>");
        }
        indent.AppendLine($"public static {TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlEnum)} ToDamlEnum(this {enumReference} value)");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine("return value switch");
        indent.AppendLine("{");
        indent.Indent();
        foreach (var ctor in enumDef.Constructors)
        {
            indent.AppendLine($"{enumReference}.{EmitterHelpers.SanitizeIdentifier(ctor)} => {TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlEnum)}.Create(\"{ctor}\"),");
        }
        indent.AppendLine("_ => throw new global::System.ArgumentOutOfRangeException(nameof(value), value, null)");
        indent.Dedent();
        indent.AppendLine("};");
        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Creates an instance from a DamlEnum value.</summary>");
        }
        indent.AppendLine($"public static {enumReference} FromDamlEnum({TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlEnum)} value)");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine("return value.Constructor switch");
        indent.AppendLine("{");
        indent.Indent();
        foreach (var ctor in enumDef.Constructors)
        {
            indent.AppendLine($"\"{ctor}\" => {enumReference}.{EmitterHelpers.SanitizeIdentifier(ctor)},");
        }
        indent.AppendLine("_ => throw new global::System.ArgumentOutOfRangeException(nameof(value), value.Constructor, null)");
        indent.Dedent();
        indent.AppendLine("};");
        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();

        WriteReadDamlLfJsonMethod(indent, enumName, enumDef);

        indent.Dedent();
        indent.AppendLine("}");
    }

    /// <summary>
    /// Writes the static <c>__ReadDamlLfJson</c> method decoding Daml-LF JSON directly into a
    /// <c>DamlEnum</c> for <paramref name="enumDef"/>'s constructors, without going through
    /// reflection, plus the private static <c>ExpectedConstructors</c> field (constructor order)
    /// it reports unknown tags against. <c>DamlLfJsonDecoders.ReadEnumConstructor</c> already
    /// validates the wire constructor string against the known set and produces the
    /// <c>DamlEnum</c>, so this composes no further decode logic of its own — matching how
    /// <c>ToDamlEnum</c> is a plain dispatch rather than owning any encode logic either. Not part
    /// of <c>IDamlRecord&lt;TSelf&gt;</c>/<c>IDamlVariant&lt;TSelf&gt;</c>: an enum has no such
    /// marker interface, so <c>DamlTypeMapper.FromJson</c> dispatches straight to this static
    /// method by name (see <c>QualifiedEnumExtensionsCall</c>), the same way <c>ToDamlEnum</c>/
    /// <c>FromDamlEnum</c> are already called without one.
    /// </summary>
    private void WriteReadDamlLfJsonMethod(IndentWriter indent, string enumName, DamlEnumDefinition enumDef)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Decodes a Daml-LF JSON enum constructor directly into a DamlEnum, without going through reflection.</summary>");
        }

        indent.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        indent.AppendLine($"public static {TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlEnum)} __ReadDamlLfJson(global::System.Text.Json.JsonElement json, {DamlTypeMapper.DamlLfJsonDecodeContextQualifiedName} context) =>");
        indent.Indent();
        indent.AppendLine($"{DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.ReadEnumConstructor(json, context, ExpectedConstructors);");
        indent.Dedent();
        indent.AppendLine();

        var expectedConstructors = string.Join(", ", enumDef.Constructors.Select(ctor => $"\"{ctor}\""));
        indent.AppendLine($"private static readonly string[] ExpectedConstructors = [{expectedConstructors}];");
        indent.AppendLine();
    }
}
