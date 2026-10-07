// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Emits the C# for a Daml record: the sealed record declaration. The field-bearing
/// serialization surface (constructor parameters,
/// <c>ToRecord</c> / <c>FromRecord</c>) is delegated to the shared
/// <see cref="RecordSerializationEmitter"/> so record, template, and nested
/// choice-argument output stay byte-identical. Constructed once per package over
/// the package's <see cref="PackageEmitContext"/>, the shared
/// <see cref="CodeGenOptions"/>, and the package's
/// <see cref="RecordSerializationEmitter"/>. The caller owns the file scaffold and
/// the common usings; this emitter writes the record body into the provided
/// <see cref="IndentWriter"/>.
/// </summary>
internal sealed class RecordEmitter(
    PackageEmitContext context,
    CodeGenOptions options,
    RecordSerializationEmitter serialization)
{
    /// <summary>
    /// Writes the record declaration and its serialization round-trip for
    /// <paramref name="dataType"/> into <paramref name="indent"/>.
    /// </summary>
    internal void WriteRecordType(IndentWriter indent, DamlModule module, DamlDataType dataType, DamlRecordDefinition record)
    {
        var className = context.EmittedTypeName(module.Name, dataType.Name);
        indent.CurrentTypeName = className;
        indent.CurrentReservedMemberNames = ReservedMemberNames.OfRecordField();
        var typeParams = EmitterHelpers.GetTypeParametersDeclaration(dataType.TypeParams);
        var typeParamConstraints = EmitterHelpers.GetTypeParameterConstraints(dataType.TypeParams);
        var fullClassName = $"{className}{typeParams}";

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Generated from Daml record {dataType.Name}");
            if (dataType.TypeParams.Count > 0)
            {
                indent.AppendLine($"/// Type parameters: {string.Join(", ", dataType.TypeParams)}");
            }
            indent.AppendLine("/// </summary>");
            EmitterHelpers.WriteTypeParamDocs(indent, dataType.TypeParams);
        }

        var recordInterface = InterfaceDeclaration(module, dataType, className);

        if (record.Fields.Count > 0)
        {
            indent.Append($"public sealed record {fullClassName}(");
            serialization.WriteRecordParameters(indent, record.Fields);
            indent.AppendLine($"){recordInterface}{typeParamConstraints}");
        }
        else
        {
            indent.AppendLine($"public sealed record {fullClassName}{recordInterface}{typeParamConstraints}");
        }

        indent.AppendLine("{");
        indent.Indent();

        serialization.WriteCollectionValueSemantics(indent, fullClassName, record.Fields);
        serialization.WriteToRecordMethod(indent, record.Fields, dataType.TypeParams);
        serialization.WriteFromRecordMethod(indent, fullClassName, record.Fields, dataType.TypeParams);
        serialization.WriteReadDamlLfJsonMethod(indent, record.Fields, dataType.TypeParams);

        indent.Dedent();
        indent.AppendLine("}");
    }

    /// <summary>
    /// Returns the record's interface clause: for a non-generic record,
    /// <c>: IDamlRecord&lt;T&gt;</c> with <c>T</c> naming the record itself (which extends
    /// the plain <c>IDamlRecord</c>), preceded by the marker name of the single local
    /// interface declaring the record as its view type (see
    /// <see cref="PackageEmitContext.LocalViewRecordMarkerNames"/>) so a view answers with
    /// its interface's identity through the marker's inherited statics; empty for generic
    /// records, whose <c>ToRecord</c> and <c>FromRecord</c> take one converter delegate
    /// per type parameter and so can satisfy neither the parameterless
    /// <c>IDamlRecord.ToRecord()</c> contract nor the static abstract
    /// <c>IDamlRecord&lt;TSelf&gt;.FromRecord(DamlRecord)</c> factory — matching the
    /// hand-written stdlib generics (Set, Tuple2, NonEmpty).
    /// </summary>
    private string InterfaceDeclaration(DamlModule module, DamlDataType dataType, string className)
    {
        if (dataType.TypeParams.Count > 0)
        {
            return string.Empty;
        }

        var recordFacet = $"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.IDamlRecord)}<{className}>";
        return context.LocalViewRecordMarkerNames.TryGetValue($"{module.Name}:{dataType.Name}", out var marker)
            ? $" : {marker}, {recordFacet}"
            : $" : {recordFacet}";
    }
}
