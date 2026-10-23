// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

namespace Daml.Runtime.Serialization;

/// <summary>
/// Holds a hand-written converter's output to <see cref="JsonSerializerOptions.MaxDepth"/>, counted from
/// the caller's <see cref="Utf8JsonWriter"/>, so that what it writes is what a reader on the same options
/// accepts. A member serialized apart from that writer starts again at depth zero, and a
/// <see cref="Utf8JsonWriter"/> the caller built carries its own, larger limit, so neither can be left to
/// check itself.
/// </summary>
internal static class WriterDepth
{
    private const int DefaultMaxDepth = 64;

    /// <summary>Refuses to open a container where the reader would refuse one.</summary>
    internal static void EnsureRoomToNest(Utf8JsonWriter writer, JsonSerializerOptions options, string typeName)
    {
        var maxDepth = MaxDepth(options);
        if (writer.CurrentDepth >= maxDepth)
        {
            throw NestedTooDeep(typeName, maxDepth, null);
        }
    }

    /// <summary>
    /// Parses JSON serialized apart from <paramref name="writer"/>, to be written at its current depth,
    /// refusing it when its nesting would take the writer past <see cref="JsonSerializerOptions.MaxDepth"/>.
    /// </summary>
    internal static JsonDocument ParseWithinRemainingDepth(
        byte[] json,
        Utf8JsonWriter writer,
        JsonSerializerOptions options,
        string typeName)
    {
        EnsureRoomToNest(writer, options, typeName);
        var maxDepth = MaxDepth(options);
        try
        {
            return JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = maxDepth - writer.CurrentDepth });
        }
        catch (JsonException ex)
        {
            throw NestedTooDeep(typeName, maxDepth, ex);
        }
    }

    private static int MaxDepth(JsonSerializerOptions options) => options.MaxDepth == 0 ? DefaultMaxDepth : options.MaxDepth;

    private static JsonException NestedTooDeep(string typeName, int maxDepth, Exception? inner) =>
        new($"{typeName} is nested deeper than the maximum depth of {maxDepth}; raise JsonSerializerOptions.MaxDepth to write it.", inner);
}
