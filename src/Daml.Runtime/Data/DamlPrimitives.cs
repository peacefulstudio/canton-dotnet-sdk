// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Data;

/// <summary>
/// Represents a Daml Int value (64-bit signed integer).
/// </summary>
public sealed record DamlInt64(long Value) : DamlValue
{
    /// <summary>Unwraps the underlying 64-bit integer.</summary>
    public static implicit operator long(DamlInt64 value) => value.Value;

    /// <summary>Wraps a 64-bit integer as a Daml Int value.</summary>
    public static implicit operator DamlInt64(long value) => new(value);
}

/// <summary>
/// Represents a Daml Numeric value (fixed-point decimal).
/// </summary>
/// <remarks>
/// A Daml Numeric carries up to 38 significant digits on the ledger. This type
/// backs the value with a sign, a <see cref="BigInteger"/> unscaled mantissa, and
/// an integer mantissa scale, so it round-trips any legal Daml-LF Numeric —
/// including magnitudes above <see cref="decimal.MaxValue"/> — with zero precision
/// loss. <see cref="Value"/> narrows to a <see cref="decimal"/> for convenience and
/// throws <see cref="OverflowException"/> rather than silently rounding when the
/// stored value has more precision than a <see cref="decimal"/> can represent
/// exactly.
/// <para>
/// Equality and hashing compare the numeric value only, normalized by stripping
/// trailing mantissa zeros: <see cref="Scale"/> is not part of the wire format
/// (<see cref="Serialization.DamlJsonSerializer"/> never writes it and
/// deserialization reconstructs the default of 10), so two Numerics with the same
/// value but different <see cref="Scale"/> hints — or different mantissa
/// precision, e.g. <c>1.50</c> vs <c>1.5</c> — are equal. The <see cref="Scale"/>
/// property is retained as the hook for future scale-padded reading.
/// </para>
/// </remarks>
public sealed record DamlNumeric : DamlValue
{
    private const int MaxSignificantDigits = 38;
    private const int MaxMantissaScale = 37;
    private const int MaxDecimalMantissaScale = 28;
    private const int DefaultScale = 10;

    private static readonly BigInteger MaxDecimalUnscaledMagnitude = (BigInteger.One << 96) - 1;
    private static readonly BigInteger UInt32Mask = uint.MaxValue;

    private readonly bool _isNegative;
    private readonly BigInteger _unscaledMagnitude;
    private readonly int _mantissaScale;

    /// <summary>The scale (number of decimal places) of the numeric; not part of the wire format.</summary>
    public int Scale { get; }

    /// <summary>Creates a Daml Numeric from a <see cref="decimal"/>, preserving its exact bit pattern.</summary>
    public DamlNumeric(decimal value, int scale = DefaultScale)
        : this(DecomposeDecimal(value), scale)
    {
    }

    private DamlNumeric((bool IsNegative, BigInteger UnscaledMagnitude, int MantissaScale) decomposed, int scale)
        : this(decomposed.IsNegative, decomposed.UnscaledMagnitude, decomposed.MantissaScale, scale)
    {
    }

