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
/// Pins <see cref="DayOfWeekExtensions"/>'s round trip between the hand-coded
/// <see cref="Daml.Runtime.Stdlib.DayOfWeek"/> enum and its <see cref="DamlEnum"/> wire shape,
/// including the out-of-range guards a generated enum's own switch would otherwise carry.
/// </summary>
public sealed class DayOfWeekExtensionsTests
{
    [Theory]
    [InlineData(Daml.Runtime.Stdlib.DayOfWeek.Monday, "Monday")]
    [InlineData(Daml.Runtime.Stdlib.DayOfWeek.Tuesday, "Tuesday")]
    [InlineData(Daml.Runtime.Stdlib.DayOfWeek.Wednesday, "Wednesday")]
    [InlineData(Daml.Runtime.Stdlib.DayOfWeek.Thursday, "Thursday")]
    [InlineData(Daml.Runtime.Stdlib.DayOfWeek.Friday, "Friday")]
    [InlineData(Daml.Runtime.Stdlib.DayOfWeek.Saturday, "Saturday")]
    [InlineData(Daml.Runtime.Stdlib.DayOfWeek.Sunday, "Sunday")]
    public void ToDamlEnum_maps_each_day_to_its_named_constructor(Daml.Runtime.Stdlib.DayOfWeek day, string constructor)
    {
        day.ToDamlEnum().Should().Be(DamlEnum.Create(constructor));
    }

    [Theory]
    [InlineData("Monday", Daml.Runtime.Stdlib.DayOfWeek.Monday)]
    [InlineData("Tuesday", Daml.Runtime.Stdlib.DayOfWeek.Tuesday)]
    [InlineData("Wednesday", Daml.Runtime.Stdlib.DayOfWeek.Wednesday)]
    [InlineData("Thursday", Daml.Runtime.Stdlib.DayOfWeek.Thursday)]
    [InlineData("Friday", Daml.Runtime.Stdlib.DayOfWeek.Friday)]
    [InlineData("Saturday", Daml.Runtime.Stdlib.DayOfWeek.Saturday)]
    [InlineData("Sunday", Daml.Runtime.Stdlib.DayOfWeek.Sunday)]
    public void FromDamlEnum_maps_each_named_constructor_to_its_day(string constructor, Daml.Runtime.Stdlib.DayOfWeek day)
    {
        DayOfWeekExtensions.FromDamlEnum(DamlEnum.Create(constructor)).Should().Be(day);
    }

    [Fact]
    public void ToDamlEnum_rejects_a_day_outside_the_declared_range()
    {
        var outOfRange = (Daml.Runtime.Stdlib.DayOfWeek)99;

        var act = () => outOfRange.ToDamlEnum();

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("value");
    }

    [Fact]
    public void FromDamlEnum_rejects_an_unrecognized_constructor()
    {
        var act = () => DayOfWeekExtensions.FromDamlEnum(DamlEnum.Create("Blursday"));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("value");
    }

    [Fact]
    public void ReadDamlLfJson_decodes_a_known_constructor()
    {
        using var document = JsonDocument.Parse("\"Friday\"");
        var context = DamlLfJsonDecodeContext.Root("DayOfWeek");

        DayOfWeekExtensions.__ReadDamlLfJson(document.RootElement, context)
            .Should().Be(DamlEnum.Create("Friday"));
    }

    [Fact]
    public void ReadDamlLfJson_rejects_a_constructor_outside_the_expected_set()
    {
        using var document = JsonDocument.Parse("\"Blursday\"");
        var context = DamlLfJsonDecodeContext.Root("DayOfWeek");

        var act = () => DayOfWeekExtensions.__ReadDamlLfJson(document.RootElement, context);

        act.Should().Throw<JsonException>().WithMessage(
            "Unknown Daml enum constructor 'Blursday' at 'DayOfWeek'; "
            + "expected one of Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday");
    }
}
