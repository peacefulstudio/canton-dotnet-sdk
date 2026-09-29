// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Serialization;

/// <summary>
/// Reads one already-decoded element of a composite LF-JSON value — the payload of a
/// <c>List</c>, <c>Optional</c>, <c>TextMap</c>, <c>GenMap</c>, tuple component, <c>Either</c>
/// arm, or similar — at the position described by the supplied <see cref="DamlLfJsonDecodeContext"/>.
/// </summary>
/// <remarks>
/// Every leaf entry point on <see cref="DamlLfJsonDecoders"/> — <see cref="DamlLfJsonDecoders.ReadInt64(JsonElement, DamlLfJsonDecodeContext)"/>,
/// <see cref="DamlLfJsonDecoders.ReadText(JsonElement, DamlLfJsonDecodeContext)"/>, and so on — matches this delegate's shape by
/// method-group conversion, so it can be passed directly wherever an element reader is expected.
/// </remarks>
public delegate DamlValue DamlLfElementReader(JsonElement json, DamlLfJsonDecodeContext context);

/// <summary>
/// Composable building blocks for decoding LF-JSON against an explicit shape. Generated code
/// composes these to decode every Daml value, including one whose element optionality a bare
/// CLR type cannot carry — a generic Daml type family such as <c>List</c>, <c>Optional</c>,
/// <c>TextMap</c>, <c>GenMap</c>, a tuple, an <c>Either</c>, or a choice or key type built from them.
/// </summary>
/// <remarks>
/// Every reader here returns a <see cref="DamlValue"/> — the same intermediate representation
/// <see cref="DamlLfJsonReader"/> produces — for the generated <c>FromValue</c>/<c>FromRecord</c>
/// conversions to consume. <see cref="DamlLfJsonReader"/> is the top-level front end that applies
/// the decode limits to a whole document; this is the shared decoding core.
/// </remarks>
public static class DamlLfJsonDecoders
{
    private const string VariantTagKey = "tag";
    private const string VariantValueKey = "value";
    internal const string VariantConstructorKind = "variant constructor";
    internal const string EnumConstructorKind = "enum constructor";
    internal const string GenMapContainerName = "GenMap";
    internal const string StdlibSetContainerName = "Set";
    internal const string StdlibMapContainerName = "Map";
    private const string StdlibMapFieldLabel = "map";
    private const string NonEmptyHeadFieldLabel = "hd";
    private const string NonEmptyTailFieldLabel = "tl";
    private const int MaximumEchoedValueLength = 64;

    private static readonly string[] StdlibTupleFieldLabels = ["_1", "_2", "_3"];
    private static readonly string[] EitherConstructors = ["Left", "Right"];
    private static readonly JsonElement OmittedFlatOptional = ParseConstant("null");
    private static readonly JsonElement OmittedOptionalChain = ParseConstant("[]");

    /// <summary>Decodes a Daml <c>Int64</c> leaf value.</summary>
    /// <exception cref="JsonException">The JSON is not a canonical Daml Int64 wire string.</exception>
    public static DamlInt64 ReadInt64(JsonElement json, DamlLfJsonDecodeContext context) =>
        ReadInt64(json, context.Path);

    /// <summary>Decodes a Daml <c>Numeric</c> leaf value.</summary>
    /// <exception cref="JsonException">The JSON is not a canonical Daml Numeric wire string.</exception>
    public static DamlNumeric ReadNumeric(JsonElement json, DamlLfJsonDecodeContext context) =>
        ReadNumeric(json, context.Path);

    /// <summary>Decodes a Daml <c>Text</c> leaf value.</summary>
    /// <exception cref="JsonException">The JSON is not a string.</exception>
    public static DamlText ReadText(JsonElement json, DamlLfJsonDecodeContext context) =>
        new(ReadWireString(json, context.Path));

    /// <summary>Decodes a Daml <c>Bool</c> leaf value.</summary>
    /// <exception cref="JsonException">The JSON is not a boolean.</exception>
    public static DamlBool ReadBool(JsonElement json, DamlLfJsonDecodeContext context) =>
        ReadBool(json, context.Path);

