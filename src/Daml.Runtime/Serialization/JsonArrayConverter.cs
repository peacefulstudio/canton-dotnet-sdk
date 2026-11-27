// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daml.Runtime.Serialization;

internal abstract class JsonArrayConverter<TCollection, TElement>(string typeName) : JsonConverter<TCollection>
{
    protected string TypeName { get; } = typeName;

    protected abstract TCollection Create(List<TElement> elements);

    protected TCollection ReadArray(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected array token for {TypeName}, got {reader.TokenType}.");
        }

        var elements = new List<TElement>();
        while (true)
        {
            if (!reader.Read())
            {
                throw new JsonException($"Unexpected end of JSON while reading {TypeName}.");
            }

            if (reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            elements.Add(ReadElement(ref reader, elements.Count, options));
        }

        try
        {
            return Create(elements);
        }
        catch (ArgumentException ex)
        {
            throw new JsonException($"Invalid element in {TypeName}: {ex.Message}", ex);
        }
    }

    protected void WriteArray(Utf8JsonWriter writer, IEnumerable<TElement> elements, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        var index = 0;
        foreach (var element in elements)
        {
            WriteElement(writer, element, index++, options);
        }
        writer.WriteEndArray();
    }

    private TElement ReadElement(ref Utf8JsonReader reader, int index, JsonSerializerOptions options)
    {
        try
        {
            return JsonSerializer.Deserialize<TElement>(ref reader, options)!;
        }
        catch (Exception ex)
        {
            throw new JsonException($"Cannot read element {index} of {TypeName}: {ex.Message}", ex);
        }
    }

    private void WriteElement(Utf8JsonWriter writer, TElement element, int index, JsonSerializerOptions options)
    {
        try
        {
            JsonSerializer.Serialize(writer, element, options);
        }
        catch (Exception ex)
        {
            throw new JsonException($"Cannot write element {index} of {TypeName}: {ex.Message}", ex);
        }
    }
}
