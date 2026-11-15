// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlNumericCanonicalStringTests
{
    private static readonly JsonSerializerOptions Registered = new JsonSerializerOptions().AddDamlConverters();

    private static DamlNumeric Parse(string canonical)
    {
        DamlNumeric.TryParseCanonical(canonical, out var value).Should().BeTrue();
        return value;
    }

    public static TheoryData<string> WideCanonicalStrings() => new()
    {
        "99999999999999999999999999999999999999.",
        "-99999999999999999999999999999999999999.",
        "9999999999999999999999999999999999999.9",
        "-0.9999999999999999999999999999999999999",
        "12345678901234567890.123456789012345678",
        "0.00000000000000000000000000001",
    };

    [Theory]
    [MemberData(nameof(WideCanonicalStrings))]
    public void ToString_returns_the_canonical_string_for_a_value_decimal_cannot_hold(string canonical)
    {
        Parse(canonical).ToString().Should().Be(canonical);
    }

    [Fact]
    public void ToString_returns_the_canonical_string_for_a_value_decimal_can_hold()
    {
        new DamlNumeric(1234.5678m).ToString().Should().Be("1234.5678");
    }

    [Theory]
    [MemberData(nameof(WideCanonicalStrings))]
    public void DamlValue_serializes_a_wide_numeric_as_its_canonical_string(string canonical)
    {
        JsonSerializer.Serialize<DamlValue>(Parse(canonical))
            .Should().Be($$"""{"$case":"DamlNumeric","Value":"{{canonical}}"}""");
    }

    [Theory]
    [MemberData(nameof(WideCanonicalStrings))]
    public void DamlValue_reads_a_wide_numeric_back_equal_on_default_and_registered_options(string canonical)
    {
        var json = $$"""{"$case":"DamlNumeric","Value":"{{canonical}}"}""";

        JsonSerializer.Deserialize<DamlValue>(json).Should().Be(Parse(canonical));
        JsonSerializer.Deserialize<DamlValue>(json, Registered).Should().Be(Parse(canonical));
    }

    [Theory]
    [MemberData(nameof(WideCanonicalStrings))]
    public void DamlNumeric_serializes_standalone_as_an_object_carrying_its_canonical_string(string canonical)
    {
        JsonSerializer.Serialize(Parse(canonical)).Should().Be($$"""{"Value":"{{canonical}}"}""");
    }

    [Theory]
    [MemberData(nameof(WideCanonicalStrings))]
    public void DamlNumeric_reads_standalone_back_equal(string canonical)
    {
        JsonSerializer.Deserialize<DamlNumeric>($$"""{"Value":"{{canonical}}"}""").Should().Be(Parse(canonical));
    }
}
