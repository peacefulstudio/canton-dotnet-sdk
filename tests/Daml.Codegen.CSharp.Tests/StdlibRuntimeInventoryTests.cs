// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class StdlibRuntimeInventoryTests
{
    [Theory]
    [InlineData("DA.Types", "Tuple4", "Tuple4")]
    [InlineData("DA.Types", "Tuple20", "Tuple20")]
    [InlineData("GHC.Types", "Ordering", "Ordering")]
    [InlineData("GHC.Tuple", "Unit", "Unit")]
    [InlineData("DA.Validation.Types", "Validation", "Validation")]
    [InlineData("DA.Random.Types", "Minstd", "Minstd")]
    [InlineData("DA.Stack.Types", "SrcLoc", "SrcLoc")]
    [InlineData("DA.Internal.Template", "Archive", "Archive")]
    [InlineData("DA.Exception.GeneralError", "GeneralError", "GeneralError")]
    public void MapStdlibType_maps_a_generated_type_to_its_runtime_stdlib_name(string module, string name, string expected)
    {
        StdlibPackages.MapStdlibType(module, name).Should().Be(expected);
    }

    [Theory]
    [InlineData("DA.Types", "Tuple2")]
    [InlineData("DA.Types", "Tuple3")]
    [InlineData("DA.Types", "Either")]
    [InlineData("DA.Set.Types", "Set")]
    [InlineData("DA.NonEmpty.Types", "NonEmpty")]
    [InlineData("DA.Map.Types", "Map")]
    [InlineData("DA.Date.Types", "DayOfWeek")]
    [InlineData("DA.Date.Types", "Month")]
    [InlineData("DA.Time.Types", "RelTime")]
    public void MapStdlibType_keeps_the_hand_written_type_identity(string module, string name)
    {
        StdlibPackages.MapStdlibType(module, name).Should().Be(name);
        StdlibRuntimeInventory.HandMapped.Should().Contain((module, name));
        StdlibRuntimeInventory.Generated.Should().NotContain((module, name));
    }

    [Fact]
    public void MapStdlibType_returns_null_for_a_type_outside_the_inventory()
    {
        StdlibPackages.MapStdlibType("DA.Internal.Fail.Types", "FailureStatus").Should().BeNull();
    }

    [Fact]
    public void Generated_runtime_types_are_never_parametric_stdlib_types()
    {
        StdlibRuntimeInventory.Generated.Should().NotIntersectWith(StdlibPackages.ParametricStdlibTypes);
    }
}
