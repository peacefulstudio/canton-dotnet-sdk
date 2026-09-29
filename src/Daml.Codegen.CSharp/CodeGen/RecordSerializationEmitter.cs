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
/// the DAR-scoped <see cref="ICrossPackageResolver"/>, the shared
/// <see cref="CodeGenOptions"/>, and the package's <see cref="DamlTypeMapper"/>.
/// </summary>
internal sealed class RecordSerializationEmitter(
    PackageEmitContext context,
    ICrossPackageResolver resolver,
    CodeGenOptions options,
    DamlTypeMapper mapper)
{
    private readonly CollectionValueSemanticsEmitter _valueSemantics = new(context, options);

    /// <summary>
    /// Writes the primary-constructor parameters for <paramref name="fields"/> into
    /// <paramref name="indent"/>, one per line and indented one level, leaving the writer
    /// at the start of the line that closes the parameter list. The caller has already
    /// written the opening parenthesis and writes the closing one, so the closing
    /// parenthesis and any base list land on their own line.
    /// <paramref name="nestedArgTypeNames"/> carries the names
    /// <see cref="ChoiceEmitter.GetNestedChoiceArgumentTypeNames"/> resolved for the enclosing
    /// template's choices, so a same-package choice-argument record nested inside the template
    /// partial that happens to be named <c>DamlFieldAttribute</c> gets root-qualified instead of
    /// shadowing the runtime <see cref="Daml.Runtime.Data.DamlFieldAttribute"/>; null qualifies
    /// through the ordinary <see cref="TypeReferenceQualifier"/>.
    /// </summary>
    internal void WriteRecordParameters(
        IndentWriter indent,
        IReadOnlyList<DamlFieldDefinition> fields,
        IReadOnlySet<string>? nestedArgTypeNames = null)
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
                : $"[property: {DamlFieldAttributeSyntax(field.Name, nestedArgTypeNames)}] ";
            indent.AppendLine($"{attribute}{member.CSharpType} {member.Name}{separator}");
        }
        indent.Dedent();
    }

    /// <summary>
    /// Writes the copying properties, <c>Equals</c> and <c>GetHashCode</c> that give
    /// <paramref name="selfType"/> value semantics over its collection-typed fields, or nothing
    /// when it carries none. See <see cref="WriteRecordParameters"/> for
    /// <paramref name="nestedArgTypeNames"/>; the same root-qualification applies to each
    /// redeclared property's <c>DamlFieldAttribute</c> here.
    /// </summary>
    /// <param name="indent">Writer positioned at the top of the record body.</param>
    /// <param name="selfType">The record's own type, including type parameters.</param>
    /// <param name="fields">The record's Daml fields, in declaration order.</param>
    /// <param name="nestedArgTypeNames">
    /// See <see cref="WriteRecordParameters"/>; the same root-qualification applies to each
    /// redeclared property's <c>DamlFieldAttribute</c> here.
    /// </param>
    internal void WriteCollectionValueSemantics(
        IndentWriter indent,
        string selfType,
        IReadOnlyList<DamlFieldDefinition> fields,
        IReadOnlySet<string>? nestedArgTypeNames = null) =>
        _valueSemantics.Write(indent, selfType, ValueMembers(indent, fields), derivesFromRecord: false, nestedArgTypeNames);

    private IReadOnlyList<ValueMember> ValueMembers(IndentWriter indent, IReadOnlyList<DamlFieldDefinition> fields) =>
    [
        .. fields.Select(field => new ValueMember(
            MemberName(field.Name, indent.CurrentTypeName),
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
    /// <paramref name="nestedArgTypeNames"/> carries the names
    /// <see cref="ChoiceEmitter.GetNestedChoiceArgumentTypeNames"/> resolved for the enclosing
    /// template's choices, so a same-package choice-argument record nested inside the template
    /// partial that happens to be named <c>DamlRecord</c> or <c>DamlField</c> gets root-qualified
    /// instead of shadowing the runtime <see cref="Daml.Runtime.Data.DamlRecord"/> or
    /// <see cref="Daml.Runtime.Data.DamlField"/> respectively; null qualifies through the ordinary
    /// <see cref="TypeReferenceQualifier"/>.
    /// </summary>
    internal void WriteToRecordMethod(
        IndentWriter indent,
        IReadOnlyList<DamlFieldDefinition> fields,
        IReadOnlyList<string> typeParams,
        IReadOnlySet<string>? nestedArgTypeNames = null)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Converts this value to a DamlRecord.</summary>");
        }

        var parameters = ConverterParameters(indent, typeParams, EmitterHelpers.SerializeConverterParameters);
        var delegates = EmitterHelpers.ConverterNameMap(typeParams);
        var damlRecordRef = DamlRecordReference(nestedArgTypeNames);

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
            var fieldName = MemberName(field.Name, indent.CurrentTypeName);
            var conversion = mapper.ToValue(field.Type, fieldName, delegates);
            var comma = i < fields.Count - 1 ? "," : "";
            StdlibPackages.RequireForFieldType(resolver, context.Package, indent, field.Type);

            indent.AppendLine($"{DamlFieldReference(nestedArgTypeNames)}.Create(\"{field.Name}\", {conversion}){comma}");
        }

        indent.Dedent();
        indent.AppendLine(");");
        indent.AppendLine();
    }

    /// <summary>
    /// Writes the static <c>FromRecord</c> factory that reconstructs a
    /// <paramref name="className"/> instance from a DamlRecord into
    /// <paramref name="indent"/>. See <see cref="WriteToRecordMethod"/> for
    /// <paramref name="nestedArgTypeNames"/>; the same root-qualification applies to the
    /// <c>record</c> parameter's type here.
    /// </summary>
    internal void WriteFromRecordMethod(
        IndentWriter indent,
        string className,
        IReadOnlyList<DamlFieldDefinition> fields,
        IReadOnlyList<string> typeParams,
        IReadOnlySet<string>? nestedArgTypeNames = null)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Creates an instance from a DamlRecord.</summary>");
        }

        var converterParameters = ConverterParameters(indent, typeParams, EmitterHelpers.DeserializeConverterParameters);
        var parameters = $"{DamlRecordReference(nestedArgTypeNames)} record{Prefixed(converterParameters)}";
        var delegates = EmitterHelpers.ConverterNameMap(typeParams);

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
            var fieldName = MemberName(field.Name, indent.CurrentTypeName);
            var conversion = FieldFromValue(field, delegates, nestedArgTypeNames);
            var comma = i < fields.Count - 1 ? "," : "";

            indent.AppendLine($"{fieldName}: {conversion}{comma}");
        }

        indent.Dedent();
        indent.AppendLine(");");
        indent.AppendLine();
    }

    /// <summary>
    /// Writes the static <c>__ReadDamlLfJson</c> method that decodes Daml-LF JSON directly into a
    /// <c>DamlRecord</c> for <paramref name="fields"/>, without going through reflection.
    /// Implements <see cref="Daml.Runtime.Data.IDamlRecord{TSelf}.__ReadDamlLfJson"/> for a
    /// non-generic record (<paramref name="typeParams"/> empty); a generic record cannot
    /// implement that interface at all — see <see cref="RecordEmitter.InterfaceDeclaration"/> —
    /// so it gets a plain static method of the same name instead, taking one
    /// <c>DamlLfElementReader</c> per type parameter and called directly by name rather than
    /// through the interface, the shape
    /// <see cref="DamlTypeMapper.FromJson(DamlType, string, string, IReadOnlySet{string}, IReadOnlyDictionary{string, string})"/>
    /// emits for an instantiated generic record. See <see cref="WriteToRecordMethod"/> for
    /// <paramref name="nestedArgTypeNames"/>.
    /// </summary>
    internal void WriteReadDamlLfJsonMethod(
        IndentWriter indent,
        IReadOnlyList<DamlFieldDefinition> fields,
        IReadOnlyList<string> typeParams,
        IReadOnlySet<string>? nestedArgTypeNames = null)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Decodes a Daml-LF JSON record directly into a DamlRecord, without going through reflection.</summary>");
        }

        var readerParameters = typeParams.Count == 0
            ? string.Empty
            : EmitterHelpers.JsonReaderParameters(typeParams, DamlTypeMapper.DamlLfElementReaderQualifiedName);
        var typeVarReaders = EmitterHelpers.ReaderNameMap(typeParams);
        var damlRecordRef = DamlRecordReference(nestedArgTypeNames);
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

        indent.AppendLine($"return {damlRecordRef}.Create(");
        indent.Indent();

        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            StdlibPackages.RequireForFieldType(resolver, context.Package, indent, field.Type);
            var decoded = FieldFromJson(field, nestedArgTypeNames, typeVarReaders);
            var comma = i < fields.Count - 1 ? "," : "";

            indent.AppendLine($"{DamlFieldReference(nestedArgTypeNames)}.Create(\"{field.Name}\", {decoded}){comma}");
        }

        indent.Dedent();
        indent.AppendLine(");");
        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();
    }

    private string FieldFromJson(
        DamlFieldDefinition field,
        IReadOnlySet<string>? nestedArgTypeNames,
        IReadOnlyDictionary<string, string> typeVarReaders)
    {
        var decoders = DamlTypeMapper.DamlLfJsonDecodersQualifiedName;
        var fieldName = $"\"{field.Name}\"";
        if (field.Type is DamlTypeVar typeVar && typeVarReaders.TryGetValue(typeVar.Name, out var reader))
        {
            return $"{decoders}.ReadTypeParameterField(json, context, {fieldName}, {reader})";
        }

        var jsonExpr = mapper.OptionalWireEncoding(field.Type) is { } encoding
            ? $"{decoders}.{OmissibleFieldAccessor(encoding)}(json, {fieldName})"
            : $"{decoders}.RequireField(json, context, {fieldName})";
        return mapper.FromJson(field.Type, jsonExpr, $"context.Field({fieldName})", nestedArgTypeNames, typeVarReaders);
    }

    private string FieldFromValue(
        DamlFieldDefinition field,
        IReadOnlyDictionary<string, string> converters,
        IReadOnlySet<string>? nestedArgTypeNames)
    {
        var fieldName = $"\"{field.Name}\"";
        if (field.Type is DamlTypeVar typeVar && converters.TryGetValue(typeVar.Name, out var convert))
        {
            return $"record.GetTypeParameterField({fieldName}, {convert})";
        }

        var valueExpr = mapper.OptionalWireEncoding(field.Type) is { } encoding
            ? $"record.Get{OmissibleFieldAccessor(encoding)}({fieldName})"
            : $"record.GetRequiredField({fieldName})";
        return mapper.FromValue(field.Type, valueExpr, converters, nestedArgTypeNames);
    }

