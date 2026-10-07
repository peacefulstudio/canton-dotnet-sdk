// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Canton.Ledger.Abstractions;

namespace Canton.Ledger.Testing;

internal static class PqsLeafValues
{
    private const long TicksPerMicrosecond = 10;
    private static readonly DateTime UnixEpoch = DateTime.UnixEpoch;

    public static IComparable Cast(PqsLeafKind kind, string text) =>
        kind switch
        {
            PqsLeafKind.Int64 => ParseInt64(text),
            PqsLeafKind.Numeric => ExactNumeric.Parse(text),
            PqsLeafKind.Bool => ParseBool(text),
            PqsLeafKind.Date => ParseDate(text),
            PqsLeafKind.Time => ParseTimestamp(text),
            _ => text,
        };

    public static IComparable FromOperand(PqsOperand operand) =>
        operand.Value switch
        {
            string text when operand.Kind == PqsLeafKind.Numeric => ExactNumeric.Parse(text),
            string text => text,
            long number => number,
            decimal number => ExactNumeric.From(number),
            bool flag => flag,
            DateOnly date => DateValue(date),
            DateTimeOffset instant => TimestampValue(instant),
            _ => throw new ArgumentOutOfRangeException(nameof(operand), operand.Value, "Unsupported PQS operand value."),
        };

    private static long ParseInt64(string text) =>
        long.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
            CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException($"'{text}' is not a valid bigint.");

    private static bool ParseBool(string text)
    {
        var word = text.Trim().ToLowerInvariant();
        if (word.Length > 0 && ("true".StartsWith(word, StringComparison.Ordinal) || "yes".StartsWith(word, StringComparison.Ordinal) || word == "on" || word == "1"))
            return true;
        if (word.Length > 0 && ("false".StartsWith(word, StringComparison.Ordinal) || "no".StartsWith(word, StringComparison.Ordinal) || word == "0"
            || (word.Length >= 2 && "off".StartsWith(word, StringComparison.Ordinal))))
            return false;
        throw new FormatException($"'{text}' is not a valid boolean.");
    }

    private static int ParseDate(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("infinity", StringComparison.OrdinalIgnoreCase)) return int.MaxValue;
        if (trimmed.Equals("-infinity", StringComparison.OrdinalIgnoreCase)) return int.MinValue;
        return DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.DayNumber
            : throw new FormatException($"'{text}' is not a valid date.");
    }

    private static long ParseTimestamp(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Equals("infinity", StringComparison.OrdinalIgnoreCase)) return long.MaxValue;
        if (trimmed.Equals("-infinity", StringComparison.OrdinalIgnoreCase)) return long.MinValue;
        if (!DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant))
            throw new FormatException($"'{text}' is not a valid timestamptz.");
        return RoundedMicroseconds(instant);
    }

    private static int DateValue(DateOnly date) =>
        date == DateOnly.MinValue ? int.MinValue
        : date == DateOnly.MaxValue ? int.MaxValue
        : date.DayNumber;

    private static long TimestampValue(DateTimeOffset instant) =>
        instant == DateTimeOffset.MinValue ? long.MinValue
        : instant == DateTimeOffset.MaxValue ? long.MaxValue
        : TruncatedMicroseconds(instant);

    private static long TruncatedMicroseconds(DateTimeOffset instant) =>
        FloorDivide(instant.UtcTicks - UnixEpoch.Ticks, TicksPerMicrosecond);

    private static long RoundedMicroseconds(DateTimeOffset instant) =>
        FloorDivide(instant.UtcTicks - UnixEpoch.Ticks + TicksPerMicrosecond / 2, TicksPerMicrosecond);

    private static long FloorDivide(long dividend, long divisor) =>
        dividend >= 0 ? dividend / divisor : -((-dividend + divisor - 1) / divisor);
}