    private DamlNumeric(bool isNegative, BigInteger unscaledMagnitude, int mantissaScale, int scale)
    {
        if (unscaledMagnitude.Sign < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unscaledMagnitude), unscaledMagnitude,
                "Unscaled magnitude must be non-negative; sign is tracked separately.");
        }
        if (mantissaScale is < 0 or > MaxMantissaScale)
        {
            throw new ArgumentOutOfRangeException(nameof(mantissaScale), mantissaScale,
                $"Daml-LF Numeric scale must be between 0 and {MaxMantissaScale}.");
        }
        var digitCount = unscaledMagnitude.IsZero ? 1 : unscaledMagnitude.ToString(CultureInfo.InvariantCulture).Length;
        if (digitCount > MaxSignificantDigits)
        {
            throw new ArgumentOutOfRangeException(nameof(unscaledMagnitude), unscaledMagnitude,
                $"Daml-LF Numeric supports at most {MaxSignificantDigits} significant digits.");
        }

        _isNegative = isNegative && !unscaledMagnitude.IsZero;
        _unscaledMagnitude = unscaledMagnitude;
        _mantissaScale = mantissaScale;
        Scale = scale;
    }

    /// <summary>
    /// The <see cref="decimal"/> narrowing of this value.
    /// </summary>
    /// <exception cref="OverflowException">
    /// The stored value cannot be represented exactly as a <see cref="decimal"/>: its magnitude
    /// exceeds <see cref="decimal.MaxValue"/>, or it still has more fractional digits than
    /// <see cref="decimal"/> can hold once trailing mantissa zeros are stripped. A participant
    /// pads a Numeric out to its declared scale, so that padding never decides representability.
    /// The narrowing throws rather than silently rounding.
    /// </exception>
    public decimal Value =>
        TryToDecimal(out var value)
            ? value
            : throw new OverflowException(
                $"DamlNumeric value '{ToCanonicalString()}' has more precision than decimal can represent exactly (decimal supports at most 28-29 significant digits).");

    private bool TryToDecimal(out decimal value)
    {
        if (TryNarrow(_unscaledMagnitude, _mantissaScale, out value))
        {
            return true;
        }

        var (strippedMantissa, strippedScale, _) = Normalize(_unscaledMagnitude, _mantissaScale, _isNegative);
        return TryNarrow(strippedMantissa, strippedScale, out value);
    }

    private bool TryNarrow(BigInteger unscaledMagnitude, int mantissaScale, out decimal value)
    {
        if (mantissaScale > MaxDecimalMantissaScale || unscaledMagnitude > MaxDecimalUnscaledMagnitude)
        {
            value = default;
            return false;
        }

        var lo = unchecked((int)(uint)(unscaledMagnitude & UInt32Mask));
        var mid = unchecked((int)(uint)((unscaledMagnitude >> 32) & UInt32Mask));
        var hi = unchecked((int)(uint)((unscaledMagnitude >> 64) & UInt32Mask));
        value = new decimal(lo, mid, hi, _isNegative, (byte)mantissaScale);
        return true;
    }

    /// <summary>
    /// Formats this value in the exact canonical wire shape: no scientific notation,
    /// trailing mantissa zeros stripped down to a single guaranteed fractional digit.
    /// </summary>
    /// <returns>The canonical decimal string (<c>-?digits.digits</c>), losslessly
    /// round-trippable through <see cref="TryParseCanonical"/> even for magnitudes or
    /// fractional precision beyond what <see cref="decimal"/> can represent.</returns>
    public string ToCanonicalString()
    {
        var digits = _unscaledMagnitude.ToString(CultureInfo.InvariantCulture).PadLeft(_mantissaScale + 1, '0');
        var integerPart = digits[..^_mantissaScale];
        var fractionalPart = digits[^_mantissaScale..].TrimEnd('0');
        if (fractionalPart.Length == 0)
        {
            fractionalPart = "0";
        }
        return _isNegative ? $"-{integerPart}.{fractionalPart}" : $"{integerPart}.{fractionalPart}";
    }

    /// <summary>
    /// Parses the exact canonical wire shape (<c>-?digits(.digits?)?</c>, no exponent, where the
    /// bare trailing dot is the participant's <c>Numeric 0</c> form, e.g. <c>42.</c>)
    /// into a <see cref="DamlNumeric"/> with zero precision loss, rejecting magnitudes
    /// or scales beyond the Daml-LF Numeric bound (38 significant digits, scale 0-37).
    /// </summary>
    /// <param name="text">The canonical numeric text to parse.</param>
    /// <param name="result">On success, the parsed value; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="text"/> is a well-formed canonical
    /// Numeric within the Daml-LF bound; otherwise <see langword="false"/>.</returns>
    public static bool TryParseCanonical(string text, out DamlNumeric result)
    {
        result = null!;
        if (text is null)
        {
            return false;
        }
        var isNegative = text.StartsWith('-');
        var digitsStart = isNegative ? 1 : 0;
        if (digitsStart >= text.Length)
        {
            return false;
        }

        var dotIndex = text.IndexOf('.', digitsStart);
        var integerPart = dotIndex < 0 ? text[digitsStart..] : text[digitsStart..dotIndex];
        var fractionalPart = dotIndex < 0 ? string.Empty : text[(dotIndex + 1)..];
        if (integerPart.Length == 0)
        {
            return false;
        }
        if (!IsAllAsciiDigits(integerPart) || !IsAllAsciiDigits(fractionalPart))
        {
            return false;
        }

        var mantissaScale = fractionalPart.Length;
        if (integerPart.Length + fractionalPart.Length > MaxSignificantDigits || mantissaScale > MaxMantissaScale)
        {
            return false;
        }

        var unscaledMagnitude = BigInteger.Parse(integerPart + fractionalPart, CultureInfo.InvariantCulture);
        result = new DamlNumeric(isNegative, unscaledMagnitude, mantissaScale, DefaultScale);
        return true;
    }

    private static bool IsAllAsciiDigits(string s)
    {
        foreach (var c in s)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }
        return true;
    }

    private static (bool IsNegative, BigInteger UnscaledMagnitude, int MantissaScale) DecomposeDecimal(decimal value)
    {
        var bits = decimal.GetBits(value);
        var isNegative = (bits[3] & unchecked((int)0x80000000)) != 0;
        var mantissaScale = (bits[3] >> 16) & 0x7F;
        var unscaledMagnitude = ((BigInteger)(uint)bits[2] << 64) | ((BigInteger)(uint)bits[1] << 32) | (uint)bits[0];
        return (isNegative, unscaledMagnitude, mantissaScale);
    }

    /// <summary>Unwraps the underlying decimal, discarding the Daml scale.</summary>
    /// <exception cref="OverflowException">The stored value has more precision than <see cref="decimal"/> can represent exactly.</exception>
    public static implicit operator decimal(DamlNumeric value) => value.Value;

    /// <summary>Wraps a decimal as a Daml Numeric with the default scale of 10.</summary>
    public static implicit operator DamlNumeric(decimal value) => new(value);

    /// <summary>
    /// Compares by numeric value only, normalized across differing mantissa precision;
    /// <see cref="Scale"/> never reaches the wire, so including it would break round-trip
    /// equality for any non-default scale.
    /// </summary>
    public bool Equals(DamlNumeric? other)
    {
        if (other is null)
        {
            return false;
        }
        var (mantissaA, scaleA, negativeA) = Normalize(_unscaledMagnitude, _mantissaScale, _isNegative);
        var (mantissaB, scaleB, negativeB) = Normalize(other._unscaledMagnitude, other._mantissaScale, other._isNegative);
        return negativeA == negativeB && scaleA == scaleB && mantissaA == mantissaB;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var (mantissa, scale, negative) = Normalize(_unscaledMagnitude, _mantissaScale, _isNegative);
        return HashCode.Combine(negative, scale, mantissa);
    }

    private static (BigInteger Mantissa, int Scale, bool IsNegative) Normalize(BigInteger mantissa, int scale, bool isNegative)
    {
        while (scale > 0 && mantissa % 10 == 0)
        {
            mantissa /= 10;
            scale--;
        }
        return (mantissa, scale, isNegative);
    }
}