#pragma warning disable CS8524
    private static string OmissibleFieldAccessor(OptionalEncoding encoding) => encoding switch
    {
        OptionalEncoding.Flat => "OptionalField",
        OptionalEncoding.NestedChain => "OptionalChainField",
    };
#pragma warning restore CS8524

    private string ConverterParameters(
        IndentWriter indent,
        IReadOnlyList<string> typeParams,
        Func<IReadOnlyList<string>, string, string> build)
    {
        if (typeParams.Count == 0)
        {
            return string.Empty;
        }

        indent.Require("System");
        return build(typeParams, context.Qualifier.Qualify(RuntimeTypeNames.DamlValue));
    }

    private static string Prefixed(string parameters) =>
        string.IsNullOrEmpty(parameters) ? string.Empty : $", {parameters}";

    private string DamlRecordReference(IReadOnlySet<string>? nestedArgTypeNames) =>
        nestedArgTypeNames?.Contains(RuntimeTypeNames.DamlRecord) == true
            ? Identifiers.GlobalQualified(RuntimeNamespaces.Data, RuntimeTypeNames.DamlRecord)
            : context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord);

    private string DamlFieldReference(IReadOnlySet<string>? nestedArgTypeNames) =>
        nestedArgTypeNames?.Contains(RuntimeTypeNames.DamlField) == true
            ? Identifiers.GlobalQualified(RuntimeNamespaces.Data, RuntimeTypeNames.DamlField)
            : context.Qualifier.Qualify(RuntimeTypeNames.DamlField);

    private string DamlFieldAttributeSyntax(string damlFieldName, IReadOnlySet<string>? nestedArgTypeNames) =>
        nestedArgTypeNames?.Contains(RuntimeTypeNames.DamlFieldAttribute) == true
            ? $"{Identifiers.GlobalQualified(RuntimeNamespaces.Data, RuntimeTypeNames.DamlFieldAttribute)}(\"{damlFieldName}\")"
            : $"{context.Qualifier.Qualify(RuntimeTypeNames.DamlFieldAttribute)}(\"{damlFieldName}\")";

    private static string MemberName(string damlFieldName, string enclosingTypeName) =>
        Identifiers.MemberName(damlFieldName, enclosingTypeName);
}
