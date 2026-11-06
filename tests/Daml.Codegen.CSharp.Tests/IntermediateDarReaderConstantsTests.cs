// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.Intermediate.Model;
using Xunit;
using PbConstant = Daml.Codegen.Intermediate.Constant;

namespace Daml.Codegen.CSharp.Tests;

public partial class IntermediateDarReaderTests
{
    [Fact]
    public void Read_maps_each_literal_kind_of_a_constant_to_its_model_value()
    {
        var proto = MakePackageWith(module =>
        {
            module.Constants.Add(new PbConstant { Name = "greeting", TextValue = "hello" });
            module.Constants.Add(new PbConstant { Name = "retryLimit", Int64Value = 42 });
            module.Constants.Add(new PbConstant { Name = "maintenanceMode", BoolValue = true });
            module.Constants.Add(new PbConstant { Name = "feeRate", NumericValue = "0.0025000000" });
        });

        var constants = IntermediateDarReader.Read(proto).MainPackage.Modules.Single().Constants;

        constants.Should().Equal(
            new DamlConstant("greeting", new DamlTextConstantValue("hello")),
            new DamlConstant("retryLimit", new DamlInt64ConstantValue(42)),
            new DamlConstant("maintenanceMode", new DamlBoolConstantValue(true)),
            new DamlConstant("feeRate", new DamlNumericConstantValue("0.0025000000")));
    }

    [Fact]
    public void Read_keeps_a_false_bool_constant_distinct_from_one_with_no_literal()
    {
        var proto = MakePackageWith(module =>
            module.Constants.Add(new PbConstant { Name = "disabled", BoolValue = false }));

        IntermediateDarReader.Read(proto).MainPackage.Modules.Single().Constants
            .Should().Equal(new DamlConstant("disabled", new DamlBoolConstantValue(false)));
    }

    [Fact]
    public void Read_rejects_a_constant_with_no_literal()
    {
        var proto = MakePackageWith(module =>
            module.Constants.Add(new PbConstant { Name = "bare" }));

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*bare*literal*");
    }

    [Theory]
    [InlineData("1e5")]
    [InlineData("+1.5")]
    [InlineData("1.")]
    [InlineData(".5")]
    [InlineData("1.5m; System.Console.Clear(); //")]
    [InlineData("")]
    public void Read_rejects_a_numeric_constant_outside_plain_decimal_notation(string malicious)
    {
        var proto = MakePackageWith(module =>
            module.Constants.Add(new PbConstant { Name = "rate", NumericValue = malicious }));

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*rate*plain decimal*");
    }

    [Theory]
    [MemberData(nameof(NamesOutsideTheDamlLfIdentifierGrammar))]
    public void Read_rejects_constant_names_outside_the_identifier_grammar(string maliciousName)
    {
        var proto = MakePackageWith(module =>
            module.Constants.Add(new PbConstant { Name = maliciousName, TextValue = "x" }));

        var act = () => IntermediateDarReader.Read(proto);

        act.Should().Throw<InvalidDataException>().WithMessage("*identifier*");
    }
}