/// <summary>
/// Represents a Daml Text value (string).
/// </summary>
public sealed record DamlText(string Value) : DamlValue
{
    /// <summary>Unwraps the underlying string.</summary>
    public static implicit operator string(DamlText value) => value.Value;

    /// <summary>Wraps a string as a Daml Text value.</summary>
    public static implicit operator DamlText(string value) => new(value);
}

/// <summary>
/// Represents a Daml Bool value.
/// </summary>
public sealed record DamlBool(bool Value) : DamlValue
{
    /// <summary>Unwraps the underlying boolean.</summary>
    public static implicit operator bool(DamlBool value) => value.Value;

    /// <summary>Wraps a boolean as a Daml Bool value.</summary>
    public static implicit operator DamlBool(bool value) => new(value);
}

/// <summary>
/// Represents a Daml Unit value (empty tuple).
/// </summary>
/// <remarks>
/// Through <see cref="System.Text.Json"/> it travels as an empty object, <c>{}</c>, and reads back
/// as <see cref="Instance"/>, so an <c>ExerciseOutcome&lt;DamlUnit&gt;.One</c> round-trips on bare
/// <see cref="JsonSerializerOptions"/>. The read skips any member of the object it is given rather
/// than routing through the reflection contract, so neither
/// <see cref="JsonSerializerOptions.UnmappedMemberHandling"/> nor
/// <see cref="JsonSerializerOptions.ReferenceHandler"/> reaches it.
/// </remarks>
[JsonConverter(typeof(DamlUnitJsonConverter))]
public sealed record DamlUnit : DamlValue
{
    /// <summary>The single Unit value; Unit carries no data, so one shared instance suffices.</summary>
    public static readonly DamlUnit Instance = new();
    private DamlUnit() { }
}

