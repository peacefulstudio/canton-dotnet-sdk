// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Identifier and type-parameter helpers shared across the per-type emitters
/// (<see cref="RecordEmitter"/>, <see cref="VariantEmitter"/>,
/// <see cref="EnumEmitter"/>) and the <see cref="CSharpCodeGenerator"/> file
/// scaffold. Pure functions over <see cref="Identifiers"/> so every emitter
/// sanitises names and declares type parameters identically.
/// </summary>
internal static class EmitterHelpers
{
    internal static string SanitizeIdentifier(string name) => Identifiers.Sanitize(name);

    internal static string ToPascalCase(string name) => Identifiers.ToPascalCase(name);

    /// <summary>
    /// Escapes <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, <c>'</c> and <c>"</c> for embedding in
    /// generated XML — an emitted <c>/// &lt;summary&gt;</c> doc comment or a <c>.csproj</c>
    /// element — so interpolated text that itself contains those characters (a constructed
    /// generic type name such as <c>IReadOnlyDictionary&lt;string, long&gt;</c>, a package
    /// description, a license expression) cannot break the surrounding markup.
    /// </summary>
    internal static string EscapeXmlText(string value) =>
        System.Security.SecurityElement.Escape(value) ?? string.Empty;

    /// <summary>
    /// Derives the C# type-parameter name for a Daml type variable: PascalCased and
    /// sanitised behind a <c>T</c> prefix. The <c>T</c> prefix combined with PascalCasing
    /// makes a keyword collision unreachable — all C# reserved keywords are lowercase, so
    /// <c>T</c> + an uppercase-initial identifier can never be one. The name is therefore
    /// deliberately built from <see cref="Identifiers.SanitizeBare"/> — escaping first
    /// would emit <c>T@event</c>, which parses as two identifiers rather than one.
    /// </summary>
    internal static string TypeParameterName(string damlTypeParam) =>
        $"T{ToPascalCase(Identifiers.SanitizeBare(damlTypeParam))}";

    internal static string GetTypeParametersDeclaration(IReadOnlyList<string> typeParams)
    {
        if (typeParams.Count == 0)
            return string.Empty;

        return $"<{string.Join(", ", typeParams.Select(TypeParameterName))}>";
    }

    /// <summary>
    /// Builds the <c>where T : notnull</c> clauses trailing a generic record's or variant's
    /// declaration; empty for a non-generic type. A Daml type variable ranges only over
    /// serialisable Daml types, none of which is nullable — the nullable positions are exactly
    /// the ones Daml spells <c>Optional</c>, which <see cref="DamlTypeMapper"/> renders as an
    /// explicit <c>?</c> or as a wrapper rather than as a bare type variable. Left
    /// unconstrained, a field typed by the type variable reports a nullable read-state through
    /// <see cref="System.Reflection.NullabilityInfoContext"/> at every reference-type
    /// instantiation, so a reflection-driven reader decodes it as an <c>Optional</c> the wire
    /// never carried.
    /// </summary>
    internal static string GetTypeParameterConstraints(IReadOnlyList<string> typeParams)
    {
        if (typeParams.Count == 0)
            return string.Empty;

        return string.Concat(typeParams.Select(param => $" where {TypeParameterName(param)} : notnull"));
    }

    /// <summary>
    /// Derives the injected converter-delegate parameter name for a Daml type
    /// variable — the delegate a generic record/variant's <c>ToRecord</c>/<c>FromRecord</c>
    /// (or <c>ToVariant</c>/<c>FromVariant</c>) accepts to bridge that type parameter's
    /// concrete CLR argument to and from <see cref="Daml.Runtime.Data.DamlValue"/>.
    /// </summary>
    internal static string ConverterParameterName(string damlTypeParam) =>
        $"convert{TypeParameterName(damlTypeParam)}";

    /// <summary>
    /// Maps each Daml type-variable name to its <see cref="ConverterParameterName"/>,
    /// threaded into the <see cref="DamlTypeMapper"/>'s <c>ToValue</c>/<c>FromValue</c>
    /// so a <see cref="DamlTypeVar"/> field resolves to its injected converter.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ConverterNameMap(IReadOnlyList<string> typeParams) =>
        typeParams.ToDictionary(param => param, ConverterParameterName);

    /// <summary>
    /// Derives the injected <c>DamlLfElementReader</c> parameter name for a Daml type
    /// variable — the reader a generic record's or variant's emitted <c>__ReadDamlLfJson</c>
    /// accepts to decode that type parameter's carried JSON, the JSON-side twin of
    /// <see cref="ConverterParameterName"/>.
    /// </summary>
    internal static string ReaderParameterName(string damlTypeParam) =>
        $"read{TypeParameterName(damlTypeParam)}";

    /// <summary>
    /// Maps each Daml type-variable name to its <see cref="ReaderParameterName"/>, threaded into
    /// the <see cref="DamlTypeMapper"/>'s <c>FromJson</c> as its <c>typeVarReaders</c> argument so
    /// a <see cref="DamlTypeVar"/> field resolves to its injected reader, the JSON-side twin of
    /// <see cref="ConverterNameMap"/>.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ReaderNameMap(IReadOnlyList<string> typeParams) =>
        typeParams.ToDictionary(param => param, ReaderParameterName);