    /// <summary>Decodes a Daml <c>Party</c> leaf value.</summary>
    /// <exception cref="JsonException">The JSON is not a string.</exception>
    public static DamlParty ReadParty(JsonElement json, DamlLfJsonDecodeContext context) =>
        new(ReadWireString(json, context.Path));

    /// <summary>Decodes a Daml <c>ContractId</c> leaf value.</summary>
    /// <exception cref="JsonException">The JSON is not a string.</exception>
    public static DamlContractId ReadContractId(JsonElement json, DamlLfJsonDecodeContext context) =>
        new(ReadWireString(json, context.Path));

    /// <summary>Decodes a Daml <c>Date</c> leaf value.</summary>
    /// <exception cref="JsonException">The JSON is not a canonical Daml Date wire string.</exception>
    public static DamlDate ReadDate(JsonElement json, DamlLfJsonDecodeContext context) =>
        ReadDate(json, context.Path);

    /// <summary>Decodes a Daml <c>Timestamp</c> leaf value.</summary>
    /// <exception cref="JsonException">The JSON is not a canonical Daml Timestamp wire string.</exception>
    public static DamlTimestamp ReadTimestamp(JsonElement json, DamlLfJsonDecodeContext context) =>
        ReadTimestamp(json, context.Path);

    /// <summary>Decodes a Daml <c>Unit</c> leaf value.</summary>
    /// <exception cref="JsonException">The JSON is not an object, or is a non-empty object.</exception>
    public static DamlUnit ReadUnit(JsonElement json, DamlLfJsonDecodeContext context) =>
        ReadUnit(json, context.Path);

    /// <summary>
    /// Decodes a Daml enum from its bare wire constructor string, admitting only the constructors
    /// named in <paramref name="expectedConstructors"/>.
    /// </summary>
    /// <exception cref="JsonException">The JSON is not a string, or names no expected constructor.</exception>
    public static DamlEnum ReadEnumConstructor(
        JsonElement json, DamlLfJsonDecodeContext context, IReadOnlyList<string> expectedConstructors)
    {
        var constructor = ReadWireString(json, context.Path);
        return expectedConstructors.Contains(constructor, StringComparer.Ordinal)
            ? DamlEnum.Create(constructor)
            : throw UnknownConstructor(EnumConstructorKind, constructor, context, expectedConstructors);
    }

