// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Serialization;

/// <summary>
/// The <see cref="System.Text.Json"/> converter a value declared as <see cref="DamlValue"/> takes
/// from the attribute on that type: the concrete arm's own JSON object with a <c>"$case"</c>
/// discriminator naming the arm inserted first, e.g. <c>{"$case":"DamlInt64","Value":42}</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is a CLR round-trip contract, not the Daml-LF wire encoding: unlike
/// <see cref="DamlValueJsonConverter"/>, which writes the untyped Daml-LF spelling and so cannot
/// say whether a JSON string came from a <see cref="DamlText"/> or a <see cref="DamlParty"/>,
/// every arm reads back as itself. A payload from a participant or PQS is read with
/// <see cref="DamlLfJsonReader"/>. Because <see cref="JsonConverterAttribute"/> is not inherited,
/// a value declared as its own arm keeps the arm's plain object contract, with no discriminator.
/// </para>
/// <para>
/// The leaf arms, which hold no nested <see cref="DamlValue"/>, take their members from the arm's
/// own contract. The seven composite arms are read and written member by member on the caller's
/// own <see cref="Utf8JsonReader"/> and <see cref="Utf8JsonWriter"/>, and a nested value recurses
/// into this converter directly rather than through <see cref="JsonSerializer"/>. Each level of
/// nesting therefore costs a few small frames of call stack, and
/// <see cref="JsonSerializerOptions.MaxDepth"/> bounds the nesting as it does for any other type:
/// the reader refuses a payload nested deeper, and the writer refuses a value nested deeper, with a
/// <see cref="JsonException"/>. The composite members follow
/// <see cref="JsonSerializerOptions.PropertyNamingPolicy"/>,
/// <see cref="JsonSerializerOptions.PropertyNameCaseInsensitive"/>,
/// <see cref="JsonSerializerOptions.UnmappedMemberHandling"/>,
/// <see cref="JsonSerializerOptions.AllowDuplicateProperties"/>,
/// <see cref="JsonSerializerOptions.RespectNullableAnnotations"/>,
/// <see cref="JsonSerializerOptions.RespectRequiredConstructorParameters"/> and
/// <see cref="JsonSerializerOptions.DefaultIgnoreCondition"/> as the arms' own members would. A
/// <see cref="JsonSerializerOptions.ReferenceHandler"/> is refused, as it is for the other
/// discriminated unions.
/// </para>
/// </remarks>
internal sealed class DamlValueClrJsonConverter : JsonConverter<DamlValue>
{
    private const string TypeName = nameof(DamlValue);

    private const int NoMoreMembers = -1;

    private const int UnmappedMember = -2;

    private const int CaseMember = 30;

    private static readonly ConditionalWeakTable<JsonSerializerOptions, Shapes> ShapesByOptions = new();

    private static readonly IReadOnlyDictionary<string, Type> LeafArms = new[]
    {
        typeof(DamlInt64),
        typeof(DamlNumeric),
        typeof(DamlText),
        typeof(DamlBool),
        typeof(DamlUnit),
        typeof(DamlDate),
        typeof(DamlTimestamp),
        typeof(DamlParty),
        typeof(DamlContractId),
        typeof(DamlEnum),
        typeof(DamlUndecodedJson),
    }.ToDictionary(arm => arm.Name, StringComparer.Ordinal);

    private delegate T ElementReader<out T>(ref Utf8JsonReader reader, JsonSerializerOptions options, Shapes shapes);

    private enum OptionalMember
    {
        Value,
    }

    private enum ListMember
    {
        Values,
    }

    private enum MapMember
    {
        Entries,
    }

    private enum EntryMember
    {
        Key,
        Value,
    }

    private enum RecordMember
    {
        RecordId,
        Fields,
    }

    private enum FieldMember
    {
        Label,
        Value,
    }

    private enum VariantMember
    {
        VariantId,
        Constructor,
        Value,
    }

