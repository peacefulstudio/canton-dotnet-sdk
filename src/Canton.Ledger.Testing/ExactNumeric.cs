// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Canton.Ledger.Testing;

internal readonly partial record struct ExactNumeric : IComparable<ExactNumeric>, IComparable
{
    private readonly BigInteger unscaled;
    private readonly int scale;

    private ExactNumeric(BigInteger unscaled, int scale)
    {
        if (unscaled.IsZero)
        {
            this.unscaled = BigInteger.Zero;
            this.scale = 0;
            return;
        }

        while ((unscaled % 10).IsZero)
        {
            unscaled /= 10;
            scale--;
        }

        this.unscaled = unscaled;
        this.scale = scale;
    }

    public static ExactNumeric Parse(string text)
    {
        var match = NumericText().Match(text);
        if (!match.Success)
            throw new FormatException($"'{text}' is not a numeric literal.");

        var integerDigits = match.Groups["int"].Value;
        var fractionDigits = match.Groups["frac"].Value;
        if (integerDigits.Length + fractionDigits.Length == 0)
            throw new FormatException($"'{text}' is not a numeric literal.");

        var digits = BigInteger.Parse(integerDigits + fractionDigits, CultureInfo.InvariantCulture);
        var signed = match.Groups["sign"].Value == "-" ? -digits : digits;
        var exponent = match.Groups["exp"].Success ? int.Parse(match.Groups["exp"].Value, CultureInfo.InvariantCulture) : 0;
        return new(signed, fractionDigits.Length - exponent);
    }

    public static ExactNumeric From(decimal value)
    {
        var parts = decimal.GetBits(value);
        var magnitude = new BigInteger(((ulong)(uint)parts[1] << 32) | (uint)parts[0])
            + (new BigInteger((uint)parts[2]) << 64);
        var isNegative = parts[3] < 0;
        var decimalScale = (parts[3] >> 16) & 0xFF;
        return new(isNegative ? -magnitude : magnitude, decimalScale);
    }

    public int CompareTo(ExactNumeric other)
    {
        var commonScale = Math.Max(scale, other.scale);
        return Align(commonScale).CompareTo(other.Align(commonScale));
    }

    public int CompareTo(object? obj) =>
        obj is ExactNumeric other ? CompareTo(other) : throw new ArgumentException("Object is not an ExactNumeric.", nameof(obj));

    private BigInteger Align(int commonScale) => unscaled * BigInteger.Pow(10, commonScale - scale);

    [GeneratedRegex(@"^\s*(?<sign>[+-]?)(?<int>\d*)(?:\.(?<frac>\d*))?(?:[eE](?<exp>[+-]?\d+))?\s*$")]
    private static partial Regex NumericText();
}