    /// <summary>
    /// The decoder for a Daml type the emitter could not resolve to a reader — an open type variable
    /// that escaped its generic body, or an unresolved interface-choice payload. Throws when, and only
    /// when, the decode actually reaches this leaf: an unselected variant arm, an empty collection and
    /// an absent <c>Optional</c> never invoke it.
    /// </summary>
    /// <param name="json">The JSON that reached this leaf; never inspected.</param>
    /// <param name="context">The decode context; its path names the leaf in the message.</param>
    /// <param name="damlType">The Daml type the emitter could not resolve, in its wire spelling.</param>
    /// <exception cref="NotSupportedException">Always.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DamlValue ReadUnsupported(
        JsonElement json, DamlLfJsonDecodeContext context, string damlType) =>
        throw new NotSupportedException(
            $"Daml type '{damlType}' at '{context.Path}' lies outside the emitted Daml-LF JSON decoders");

    /// <summary>Guards that <paramref name="json"/> is a JSON object, returning it unchanged.</summary>
    /// <exception cref="JsonException"><paramref name="json"/> is not a JSON object.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static JsonElement RequireObject(JsonElement json, DamlLfJsonDecodeContext context) =>
        json.ValueKind == JsonValueKind.Object
            ? json
            : throw ShapeMismatch(context.Path, JsonValueKind.Object, json.ValueKind);

    /// <summary>
    /// Reads the required field named <paramref name="field"/> from the JSON object
    /// <paramref name="json"/>, which the caller has already guarded as an object.
    /// </summary>
    /// <exception cref="JsonException"><paramref name="json"/> carries no <paramref name="field"/> property.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static JsonElement RequireField(JsonElement json, DamlLfJsonDecodeContext context, string field) =>
        json.TryGetProperty(field, out var value)
            ? value
            : throw MissingRecordField($"{context.Path}.{field}");

    /// <summary>
    /// Reads the flat-<c>Optional</c> field named <paramref name="field"/> from the JSON object
    /// <paramref name="json"/>, which the caller has already guarded as an object. An omitted field
    /// reads as JSON <c>null</c>, the wire form of <c>None</c>: the JSON Ledger API leaves a record's
    /// trailing <c>None</c> fields out, and a payload written before a Daml upgrade added a trailing
    /// <c>Optional</c> field carries no key for it.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static JsonElement OptionalField(JsonElement json, string field) =>
        json.TryGetProperty(field, out var value) ? value : OmittedFlatOptional;

    /// <summary>
    /// Reads the nested-<c>Optional</c> field named <paramref name="field"/> — one decoded by
    /// <see cref="ReadOptionalChain(JsonElement, DamlLfJsonDecodeContext, DamlLfElementReader)"/> — from
    /// the JSON object <paramref name="json"/>, which the caller has already guarded as an object. An
    /// omitted field reads as <c>[]</c>, the chain's wire form of <c>None</c>; an explicit
    /// <c>null</c> is returned unchanged, so the chain reader still rejects it.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static JsonElement OptionalChainField(JsonElement json, string field) =>
        json.TryGetProperty(field, out var value) ? value : OmittedOptionalChain;

    /// <summary>
    /// Decodes the field named <paramref name="field"/>, whose Daml type is a type parameter, from the
    /// JSON object <paramref name="json"/> with the instantiation's <paramref name="reader"/>. An
    /// omitted field decodes as the <c>None</c> the Ledger API left out at a flat or a nested
    /// <c>Optional</c> instantiation, and is reported missing at any other instantiation.
    /// </summary>
    /// <exception cref="JsonException">The field is malformed, or is omitted at an instantiation that is not an <c>Optional</c>.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DamlValue ReadTypeParameterField(
        JsonElement json, DamlLfJsonDecodeContext context, string field, DamlLfElementReader reader)
    {
        var fieldContext = context.Field(field);
        return ReadOmissibleField(json, field, fieldContext.Path, (element, _) => reader(element, fieldContext));
    }

    /// <summary>Guards that <paramref name="json"/> is a JSON object, then decodes its variant tag.</summary>
    /// <exception cref="JsonException"><paramref name="json"/> is not a JSON object, or carries no <c>tag</c> string.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static string ReadVariantTag(JsonElement json, DamlLfJsonDecodeContext context)
    {
        RequireObject(json, context);
        return ReadVariantTag(json, context.Path);
    }

    /// <summary>
    /// Reads the selected arm's <c>value</c> member from the JSON object <paramref name="json"/>,
    /// which the caller has already guarded as an object (typically via <see cref="ReadVariantTag(JsonElement, DamlLfJsonDecodeContext)"/>).
    /// </summary>
    /// <exception cref="JsonException"><paramref name="json"/> carries no <c>value</c> property.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static JsonElement RequireVariantValue(JsonElement json, DamlLfJsonDecodeContext context) =>
        json.TryGetProperty(VariantValueKey, out var value)
            ? value
            : throw MissingVariantMember(context.Path, VariantValueKey);

    /// <summary>Builds the exception for a wire constructor naming none of a type's known constructors.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static JsonException UnknownConstructor(
        string kind, string name, DamlLfJsonDecodeContext context, IReadOnlyList<string> known) =>
        UnknownConstructor(kind, name, context.Path, known);

    /// <summary>
    /// Decodes a generated Daml record through its own emitted <c>__ReadDamlLfJson</c>.
    /// </summary>
    /// <exception cref="JsonException">The JSON does not match the shape of <typeparamref name="T"/>, or a decode limit is exceeded.</exception>
    public static DamlRecord ReadRecord<T>(JsonElement json, DamlLfJsonDecodeContext context)
        where T : IDamlRecord<T> =>
        T.__ReadDamlLfJson(json, context);

    /// <summary>
    /// Decodes a generated Daml variant through its own emitted <c>__ReadDamlLfJson</c>.
    /// </summary>
    /// <exception cref="JsonException">The JSON does not match the shape of <typeparamref name="T"/>, or a decode limit is exceeded.</exception>
    public static DamlVariant ReadVariant<T>(JsonElement json, DamlLfJsonDecodeContext context)
        where T : IDamlVariant<T> =>
        T.__ReadDamlLfJson(json, context);

    /// <summary>Decodes a Daml <c>List</c>, applying <paramref name="elementReader"/> to each element.</summary>
    /// <exception cref="JsonException">The JSON is not an array, or exceeds the configured array-breadth limit.</exception>
    public static DamlList ReadList(JsonElement json, DamlLfJsonDecodeContext context, DamlLfElementReader elementReader) =>
        ReadList(json, (element, path) => elementReader(element, context.Nested(path)), context.Limits, context.Path);

    /// <summary>
    /// Decodes a flat Daml <c>Optional</c> — JSON <c>null</c> for <c>None</c>, or the bare
    /// carried value for <c>Some</c> — applying <paramref name="elementReader"/> when present.
    /// </summary>
    public static DamlOptional ReadOptional(JsonElement json, DamlLfJsonDecodeContext context, DamlLfElementReader elementReader) =>
        ReadOptional(json, (element, path) => elementReader(element, context.Nested(path)), context.Path);

    /// <summary>
    /// Decodes one level of a nested Daml <c>Optional</c> chain — the array-of-at-most-one-element
    /// wire form every level below the outermost uses (<c>[]</c> for absent, <c>[v]</c> for
    /// present) — applying <paramref name="elementReader"/> to the carried element when present.
    /// Compose calls to this method to decode <c>Optional (Optional a)</c> and deeper: the
    /// element reader for an outer level is itself a call to this method for the level it carries.
    /// </summary>
    /// <exception cref="JsonException">The JSON is not an array, or is an array of more than one element.</exception>
    public static DamlOptionalChain ReadOptionalChain(JsonElement json, DamlLfJsonDecodeContext context, DamlLfElementReader elementReader) =>
        ReadOptionalChain(json, (element, path) => elementReader(element, context.Nested(path)), context.Path);

    /// <summary>Decodes a Daml <c>TextMap</c>, applying <paramref name="valueReader"/> to each entry value.</summary>
    /// <exception cref="JsonException">The JSON is not an object, exceeds the configured entry-count limit, or carries a duplicate key.</exception>
    public static DamlTextMap ReadTextMap(JsonElement json, DamlLfJsonDecodeContext context, DamlLfElementReader valueReader) =>
        ReadTextMap(json, (value, path) => valueReader(value, context.Nested(path)), context.Limits, context.Path);

    /// <summary>
    /// Decodes a Daml <c>GenMap</c>, applying <paramref name="keyReader"/> and
    /// <paramref name="valueReader"/> to each entry's key and value.
    /// </summary>
    /// <exception cref="JsonException">The JSON is not an array of two-element entries, exceeds the configured array-breadth limit, or carries a duplicate key.</exception>
    public static DamlGenMap ReadGenMap(
        JsonElement json, DamlLfJsonDecodeContext context, DamlLfElementReader keyReader, DamlLfElementReader valueReader) =>
        ReadGenMapEntries(
            json,
            (key, path) => keyReader(key, context.Nested(path)),
            (value, path) => valueReader(value, context.Nested(path)),
            GenMapContainerName,
            context.Limits,
            context.Path);

    /// <summary>
    /// Decodes a stdlib <c>Set</c> — a record wrapping a <c>GenMap</c> from element to <c>Unit</c> —
    /// applying <paramref name="elementReader"/> to each element.
    /// </summary>
    /// <exception cref="JsonException">The JSON does not match a Set's wire shape, exceeds the configured array-breadth limit, or carries a duplicate element.</exception>
    public static DamlRecord ReadSet(JsonElement json, DamlLfJsonDecodeContext context, DamlLfElementReader elementReader)
    {
        var (entries, entriesPath) = ReadStdlibMapField(json, context.Path);
        var entriesContext = context.Nested(entriesPath);
        return WrapStdlibMap(ReadGenMapEntries(
            entries,
            (key, path) => elementReader(key, entriesContext.Nested(path)),
            (value, path) => ReadUnit(value, path),
            StdlibSetContainerName,
            context.Limits,
            entriesPath));
    }

    /// <summary>
    /// Decodes a stdlib <c>Map</c> — a record wrapping a <c>GenMap</c> — applying
    /// <paramref name="keyReader"/> and <paramref name="valueReader"/> to each entry.
    /// </summary>
    /// <exception cref="JsonException">The JSON does not match a Map's wire shape, exceeds the configured array-breadth limit, or carries a duplicate key.</exception>
    public static DamlRecord ReadStdlibMap(
        JsonElement json, DamlLfJsonDecodeContext context, DamlLfElementReader keyReader, DamlLfElementReader valueReader)
    {
        var (entries, entriesPath) = ReadStdlibMapField(json, context.Path);
        var entriesContext = context.Nested(entriesPath);
        return WrapStdlibMap(ReadGenMapEntries(
            entries,
            (key, path) => keyReader(key, entriesContext.Nested(path)),
            (value, path) => valueReader(value, entriesContext.Nested(path)),
            StdlibMapContainerName,
            context.Limits,
            entriesPath));
    }

    /// <summary>
    /// Decodes a stdlib <c>NonEmpty</c> — a record with a head element and a tail list — applying
    /// <paramref name="elementReader"/> to the head and to each tail element.
    /// </summary>
    /// <exception cref="JsonException">The JSON does not match a NonEmpty's wire shape, or its tail exceeds the configured array-breadth limit.</exception>
    public static DamlRecord ReadNonEmpty(JsonElement json, DamlLfJsonDecodeContext context, DamlLfElementReader elementReader) =>
        ReadNonEmpty(
            json,
            (element, path) => elementReader(element, context.Nested(path)),
            (element, path) => elementReader(element, context.Field(NonEmptyTailFieldLabel).Nested(path)),
            context.Limits,
            context.Path);

    /// <summary>Decodes a stdlib <c>Tuple2</c>, applying a reader to each of its two components.</summary>
    /// <exception cref="JsonException">The JSON does not match a Tuple2's wire shape.</exception>
    public static DamlRecord ReadTuple2(
        JsonElement json, DamlLfJsonDecodeContext context, DamlLfElementReader reader1, DamlLfElementReader reader2) =>
        ReadTuple(
            json,
            [
                (element, path) => reader1(element, context.Nested(path)),
                (element, path) => reader2(element, context.Nested(path))
            ],
            context.Path);

    /// <summary>Decodes a stdlib <c>Tuple3</c>, applying a reader to each of its three components.</summary>
    /// <exception cref="JsonException">The JSON does not match a Tuple3's wire shape.</exception>
    public static DamlRecord ReadTuple3(
        JsonElement json,
        DamlLfJsonDecodeContext context,
        DamlLfElementReader reader1,
        DamlLfElementReader reader2,
        DamlLfElementReader reader3) =>
        ReadTuple(
            json,
            [
                (element, path) => reader1(element, context.Nested(path)),
                (element, path) => reader2(element, context.Nested(path)),
                (element, path) => reader3(element, context.Nested(path))
            ],
            context.Path);

    /// <summary>
    /// Decodes a stdlib <c>Either</c> — a variant tagged <c>Left</c> or <c>Right</c> — applying
    /// <paramref name="leftReader"/> or <paramref name="rightReader"/> to the carried payload.
    /// </summary>
    /// <exception cref="JsonException">The JSON does not match Either's wire shape, or its tag is not <c>Left</c> or <c>Right</c>.</exception>
    public static DamlVariant ReadEither(
        JsonElement json, DamlLfJsonDecodeContext context, DamlLfElementReader leftReader, DamlLfElementReader rightReader) =>
        ReadEither(
            json,
            (element, path) => leftReader(element, context.Nested(path)),
            (element, path) => rightReader(element, context.Nested(path)),
            context.Path);

    internal static string ReadWireString(JsonElement json, string path) =>
        json.ValueKind == JsonValueKind.String
            ? json.GetString()!
            : throw ShapeMismatch(path, JsonValueKind.String, json.ValueKind);

    internal static DamlBool ReadBool(JsonElement json, string path) =>
        json.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? new DamlBool(json.GetBoolean())
            : throw ShapeMismatch(path, "boolean", json.ValueKind);

    internal static DamlInt64 ReadInt64(JsonElement json, string path)
    {
        var raw = ReadWireString(json, path);
        return DamlJsonSerializer.MatchesCanonicalIntegerGrammar(raw)
            && long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? new DamlInt64(value)
            : throw MalformedScalar(path, raw, "Int64");
    }

    internal static DamlNumeric ReadNumeric(JsonElement json, string path)
    {
        var raw = ReadWireString(json, path);
        return DamlNumeric.TryParseCanonical(raw, out var numeric)
            ? numeric
            : throw MalformedScalar(path, raw, "Numeric");
    }

    internal static DamlDate ReadDate(JsonElement json, string path)
    {
        var raw = ReadWireString(json, path);
        return DateOnly.TryParseExact(raw, DamlJsonSerializer.CanonicalDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? new DamlDate(date)
            : throw MalformedScalar(path, raw, "Date");
    }

    internal static DamlTimestamp ReadTimestamp(JsonElement json, string path)
    {
        var raw = ReadWireString(json, path);
        return DateTimeOffset.TryParseExact(raw, DamlJsonSerializer.CanonicalTimestampParseFormat, CultureInfo.InvariantCulture, DamlJsonSerializer.UtcNormalizingTimestampParseStyles, out var timestamp)
            ? new DamlTimestamp(timestamp)
            : throw MalformedScalar(path, raw, "Timestamp");
    }

    internal static DamlUnit ReadUnit(JsonElement json, string path)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw ShapeMismatch(path, JsonValueKind.Object, json.ValueKind);
        }
        var propertyCount = json.GetPropertyCount();
        return propertyCount == 0 ? DamlUnit.Instance : throw NonEmptyUnit(path, propertyCount);
    }

    internal static string ReadVariantTag(JsonElement json, string path)
    {
        if (!json.TryGetProperty(VariantTagKey, out var tagElement))
        {
            throw MissingVariantMember(path, VariantTagKey);
        }
        return tagElement.ValueKind == JsonValueKind.String
            ? tagElement.GetString()!
            : throw ShapeMismatch($"{path}.{VariantTagKey}", JsonValueKind.String, tagElement.ValueKind);
    }

    internal static DamlList ReadList(
        JsonElement json, Func<JsonElement, string, DamlValue> readElement, DamlJsonDeserializationLimits limits, string path)
    {
        if (json.ValueKind != JsonValueKind.Array)
        {
            throw ShapeMismatch(path, JsonValueKind.Array, json.ValueKind);
        }

        var length = json.GetArrayLength();
        if (length > limits.MaxArrayElements)
        {
            throw DamlJsonSerializer.ArrayBreadthExceeded(length, limits.MaxArrayElements);
        }

        var values = new List<DamlValue>(length);
        foreach (var element in json.EnumerateArray())
        {
            values.Add(readElement(element, $"{path}[{values.Count}]"));
        }
        return new DamlList(values);
    }

    internal static DamlOptional ReadOptional(JsonElement json, Func<JsonElement, string, DamlValue> readValue, string path) =>
        json.ValueKind == JsonValueKind.Null
            ? DamlOptional.None
            : DamlOptional.Some(readValue(json, path));

    internal static DamlOptionalChain ReadOptionalChain(
        JsonElement json, Func<JsonElement, string, DamlValue> readCarried, string path)
    {
        if (json.ValueKind != JsonValueKind.Array)
        {
            throw ShapeMismatch(path, JsonValueKind.Array, json.ValueKind);
        }

        var length = json.GetArrayLength();
        if (length == 0)
        {
            return DamlOptionalChain.None;
        }
        if (length > 1)
        {
            throw OverfullOptionalChain(path, length);
        }
        return DamlOptionalChain.Some(readCarried(json[0], $"{path}[0]"));
    }

    internal static DamlTextMap ReadTextMap(
        JsonElement json, Func<JsonElement, string, DamlValue> readValue, DamlJsonDeserializationLimits limits, string path)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw ShapeMismatch(path, JsonValueKind.Object, json.ValueKind);
        }

        var count = json.GetPropertyCount();
        if (count > limits.MaxArrayElements)
        {
            throw MapBreadthExceeded(count, limits.MaxArrayElements);
        }

        var values = new Dictionary<string, DamlValue>(count);
        foreach (var entry in json.EnumerateObject())
        {
            if (!values.TryAdd(entry.Name, readValue(entry.Value, MapEntryPath(path, entry.Name))))
            {
                throw new JsonException($"Duplicate key '{entry.Name}' at '{path}' in a Daml TextMap");
            }
        }
        return new DamlTextMap(values);
    }

    internal static DamlGenMap ReadGenMapEntries(
        JsonElement json,
        Func<JsonElement, string, DamlValue> readKey,
        Func<JsonElement, string, DamlValue> readValue,
        string containerName,
        DamlJsonDeserializationLimits limits,
        string path)
    {
        if (json.ValueKind != JsonValueKind.Array)
        {
            throw ShapeMismatch(path, JsonValueKind.Array, json.ValueKind);
        }

        var length = json.GetArrayLength();
        if (length > limits.MaxArrayElements)
        {
            throw DamlJsonSerializer.ArrayBreadthExceeded(length, limits.MaxArrayElements);
        }

        var entries = new List<(DamlValue Key, DamlValue Value)>(length);
        var seenKeys = new HashSet<DamlValue>();
        foreach (var entry in json.EnumerateArray())
        {
            var entryPath = $"{path}[{entries.Count}]";
            if (entry.ValueKind != JsonValueKind.Array)
            {
                throw ShapeMismatch(entryPath, JsonValueKind.Array, entry.ValueKind);
            }
            if (entry.GetArrayLength() != 2)
            {
                throw new JsonException(
                    $"Expected a two-element key/value pair at '{entryPath}' but found {entry.GetArrayLength()} element(s)");
            }

            var key = readKey(entry[0], $"{entryPath}.key");
            if (!seenKeys.Add(key))
            {
                throw new JsonException($"Duplicate key at '{entryPath}' in a Daml {containerName}");
            }
            entries.Add((key, readValue(entry[1], $"{entryPath}.value")));
        }
        return new DamlGenMap(entries);
    }

    internal static (JsonElement Entries, string Path) ReadStdlibMapField(JsonElement json, string path)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw ShapeMismatch(path, JsonValueKind.Object, json.ValueKind);
        }

        var entriesPath = $"{path}.{StdlibMapFieldLabel}";
        return json.TryGetProperty(StdlibMapFieldLabel, out var entries)
            ? (entries, entriesPath)
            : throw MissingRecordField(entriesPath);
    }

    internal static DamlRecord WrapStdlibMap(DamlGenMap entries) =>
        new(null, [new DamlField(StdlibMapFieldLabel, entries)]);

    internal static DamlRecord ReadNonEmpty(
        JsonElement json,
        Func<JsonElement, string, DamlValue> readHead,
        Func<JsonElement, string, DamlValue> readTailElement,
        DamlJsonDeserializationLimits limits,
        string path)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw ShapeMismatch(path, JsonValueKind.Object, json.ValueKind);
        }

        var headPath = $"{path}.{NonEmptyHeadFieldLabel}";
        if (!json.TryGetProperty(NonEmptyHeadFieldLabel, out var head))
        {
            throw MissingRecordField(headPath);
        }

        var tailPath = $"{path}.{NonEmptyTailFieldLabel}";
        if (!json.TryGetProperty(NonEmptyTailFieldLabel, out var tail))
        {
            throw MissingRecordField(tailPath);
        }

        return new DamlRecord(null, [
            new DamlField(NonEmptyHeadFieldLabel, readHead(head, headPath)),
            new DamlField(NonEmptyTailFieldLabel, ReadList(tail, readTailElement, limits, tailPath))]);
    }

    internal static DamlRecord ReadTuple(
        JsonElement json, IReadOnlyList<Func<JsonElement, string, DamlValue>> componentReaders, string path)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw ShapeMismatch(path, JsonValueKind.Object, json.ValueKind);
        }

        var fields = new List<DamlField>(componentReaders.Count);
        for (var component = 0; component < componentReaders.Count; component++)
        {
            var label = StdlibTupleFieldLabels[component];
            fields.Add(new DamlField(label, ReadOmissibleField(json, label, $"{path}.{label}", componentReaders[component])));
        }
        return new DamlRecord(null, fields);
    }

    private static DamlValue ReadOmissibleField(
        JsonElement record, string field, string fieldPath, Func<JsonElement, string, DamlValue> read)
    {
        if (record.TryGetProperty(field, out var value))
        {
            return read(value, fieldPath);
        }

        if (TryReadOmitted(read, OmittedFlatOptional, fieldPath) is { } flatNone)
        {
            return flatNone;
        }

        if (TryReadOmitted(read, OmittedOptionalChain, fieldPath) is DamlOptionalChain chainNone)
        {
            return chainNone;
        }

        throw MissingRecordField(fieldPath);
    }

    private static DamlValue? TryReadOmitted(Func<JsonElement, string, DamlValue> read, JsonElement omittedNone, string fieldPath)
    {
        try
        {
            return read(omittedNone, fieldPath);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static DamlVariant ReadEither(
        JsonElement json,
        Func<JsonElement, string, DamlValue> readLeft,
        Func<JsonElement, string, DamlValue> readRight,
        string path)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw ShapeMismatch(path, JsonValueKind.Object, json.ValueKind);
        }

        var tag = ReadVariantTag(json, path);
        var component = Array.IndexOf(EitherConstructors, tag);
        if (component < 0)
        {
            throw UnknownConstructor(VariantConstructorKind, tag, path, EitherConstructors);
        }
        if (!json.TryGetProperty(VariantValueKey, out var valueElement))
        {
            throw MissingVariantMember(path, VariantValueKey);
        }

        var payloadPath = $"{path}.{VariantValueKey}";
        var readPayload = component == 0 ? readLeft : readRight;
        return DamlVariant.Create(tag, readPayload(valueElement, payloadPath));
    }

    private static JsonElement ParseConstant(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    internal static JsonException ShapeMismatch(string path, JsonValueKind expected, JsonValueKind actual) =>
        ShapeMismatch(path, expected.ToString(), actual);

    internal static JsonException ShapeMismatch(string path, string expected, JsonValueKind actual) =>
        new($"Expected JSON {expected} at '{path}' but found {actual}");

    internal static JsonException NonEmptyUnit(string path, int propertyCount) =>
        new($"Expected an empty JSON Object at '{path}' (Daml Unit) but found {propertyCount} "
            + $"propert{(propertyCount == 1 ? "y" : "ies")}");

    internal static JsonException OverfullOptionalChain(string path, int length) =>
        new($"A nested Daml Optional at '{path}' encodes as an array of at most one element "
            + $"but found {length}");

    internal static JsonException MissingRecordField(string fieldPath) =>
        new($"Required Daml field '{fieldPath}' is missing from the JSON object");

    internal static JsonException MissingVariantMember(string path, string member) =>
        new($"Required Daml variant field '{path}.{member}' is missing from the JSON object");

    internal static JsonException UnknownConstructor(string kind, string name, string path, IReadOnlyList<string> known) =>
        new($"Unknown Daml {kind} '{Elide(name)}' at '{path}'; expected one of {string.Join(", ", known)}");

    internal static JsonException MalformedScalar(string path, string raw, string damlType) =>
        new($"Value '{Elide(raw)}' at '{path}' is not a valid Daml {damlType}");

    internal static JsonException MapBreadthExceeded(int count, int maxEntries) =>
        new($"JSON object property count {count} exceeds the maximum supported Daml TextMap entry count of {maxEntries}");

    internal static string MapEntryPath(string path, string key) =>
        $"{path}['{Elide(key).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)}']";

    internal static string Elide(string raw)
    {
        if (raw.Length <= MaximumEchoedValueLength)
        {
            return raw;
        }
        var boundary = char.IsHighSurrogate(raw[MaximumEchoedValueLength - 1])
            ? MaximumEchoedValueLength - 1
            : MaximumEchoedValueLength;
        return string.Concat(raw.AsSpan(0, boundary), "…");
    }
}
