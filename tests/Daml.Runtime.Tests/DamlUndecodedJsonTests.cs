// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Runtime.Tests;

public sealed class DamlUndecodedJsonTests
{
    [Fact]
    public void DamlUndecodedJson_carries_the_json_text_it_was_given()
    {
        new DamlUndecodedJson("{\"owner\":\"alice::ns\"}").LfJson.Should().Be("{\"owner\":\"alice::ns\"}");
    }

    [Fact]
    public void DamlUndecodedJson_equals_another_value_carrying_the_same_text()
    {
        var left = new DamlUndecodedJson("{\"owner\":\"alice::ns\"}");
        var right = new DamlUndecodedJson("{\"owner\":\"alice::ns\"}");

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void DamlUndecodedJson_differs_when_the_text_differs()
    {
        new DamlUndecodedJson("{\"owner\":\"alice::ns\"}").Should().NotBe(new DamlUndecodedJson("{\"owner\":\"bob::ns\"}"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("\"7\"")]
    public void DamlUndecodedJson_keeps_the_text_verbatim(string lfJson)
    {
        new DamlUndecodedJson(lfJson).LfJson.Should().Be(lfJson);
    }

    [Fact]
    public void DamlUndecodedJson_is_a_DamlValue()
    {
        DamlValue value = new DamlUndecodedJson("null");

        value.Should().BeOfType<DamlUndecodedJson>();
    }

    [Fact]
    public void DamlUndecodedJson_differs_from_a_DamlText_carrying_the_same_characters()
    {
        DamlValue undecoded = new DamlUndecodedJson("hello");

        undecoded.Should().NotBe(new DamlText("hello"));
    }
}
