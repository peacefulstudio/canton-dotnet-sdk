// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.CSharp;
using Daml.Codegen.Intermediate.Model;
using Xunit;
using PbConstant = Daml.Codegen.Intermediate.Constant;

namespace Daml.Codegen.DarParser.Tests;

public sealed class IntermediateDarWriterConstantsTests
{
    private static DarModel DarWith(params DamlConstant[] constants) =>
        new()
        {
            MainPackage = new DamlPackage
            {
                PackageId = "1111111111111111111111111111111111111111111111111111111111111111",
                Name = "pkg",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules =
                [
                    new DamlModule
                    {
                        Name = "M",
                        DataTypes = [],
                        Templates = [],
                        Interfaces = [],
                        Constants = constants,
                    },
                ],
                DependencyReferences = [],
            },
            Dependencies = [],
        };

    [Fact]
    public void Write_maps_each_constant_kind_to_the_matching_proto_literal()
    {
        var proto = IntermediateDarWriter.Write(DarWith(
            new DamlConstant("greeting", new DamlTextConstantValue("hello")),
            new DamlConstant("retryLimit", new DamlInt64ConstantValue(42)),
            new DamlConstant("disabled", new DamlBoolConstantValue(false)),
            new DamlConstant("feeRate", new DamlNumericConstantValue("0.0025000000"))));

        var constants = proto.Main.Modules.Single().Constants;

        constants.Select(constant => (constant.Name, constant.LiteralCase)).Should().Equal(
            ("disabled", PbConstant.LiteralOneofCase.BoolValue),
            ("feeRate", PbConstant.LiteralOneofCase.NumericValue),
            ("greeting", PbConstant.LiteralOneofCase.TextValue),
            ("retryLimit", PbConstant.LiteralOneofCase.Int64Value));
        constants.Single(constant => constant.Name == "disabled").BoolValue.Should().BeFalse();
        constants.Single(constant => constant.Name == "feeRate").NumericValue.Should().Be("0.0025000000");
        constants.Single(constant => constant.Name == "greeting").TextValue.Should().Be("hello");
        constants.Single(constant => constant.Name == "retryLimit").Int64Value.Should().Be(42);
    }

    [Fact]
    public void Write_orders_constants_by_ordinal_name()
    {
        var proto = IntermediateDarWriter.Write(DarWith(
            new DamlConstant("beta", new DamlInt64ConstantValue(1)),
            new DamlConstant("Zeta", new DamlInt64ConstantValue(2)),
            new DamlConstant("alpha", new DamlInt64ConstantValue(3))));

        proto.Main.Modules.Single().Constants.Select(constant => constant.Name).Should().Equal("Zeta", "alpha", "beta");
    }

    [Fact]
    public void Write_then_Read_round_trips_the_constants_of_a_module()
    {
        var constants = new[]
        {
            new DamlConstant("disabled", new DamlBoolConstantValue(false)),
            new DamlConstant("feeRate", new DamlNumericConstantValue("0.0025000000")),
            new DamlConstant("greeting", new DamlTextConstantValue("say \"hi\"\né")),
            new DamlConstant("retryLimit", new DamlInt64ConstantValue(long.MinValue)),
        };

        var read = IntermediateDarReader.Read(IntermediateDarWriter.Write(DarWith(constants)));

        read.MainPackage.Modules.Single().Constants.Should().Equal(constants);
    }
}
