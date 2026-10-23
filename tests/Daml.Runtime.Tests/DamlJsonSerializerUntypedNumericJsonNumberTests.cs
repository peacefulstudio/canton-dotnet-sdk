// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlJsonSerializerUntypedNumericJsonNumberTests
{
    private static DamlNumeric DeserializeAmount(string jsonNumber) =>
        DamlJsonSerializer.DeserializeRecord($"{{\"amount\":{jsonNumber}}}")
            .GetRequiredField("amount")
            .As<DamlNumeric>();

    private static Action DeserializingAmount(string jsonNumber) =>
        () => DamlJsonSerializer.DeserializeRecord($"{{\"amount\":{jsonNumber}}}");

    [Theory]
    [InlineData("1.234567890123456789012345678901234567890")]
    [InlineData("12345678901234567890123456789012345678901.5")]
    [InlineData("1234567890123456789012345678901234567890")]
    [InlineData("1E-40")]
    [InlineData("1.5E-38")]
    [InlineData("1E38")]
    [InlineData("12345678901234567890123456789012345678E1")]
    [InlineData("1E999999")]
    [InlineData("1E2147483647")]
    [InlineData("1E99999999999")]
    [InlineData("1E-2147483648")]
    [InlineData("1E-999999")]
    [InlineData("1E-101")]
    [InlineData("0E101")]
    [InlineData("-1E-40")]
    public void Deserialize_rejects_json_number_beyond_38_digits_or_scale_37(string jsonNumber)
    {
        DeserializingAmount(jsonNumber).Should()
            .Throw<JsonException>()
            .WithMessage($"Number '{jsonNumber}' cannot be represented as a Daml Numeric");
    }

    [Theory]
    [InlineData("1.2345678901234567890123456789012345678E0", "1.2345678901234567890123456789012345678")]
    [InlineData("12345678901234567890123456789012345678E-37", "1.2345678901234567890123456789012345678")]
    [InlineData("1E-37", "0.0000000000000000000000000000000000001")]
    [InlineData("1.5E3", "1500.0")]
    [InlineData("1e2", "100.0")]
    [InlineData("25E-1", "2.5")]
    [InlineData("-25E-1", "-2.5")]
    [InlineData("-1.25e+2", "-125.0")]
    [InlineData("0.12345678901234567890123456789012345678E1", "1.2345678901234567890123456789012345678")]
    [InlineData("0E0", "0.0")]
    [InlineData("0E100", "0.0")]
    [InlineData("1E37", "10000000000000000000000000000000000000.")]
    public void Deserialize_reads_exponent_json_number_exactly(string jsonNumber, string expectedCanonical)
    {
        var numeric = DeserializeAmount(jsonNumber);

        numeric.ToCanonicalString().Should().Be(expectedCanonical);
    }
}
