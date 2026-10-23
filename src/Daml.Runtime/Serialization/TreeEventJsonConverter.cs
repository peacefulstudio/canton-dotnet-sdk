// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Daml.Runtime.Contracts;

namespace Daml.Runtime.Serialization;

/// <summary>
/// The <see cref="System.Text.Json"/> converter a value declared as <see cref="TreeEvent"/> takes
/// from the attribute on that type: the arm's own JSON object with a <c>"$case"</c> discriminator
/// naming the arm inserted first, e.g. <c>{"$case":"Created","EventId":"1:0",…}</c>.
/// </summary>
/// <remarks>
/// <para>
/// An arm's members other than <see cref="TreeEvent.Exercised.ChildEvents"/> take their contract from
/// the arm's own type, so <see cref="JsonSerializerOptions.PropertyNamingPolicy"/>, unmapped-member
/// handling, required and nullable members and the ignore conditions apply to them as they would to
/// the arm declared as itself. <see cref="TreeEvent.Exercised.ChildEvents"/> is read and written here,
/// on the caller's own <see cref="Utf8JsonReader"/> and <see cref="Utf8JsonWriter"/>, and each child
/// recurses into this converter directly rather than through <see cref="JsonSerializer"/>. A level of
/// nesting therefore costs a few small frames of call stack, and
/// <see cref="JsonSerializerOptions.MaxDepth"/> bounds the nesting: the reader refuses a payload
/// nested deeper, and the writer refuses a tree nested deeper, with a <see cref="JsonException"/>.
/// </para>
/// <para>
/// Because <see cref="JsonConverterAttribute"/> is not inherited, an arm declared as itself keeps its
/// plain object contract with no discriminator; its child events still carry theirs. A
/// <see cref="JsonSerializerOptions.ReferenceHandler"/> is refused, as it is for the other
/// discriminated unions.
/// </para>
/// </remarks>
internal sealed class TreeEventJsonConverter : JsonConverter<TreeEvent>
{
    private const string TypeName = nameof(TreeEvent);

    private const string ChildEventsMember = nameof(TreeEvent.Exercised.ChildEvents);

