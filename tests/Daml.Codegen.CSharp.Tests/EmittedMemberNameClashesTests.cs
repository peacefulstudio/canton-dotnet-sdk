// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Xunit;
using static Daml.Codegen.CSharp.Tests.ReservedTypeNameFixtures;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedMemberNameClashesTests
{
    private static void RequireRecordFields(string typeName, params string[] damlFieldNames) =>
        EmittedMemberNameClashes.Require(
            "test-package",
            "Test.Module",
            "record",
            typeName,
            damlFieldNames,
            damlFieldName => Identifiers.MemberName(damlFieldName, typeName, ReservedMemberNames.OfRecordField()));

    [Theory]
    [InlineData("my_field", "myField", "MyField")]
    [InlineData("myField", "MyField", "MyField")]
    [InlineData("foo_bar", "fooBar", "FooBar")]
    [InlineData("period", "period_", "Period_")]
    public void Require_rejects_two_fields_that_emit_the_same_member_naming_both_of_them(string first, string second, string member)
    {
        var failure = () => RequireRecordFields("Period", first, second);

        failure.Should().Throw<CodegenException>().Which.Message.Should().Be(
            $"The Daml fields '{first}' and '{second}' of record 'Test.Module:Period' in package 'test-package' would both be emitted as the C# member '{member}'. " +
            $"Rename '{first}' or '{second}' in Daml.");
    }

    [Fact]
    public void Require_accepts_fields_that_emit_distinct_members()
    {
        var accepting = () => RequireRecordFields("Rec", "my_field", "myFields", "other");

        accepting.Should().NotThrow();
    }

    [Fact]
    public void Require_accepts_a_record_without_fields()
    {
        var accepting = () => RequireRecordFields("Rec");

        accepting.Should().NotThrow();
    }

    [Fact]
    public void Generate_fails_naming_both_fields_when_two_record_fields_emit_the_same_property()
    {
        var dar = DarOf([], Record("Rec", Field("my_field", IntType), Field("myField", IntType)));

        var failure = () => CreateGenerator().Generate(dar);

        failure.Should().Throw<CodegenException>().Which.Message.Should()
            .Contain("'my_field' and 'myField'").And.Contain("record 'Test.Module:Rec'").And.Contain("'MyField'");
    }

    [Fact]
    public void Generate_fails_naming_both_fields_when_two_template_payload_fields_emit_the_same_property()
    {
        var dar = DarOf(
            [Template("Asset", null)],
            Record("Asset", Field("owner", PartyType), Field("face_value", IntType), Field("faceValue", IntType)));

        var failure = () => CreateGenerator().Generate(dar);

        failure.Should().Throw<CodegenException>().Which.Message.Should()
            .Contain("'face_value' and 'faceValue'").And.Contain("template 'Test.Module:Asset'");
    }

    [Fact]
    public void Generate_fails_naming_both_fields_when_two_choice_argument_fields_emit_the_same_property()
    {
        var dar = DarOf(
            [Template("Asset", null, Choice("Spawn", IntType, Ref("SpawnArg")))],
            Record("Asset", Field("owner", PartyType)),
            Record("SpawnArg", Field("new_owner", PartyType), Field("newOwner", PartyType)));

        var failure = () => CreateGenerator().Generate(dar);

        failure.Should().Throw<CodegenException>().Which.Message.Should()
            .Contain("'new_owner' and 'newOwner'").And.Contain("choice argument 'Test.Module:Asset.Spawn'");
    }

    [Fact]
    public void Generate_accepts_a_record_whose_fields_emit_distinct_properties()
    {
        var dar = DarOf([], Record("Rec", Field("my_field", IntType), Field("other", IntType)));

        var generating = () => CreateGenerator().Generate(dar);

        generating.Should().NotThrow();
    }
}
