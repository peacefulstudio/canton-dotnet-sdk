// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins <see cref="MonthExtensions"/>'s round trip between the hand-coded
/// <see cref="Month"/> enum and its <see cref="DamlEnum"/> wire shape.
/// </summary>
public sealed class MonthExtensionsTests
{
    public static TheoryData<Month, string> MonthsWithConstructors => new()
    {
        { Month.Jan, "Jan" }, { Month.Feb, "Feb" }, { Month.Mar, "Mar" }, { Month.Apr, "Apr" },
        { Month.May, "May" }, { Month.Jun, "Jun" }, { Month.Jul, "Jul" }, { Month.Aug, "Aug" },
        { Month.Sep, "Sep" }, { Month.Oct, "Oct" }, { Month.Nov, "Nov" }, { Month.Dec, "Dec" },
    };

    [Theory]
    [MemberData(nameof(MonthsWithConstructors))]
    public void ToDamlEnum_maps_each_month_to_its_named_constructor(Month month, string constructor)
    {
        month.ToDamlEnum().Should().Be(DamlEnum.Create(constructor));
    }

    [Theory]
    [MemberData(nameof(MonthsWithConstructors))]
    public void FromDamlEnum_maps_each_named_constructor_to_its_month(Month month, string constructor)
    {
        MonthExtensions.FromDamlEnum(DamlEnum.Create(constructor)).Should().Be(month);
    }

    [Fact]
    public void ToDamlEnum_rejects_a_month_outside_the_declared_range()
    {
        var act = () => ((Month)99).ToDamlEnum();

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("value");
    }

    [Fact]
    public void FromDamlEnum_rejects_an_unrecognized_constructor()
    {
        var act = () => MonthExtensions.FromDamlEnum(DamlEnum.Create("Smarch"));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("value");
    }

    [Fact]
    public void ReadDamlLfJson_decodes_a_known_constructor()
    {
        using var document = JsonDocument.Parse("\"Oct\"");

        MonthExtensions.__ReadDamlLfJson(document.RootElement, DamlLfJsonDecodeContext.Root("Month"))
            .Should().Be(DamlEnum.Create("Oct"));
    }

    [Fact]
    public void ReadDamlLfJson_rejects_a_constructor_outside_the_expected_set()
    {
        using var document = JsonDocument.Parse("\"Smarch\"");

        var act = () => MonthExtensions.__ReadDamlLfJson(document.RootElement, DamlLfJsonDecodeContext.Root("Month"));

        act.Should().Throw<JsonException>().WithMessage(
            "Unknown Daml enum constructor 'Smarch' at 'Month'; "
            + "expected one of Jan, Feb, Mar, Apr, May, Jun, Jul, Aug, Sep, Oct, Nov, Dec");
    }
}
