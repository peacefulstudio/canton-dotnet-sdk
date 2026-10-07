// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using RuntimeNamespaces = Daml.Runtime.RuntimeNamespaces;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Emits the record-serialization surface shared by every field-bearing C# type:
/// the primary-constructor parameters and the <c>ToRecord</c> / <c>FromRecord</c>
/// round-trip. The same emitter feeds all three consumers — plain records
/// (<see cref="RecordEmitter"/>), templates, and nested choice-argument records —
/// so their serialization output stays byte-identical.
/// Constructed once per package over the package's <see cref="PackageEmitContext"/>,
/// the DAR-scoped <see cref="DarCrossPackageResolver"/>, the shared
/// <see cref="CodeGenOptions"/>, and the package's <see cref="DamlTypeMapper"/>.
/// </summary>
internal sealed class RecordSerializationEmitter(
    PackageEmitContext context,
    DarCrossPackageResolver resolver,
    CodeGenOptions options,
    DamlTypeMapper mapper)
{
    private readonly CollectionValueSemanticsEmitter _valueSemantics = new(options);

    /// <summary>
    /// Writes the primary-constructor parameters for <paramref name="fields"/> into
    /// <paramref name="indent"/>, one per line and indented one level, leaving the writer
    /// at the start of the line that closes the parameter list. The caller has already
    /// written the opening parenthesis and writes the closing one, so the closing
    /// parenthesis and any base list land on their own line.
    /// </summary>
    internal void WriteRecordParameters(
        IndentWriter indent,
        IReadOnlyList<DamlFieldDefinition> fields)
    {
        var members = ValueMembers(indent, fields);
        var redeclaresProperties = CollectionValueSemanticsEmitter.NeedsValueSemantics(members);

        indent.AppendLine();
        indent.Indent();
        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var member = members[i];
            StdlibPackages.RequireForFieldType(resolver, context.Package, indent, field.Type);
            var separator = i == fields.Count - 1 ? "" : ",";
            var attribute = redeclaresProperties
                ? string.Empty
                : $"[property: {TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlFieldAttribute)}(\"{field.Name}\")] ";
            indent.AppendLine($"{attribute}{member.CSharpType} {member.Name}{separator}");
        }
        indent.Dedent();
    }

    /// <summary>
    /// Writes the copying properties, <c>Equals</c> and <c>GetHashCode</c> that give
    /// <paramref name="selfType"/> value semantics over its collection-typed fields, or nothing
    /// when it carries none.
    /// </summary>
    /// <param name="indent">Writer positioned at the top of the record body.</param>
    /// <param name="selfType">The record's own type, including type parameters.</param>
    /// <param name="fields">The record's Daml fields, in declaration order.</param>
    internal void WriteCollectionValueSemantics(
        IndentWriter indent,
        string selfType,
        IReadOnlyList<DamlFieldDefinition> fields) =>
        _valueSemantics.Write(indent, selfType, ValueMembers(indent, fields), derivesFromRecord: false);

    private IReadOnlyList<ValueMember> ValueMembers(IndentWriter indent, IReadOnlyList<DamlFieldDefinition> fields) =>
    [
        .. fields.Select(field => new ValueMember(
            MemberName(field.Name, indent.CurrentTypeName, indent.CurrentReservedMemberNames),
            mapper.MapType(field.Type),
            mapper.ClassifyCollection(field.Type),
            $"The Daml field <c>{field.Name}</c>.",
            field.Name)),
    ];

    /// <summary>
    /// Writes the <c>ToRecord</c> method that serializes <paramref name="fields"/> to a
    /// DamlRecord into <paramref name="indent"/>. When <paramref name="typeParams"/> is
    /// non-empty the method accepts one <c>Func&lt;T, DamlValue&gt;</c> converter per type
    /// parameter, and type-variable fields serialize through the matching converter.
    /// </summary>
    internal void WriteToRecordMethod(
        IndentWriter indent,
        IReadOnlyList<DamlFieldDefinition> fields,
        IReadOnlyList<string> typeParams)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Converts this value to a DamlRecord.</summary>");
        }

        var parameters = ConverterParameters(indent, typeParams, EmitterHelpers.SerializeConverterParameters);
        var delegates = EmitterHelpers.ConverterNameMap(typeParams);
        var damlRecordRef = TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlRecord);

        if (fields.Count == 0)
        {
            indent.AppendLine($"public {damlRecordRef} ToRecord({parameters}) => {damlRecordRef}.Create();");
            indent.AppendLine();
            return;
        }

        indent.AppendLine($"public {damlRecordRef} ToRecord({parameters}) => {damlRecordRef}.Create(");
        indent.Indent();

        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var fieldName = MemberName(field.Name, indent.CurrentTypeName, indent.CurrentReservedMemberNames);
            var conversion = mapper.ToValue(field.Type, fieldName, delegates);
            var comma = i < fields.Count - 1 ? "," : "";
            StdlibPackages.RequireForFieldType(resolver, context.Package, indent, field.Type);

            indent.AppendLine($"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlField)}.Create(\"{field.Name}\", {conversion}){comma}");
        }

        indent.Dedent();
        indent.AppendLine(");");
        indent.AppendLine();
    }

    /// <summary>
    /// Writes the static <c>FromRecord</c> factory that reconstructs a
    /// <paramref name="className"/> instance from a DamlRecord into
    /// <paramref name="indent"/>.
    /// </summary>
    internal void WriteFromRecordMethod(
        IndentWriter indent,
        string className,
        IReadOnlyList<DamlFieldDefinition> fields,
        IReadOnlyList<string> typeParams)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine($"/// <summary>Creates an instance from a DamlRecord.{AbsenceDocSentence(typeParams)}</summary>");
        }

        var converterParameters = ConverterParameters(indent, typeParams, EmitterHelpers.DeserializeConverterParametersWithAbsences);
        var parameters = $"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlRecord)} record{Prefixed(converterParameters)}";
        var delegates = EmitterHelpers.ConverterNameMapWithAbsences(typeParams);

        if (fields.Count == 0)
        {
            indent.AppendLine($"public static {className} FromRecord({parameters}) => new {className}();");
            indent.AppendLine();
            return;
        }

        foreach (var field in fields)
        {
            StdlibPackages.RequireForFieldType(resolver, context.Package, indent, field.Type);
        }

        indent.AppendLine($"public static {className} FromRecord({parameters}) => new {className}(");
        indent.Indent();

        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var fieldName = MemberName(field.Name, indent.CurrentTypeName, indent.CurrentReservedMemberNames);
            var conversion = FieldFromValue(field, delegates);
            var comma = i < fields.Count - 1 ? "," : "";

            indent.AppendLine($"{fieldName}: {conversion}{comma}");
        }

        indent.Dedent();
        indent.AppendLine(");");
        indent.AppendLine();
    }

    private const string PresentElement = "present";

    /// <summary>
    /// Writes the static <c>__ReadDamlLfJson</c> method that decodes Daml-LF JSON directly into a
    /// <c>DamlRecord</c> for <paramref name="fields"/>, without going through reflection.
    /// Implements <see cref="Daml.Runtime.Data.IDamlRecord{TSelf}.__ReadDamlLfJson"/> for a
    /// non-generic record (<paramref name="typeParams"/> empty); a generic record cannot
    /// implement that interface at all — see <see cref="RecordEmitter.InterfaceDeclaration"/> —
    /// so it gets a plain static method of the same name instead, taking one
    /// <c>DamlLfElementReader</c> per type parameter and called directly by name rather than
    /// through the interface, the shape
    /// <see cref="DamlTypeMapper.FromJson(DamlType, string, string, IReadOnlyDictionary{string, string})"/>
    /// emits for an instantiated generic record.
    /// </summary>
    internal void WriteReadDamlLfJsonMethod(
        IndentWriter indent,
        IReadOnlyList<DamlFieldDefinition> fields,
        IReadOnlyList<string> typeParams)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine($"/// <summary>Decodes a Daml-LF JSON record directly into a DamlRecord, without going through reflection.{AbsenceDocSentence(typeParams)}</summary>");
        }

        var readerParameters = typeParams.Count == 0
            ? string.Empty
            : EmitterHelpers.JsonReaderParametersWithAbsences(
                typeParams,
                DamlTypeMapper.DamlLfElementReaderQualifiedName,
                TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlValue));
        var typeVarReaders = EmitterHelpers.ReaderNameMapWithAbsences(typeParams);
        var damlRecordRef = TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlRecord);
        var parameters = "global::System.Text.Json.JsonElement json, "
            + $"{DamlTypeMapper.DamlLfJsonDecodeContextQualifiedName} context{Prefixed(readerParameters)}";

        indent.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        indent.AppendLine($"public static {damlRecordRef} __ReadDamlLfJson({parameters})");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"{DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.RequireObject(json, context);");

        if (fields.Count == 0)
        {
            indent.AppendLine($"return {damlRecordRef}.Create();");
            indent.Dedent();
            indent.AppendLine("}");
            indent.AppendLine();
            return;
        }

        foreach (var field in fields)
        {
            StdlibPackages.RequireForFieldType(resolver, context.Package, indent, field.Type);
        }

        if (fields.Any(field => IsAddedConditionally(field, typeVarReaders)))
        {
            WriteConditionalFieldsReturn(indent, fields, typeVarReaders, damlRecordRef);
        }
        else
        {
            WriteFixedArityFieldsReturn(indent, fields, typeVarReaders, damlRecordRef);
        }

        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();
    }

    private bool IsAddedConditionally(DamlFieldDefinition field, IReadOnlyDictionary<string, string> typeVarReaders) =>
        IsTypeParameterField(field, typeVarReaders) || IsDeclaredOptional(field);

    private static bool IsTypeParameterField(
        DamlFieldDefinition field, IReadOnlyDictionary<string, string> typeVarReaders) =>
        field.Type is DamlTypeVar typeVar && typeVarReaders.ContainsKey(typeVar.Name);

    private bool IsDeclaredOptional(DamlFieldDefinition field) =>
        field.Type is not DamlTypeVar && mapper.OptionalWireEncoding(field.Type) is not null;

    private static string AbsenceDocSentence(IReadOnlyList<string> typeParams) =>
        typeParams.Count == 0 ? string.Empty : $" {EmitterHelpers.AbsenceParametersDoc}";

    private void WriteFixedArityFieldsReturn(
        IndentWriter indent,
        IReadOnlyList<DamlFieldDefinition> fields,
        IReadOnlyDictionary<string, string> typeVarReaders,
        string damlRecordRef)
    {
        indent.AppendLine($"return {damlRecordRef}.Create(");
        indent.Indent();

        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var decoded = FieldFromJson(field, typeVarReaders);
            var comma = i < fields.Count - 1 ? "," : "";

            indent.AppendLine($"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlField)}.Create(\"{field.Name}\", {decoded}){comma}");
        }

        indent.Dedent();
        indent.AppendLine(");");
    }

    private void WriteConditionalFieldsReturn(
        IndentWriter indent,
        IReadOnlyList<DamlFieldDefinition> fields,
        IReadOnlyDictionary<string, string> typeVarReaders,
        string damlRecordRef)
    {
        var damlFieldRef = TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlField);
        indent.AppendLine($"var fields = new global::System.Collections.Generic.List<{damlFieldRef}>({fields.Count});");

        foreach (var field in fields)
        {
            if (field.Type is DamlTypeVar typeVar && typeVarReaders.TryGetValue(typeVar.Name, out var reader))
            {
                indent.AppendLine(
                    $"{DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.AddTypeParameterField(fields, json, context, \"{field.Name}\", "
                    + $"{reader}, {typeVarReaders[EmitterHelpers.AbsenceKey(typeVar.Name)]});");
            }
            else if (IsDeclaredOptional(field))
            {
                var decoded = mapper.FromJson(
                    field.Type, PresentElement, $"context.Field(\"{field.Name}\")", typeVarReaders);
                indent.AppendLine(
                    $"{DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.AddFieldIfPresent(fields, json, \"{field.Name}\", {PresentElement} => {decoded});");
            }
            else
            {
                indent.AppendLine($"fields.Add({damlFieldRef}.Create(\"{field.Name}\", {FieldFromJson(field, typeVarReaders)}));");
            }
        }

        indent.AppendLine($"return {damlRecordRef}.Create(fields.ToArray());");
    }

    private string FieldFromJson(
        DamlFieldDefinition field,
        IReadOnlyDictionary<string, string> typeVarReaders)
    {
        var decoders = DamlTypeMapper.DamlLfJsonDecodersQualifiedName;
        var fieldName = $"\"{field.Name}\"";
        return mapper.FromJson(
            field.Type,
            $"{decoders}.RequireField(json, context, {fieldName})",
            $"context.Field({fieldName})",
            typeVarReaders);
    }

    private string FieldFromValue(
        DamlFieldDefinition field,
        IReadOnlyDictionary<string, string> converters)
    {
        var fieldName = $"\"{field.Name}\"";
        if (field.Type is DamlTypeVar typeVar && converters.TryGetValue(typeVar.Name, out var convert))
        {
            return $"record.GetTypeParameterField({fieldName}, {convert}, {converters[EmitterHelpers.AbsenceKey(typeVar.Name)]})";
        }

        var valueExpr = mapper.OptionalWireEncoding(field.Type) is { } encoding
            ? $"record.{OptionalRecordAccessor(encoding)}({fieldName})"
            : $"record.GetRequiredField({fieldName})";
        return mapper.FromValue(field.Type, valueExpr, converters);
    }

#pragma warning disable CS8524
    private static string OptionalRecordAccessor(OptionalEncoding encoding) => encoding switch
    {
        OptionalEncoding.Flat => "GetOptionalField",
        OptionalEncoding.NestedChain => "GetOptionalChainField",
    };
#pragma warning restore CS8524

    private static string ConverterParameters(
        IndentWriter indent,
        IReadOnlyList<string> typeParams,
        Func<IReadOnlyList<string>, string, string> build)
    {
        if (typeParams.Count == 0)
        {
            return string.Empty;
        }

        indent.Require("System");
        return build(typeParams, TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlValue));
    }

    private static string Prefixed(string parameters) =>
        string.IsNullOrEmpty(parameters) ? string.Empty : $", {parameters}";

    private static string MemberName(string damlFieldName, string enclosingTypeName, IReadOnlySet<string> reservedMemberNames) =>
        Identifiers.MemberName(damlFieldName, enclosingTypeName, reservedMemberNames);
}
