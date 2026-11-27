// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Serialization;

namespace Daml.Runtime;

/// <summary>
/// A position in the participant's ledger stream. Replaces raw <see cref="long"/>
/// offsets so callers use <see cref="Begin"/> instead of a magic <c>0</c>/<c>null</c>.
/// </summary>
[JsonConverter(typeof(LedgerOffsetJsonConverter))]
[AbsenceBindsAnUnstatedValue]
public readonly record struct LedgerOffset
{
    private LedgerOffset(long value) => Value = value;

    /// <summary>The well-known participant start of stream.</summary>
    public static LedgerOffset Begin { get; } = new(0);

    /// <summary>A concrete offset at <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public static LedgerOffset At(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return new(value);
    }

    /// <summary>The underlying participant offset.</summary>
    public long Value { get; }
}

/// <summary>
/// System.Text.Json converter for <see cref="LedgerOffset"/>. Reads and writes the bare JSON
/// number a participant puts on its <c>offset</c> fields, so an offset that was persisted
/// resumes the stream where it left off instead of restarting from <see cref="LedgerOffset.Begin"/>.
/// </summary>
internal sealed class LedgerOffsetJsonConverter : JsonConverter<LedgerOffset>
{
    /// <inheritdoc/>
    public override bool HandleNull => true;

    /// <remarks>
    /// A JSON null on a non-nullable offset is refused as a <see cref="JsonException"/> naming
    /// the type rather than read as a silent <see cref="LedgerOffset.Begin"/>, matching the
    /// posture <see cref="Daml.Runtime.Serialization.OpaqueStringIdJsonConverter{TId}"/> holds
    /// for the identity structs. A <c>LedgerOffset?</c> is unaffected: the serializer
    /// short-circuits null for it before the converter runs. The offset is always a bare JSON
    /// number, whatever <see cref="JsonSerializerOptions.NumberHandling"/> a host sets: a
    /// quoted offset is refused on read and never produced on write.
    /// </remarks>
    public override LedgerOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw new JsonException(
                $"Expected number token for {nameof(LedgerOffset)}, got {reader.TokenType}.");
        }

        if (!reader.TryGetInt64(out var value))
        {
            var raw = RawToken(ref reader);
            throw new JsonException(
                raw.AsSpan().ContainsAny('.', 'e', 'E')
                    ? $"{nameof(LedgerOffset)} must be a whole-number literal; fractional and " +
                      $"exponent forms are refused, got {raw}."
                    : $"{nameof(LedgerOffset)} must lie within Int64 range; got {raw}.");
        }

        try
        {
            return LedgerOffset.At(value);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new JsonException($"{nameof(LedgerOffset)} cannot be negative; got {value}.", ex);
        }
    }

    private static string RawToken(ref Utf8JsonReader reader) =>
        Encoding.UTF8.GetString(
            reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, LedgerOffset value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);
}
