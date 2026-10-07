// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class FilterFieldPathTests
{
    [Fact]
    public void Field_reads_wire_name_from_metadata()
    {
        RenderSql(Filter.Field<CleanFields>(x => x.Owner, "alice")).Should().Be("payload->>'owner' = @p0");
    }

    [Fact]
    public void Field_uses_snake_case_wire_name_not_transformed_property()
    {
        RenderSql(Filter.Field<DivergentFields>(x => x.Label, "x")).Should().Be("payload->>'created_label' = @p0");
    }

    [Fact]
    public void Field_uses_reserved_word_escape_wire_name()
    {
        RenderSql(Filter.Field<DivergentFields>(x => x.Operator, "x")).Should().Be("payload->>'operator' = @p0");
    }

    [Fact]
    public void Field_reads_wire_name_for_value_type_property_with_boxing()
    {
        RenderSql(Filter.Field<CleanFields>(x => x.Count, "3")).Should().Be("(payload->>'count')::bigint = @p0");
    }

    [Fact]
    public void Field_navigates_a_nested_record_path()
    {
        RenderSql(Filter.Field<NestedFields>(x => x.Inner.Owner, "alice"))
            .Should().Be("payload->'inner'->>'owner' = @p0");
    }

    [Fact]
    public void Field_compares_the_value_of_a_flat_Optional_wrapper_field()
    {
        RenderSql(Filter.Field<TypeCorners>(t => t.Crate.Item, "crated"))
            .Should().Be("payload->'crate'->>'item' = @p0");
    }

    [Fact]
    public void Field_compares_the_innermost_value_of_a_nested_Optional_wrapper_field()
    {
        RenderSql(Filter.Field<TypeCorners>(t => t.MaybeMaybeNote, "deep"))
            .Should().Be("payload->'maybeMaybeNote'->0->>0 = @p0");
    }

    [Fact]
    public void Field_throws_when_DamlFieldAttribute_absent()
    {
        var act = () => Filter.Field<MissingMetadata>(x => x.Unmarked, "x");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unmarked*")
            .WithMessage("*regenerate*");
    }

    public static TheoryData<string, Func<PqsFilter>> NonDamlMembers => new()
    {
        { "string_length", () => Filter.Field<RichRecord>(r => r.Label.Length, "3") },
        { "list_count", () => Filter.Field<RichRecord>(r => r.Tags.Count, "1") },
        { "party_value", () => Filter.Field<RichRecord>(r => r.Owner.Value, "alice::0000") },
        { "date_time_offset_year", () => Filter.Field<RichRecord>(r => r.ObservedAt.Year, "2026") },
        { "variant_tag", () => Filter.Field<RichRecord>(r => r.Outcome.Tag, "Win") },
    };

    [Theory]
    [MemberData(nameof(NonDamlMembers))]
    public void Field_throws_the_unsupported_expression_error_for_a_member_of_a_non_Daml_type(
        string memberName, Func<PqsFilter> build)
    {
        var failure = Assert.Throws<ArgumentException>(build);

        failure.ParamName.Should().Be("selector", memberName);
        failure.Message.Should().StartWith("Unsupported expression in a PQS filter", memberName);
    }

    [Fact]
    public void Field_throws_on_method_call()
    {
        var act = () => Filter.Field<CleanFields>(x => x.Owner.ToUpperInvariant(), "x");
        act.Should().Throw<ArgumentException>()
            .WithParameterName("selector")
            .WithMessage("*Unsupported expression*");
    }

    [Fact]
    public void Field_throws_when_the_selected_member_is_a_record_not_a_leaf()
    {
        var act = () => Filter.Field<NestedFields>(x => x.Inner, "x");
        act.Should().Throw<ArgumentException>()
            .WithParameterName("selector")
            .WithMessage("*'InnerFields' is not a Daml leaf type*");
    }

    private static string RenderSql(PqsFilter filter)
    {
        var parameters = new List<(string Name, object Value)>();
        var paramIndex = 0;
        return filter.ToSqlClause(parameters, ref paramIndex);
    }

    private abstract record TestTemplate : ITemplate
    {
        public static Identifier TemplateId { get; } = new("pkg", "Test.Module", "TestTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "test-package";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => throw new NotSupportedException();
    }

    private sealed record CleanFields(
        [property: DamlFieldAttribute("owner")] string Owner,
        [property: DamlFieldAttribute("count")] long Count) : TestTemplate;

    private sealed record DivergentFields(
        [property: DamlFieldAttribute("created_label")] string Label,
        [property: DamlFieldAttribute("operator")] string Operator) : TestTemplate;

    private sealed record MissingMetadata(string Unmarked) : TestTemplate;

    private sealed record InnerFields([property: DamlFieldAttribute("owner")] string Owner);

    private sealed record NestedFields([property: DamlFieldAttribute("inner")] InnerFields Inner) : TestTemplate;
}
