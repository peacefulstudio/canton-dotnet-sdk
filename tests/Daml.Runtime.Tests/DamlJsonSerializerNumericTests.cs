// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Globalization;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlJsonSerializerNumericTests
{
    [Fact]
    public void Serialize_should_handle_DamlNumeric_with_precision()
    {
        var record = DamlRecord.Create(
            DamlField.Create("amount", new DamlNumeric(123.456789m))
        );

        var json = DamlJsonSerializer.Serialize(record);

        json.Should().Contain("\"amount\":\"123.456789\"");
    }

    [Fact]
    public void Serialize_DamlNumeric_should_strip_trailing_zeros()
    {
        var record = DamlRecord.Create(
            DamlField.Create("amount", new DamlNumeric(1.50m))
        );

        var json = DamlJsonSerializer.Serialize(record);

        json.Should().Contain("\"amount\":\"1.5\"");
    }

    [Fact]
    public void Serialize_DamlNumeric_integer_value_should_get_single_trailing_zero()
    {
        var record = DamlRecord.Create(
            DamlField.Create("amount", new DamlNumeric(42m))
        );

        var json = DamlJsonSerializer.Serialize(record);

        json.Should().Contain("\"amount\":\"42.0\"");
    }

    [Fact]
    public void Serialize_DamlNumeric_high_precision_should_not_use_scientific_notation()
    {
        var record = DamlRecord.Create(
            DamlField.Create("amount", new DamlNumeric(0.0000000001m))
        );

        var json = DamlJsonSerializer.Serialize(record);

        json.Should().Contain("\"amount\":\"0.0000000001\"");
    }

    [Fact]
    public void Serialize_DamlNumeric_should_be_independent_of_construction_scale()
    {
        var fromShort = DamlJsonSerializer.Serialize(new DamlNumeric(1.5m));
        var fromPadded = DamlJsonSerializer.Serialize(new DamlNumeric(1.500000m));

        fromShort.Should().Be("\"1.5\"");
        fromPadded.Should().Be(fromShort);
    }

    [Fact]
    public void Serialize_DamlNumeric_negative_value_should_keep_sign_and_strip_trailing_zeros()
    {
        var fractional = DamlJsonSerializer.Serialize(new DamlNumeric(-1.50m));
        var integral = DamlJsonSerializer.Serialize(new DamlNumeric(-42m));

        fractional.Should().Be("\"-1.5\"");
        integral.Should().Be("\"-42.0\"");
    }

    [Fact]
    public void Serialize_DamlNumeric_zero_should_be_canonical_zero_with_trailing_zero()
    {
        var json = DamlJsonSerializer.Serialize(new DamlNumeric(0m));

        json.Should().Be("\"0.0\"");
    }

    [Fact]
    public void DeserializeRecord_should_handle_DamlNumeric()
    {
        var json = """{"amount":123.456}""";

        var record = DamlJsonSerializer.DeserializeRecord(json);

        record.GetField("amount")!.As<DamlNumeric>().Value.Should().Be(123.456m);
    }

    [Fact]
    public void DeserializeRecord_should_preserve_raw_json_number_precision_beyond_decimal_range()
    {
        var thirtyEightSignificantDigits = "1.2345678901234567890123456789012345678";
        var json = $"{{\"amount\":{thirtyEightSignificantDigits}}}";

        var record = DamlJsonSerializer.DeserializeRecord(json);
        var numeric = record.GetRequiredField("amount").As<DamlNumeric>();

        var roundTripped = DamlJsonSerializer.Serialize(numeric);

        roundTripped.Should().Be($"\"{thirtyEightSignificantDigits}\"",
            "a raw JSON number beyond decimal's ~29-digit range must still round-trip with zero precision loss");
    }

    [Fact]
    public void DeserializeRecord_should_preserve_magnitude_beyond_decimal_max_value()
    {
        var beyondDecimalMaxValue = "79228162514264337593543950336.0";
        var json = $"{{\"amount\":{beyondDecimalMaxValue}}}";

        var record = DamlJsonSerializer.DeserializeRecord(json);
        var numeric = record.GetRequiredField("amount").As<DamlNumeric>();

        var roundTripped = DamlJsonSerializer.Serialize(numeric);

        roundTripped.Should().Be($"\"{beyondDecimalMaxValue}\"");
    }

    [Fact]
    public void DamlNumeric_value_should_throw_on_inexact_narrowing_beyond_decimal_range()
    {
        var thirtyEightSignificantDigits = "1.2345678901234567890123456789012345678";
        var json = $"{{\"amount\":{thirtyEightSignificantDigits}}}";
        var record = DamlJsonSerializer.DeserializeRecord(json);
        var numeric = record.GetRequiredField("amount").As<DamlNumeric>();

        var act = () => numeric.Value;

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void DamlNumeric_value_should_throw_on_magnitude_beyond_decimal_max_value()
    {
        var beyondDecimalMaxValue = "79228162514264337593543950336.0";
        var json = $"{{\"amount\":{beyondDecimalMaxValue}}}";
        var record = DamlJsonSerializer.DeserializeRecord(json);
        var numeric = record.GetRequiredField("amount").As<DamlNumeric>();

        var act = () => numeric.Value;

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void RoundTrip_should_preserve_DamlNumeric_record_field()
    {
        var original = DamlRecord.Create(DamlField.Create("amount", new DamlNumeric(123.456789m)));

        var json = DamlJsonSerializer.Serialize(original);
        var deserialized = DamlJsonSerializer.DeserializeRecord(json);

        deserialized.GetRequiredField("amount").As<DamlNumeric>().Value.Should().Be(123.456789m);
    }

    [Fact]
    public void Deserialize_should_throw_JsonException_for_number_that_does_not_fit_decimal()
    {
        var act = () => DamlJsonSerializer.Deserialize("""{"amount":1e300}""");

        act.Should().Throw<JsonException>().WithMessage("*1e300*");
    }

    [Fact]
    public void Serialize_DamlNumeric_should_be_locale_independent()
    {
        var previousCulture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            var record = DamlRecord.Create(
                DamlField.Create("amount", new DamlNumeric(123.456789m))
            );

            var json = DamlJsonSerializer.Serialize(record);

            json.Should().Contain("\"amount\":\"123.456789\"");
            json.Should().NotContain("\"amount\":\"123,456789\"");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void Deserialize_should_parse_canonical_numeric_strings_with_invariant_culture()
    {
        var previousCulture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var value = DamlJsonSerializer.Deserialize("\"1.5\"");

            value.As<DamlNumeric>().Value.Should().Be(1.5m);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previousCulture;
        }
    }
}