/// <summary>
/// Supplies the <see cref="System.Text.Json"/> converter for <see cref="DamlUnit"/>. Without it, the
/// private constructor leaves the reflection-based serializer nothing to call, and a read of
/// <c>{}</c> throws <see cref="NotSupportedException"/> instead of yielding <see cref="DamlUnit.Instance"/>.
/// </summary>
internal sealed class DamlUnitJsonConverter : JsonConverter<DamlUnit>
{
    public override DamlUnit Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected a JSON object for {nameof(DamlUnit)}, got {reader.TokenType}.");
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            reader.Skip();
        }

        return DamlUnit.Instance;
    }

    public override void Write(Utf8JsonWriter writer, DamlUnit value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteEndObject();
    }
}

/// <summary>
/// Represents a Daml Date value.
/// </summary>
public sealed record DamlDate(DateOnly Value) : DamlValue
{
    private static readonly int EpochDayNumber = DateOnly.FromDateTime(DateTime.UnixEpoch).DayNumber;

    /// <summary>
    /// Creates a DamlDate from days since epoch (1970-01-01).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="days"/> does not resolve to a date within the Daml-LF Date range
    /// (0001-01-01 to 9999-12-31), including values that would otherwise silently overflow
    /// the underlying day-number arithmetic.
    /// </exception>
    public static DamlDate FromDaysSinceEpoch(int days)
    {
        int dayNumber;
        try
        {
            dayNumber = checked(days + EpochDayNumber);
        }
        catch (OverflowException)
        {
            throw DaysOutOfRange(days);
        }

        if (dayNumber < DateOnly.MinValue.DayNumber || dayNumber > DateOnly.MaxValue.DayNumber)
        {
            throw DaysOutOfRange(days);
        }

        return new(DateOnly.FromDayNumber(dayNumber));
    }

    private static ArgumentOutOfRangeException DaysOutOfRange(int days) =>
        new(nameof(days), days,
            "Days since epoch must resolve to a date within the Daml-LF Date range (0001-01-01 to 9999-12-31).");

    /// <summary>
    /// Gets the number of days since epoch.
    /// </summary>
    public int DaysSinceEpoch =>
        Value.DayNumber - EpochDayNumber;

    /// <summary>Unwraps the underlying calendar date.</summary>
    public static implicit operator DateOnly(DamlDate value) => value.Value;

    /// <summary>Wraps a calendar date as a Daml Date value.</summary>
    public static implicit operator DamlDate(DateOnly value) => new(value);
}

/// <summary>
/// Represents a Daml Time value (timestamp with microsecond precision).
/// </summary>
public sealed record DamlTimestamp(DateTimeOffset Value) : DamlValue
{
    private const long TicksPerMicrosecond = 10;
    private static readonly long MinTicksSinceEpoch = (DateTimeOffset.MinValue - DateTimeOffset.UnixEpoch).Ticks;
    private static readonly long MaxTicksSinceEpoch =
        (DateTimeOffset.MaxValue - DateTimeOffset.UnixEpoch).Ticks / TicksPerMicrosecond * TicksPerMicrosecond;

    /// <summary>
    /// Creates a DamlTimestamp from microseconds since epoch.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="microseconds"/> does not resolve to a timestamp within the Daml-LF
    /// Timestamp range (0001-01-01T00:00:00Z to 9999-12-31T23:59:59.999999Z), including
    /// values that would otherwise silently overflow the underlying tick arithmetic.
    /// </exception>
    public static DamlTimestamp FromMicrosecondsSinceEpoch(long microseconds)
    {
        long ticks;
        try
        {
            ticks = checked(microseconds * TicksPerMicrosecond);
        }
        catch (OverflowException)
        {
            throw MicrosecondsOutOfRange(microseconds);
        }

        if (ticks < MinTicksSinceEpoch || ticks > MaxTicksSinceEpoch)
        {
            throw MicrosecondsOutOfRange(microseconds);
        }

        return new(DateTimeOffset.UnixEpoch.AddTicks(ticks));
    }

