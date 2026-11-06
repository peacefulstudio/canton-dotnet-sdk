// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.DefaultTarget;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class DefaultTargetConstantsTests
{
    [Fact]
    public void Constants_carries_the_text_value_with_quotes_newline_backslash_and_astral_characters_intact()
    {
        Constants.Greeting.Should().Be("say \"hi\"\n\\ é \U0001F389");
    }

    [Fact]
    public void Constants_carries_the_int_values_including_a_negative_one()
    {
        Constants.RetryLimit.Should().Be(42L);
        Constants.NegativeLimit.Should().Be(-7L);
    }

    [Fact]
    public void Constants_carries_both_bool_values()
    {
        Constants.MaintenanceMode.Should().BeTrue();
        Constants.Disabled.Should().BeFalse();
    }

    [Fact]
    public void Constants_carries_numeric_values_as_exact_decimals()
    {
        Constants.FeeRate.Should().Be(0.0025m);
        Constants.LegacyDecimal.Should().Be(3.14m);
    }

    [Fact]
    public void Constants_names_a_value_with_a_prime_through_the_identifier_escape()
    {
        Constants.NameU0027.Should().Be("primed");
    }

    [Fact]
    public void Constants_declares_only_the_single_literal_values_of_the_module()
    {
        var declared = typeof(Constants)
            .GetFields()
            .Select(field => field.Name)
            .Order(StringComparer.Ordinal);

        declared.Should().Equal(
            "Disabled",
            "FeeRate",
            "Greeting",
            "LegacyDecimal",
            "MaintenanceMode",
            "NameU0027",
            "NegativeLimit",
            "RetryLimit");
    }

    [Fact]
    public void Constants_leaves_out_the_numeric_whose_scale_decimal_cannot_hold()
    {
        typeof(Constants).GetField("TinyRate").Should().BeNull();
    }
}
