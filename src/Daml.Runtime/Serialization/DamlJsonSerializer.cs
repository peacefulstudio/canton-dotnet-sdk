// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Serialization;

/// <summary>
/// Input hardening limits applied by <see cref="DamlJsonSerializer"/> before materializing JSON into Daml values.
/// </summary>
/// <remarks>
/// A record class, not a record struct: a struct always carries a compiler-synthesized
/// parameterless constructor that zeroes every field instead of applying the primary
/// constructor's default arguments, so <c>new DamlJsonDeserializationLimits()</c> and
/// <c>default(DamlJsonDeserializationLimits)</c> would silently produce a limits value that
/// rejects every input. A record class has no such constructor, so <c>new()</c> always
/// resolves to the primary constructor below, defaults and all; <see cref="Default"/> names
/// the same instance for callers who want it by reference rather than by construction.
/// </remarks>
/// <param name="MaxInputCharacters">Maximum accepted JSON string length in UTF-16 code units.</param>
/// <param name="MaxArrayElements">Maximum accepted element count for any single JSON array.</param>
public sealed record DamlJsonDeserializationLimits(
    int MaxInputCharacters = 16 * 1024 * 1024,
    int MaxArrayElements = 100_000)
{
    /// <summary>The hardened default limits: 16 MiB of input, 100,000 elements per array.</summary>
    public static DamlJsonDeserializationLimits Default { get; } = new();
}