    /// <summary>
    /// Builds the <c>DamlLfElementReader</c> parameter list for a generic record's or variant's
    /// <c>__ReadDamlLfJson</c>, one per type parameter, named per <see cref="ReaderParameterName"/> —
    /// the JSON-side twin of <see cref="SerializeConverterParameters"/>/<see cref="DeserializeConverterParameters"/>.
    /// </summary>
    internal static string JsonReaderParameters(IReadOnlyList<string> typeParams, string qualifiedDamlLfElementReader) =>
        string.Join(", ", typeParams.Select(param => $"{qualifiedDamlLfElementReader} {ReaderParameterName(param)}"));

    /// <summary>
    /// Derives the <c>DamlValue?</c> parameter name that carries what an omitted field of a
    /// type-parameter type reads as, next to <see cref="ConverterParameterName"/> and
    /// <see cref="ReaderParameterName"/>: the call site passes <c>DamlOptional.None</c> when the
    /// instantiation is an <c>Optional</c>, and <c>null</c> when the field is required.
    /// </summary>
    internal static string AbsenceParameterName(string damlTypeParam) =>
        $"absent{TypeParameterName(damlTypeParam)}";

    /// <summary>
    /// The key under which <see cref="ConverterNameMapWithAbsences"/> and <see cref="ReaderNameMapWithAbsences"/>
    /// hold a type variable's <see cref="AbsenceParameterName"/>. A Daml type variable name never
    /// contains a colon, so the key cannot collide with a type variable's own entry.
    /// </summary>
    internal static string AbsenceKey(string damlTypeParam) =>
        $"absent:{damlTypeParam}";

    /// <summary>
    /// <see cref="ConverterNameMap"/> for a generic record or variant's own <c>FromRecord</c> or <c>FromVariant</c>, which also takes
    /// one absence parameter per type parameter, found under <see cref="AbsenceKey"/>.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ConverterNameMapWithAbsences(IReadOnlyList<string> typeParams) =>
        WithAbsenceParameters(ConverterNameMap(typeParams), typeParams);

    /// <summary>
    /// <see cref="ReaderNameMap"/> for a generic record or variant's own <c>__ReadDamlLfJson</c>, which also
    /// takes one absence parameter per type parameter, found under <see cref="AbsenceKey"/>.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ReaderNameMapWithAbsences(IReadOnlyList<string> typeParams) =>
        WithAbsenceParameters(ReaderNameMap(typeParams), typeParams);

    private static IReadOnlyDictionary<string, string> WithAbsenceParameters(
        IReadOnlyDictionary<string, string> names, IReadOnlyList<string> typeParams) =>
        names
            .Concat(typeParams.Select(param => KeyValuePair.Create(AbsenceKey(param), AbsenceParameterName(param))))
            .ToDictionary();

    /// <summary>
    /// Builds a generic record or variant's <c>FromRecord</c> or <c>FromVariant</c> converter parameter list: for each type parameter,
    /// the converter delegate followed by its <see cref="AbsenceParameterName"/>.
    /// </summary>
    internal static string DeserializeConverterParametersWithAbsences(IReadOnlyList<string> typeParams, string qualifiedDamlValue) =>
        string.Join(", ", typeParams.Select(param =>
            $"{TypeReferenceQualifier.Qualify("Func")}<{qualifiedDamlValue}, {TypeParameterName(param)}> {ConverterParameterName(param)}, "
            + $"{qualifiedDamlValue}? {AbsenceParameterName(param)}"));

    /// <summary>
    /// Builds a generic record or variant's <c>__ReadDamlLfJson</c> reader parameter list: for each type
    /// parameter, the <c>DamlLfElementReader</c> followed by its <see cref="AbsenceParameterName"/>.
    /// </summary>
    internal static string JsonReaderParametersWithAbsences(
        IReadOnlyList<string> typeParams, string qualifiedDamlLfElementReader, string qualifiedDamlValue) =>
        string.Join(", ", typeParams.Select(param =>
            $"{qualifiedDamlLfElementReader} {ReaderParameterName(param)}, {qualifiedDamlValue}? {AbsenceParameterName(param)}"));

    internal const string AbsenceParametersDoc =
        "Each <c>absent</c> argument is what an omitted field of that type parameter reads as: "
        + "<c>DamlOptional.None</c> when the instantiation is an <c>Optional</c>, <c>null</c> when it is required.";

    internal static string SerializeConverterParameters(IReadOnlyList<string> typeParams, string qualifiedDamlValue) =>
        string.Join(", ", typeParams.Select(param =>
            $"{TypeReferenceQualifier.Qualify("Func")}<{TypeParameterName(param)}, {qualifiedDamlValue}> {ConverterParameterName(param)}"));

    internal static string DeserializeConverterParameters(IReadOnlyList<string> typeParams, string qualifiedDamlValue) =>
        string.Join(", ", typeParams.Select(param =>
            $"{TypeReferenceQualifier.Qualify("Func")}<{qualifiedDamlValue}, {TypeParameterName(param)}> {ConverterParameterName(param)}"));

    internal static void WriteTypeParamDocs(IndentWriter indent, IReadOnlyList<string> typeParams)
    {
        foreach (var param in typeParams)
        {
            indent.AppendLine($"/// <typeparam name=\"{TypeParameterName(param)}\">Type parameter {param}</typeparam>");
        }
    }
}
