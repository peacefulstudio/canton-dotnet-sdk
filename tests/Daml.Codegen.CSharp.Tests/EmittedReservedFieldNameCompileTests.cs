// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.Intermediate.Model;
using Xunit;
using static Daml.Codegen.CSharp.Tests.ReservedTypeNameFixtures;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedReservedFieldNameCompileTests
{
    public static TheoryData<string> RecordFieldNames =>
        [
            "toRecord", "fromRecord", "equals", "getHashCode", "toString", "printMembers", "equalityContract",
            "getType", "memberwiseClone", "referenceEquals", "clone", "deconstruct",
        ];

    public static TheoryData<string> TemplateOnlyFieldNames =>
        ["templateId", "packageId", "packageName", "packageVersion", "damlTypeId"];

    public static TheoryData<string> FieldNamesThatCompiledBefore =>
        ["tag", "toVariant", "fromVariant", "choices", "item", "value", "key", "inner"];

    [Theory]
    [MemberData(nameof(RecordFieldNames))]
    public void Record_field_named_like_a_record_member_compiles(string fieldName)
    {
        CompileErrors(DarOf([], Record("Box", Field(fieldName, IntType), Field("other", IntType)))).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(RecordFieldNames))]
    [MemberData(nameof(TemplateOnlyFieldNames))]
    public void Template_payload_field_named_like_a_template_member_compiles_with_a_keyed_choice(string fieldName)
    {
        var dar = DarOf(
            [Template("Asset", new DamlPrimitiveType(DamlPrimitive.Party), Choice("Reissue", IntType))],
            Record("Asset", Field(fieldName, PartyType), Field("owner", PartyType)));

        CompileErrors(dar).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(RecordFieldNames))]
    public void Choice_argument_field_named_like_a_record_member_compiles(string fieldName)
    {
        var dar = DarOf(
            [Template("Asset", null, Choice("Reissue", IntType, Ref("Reissue")))],
            Record("Asset", Field("owner", PartyType)),
            Record("Reissue", Field(fieldName, IntType)));

        CompileErrors(dar).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(RecordFieldNames))]
    public void Record_field_named_like_a_record_member_is_emitted_with_one_trailing_underscore(string fieldName)
    {
        var dar = DarOf([], Record("Box", Field(fieldName, IntType)));
        var propertyName = char.ToUpperInvariant(fieldName[0]) + fieldName[1..] + "_";

        var emitted = Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory.CreateGenerator().Generate(dar)
            .Single(file => file.RelativePath.EndsWith("Box.cs", StringComparison.Ordinal)).Content;

        emitted.Should().Contain($" {propertyName}");
        emitted.Should().Contain($"DamlFieldAttribute(\"{fieldName}\")");
    }

    [Theory]
    [MemberData(nameof(FieldNamesThatCompiledBefore))]
    public void Record_field_name_that_compiled_before_is_emitted_unchanged(string fieldName)
    {
        var dar = DarOf([], Record("Box", Field(fieldName, IntType)));
        var propertyName = char.ToUpperInvariant(fieldName[0]) + fieldName[1..];

        var emitted = Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory.CreateGenerator().Generate(dar)
            .Single(file => file.RelativePath.EndsWith("Box.cs", StringComparison.Ordinal)).Content;

        CompileErrors(dar).Should().BeEmpty();
        emitted.Should().MatchRegex($"long {propertyName}\\s");
    }
}