    public override DamlValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        DiscriminatedUnionJson.EnsureNoReferenceHandler(options, TypeName);
        return ReadValue(ref reader, options, ShapesFor(options));
    }

    public override void Write(Utf8JsonWriter writer, DamlValue value, JsonSerializerOptions options)
    {
        DiscriminatedUnionJson.EnsureNoReferenceHandler(options, TypeName);
        WriteValue(writer, value, options, ShapesFor(options));
    }

    internal static DamlGenMap ReadGenMapDeclaredAsItself(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        DiscriminatedUnionJson.EnsureNoReferenceHandler(options, nameof(DamlGenMap));
        var shapes = ShapesFor(options);
        EnsureObject(ref reader, shapes.GenMapDeclaredAsItself);
        return ReadGenMap(ref reader, options, shapes, shapes.GenMapDeclaredAsItself);
    }

    internal static void WriteGenMapDeclaredAsItself(Utf8JsonWriter writer, DamlGenMap genMap, JsonSerializerOptions options)
    {
        DiscriminatedUnionJson.EnsureNoReferenceHandler(options, nameof(DamlGenMap));
        var shapes = ShapesFor(options);
        StartObject(writer, options);
        WriteGenMapEntries(writer, genMap, options, shapes);
        writer.WriteEndObject();
    }

    private static Shapes ShapesFor(JsonSerializerOptions options) =>
        ShapesByOptions.GetValue(options, static created => new Shapes(created.PropertyNamingPolicy));

    private static DamlValue ReadValue(ref Utf8JsonReader reader, JsonSerializerOptions options, Shapes shapes)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected object token for {TypeName}, got {reader.TokenType}.");
        }

        var caseName = PeekCase(reader);
        return caseName switch
        {
            nameof(DamlOptional) => new DamlOptional(ReadOptionalValue(ref reader, options, shapes, shapes.Optional)),
            nameof(DamlOptionalChain) => new DamlOptionalChain(ReadOptionalValue(ref reader, options, shapes, shapes.OptionalChain)),
            nameof(DamlList) => ReadList(ref reader, options, shapes),
            nameof(DamlTextMap) => ReadTextMap(ref reader, options, shapes),
            nameof(DamlGenMap) => ReadGenMap(ref reader, options, shapes, shapes.GenMap),
            nameof(DamlRecord) => ReadRecord(ref reader, options, shapes),
            nameof(DamlVariant) => ReadVariant(ref reader, options, shapes),
            _ when LeafArms.TryGetValue(caseName, out var leafArm) => ReadLeaf(ref reader, caseName, leafArm, options),
            _ => throw new JsonException(
                $"{TypeName} names an unknown case \"{caseName}\"; expected one of {string.Join(", ", KnownCases())}."),
        };
    }

    private static IEnumerable<string> KnownCases() =>
        LeafArms.Keys.Concat(
        [
            nameof(DamlOptional),
            nameof(DamlOptionalChain),
            nameof(DamlList),
            nameof(DamlTextMap),
            nameof(DamlGenMap),
            nameof(DamlRecord),
            nameof(DamlVariant),
        ]);

    private static string PeekCase(Utf8JsonReader scout)
    {
        var objectDepth = scout.CurrentDepth;
        while (scout.Read() && !(scout.TokenType == JsonTokenType.EndObject && scout.CurrentDepth == objectDepth))
        {
            var isCase = scout.ValueTextEquals(DiscriminatedUnionJson.CaseProperty);
            scout.Read();
            if (!isCase)
            {
                scout.Skip();
                continue;
            }

            return scout.TokenType == JsonTokenType.String
                ? scout.GetString()!
                : throw new JsonException($"{TypeName} has a \"{DiscriminatedUnionJson.CaseProperty}\" discriminator that is not a string.");
        }

        throw new JsonException($"{TypeName} is missing the \"{DiscriminatedUnionJson.CaseProperty}\" discriminator.");
    }

    private static DamlValue? ReadOptionalValue(ref Utf8JsonReader reader, JsonSerializerOptions options, Shapes shapes, Shape shape)
    {
        DamlValue? value = null;
        var seen = 0;
        while (NextMember(ref reader, options, shape, ref seen) != NoMoreMembers)
        {
            value = ReadNullableValue(ref reader, options, shapes);
        }

        EnsureRequiredMembersSeen(shape, seen, options);
        return value;
    }

    private static DamlList ReadList(ref Utf8JsonReader reader, JsonSerializerOptions options, Shapes shapes)
    {
        var shape = shapes.List;
        List<DamlValue>? values = null;
        var seen = 0;
        while (NextMember(ref reader, options, shape, ref seen) != NoMoreMembers)
        {
            values = ReadArray(ref reader, options, shapes, shape, (int)ListMember.Values, ReadValue);
        }

        EnsureRequiredMembersSeen(shape, seen, options);
        return new DamlList(values!);
    }

    private static DamlTextMap ReadTextMap(ref Utf8JsonReader reader, JsonSerializerOptions options, Shapes shapes)
    {
        var shape = shapes.TextMap;
        List<KeyValuePair<string, DamlValue>>? entries = null;
        var seen = 0;
        while (NextMember(ref reader, options, shape, ref seen) != NoMoreMembers)
        {
            entries = ReadArray(ref reader, options, shapes, shape, (int)MapMember.Entries, ReadTextMapEntry);
        }

        EnsureRequiredMembersSeen(shape, seen, options);
        var values = new Dictionary<string, DamlValue>();
        foreach (var (key, value) in entries!)
        {
            if (!values.TryAdd(key, value))
            {
                throw new JsonException($"{nameof(DamlTextMap)} has a duplicate key \"{key}\".");
            }
        }

        return new DamlTextMap(values);
    }

    private static KeyValuePair<string, DamlValue> ReadTextMapEntry(ref Utf8JsonReader reader, JsonSerializerOptions options, Shapes shapes)
    {
        var shape = shapes.TextMapEntry;
        EnsureObject(ref reader, shape);
        string? key = null;
        DamlValue? value = null;
        var seen = 0;
        int member;
        while ((member = NextMember(ref reader, options, shape, ref seen)) != NoMoreMembers)
        {
            switch ((EntryMember)member)
            {
                case EntryMember.Key:
                    key = ReadString(ref reader, shape, member);
                    break;
                case EntryMember.Value:
                    value = ReadNullableValue(ref reader, options, shapes);
                    break;
            }
        }

        EnsureRequiredMembersSeen(shape, seen, options);
        return new(
            key ?? throw NullMember(shape, (int)EntryMember.Key),
            RefuseNullIfAnnotated(value, options, shape, (int)EntryMember.Value)!);
    }

    private static DamlGenMap ReadGenMap(ref Utf8JsonReader reader, JsonSerializerOptions options, Shapes shapes, Shape shape)
    {
        List<(DamlValue Key, DamlValue Value)>? entries = null;
        var seen = 0;
        while (NextMember(ref reader, options, shape, ref seen) != NoMoreMembers)
        {
            entries = ReadArray(ref reader, options, shapes, shape, (int)MapMember.Entries, ReadGenMapEntry);
        }

        EnsureRequiredMembersSeen(shape, seen, options);
        var keys = new HashSet<DamlValue>();
        foreach (var (key, _) in entries!)
        {
            if (!keys.Add(key))
            {
                throw new JsonException($"{nameof(DamlGenMap)} has a duplicate key '{key}'.");
            }
        }

        return new DamlGenMap(entries);
    }

    private static (DamlValue Key, DamlValue Value) ReadGenMapEntry(ref Utf8JsonReader reader, JsonSerializerOptions options, Shapes shapes)
    {
        var shape = shapes.GenMapEntry;
        EnsureObject(ref reader, shape);
        DamlValue? key = null;
        DamlValue? value = null;
        var seen = 0;
        int member;
        while ((member = NextMember(ref reader, options, shape, ref seen)) != NoMoreMembers)
        {
            switch ((EntryMember)member)
            {
                case EntryMember.Key:
                    key = ReadNullableValue(ref reader, options, shapes);
                    break;
                case EntryMember.Value:
                    value = ReadNullableValue(ref reader, options, shapes);
                    break;
            }
        }

        EnsureRequiredMembersSeen(shape, seen, options);
        return (
            RefuseNullIfAnnotated(key, options, shape, (int)EntryMember.Key)!,
            RefuseNullIfAnnotated(value, options, shape, (int)EntryMember.Value)!);
    }

    private static DamlRecord ReadRecord(ref Utf8JsonReader reader, JsonSerializerOptions options, Shapes shapes)
    {
        var shape = shapes.Record;
        Identifier? recordId = null;
        List<DamlField>? fields = null;
        var seen = 0;
        int member;
        while ((member = NextMember(ref reader, options, shape, ref seen)) != NoMoreMembers)
        {
            switch ((RecordMember)member)
            {
                case RecordMember.RecordId:
                    recordId = JsonSerializer.Deserialize<Identifier>(ref reader, options);
                    break;
                case RecordMember.Fields:
                    fields = ReadArray(ref reader, options, shapes, shape, member, ReadField);
                    break;
            }
        }

        EnsureRequiredMembersSeen(shape, seen, options);
        return new DamlRecord(recordId, fields!);
    }

    private static DamlField ReadField(ref Utf8JsonReader reader, JsonSerializerOptions options, Shapes shapes)
    {
        var shape = shapes.Field;
        EnsureObject(ref reader, shape);
        string? label = null;
        DamlValue? value = null;
        var seen = 0;
        int member;
        while ((member = NextMember(ref reader, options, shape, ref seen)) != NoMoreMembers)
        {
            switch ((FieldMember)member)
            {
                case FieldMember.Label:
                    label = ReadString(ref reader, shape, member);
                    break;
                case FieldMember.Value:
                    value = ReadNullableValue(ref reader, options, shapes);
                    break;
            }
        }

        EnsureRequiredMembersSeen(shape, seen, options);
        return new DamlField(
            RefuseNullIfAnnotated(label, options, shape, (int)FieldMember.Label)!,
            RefuseNullIfAnnotated(value, options, shape, (int)FieldMember.Value)!);
    }

    private static DamlVariant ReadVariant(ref Utf8JsonReader reader, JsonSerializerOptions options, Shapes shapes)
    {
        var shape = shapes.Variant;
        Identifier? variantId = null;
        string? constructor = null;
        DamlValue? value = null;
        var seen = 0;
        int member;
        while ((member = NextMember(ref reader, options, shape, ref seen)) != NoMoreMembers)
        {
            switch ((VariantMember)member)
            {
                case VariantMember.VariantId:
                    variantId = JsonSerializer.Deserialize<Identifier>(ref reader, options);
                    break;
                case VariantMember.Constructor:
                    constructor = ReadString(ref reader, shape, member);
                    break;
                case VariantMember.Value:
                    value = ReadNullableValue(ref reader, options, shapes);
                    break;
            }
        }

        EnsureRequiredMembersSeen(shape, seen, options);
        return new DamlVariant(
            variantId,
            RefuseNullIfAnnotated(constructor, options, shape, (int)VariantMember.Constructor)!,
            RefuseNullIfAnnotated(value, options, shape, (int)VariantMember.Value)!);
    }

    private static DamlValue? ReadNullableValue(ref Utf8JsonReader reader, JsonSerializerOptions options, Shapes shapes) =>
        reader.TokenType == JsonTokenType.Null ? null : ReadValue(ref reader, options, shapes);

    private static List<T> ReadArray<T>(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options,
        Shapes shapes,
        Shape shape,
        int member,
        ElementReader<T> readElement)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw reader.TokenType == JsonTokenType.Null
                ? NullMember(shape, member)
                : new JsonException($"{shape.Owner} member \"{shape[member]}\" must be an array, got {reader.TokenType}.");
        }

        var items = new List<T>();
        while (Advance(ref reader) != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                throw new JsonException($"{shape.Owner} holds a null element, which a Daml value cannot be.");
            }

            items.Add(readElement(ref reader, options, shapes));
        }

        return items;
    }

    private static string? ReadString(ref Utf8JsonReader reader, Shape shape, int member) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            _ => throw new JsonException($"{shape.Owner} member \"{shape[member]}\" must be a string, got {reader.TokenType}."),
        };

    private static void EnsureObject(ref Utf8JsonReader reader, Shape shape)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected object token for {shape.Owner}, got {reader.TokenType}.");
        }
    }

    private static int NextMember(ref Utf8JsonReader reader, JsonSerializerOptions options, Shape shape, ref int seen)
    {
        while (Advance(ref reader) != JsonTokenType.EndObject)
        {
            var member = shape.Match(ref reader, options.PropertyNameCaseInsensitive);
            if (member == UnmappedMember)
            {
                if (options.UnmappedMemberHandling == JsonUnmappedMemberHandling.Disallow)
                {
                    throw new JsonException(
                        $"{shape.Owner} has a member \"{reader.GetString()}\" it does not define, and unmapped members are disallowed.");
                }

                Advance(ref reader);
                reader.Skip();
                continue;
            }

            if (!options.AllowDuplicateProperties && (seen & (1 << member)) != 0)
            {
                throw new JsonException($"{shape.Owner} has a duplicate member \"{shape[member]}\".");
            }

            seen |= 1 << member;
            Advance(ref reader);
            if (member == CaseMember)
            {
                reader.Skip();
                continue;
            }

            return member;
        }

        return NoMoreMembers;
    }

    private static JsonTokenType Advance(ref Utf8JsonReader reader) =>
        reader.Read() ? reader.TokenType : throw new JsonException($"{TypeName} ended before the value closed.");

    private static void EnsureRequiredMembersSeen(Shape shape, int seen, JsonSerializerOptions options)
    {
        var required = options.RespectRequiredConstructorParameters ? shape.AllMembers : shape.RequiredMembers;
        var missing = required & ~seen;
        if (missing != 0)
        {
            throw new JsonException(
                $"{shape.Owner} is missing the required member \"{shape[BitOperations.TrailingZeroCount(missing)]}\".");
        }
    }

    private static T? RefuseNullIfAnnotated<T>(T? value, JsonSerializerOptions options, Shape shape, int member)
        where T : class =>
        value is null && options.RespectNullableAnnotations ? throw NullMember(shape, member) : value;

    private static JsonException NullMember(Shape shape, int member) =>
        new($"{shape.Owner} member \"{shape[member]}\" is null, which it cannot be.");

    private static DamlValue ReadLeaf(ref Utf8JsonReader reader, string caseName, Type leafArm, JsonSerializerOptions options)
    {
        try
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var armJson = JsonObject.Create(document.RootElement)!;
            armJson.Remove(DiscriminatedUnionJson.CaseProperty);
            return (DamlValue)(armJson.Deserialize(leafArm, options)
                ?? throw new JsonException($"{TypeName} deserialized to null."));
        }
        catch (Exception ex) when (ex is not JsonException)
        {
            throw new JsonException($"Cannot read {TypeName} case \"{caseName}\": {ex.Message}", ex);
        }
    }

    private static void WriteValue(Utf8JsonWriter writer, DamlValue value, JsonSerializerOptions options, Shapes shapes)
    {
        switch (value)
        {
            case DamlOptional optional:
                StartArm(writer, nameof(DamlOptional), options);
                WriteNullableValue(writer, shapes.Optional[(int)OptionalMember.Value], optional.Value, options, shapes);
                writer.WriteEndObject();
                break;
            case DamlOptionalChain chain:
                StartArm(writer, nameof(DamlOptionalChain), options);
                WriteNullableValue(writer, shapes.OptionalChain[(int)OptionalMember.Value], chain.Value, options, shapes);
                writer.WriteEndObject();
                break;
            case DamlList list:
                StartArm(writer, nameof(DamlList), options);
                StartArray(writer, shapes.List[(int)ListMember.Values], options);
                foreach (var element in list.Values)
                {
                    WriteElement(writer, element, options, shapes);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                break;
            case DamlTextMap textMap:
                StartArm(writer, nameof(DamlTextMap), options);
                StartArray(writer, shapes.TextMap[(int)MapMember.Entries], options);
                foreach (var (key, entryValue) in textMap.Values)
                {
                    StartObject(writer, options);
                    WriteNullableString(writer, shapes.TextMapEntry[(int)EntryMember.Key], key, options);
                    WriteNullableValue(writer, shapes.TextMapEntry[(int)EntryMember.Value], entryValue, options, shapes);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                break;
            case DamlGenMap genMap:
                StartArm(writer, nameof(DamlGenMap), options);
                WriteGenMapEntries(writer, genMap, options, shapes);
                writer.WriteEndObject();
                break;
            case DamlRecord record:
                StartArm(writer, nameof(DamlRecord), options);
                WriteIdentifier(writer, shapes.Record[(int)RecordMember.RecordId], record.RecordId, options);
                StartArray(writer, shapes.Record[(int)RecordMember.Fields], options);
                foreach (var field in record.Fields)
                {
                    WriteField(writer, field, options, shapes);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                break;
            case DamlVariant variant:
                StartArm(writer, nameof(DamlVariant), options);
                WriteIdentifier(writer, shapes.Variant[(int)VariantMember.VariantId], variant.VariantId, options);
                WriteNullableString(writer, shapes.Variant[(int)VariantMember.Constructor], variant.Constructor, options);
                WriteNullableValue(writer, shapes.Variant[(int)VariantMember.Value], variant.Value, options, shapes);
                writer.WriteEndObject();
                break;
            default:
                WriteLeaf(writer, value, options);
                break;
        }
    }

    private static void WriteGenMapEntries(Utf8JsonWriter writer, DamlGenMap genMap, JsonSerializerOptions options, Shapes shapes)
    {
        StartArray(writer, shapes.GenMap[(int)MapMember.Entries], options);
        foreach (var (key, value) in genMap.Entries)
        {
            StartObject(writer, options);
            WriteNullableValue(writer, shapes.GenMapEntry[(int)EntryMember.Key], key, options, shapes);
            WriteNullableValue(writer, shapes.GenMapEntry[(int)EntryMember.Value], value, options, shapes);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteElement(Utf8JsonWriter writer, DamlValue? element, JsonSerializerOptions options, Shapes shapes)
    {
        if (element is null)
        {
            writer.WriteNullValue();
            return;
        }

        WriteValue(writer, element, options, shapes);
    }

    private static void WriteField(Utf8JsonWriter writer, DamlField? field, JsonSerializerOptions options, Shapes shapes)
    {
        if (field is null)
        {
            writer.WriteNullValue();
            return;
        }

        StartObject(writer, options);
        WriteNullableString(writer, shapes.Field[(int)FieldMember.Label], field.Label, options);
        WriteNullableValue(writer, shapes.Field[(int)FieldMember.Value], field.Value, options, shapes);
        writer.WriteEndObject();
    }

    private static void WriteNullableValue(
        Utf8JsonWriter writer,
        string name,
        DamlValue? value,
        JsonSerializerOptions options,
        Shapes shapes)
    {
        if (value is null)
        {
            WriteNullMember(writer, name, options);
            return;
        }

        writer.WritePropertyName(name);
        WriteValue(writer, value, options, shapes);
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            WriteNullMember(writer, name, options);
            return;
        }

        writer.WriteString(name, value);
    }

    private static void WriteIdentifier(Utf8JsonWriter writer, string name, Identifier? identifier, JsonSerializerOptions options)
    {
        if (identifier is null)
        {
            WriteNullMember(writer, name, options);
            return;
        }

        writer.WritePropertyName(name);
        using var identifierJson = WriterDepth.ParseWithinRemainingDepth(
            JsonSerializer.SerializeToUtf8Bytes(identifier, options), writer, options, TypeName);
        identifierJson.RootElement.WriteTo(writer);
    }

    private static void WriteNullMember(Utf8JsonWriter writer, string name, JsonSerializerOptions options)
    {
        if (options.DefaultIgnoreCondition is not (JsonIgnoreCondition.WhenWritingNull or JsonIgnoreCondition.WhenWritingDefault))
        {
            writer.WriteNull(name);
        }
    }

    private static void StartArm(Utf8JsonWriter writer, string caseName, JsonSerializerOptions options)
    {
        StartObject(writer, options);
        writer.WriteString(DiscriminatedUnionJson.CaseProperty, caseName);
    }

    private static void StartObject(Utf8JsonWriter writer, JsonSerializerOptions options)
    {
        EnsureRoomToNest(writer, options);
        writer.WriteStartObject();
    }

    private static void StartArray(Utf8JsonWriter writer, string name, JsonSerializerOptions options)
    {
        EnsureRoomToNest(writer, options);
        writer.WriteStartArray(name);
    }

    private static void EnsureRoomToNest(Utf8JsonWriter writer, JsonSerializerOptions options) =>
        WriterDepth.EnsureRoomToNest(writer, options, TypeName);

    private static void WriteLeaf(Utf8JsonWriter writer, DamlValue value, JsonSerializerOptions options)
    {
        var arm = value.GetType();
        if (!LeafArms.TryGetValue(arm.Name, out var knownArm) || knownArm != arm)
        {
            throw new JsonException(
                $"Cannot write {TypeName} case \"{arm.Name}\": it is not one of the arms the runtime defines.");
        }

        using var armJson = WriterDepth.ParseWithinRemainingDepth(
            JsonSerializer.SerializeToUtf8Bytes(value, arm, options), writer, options, TypeName);
        StartArm(writer, arm.Name, options);
        foreach (var member in armJson.RootElement.EnumerateObject())
        {
            member.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    private sealed class Shape
    {
        private readonly string[] _members;

        private Shape(string owner, bool carriesCase, string[] members, int requiredMembers)
        {
            Owner = owner;
            CarriesCase = carriesCase;
            _members = members;
            RequiredMembers = requiredMembers;
        }

        public string Owner { get; }

        public bool CarriesCase { get; }

        public int RequiredMembers { get; }

        public int AllMembers => (1 << _members.Length) - 1;

        public string this[int member] =>
            member == CaseMember ? DiscriminatedUnionJson.CaseProperty : _members[member];

        public static Shape Of<TMember>(string owner, bool carriesCase, JsonNamingPolicy? namingPolicy, params TMember[] required)
            where TMember : struct, Enum =>
            new(
                owner,
                carriesCase,
                Enum.GetNames<TMember>().Select(name => namingPolicy?.ConvertName(name) ?? name).ToArray(),
                required.Aggregate(0, (mask, member) => mask | (1 << Convert.ToInt32(member, System.Globalization.CultureInfo.InvariantCulture))));

        public int Match(ref Utf8JsonReader reader, bool caseInsensitive)
        {
            for (var member = 0; member < _members.Length; member++)
            {
                if (reader.ValueTextEquals(_members[member]))
                {
                    return member;
                }
            }

            if (CarriesCase && reader.ValueTextEquals(DiscriminatedUnionJson.CaseProperty))
            {
                return CaseMember;
            }

            return caseInsensitive ? MatchIgnoringCase(reader.GetString()!) : UnmappedMember;
        }

        private int MatchIgnoringCase(string name)
        {
            var member = Array.FindIndex(_members, candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));
            if (member >= 0)
            {
                return member;
            }

            return CarriesCase && string.Equals(name, DiscriminatedUnionJson.CaseProperty, StringComparison.OrdinalIgnoreCase)
                ? CaseMember
                : UnmappedMember;
        }
    }

    private sealed class Shapes(JsonNamingPolicy? namingPolicy)
    {
        public Shape Optional { get; } = Shape.Of<OptionalMember>(nameof(DamlOptional), true, namingPolicy);

        public Shape OptionalChain { get; } = Shape.Of<OptionalMember>(nameof(DamlOptionalChain), true, namingPolicy);

        public Shape List { get; } = Shape.Of(nameof(DamlList), true, namingPolicy, ListMember.Values);

        public Shape TextMap { get; } = Shape.Of(nameof(DamlTextMap), true, namingPolicy, MapMember.Entries);

        public Shape TextMapEntry { get; } =
            Shape.Of($"{nameof(DamlTextMap)} entry", false, namingPolicy, EntryMember.Key, EntryMember.Value);

        public Shape GenMap { get; } = Shape.Of(nameof(DamlGenMap), true, namingPolicy, MapMember.Entries);

        public Shape GenMapDeclaredAsItself { get; } = Shape.Of(nameof(DamlGenMap), false, namingPolicy, MapMember.Entries);

        public Shape GenMapEntry { get; } =
            Shape.Of($"{nameof(DamlGenMap)} entry", false, namingPolicy, EntryMember.Key, EntryMember.Value);

        public Shape Record { get; } = Shape.Of(nameof(DamlRecord), true, namingPolicy, RecordMember.Fields);

        public Shape Field { get; } = Shape.Of<FieldMember>(nameof(DamlField), false, namingPolicy);

        public Shape Variant { get; } =
            Shape.Of(nameof(DamlVariant), true, namingPolicy, VariantMember.Constructor, VariantMember.Value);
    }
}