    private static ArgumentOutOfRangeException MicrosecondsOutOfRange(long microseconds) =>
        new(nameof(microseconds), microseconds,
            "Microseconds since epoch must resolve to a timestamp within the Daml-LF Timestamp range (0001-01-01T00:00:00Z to 9999-12-31T23:59:59.999999Z).");

    /// <summary>
    /// Gets the microseconds since epoch.
    /// </summary>
    public long MicrosecondsSinceEpoch =>
        (Value - DateTimeOffset.UnixEpoch).Ticks / TicksPerMicrosecond;

    /// <summary>Unwraps the underlying timestamp.</summary>
    public static implicit operator DateTimeOffset(DamlTimestamp value) => value.Value;

    /// <summary>Wraps a timestamp as a Daml Time value. The conversion preserves the full 100ns tick precision; the ledger truncates to microseconds.</summary>
    public static implicit operator DamlTimestamp(DateTimeOffset value) => new(value);
}

/// <summary>
/// Represents a first-class Daml Party identifier.
/// Conversions to and from <see cref="string"/> are both explicit, so a party can
/// never be silently mistaken for an arbitrary string (or vice versa); use
/// <see cref="Value"/> or <see cref="ToString"/> for logging and interpolation.
/// </summary>
[JsonConverter(typeof(PartyJsonConverter))]
public readonly record struct Party
{
    private readonly string? _id;

    /// <summary>
    /// The full party identifier string (e.g. "Alice::1220abcd..."); throws
    /// <see cref="InvalidOperationException"/> for a default-constructed Party.
    /// </summary>
    public string Value =>
        _id ?? throw new InvalidOperationException("Cannot access Value of a default (uninitialized) Party.");

    /// <summary>
    /// Creates a Party from its identifier string; rejects null or whitespace ids.
    /// </summary>
    public Party(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id, nameof(id));
        _id = id;
    }

    /// <summary>Extracts the party identifier; explicit so a Party is never silently used as text.</summary>
    public static explicit operator string(Party party) =>
        party._id ?? throw new InvalidOperationException("Cannot convert a default (uninitialized) Party to string.");

    /// <summary>Parses a party identifier; explicit so arbitrary strings never silently become parties.</summary>
    public static explicit operator Party(string id) => new(id);

    /// <summary>Returns the party identifier, or a placeholder for a default-constructed Party.</summary>
    public override string ToString() => _id ?? "<uninitialized Party>";

    /// <summary>Converts this Party to its wire-level <see cref="DamlParty"/> carrier.</summary>
    public DamlParty ToDamlValue() =>
        new(_id ?? throw new InvalidOperationException("Cannot serialize a default (uninitialized) Party."));

    /// <summary>Builds a validated Party from a wire-level <see cref="DamlParty"/> carrier.</summary>
    public static Party FromDamlValue(DamlParty value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(value.Value ?? throw new InvalidOperationException("Cannot create Party from DamlParty with null Value."));
    }
}

/// <summary>
/// System.Text.Json converter for <see cref="Party"/>. Serializes as a plain JSON string
/// so Party round-trips through JSON payloads produced by PQS and the JSON Ledger API,
/// which encode parties as raw strings (e.g. "Alice::1220abcd...").
/// </summary>
internal sealed class PartyJsonConverter : OpaqueStringIdJsonConverter<Party>
{
    /// <inheritdoc/>
    protected override Party Parse(string id) => new(id);

    /// <inheritdoc/>
    protected override string Format(Party value) => value.Value;
}

/// <summary>
/// Represents a Daml Party identifier.
/// </summary>
public sealed record DamlParty(string Value) : DamlValue
{
    /// <summary>Unwraps the underlying party identifier string.</summary>
    public static implicit operator string(DamlParty value) => value.Value;

    /// <summary>Wraps a party identifier string as a wire-level Daml Party value.</summary>
    public static implicit operator DamlParty(string value) => new(value);

    /// <summary>Returns the party identifier string.</summary>
    public override string ToString() => Value;
}