    public override TreeEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        DiscriminatedUnionJson.EnsureNoReferenceHandler(options, TypeName);
        return ReadEvent(ref reader, options, ChildEventsName(options));
    }

    public override void Write(Utf8JsonWriter writer, TreeEvent value, JsonSerializerOptions options)
    {
        DiscriminatedUnionJson.EnsureNoReferenceHandler(options, TypeName);
        WriteEvent(writer, value, options, ChildEventsName(options));
    }

    private static string ChildEventsName(JsonSerializerOptions options) =>
        options.PropertyNamingPolicy?.ConvertName(ChildEventsMember) ?? ChildEventsMember;

    private static TreeEvent ReadEvent(ref Utf8JsonReader reader, JsonSerializerOptions options, string childEventsName)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected object token for {TypeName}, got {reader.TokenType}.");
        }

        var arm = ArmNamed(PeekCase(reader));
        var nodeOptions = new JsonNodeOptions { PropertyNameCaseInsensitive = options.PropertyNameCaseInsensitive };
        var members = new JsonObject(nodeOptions);
        EquatableArray<TreeEvent>? childEvents = null;
        var caseSeen = false;
        while (Advance(ref reader) != JsonTokenType.EndObject)
        {
            var name = reader.GetString()!;
            Advance(ref reader);
            if (NameMatches(name, DiscriminatedUnionJson.CaseProperty, options))
            {
                RefuseDuplicate(caseSeen, DiscriminatedUnionJson.CaseProperty, arm, options);
                caseSeen = true;
                reader.Skip();
            }
            else if (arm == typeof(TreeEvent.Exercised)
                && reader.TokenType != JsonTokenType.Null
                && NameMatches(name, childEventsName, options))
            {
                RefuseDuplicate(childEvents is not null, childEventsName, arm, options);
                childEvents = ReadChildEvents(ref reader, options, childEventsName);
            }
            else
            {
                RefuseDuplicate(members.ContainsKey(name), name, arm, options);
                members[name] = JsonNode.Parse(ref reader, nodeOptions);
            }
        }

        if (childEvents is not null)
        {
            members[childEventsName] = new JsonArray();
        }

        var read = (TreeEvent)(members.Deserialize(arm, options)
            ?? throw new JsonException($"{TypeName} case \"{arm.Name}\" deserialized to null."));
        return childEvents is { } children ? ((TreeEvent.Exercised)read) with { ChildEvents = children } : read;
    }

    private static Type ArmNamed(string caseName) =>
        caseName switch
        {
            nameof(TreeEvent.Created) => typeof(TreeEvent.Created),
            nameof(TreeEvent.Exercised) => typeof(TreeEvent.Exercised),
            _ => throw new JsonException(
                $"{TypeName} names an unknown case \"{caseName}\"; expected one of {nameof(TreeEvent.Created)}, {nameof(TreeEvent.Exercised)}."),
        };

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

    private static EquatableArray<TreeEvent> ReadChildEvents(ref Utf8JsonReader reader, JsonSerializerOptions options, string childEventsName)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException(
                $"{nameof(TreeEvent.Exercised)} member \"{childEventsName}\" must be an array, got {reader.TokenType}.");
        }

        var children = new List<TreeEvent>();
        while (Advance(ref reader) != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                throw new JsonException($"{nameof(TreeEvent.Exercised)} member \"{childEventsName}\" holds a null element.");
            }

            children.Add(ReadEvent(ref reader, options, childEventsName));
        }

        return EquatableArray.Create(children);
    }

    private static bool NameMatches(string name, string expected, JsonSerializerOptions options) =>
        string.Equals(name, expected, options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void RefuseDuplicate(bool alreadySeen, string name, Type arm, JsonSerializerOptions options)
    {
        if (alreadySeen && !options.AllowDuplicateProperties)
        {
            throw new JsonException($"{TypeName} case \"{arm.Name}\" has a duplicate member \"{name}\".");
        }
    }

    private static JsonTokenType Advance(ref Utf8JsonReader reader) =>
        reader.Read() ? reader.TokenType : throw new JsonException($"{TypeName} ended before the value closed.");

    private static void WriteEvent(Utf8JsonWriter writer, TreeEvent value, JsonSerializerOptions options, string childEventsName)
    {
        var (arm, childEvents) = value switch
        {
            TreeEvent.Created created => (JsonSerializer.SerializeToUtf8Bytes(created, options), (EquatableArray<TreeEvent>?)null),
            TreeEvent.Exercised exercised => (
                JsonSerializer.SerializeToUtf8Bytes(exercised with { ChildEvents = EquatableArray<TreeEvent>.Empty }, options),
                exercised.ChildEvents),
            _ => throw new JsonException($"Cannot write {TypeName} case \"{value.GetType().Name}\": it is not one of the arms the runtime defines."),
        };

        using var members = WriterDepth.ParseWithinRemainingDepth(arm, writer, options, TypeName);
        writer.WriteStartObject();
        writer.WriteString(DiscriminatedUnionJson.CaseProperty, value.GetType().Name);
        foreach (var member in members.RootElement.EnumerateObject())
        {
            if (childEvents is { } children && member.NameEquals(childEventsName))
            {
                WriteChildEvents(writer, children, options, childEventsName);
                childEvents = null;
                continue;
            }

            member.WriteTo(writer);
        }

        if (childEvents is { } unwritten)
        {
            WriteChildEvents(writer, unwritten, options, childEventsName);
        }

        writer.WriteEndObject();
    }

    private static void WriteChildEvents(
        Utf8JsonWriter writer,
        EquatableArray<TreeEvent> children,
        JsonSerializerOptions options,
        string childEventsName)
    {
        EnsureRoomToNest(writer, options);
        writer.WriteStartArray(childEventsName);
        foreach (var child in children)
        {
            WriteEvent(
                writer,
                child ?? throw new JsonException($"{nameof(TreeEvent.Exercised)} member \"{childEventsName}\" holds a null element."),
                options,
                childEventsName);
        }

        writer.WriteEndArray();
    }

    private static void EnsureRoomToNest(Utf8JsonWriter writer, JsonSerializerOptions options) =>
        WriterDepth.EnsureRoomToNest(writer, options, TypeName);
}