/// <summary>
/// Serializes and deserializes Daml values to/from JSON format compatible with the Ledger API.
/// </summary>
public static class DamlJsonSerializer
{
    private const string VariantTagKey = "tag";
    private const string VariantValueKey = "value";
    internal const string CanonicalDateFormat = "yyyy-MM-dd";
    private const string CanonicalTimestampEmitFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFF'Z'";
    internal const string CanonicalTimestampParseFormat = "yyyy-MM-dd'T'HH':'mm':'ss.FFFFFFFK";
    internal const DateTimeStyles UtcNormalizingTimestampParseStyles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
    internal const int MaximumNestingDepth = 128;
    private const int JsonReaderWriterMaxDepth = 4 * MaximumNestingDepth;
    internal static readonly DamlJsonDeserializationLimits DefaultDeserializationLimits =
        DamlJsonDeserializationLimits.Default;

    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = JsonReaderWriterMaxDepth,
        AllowDuplicateProperties = false,
        Converters = { new DamlValueJsonConverter() }
    };

    internal static readonly JsonDocumentOptions DocumentOptions = new()
    {
        MaxDepth = JsonReaderWriterMaxDepth,
        AllowDuplicateProperties = false
    };

    /// <summary>
    /// Serializes a DamlValue to JSON.
    /// </summary>
    /// <remarks>
    /// Supplying <paramref name="options"/> replaces the hardened defaults entirely:
    /// re-apply <c>AllowDuplicateProperties = false</c> and a bounded <c>MaxDepth</c>
    /// on the caller-supplied options or those protections are bypassed.
    /// </remarks>
    public static string Serialize(DamlValue value, JsonSerializerOptions? options = null) =>
        JsonSerializer.Serialize(value, options ?? DefaultOptions);

    /// <summary>
    /// Serializes a DamlRecord to JSON.
    /// </summary>
    /// <remarks>
    /// Supplying <paramref name="options"/> replaces the hardened defaults entirely:
    /// re-apply <c>AllowDuplicateProperties = false</c> and a bounded <c>MaxDepth</c>
    /// on the caller-supplied options or those protections are bypassed.
    /// </remarks>
    public static string Serialize(DamlRecord record, JsonSerializerOptions? options = null) =>
        JsonSerializer.Serialize(RecordToJsonObject(record), options ?? DefaultOptions);

    /// <summary>
    /// Deserializes JSON to a DamlValue.
    /// </summary>
    /// <remarks>
    /// Untyped deserialization disambiguates <c>GenMap</c> from <c>List (List a)</c>
    /// heuristically: a top-level JSON array whose every element is a 2-element
    /// JSON array with non-null first and second elements is reconstructed as a
    /// <see cref="DamlGenMap"/>. This disambiguation is necessarily lossy for some
    /// untyped JSON, and callers needing exact round-trip behavior for these
    /// shapes must deserialize against a type schema rather than through this
    /// entry point:
    /// <list type="bullet">
    /// <item><description>A <c>List (List a)</c> where every inner list has length 2
    /// (e.g. matrices, coordinate pairs) is reinterpreted as a <see cref="DamlGenMap"/>.</description></item>
    /// <item><description>An empty JSON array <c>[]</c> is always reconstructed as an empty
    /// <see cref="DamlList"/>, never as an empty <see cref="DamlGenMap"/> — empty <c>[]</c>
    /// is genuinely indistinguishable from an empty list without a type schema, and the
    /// far more common case is the empty list.</description></item>
    /// <item><description>A pair whose first or second element is JSON <c>null</c> is treated as
    /// a list element, not a GenMap entry, so <c>List [[None, Some 5]]</c> and
    /// <c>List [[Some 5, None]]</c> shapes both surface the original
    /// "Null array elements not supported" error rather than a misleading
    /// "GenMap key/value must not be null".</description></item>
    /// <item><description>A JSON string is inferred as <see cref="DamlDate"/>,
    /// <see cref="DamlTimestamp"/>, <see cref="DamlNumeric"/>, or <see cref="DamlInt64"/>
    /// only when it matches the exact canonical shape this serializer emits
    /// (<c>yyyy-MM-dd</c>, ISO-8601 <c>T</c>-separated timestamp, <c>-?digits.digits</c>,
    /// or a bare <c>-?digits</c> integer); every other string stays
    /// <see cref="DamlText"/>. A Text value that happens to match a canonical shape —
    /// including a numeric-looking identifier consisting only of digits — is therefore
    /// reinterpreted on an untyped round-trip.</description></item>
    /// <item><description>A bare integer string that overflows <see cref="long"/>
    /// (Daml Int's 64-bit range) stays <see cref="DamlText"/> rather than being
    /// reinterpreted as a Numeric, since the untyped path cannot tell a huge whole
    /// Numeric from malformed input.</description></item>
    /// <item><description>Daml Numeric carries up to 38 significant digits (scale 0-37);
    /// both the canonical numeric string and raw JSON number forms parse through a
    /// <see cref="BigInteger"/> mantissa with zero precision loss, including magnitudes
    /// beyond <see cref="decimal.MaxValue"/>. A value outside that 38-digit bound throws
    /// <see cref="JsonException"/> rather than degrading to <see cref="DamlText"/> or
    /// rounding.</description></item>
    /// <item><description><see cref="DamlNumeric.Scale"/> is never written to JSON and
    /// deserialization always reconstructs the default scale of 10. This is why
    /// <see cref="DamlNumeric"/> equality compares the numeric value only —
    /// a Numeric constructed with a non-default scale still round-trips equal, but the
    /// original scale itself is lost.</description></item>
    /// <item><description>An explicit JSON <c>null</c> record field deserializes to
    /// <see cref="DamlOptional.None"/>; a Some value is flattened to its inner value on
    /// write, so schema-aware readers recover the wrapper via
    /// <see cref="DamlValueExtensions.AsOptional"/>. That flattening is specific to
    /// <see cref="DamlOptional"/>: a <see cref="DamlOptionalChain"/> level writes the array
    /// form instead, <c>[]</c> or <c>[v]</c>, and comes back as a <see cref="DamlList"/> on
    /// an untyped round-trip.</description></item>
    /// <item><description>A two-property JSON object whose properties are exactly
    /// <c>tag</c> (a string) and <c>value</c> is reconstructed as a
    /// <see cref="DamlVariant"/>, so a genuine two-field record with those labels is
    /// reinterpreted as a variant.</description></item>
    /// <item><description>A <see cref="DamlTextMap"/> serializes as a JSON object and is
    /// reconstructed as a <see cref="DamlRecord"/> on an untyped round-trip.</description></item>
    /// <item><description><see cref="DamlParty"/>, <see cref="DamlContractId"/>, and
    /// <see cref="DamlEnum"/> serialize as JSON strings and come back as
    /// <see cref="DamlText"/> on an untyped round-trip.</description></item>
    /// </list>
    /// Because of these collisions, untyped deserialization output is a best-effort
    /// reconstruction and must not back security or authorization decisions; use a
    /// type schema when the distinction matters. JSON objects containing duplicate
    /// property names are rejected with <see cref="JsonException"/>, and value
    /// nesting is bounded (at a depth above Daml-LF's 100-level value limit).
    /// <para>
    /// Those two protections — duplicate-property rejection and the depth bound — live
    /// on the default options: supplying <paramref name="options"/> replaces them
    /// entirely, so caller-supplied <see cref="JsonSerializerOptions"/> must re-apply
    /// <c>AllowDuplicateProperties = false</c> and a bounded <c>MaxDepth</c>
    /// (and register <see cref="DamlValueJsonConverter"/>) or that hardening is bypassed.
    /// The 16 MiB input-size cap is a separate, unconditional check applied before
    /// <paramref name="options"/> is consulted, so it always applies regardless of the
    /// options supplied.
    /// </para>
    /// </remarks>
    public static DamlValue Deserialize(string json, JsonSerializerOptions? options = null)
    {
        EnsureWithinInputLimit(json, DefaultDeserializationLimits);
        return JsonSerializer.Deserialize<DamlValue>(json, options ?? DefaultOptions)
            ?? throw new JsonException("Failed to deserialize JSON to DamlValue");
    }

    /// <summary>
    /// Deserializes JSON to a DamlValue with caller-supplied input-size and array-breadth limits.
    /// </summary>
    /// <remarks>
    /// A <see langword="null"/> <paramref name="limits"/>, including <c>default(DamlJsonDeserializationLimits)</c>,
    /// applies <see cref="DamlJsonDeserializationLimits.Default"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limits"/> is not a valid limit configuration.</exception>
    public static DamlValue Deserialize(string json, DamlJsonDeserializationLimits? limits)
    {
        var effectiveLimits = limits ?? DefaultDeserializationLimits;
        EnsureWithinInputLimit(json, effectiveLimits);
        var node = JsonNode.Parse(json, nodeOptions: null, DocumentOptions)
            ?? throw new JsonException("Null JSON not supported");
        return JsonNodeToValue(node, effectiveLimits, depth: 0);
    }

    /// <summary>
    /// Deserializes JSON to a DamlRecord.
    /// </summary>
    public static DamlRecord DeserializeRecord(string json, JsonSerializerOptions? options = null)
    {
        EnsureWithinInputLimit(json, DefaultDeserializationLimits);
        var node = JsonNode.Parse(json, nodeOptions: null, DocumentOptions)
            ?? throw new JsonException("Expected a JSON object for a Daml record but found null");
        if (node is not JsonObject obj)
        {
            throw new JsonException($"Expected a JSON object for a Daml record but found {node.GetValueKind()}");
        }
        return JsonObjectToRecord(obj, DefaultDeserializationLimits, depth: 0);
    }

    /// <summary>
    /// Deserializes JSON to a DamlRecord with caller-supplied input-size and array-breadth limits.
    /// </summary>
    /// <remarks>
    /// A <see langword="null"/> <paramref name="limits"/>, including <c>default(DamlJsonDeserializationLimits)</c>,
    /// applies <see cref="DamlJsonDeserializationLimits.Default"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limits"/> is not a valid limit configuration.</exception>
    public static DamlRecord DeserializeRecord(string json, DamlJsonDeserializationLimits? limits)
    {
        var effectiveLimits = limits ?? DefaultDeserializationLimits;
        EnsureWithinInputLimit(json, effectiveLimits);
        var node = JsonNode.Parse(json, nodeOptions: null, DocumentOptions)
            ?? throw new JsonException("Expected a JSON object for a Daml record but found null");
        if (node is not JsonObject obj)
        {
            throw new JsonException($"Expected a JSON object for a Daml record but found {node.GetValueKind()}");
        }
        return JsonObjectToRecord(obj, effectiveLimits, depth: 0);
    }

    internal static void ValidateLimits(DamlJsonDeserializationLimits limits)
    {
        if (limits.MaxInputCharacters < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "MaxInputCharacters must be positive.");
        }
        if (limits.MaxArrayElements < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "MaxArrayElements must be non-negative.");
        }
    }

    internal static void EnsureWithinInputLimit(string json, DamlJsonDeserializationLimits limits)
    {
        ValidateLimits(limits);
        if (json.Length > limits.MaxInputCharacters)
        {
            throw new JsonException(
                $"JSON input length {json.Length} exceeds the maximum supported JSON input size of {limits.MaxInputCharacters} characters");
        }
    }

    private static JsonObject RecordToJsonObject(DamlRecord record) =>
        RecordToJsonObject(record, depth: 0);

    private static JsonObject RecordToJsonObject(DamlRecord record, int depth)
    {
        var obj = new JsonObject();
        foreach (var field in record.Fields)
        {
            if (obj.ContainsKey(field.Label))
            {
                throw new JsonException(
                    $"Duplicate field label '{field.Label}' in Daml record; refusing to serialize last-wins");
            }
            obj[field.Label] = ValueToJsonNode(field.Value, depth + 1);
        }
        return obj;
    }

    internal static JsonNode? ValueToJsonNode(DamlValue value) =>
        ValueToJsonNode(value, depth: 0);

    private static JsonNode? ValueToJsonNode(DamlValue value, int depth) => value switch
    {
        _ when depth > MaximumNestingDepth => throw DepthBoundExceeded(),
        DamlInt64 i => JsonValue.Create(i.Value.ToString(CultureInfo.InvariantCulture)),
        DamlNumeric n => JsonValue.Create(n.ToCanonicalString()),
        DamlText t => JsonValue.Create(t.Value),
        DamlBool b => JsonValue.Create(b.Value),
        DamlUnit => new JsonObject(),
        DamlDate d => JsonValue.Create(d.Value.ToString(CanonicalDateFormat, CultureInfo.InvariantCulture)),
        DamlTimestamp ts => JsonValue.Create(ts.Value.UtcDateTime.ToString(CanonicalTimestampEmitFormat, CultureInfo.InvariantCulture)),
        DamlParty p => JsonValue.Create(p.Value),
        DamlContractId c => JsonValue.Create(c.Value),
        DamlOptional opt => opt.Value is null ? null : ValueToJsonNode(opt.Value, depth + 1),
        DamlOptionalChain chain => chain.Value is null
            ? new JsonArray()
            : new JsonArray(ValueToJsonNode(chain.Value, depth + 1)),
        DamlList list => new JsonArray(list.Values.Select(v => ValueToJsonNode(v, depth + 1)).ToArray()),
        DamlTextMap map => MapToJsonObject(map, depth),
        DamlGenMap map => GenMapToJsonArray(map, depth),
        DamlVariant variant => VariantToJsonObject(variant, depth),
        DamlEnum e => JsonValue.Create(e.Constructor),
        DamlRecord rec => RecordToJsonObject(rec, depth),
        _ => throw new JsonException($"Cannot serialize {value.GetType().Name} to JSON")
    };

    internal static JsonException DepthBoundExceeded() =>
        new($"Value nesting exceeds the maximum supported depth of {MaximumNestingDepth}");

    private static JsonObject MapToJsonObject(DamlTextMap map, int depth)
    {
        var obj = new JsonObject();
        foreach (var (key, val) in map.Values)
        {
            obj[key] = ValueToJsonNode(val, depth + 1);
        }
        return obj;
    }

    private static JsonArray GenMapToJsonArray(DamlGenMap map, int depth)
    {
        var entries = new JsonArray();
        foreach (var (key, val) in map.Entries)
        {
            entries.Add(new JsonArray(ValueToJsonNode(key, depth + 1), ValueToJsonNode(val, depth + 1)));
        }
        return entries;
    }

    private static JsonObject VariantToJsonObject(DamlVariant variant, int depth)
    {
        var obj = new JsonObject
        {
            [VariantTagKey] = variant.Constructor,
            [VariantValueKey] = ValueToJsonNode(variant.Value, depth + 1)
        };
        return obj;
    }

    private static DamlRecord JsonObjectToRecord(JsonObject obj, DamlJsonDeserializationLimits limits, int depth)
    {
        var fields = new List<DamlField>(obj.Count);
        foreach (var prop in obj)
        {
            fields.Add(new DamlField(prop.Key,
                prop.Value is null ? DamlOptional.None : JsonNodeToValue(prop.Value, limits, depth + 1)));
        }
        return new DamlRecord(null, fields);
    }

    internal static DamlValue JsonNodeToValue(JsonNode node) =>
        JsonNodeToValue(node, DefaultDeserializationLimits, depth: 0);

    private static DamlValue JsonNodeToValue(JsonNode node, DamlJsonDeserializationLimits limits, int depth) => node switch
    {
        _ when depth > MaximumNestingDepth => throw DepthBoundExceeded(),
        JsonValue val => JsonValueToDamlValue(val),
        JsonArray arr when arr.Count > limits.MaxArrayElements => throw ArrayBreadthExceeded(arr.Count, limits.MaxArrayElements),
        JsonArray arr when LooksLikeGenMapEntries(arr) => GenMapFromJsonArray(arr, limits, depth),
        JsonArray arr => new DamlList(arr.Select(n => n is null
            ? throw new JsonException("Null array elements not supported")
            : JsonNodeToValue(n, limits, depth + 1)).ToList()),
        JsonObject obj when IsVariantShape(obj) =>
            new DamlVariant(null, obj[VariantTagKey]!.GetValue<string>(),
                obj[VariantValueKey] is null ? DamlUnit.Instance : JsonNodeToValue(obj[VariantValueKey]!, limits, depth + 1)),
        JsonObject obj => JsonObjectToRecord(obj, limits, depth),
        _ => throw new JsonException($"Cannot deserialize {node.GetType().Name}")
    };

    internal static JsonException ArrayBreadthExceeded(int count, int maxArrayElements) =>
        new($"JSON array length {count} exceeds the maximum supported JSON array length of {maxArrayElements}");

    private static bool IsVariantShape(JsonObject obj) =>
        obj.Count == 2
        && obj.TryGetPropertyValue(VariantTagKey, out var tag)
        && tag is JsonValue tagValue
        && tagValue.TryGetValue<string>(out _)
        && obj.ContainsKey(VariantValueKey);

    private static bool LooksLikeGenMapEntries(JsonArray arr)
    {
        if (arr.Count == 0)
        {
            return false;
        }
        foreach (var element in arr)
        {
            if (element is not JsonArray pair || pair.Count != 2 || pair[0] is null || pair[1] is null)
            {
                return false;
            }
        }
        return true;
    }

    private static DamlGenMap GenMapFromJsonArray(JsonArray arr, DamlJsonDeserializationLimits limits, int depth)
    {
        var entries = new List<(DamlValue Key, DamlValue Value)>(arr.Count);
        foreach (var element in arr)
        {
            var pair = (JsonArray)element!;
            entries.Add((JsonNodeToValue(pair[0]!, limits, depth + 1), JsonNodeToValue(pair[1]!, limits, depth + 1)));
        }
        try
        {
            return new DamlGenMap(entries);
        }
        catch (ArgumentException ex)
        {
            throw new JsonException(ex.Message, ex);
        }
    }

    private static DamlValue JsonValueToDamlValue(JsonValue val)
    {
        var element = val.GetValue<JsonElement>();
        return element.ValueKind switch
        {
            JsonValueKind.String => InferStringValue(element.GetString()!),
            JsonValueKind.Number when element.TryGetInt64(out var i) => new DamlInt64(i),
            JsonValueKind.Number => ParseJsonNumber(element),
            JsonValueKind.True => new DamlBool(true),
            JsonValueKind.False => new DamlBool(false),
            JsonValueKind.Null => throw new JsonException("Null values should be handled as Optional.None"),
            _ => throw new JsonException($"Cannot deserialize JSON value kind {element.ValueKind}")
        };
    }

    private static DamlValue ParseJsonNumber(JsonElement element)
    {
        var raw = element.GetRawText();
        return DamlNumeric.TryParseCanonical(raw, out var exact)
            ? exact
            : ParseNonCanonicalJsonNumber(raw, element);
    }

    private static DamlValue ParseNonCanonicalJsonNumber(string raw, JsonElement element) =>
        element.TryGetDecimal(out var d)
            ? new DamlNumeric(d)
            : throw new JsonException($"Number '{raw}' cannot be represented as a Daml Numeric");

    private static DamlValue InferStringValue(string s)
    {
        if (DateOnly.TryParseExact(s, CanonicalDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return new DamlDate(date);
        }

        if (DateTimeOffset.TryParseExact(s, CanonicalTimestampParseFormat, CultureInfo.InvariantCulture, UtcNormalizingTimestampParseStyles, out var ts))
        {
            return new DamlTimestamp(ts);
        }

        if (MatchesCanonicalNumericGrammar(s))
        {
            return DamlNumeric.TryParseCanonical(s, out var numeric)
                ? numeric
                : throw new JsonException($"Number '{s}' cannot be represented as a Daml Numeric");
        }

        if (MatchesCanonicalIntegerGrammar(s) && long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole))
        {
            return new DamlInt64(whole);
        }

        return new DamlText(s);
    }

    internal static bool MatchesCanonicalIntegerGrammar(string s)
    {
        var digitsStart = s.StartsWith('-') ? 1 : 0;
        if (digitsStart == s.Length)
        {
            return false;
        }

        if (s[digitsStart] == '0' && s.Length > digitsStart + 1)
        {
            return false;
        }

        for (var i = digitsStart; i < s.Length; i++)
        {
            if (!char.IsAsciiDigit(s[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesCanonicalNumericGrammar(string s)
    {
        var digitsStart = s.StartsWith('-') ? 1 : 0;
        var dotIndex = s.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex <= digitsStart || dotIndex == s.Length - 1)
        {
            return false;
        }

        if (s[digitsStart] == '0' && dotIndex > digitsStart + 1)
        {
            return false;
        }

        for (var i = digitsStart; i < s.Length; i++)
        {
            if (i != dotIndex && !char.IsAsciiDigit(s[i]))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// JSON converter for DamlValue types. Delegates to the canonical
/// <see cref="DamlJsonSerializer.ValueToJsonNode(DamlValue)"/> /
/// <see cref="DamlJsonSerializer.JsonNodeToValue(JsonNode)"/> mappers so the
/// top-level <see cref="DamlJsonSerializer.Serialize(DamlValue, JsonSerializerOptions?)"/>
/// and <see cref="DamlJsonSerializer.Deserialize(string, JsonSerializerOptions?)"/>
/// paths share semantics with the record-scoped overloads.
/// </summary>
public sealed class DamlValueJsonConverter : JsonConverter<DamlValue>
{
    /// <summary>Reads a JSON node and maps it to a <see cref="DamlValue"/>; all failures surface as <see cref="JsonException"/>.</summary>
    public override DamlValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var node = JsonNode.Parse(ref reader) ?? throw new JsonException("Null JSON not supported");
        try
        {
            return DamlJsonSerializer.JsonNodeToValue(node);
        }
        catch (Exception ex) when (ex is not JsonException)
        {
            throw new JsonException("Failed to deserialize JSON to a DamlValue", ex);
        }
    }

    /// <summary>Writes a <see cref="DamlValue"/> as its canonical JSON form; all failures surface as <see cref="JsonException"/>.</summary>
    public override void Write(Utf8JsonWriter writer, DamlValue value, JsonSerializerOptions options)
    {
        JsonNode? node;
        try
        {
            node = DamlJsonSerializer.ValueToJsonNode(value);
        }
        catch (Exception ex) when (ex is not JsonException)
        {
            throw new JsonException($"Failed to serialize {value.GetType().Name} to JSON", ex);
        }

        if (node is null)
        {
            writer.WriteNullValue();
            return;
        }
        node.WriteTo(writer);
    }
}
