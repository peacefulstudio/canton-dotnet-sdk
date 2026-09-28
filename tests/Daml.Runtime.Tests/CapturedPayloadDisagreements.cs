// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Data;

namespace Daml.Runtime.Tests;

/// <summary>
/// Compares a writer's output with a participant capture node by node, naming every path where
/// the two disagree.
/// </summary>
internal static class CapturedPayloadDisagreements
{
    internal static IReadOnlyList<string> Between(
        JsonElement captured, JsonElement written, string path)
    {
        if (captured.ValueKind != written.ValueKind)
        {
            return [$"{path}: captured {captured.ValueKind} '{captured.GetRawText()}' "
                + $"but wrote {written.ValueKind} '{written.GetRawText()}'"];
        }

        return captured.ValueKind switch
        {
            JsonValueKind.Object => ObjectDisagreements(captured, written, path),
            JsonValueKind.Array => ArrayDisagreements(captured, written, path),
            JsonValueKind.String => StringDisagreements(captured, written, path),
            _ => captured.GetRawText() == written.GetRawText()
                ? []
                : [$"{path}: captured '{captured.GetRawText()}' but wrote '{written.GetRawText()}'"],
        };
    }

    private static IReadOnlyList<string> ObjectDisagreements(
        JsonElement captured, JsonElement written, string path)
    {
        var capturedNames = captured.EnumerateObject().Select(property => property.Name).ToList();
        var writtenNames = written.EnumerateObject().Select(property => property.Name).ToList();
        if (capturedNames.Count != writtenNames.Count || capturedNames.Except(writtenNames).Any())
        {
            return [$"{path}: captured fields [{string.Join(", ", capturedNames)}] "
                + $"but wrote [{string.Join(", ", writtenNames)}]"];
        }

        return [.. capturedNames.SelectMany(name => Between(
            captured.GetProperty(name), written.GetProperty(name), $"{path}.{name}"))];
    }

    private static IReadOnlyList<string> ArrayDisagreements(
        JsonElement captured, JsonElement written, string path)
    {
        if (captured.GetArrayLength() != written.GetArrayLength())
        {
            return [$"{path}: captured {captured.GetArrayLength()} elements "
                + $"but wrote {written.GetArrayLength()}"];
        }

        return [.. captured.EnumerateArray().Zip(written.EnumerateArray())
            .SelectMany((pair, index) =>
                Between(pair.First, pair.Second, $"{path}[{index}]"))];
    }

    /// <summary>
    /// A participant pads a Numeric out to its declared scale; the declared scale is not on the
    /// wire, so the writer emits the canonical unpadded form of the same value. Every other
    /// string-valued arm has to reproduce the capture verbatim.
    /// </summary>
    private static IReadOnlyList<string> StringDisagreements(
        JsonElement captured, JsonElement written, string path)
    {
        var capturedText = captured.GetString()!;
        var writtenText = written.GetString()!;
        if (capturedText == writtenText)
        {
            return [];
        }

        var agreesAsNumeric = DamlNumeric.TryParseCanonical(capturedText, out var capturedNumeric)
            && DamlNumeric.TryParseCanonical(writtenText, out var writtenNumeric)
            && capturedNumeric.Equals(writtenNumeric);

        return agreesAsNumeric ? [] : [$"{path}: captured '{capturedText}' but wrote '{writtenText}'"];
    }
}
