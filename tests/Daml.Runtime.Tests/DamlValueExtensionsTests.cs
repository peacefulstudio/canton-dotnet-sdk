// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlValueExtensionsTests
{
    [Fact]
    public void FromDamlValue_should_convert_DamlInt64_to_long()
    {
        new DamlInt64(42).FromDamlValue<long>().Should().Be(42L);
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlBool_to_bool()
    {
        new DamlBool(true).FromDamlValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlNumeric_to_decimal()
    {
        new DamlNumeric(3.14m).FromDamlValue<decimal>().Should().Be(3.14m);
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlDate_to_DateOnly()
    {
        var date = new DateOnly(2024, 6, 15);
        new DamlDate(date).FromDamlValue<DateOnly>().Should().Be(date);
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlTimestamp_to_DateTimeOffset()
    {
        var ts = DateTimeOffset.UnixEpoch.AddSeconds(1704067200);
        new DamlTimestamp(ts).FromDamlValue<DateTimeOffset>().Should().Be(ts);
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlText_to_string()
    {
        new DamlText("hello").FromDamlValue<string>().Should().Be("hello");
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlParty_to_string()
    {
        new DamlParty("party::alice").FromDamlValue<string>().Should().Be("party::alice");
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlContractId_to_string()
    {
        new DamlContractId("00abc").FromDamlValue<string>().Should().Be("00abc");
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlParty_to_Party_value_type()
    {
        var result = new DamlParty("party::alice").FromDamlValue<Party>();

        result.Value.Should().Be("party::alice");
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlContractId_to_typed_ContractId()
    {
        var cid = new DamlContractId("00abc123");

        var result = cid.FromDamlValue<ContractId<TestTemplate>>();

        result.Should().NotBeNull();
        result!.Value.Should().Be("00abc123");
    }

    [Fact]
    public void FromDamlValue_should_return_same_instance_when_target_is_runtime_type()
    {
        var text = new DamlText("hello");

        text.FromDamlValue<DamlText>().Should().BeSameAs(text);
    }

    [Fact]
    public void FromDamlValue_should_return_same_instance_when_target_is_DamlValue()
    {
        var text = new DamlText("hello");

        text.FromDamlValue<DamlValue>().Should().BeSameAs(text);
    }

    [Fact]
    public void FromDamlValue_should_throw_for_unsupported_target_type()
    {
        var action = () => new DamlText("nope").FromDamlValue<DateTime>();

        action.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void FromDamlValue_should_throw_for_unsupported_value_to_string()
    {
        var action = () => new DamlInt64(42).FromDamlValue<string>();

        action.Should().Throw<NotSupportedException>();
    }

    // ── Unit → default(T) ───────────────────────────────────────────

    [Fact]
    public void FromDamlValue_should_throw_when_unwrapping_Unit_to_non_nullable_value_type()
    {
        var action = () => DamlUnit.Instance.FromDamlValue<long>();

        action.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void FromDamlValue_should_return_null_when_unwrapping_Unit_to_reference_type()
    {
        DamlUnit.Instance.FromDamlValue<string>().Should().BeNull();
    }

    [Fact]
    public void FromDamlValue_should_return_unit_instance_when_target_is_DamlUnit()
    {
        DamlUnit.Instance.FromDamlValue<DamlUnit>().Should().BeSameAs(DamlUnit.Instance);
    }

    [Fact]
    public void FromDamlValue_should_return_unit_instance_when_target_is_object()
    {
        // Assignable check takes precedence over the DamlUnit → default path:
        // object is assignable from DamlUnit, so we return the singleton rather than null.
        DamlUnit.Instance.FromDamlValue<object>().Should().BeSameAs(DamlUnit.Instance);
    }

    // ── Nullable<T> primitive branches (regression for asymmetry bug) ──

    [Fact]
    public void FromDamlValue_should_convert_DamlInt64_to_nullable_long()
    {
        new DamlInt64(42).FromDamlValue<long?>().Should().Be(42L);
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlBool_to_nullable_bool()
    {
        new DamlBool(true).FromDamlValue<bool?>().Should().BeTrue();
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlNumeric_to_nullable_decimal()
    {
        new DamlNumeric(3.14m).FromDamlValue<decimal?>().Should().Be(3.14m);
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlDate_to_nullable_DateOnly()
    {
        var date = new DateOnly(2024, 6, 15);
        new DamlDate(date).FromDamlValue<DateOnly?>().Should().Be(date);
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlTimestamp_to_nullable_DateTimeOffset()
    {
        var ts = DateTimeOffset.UnixEpoch.AddSeconds(1704067200);
        new DamlTimestamp(ts).FromDamlValue<DateTimeOffset?>().Should().Be(ts);
    }

    public static TheoryData<string, DamlOptional> ExistingOptionals() => new()
    {
        { "None", DamlOptional.None },
        { "Some", DamlOptional.Some(new DamlInt64(42)) },
    };

    [Theory]
    [MemberData(nameof(ExistingOptionals))]
    public void AsOptional_returns_existing_DamlOptional_unchanged(string shape, DamlOptional optional)
    {
        optional.AsOptional().Should().Be(optional, "an existing {0} optional must pass through untouched", shape);
    }

    [Fact]
    public void AsOptional_wraps_bare_value_as_Some()
    {
        new DamlInt64(42).AsOptional().Should().Be(DamlOptional.Some(new DamlInt64(42)));
    }

    [Fact]
    public void AsOptional_reads_a_DamlOptionalChain_as_the_DamlOptional_level_it_is_instead_of_wrapping_it_as_Some()
    {
        DamlOptionalChain.Some(new DamlText("deep")).AsOptional()
            .Should().Be(DamlOptional.Some(new DamlText("deep")));
        DamlOptionalChain.None.AsOptional().Should().Be(DamlOptional.None);
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlParty_to_nullable_Party()
    {
        var result = new DamlParty("party::alice").FromDamlValue<Party?>();

        result.Should().NotBeNull();
        result!.Value.Value.Should().Be("party::alice");
    }

    [Fact]
    public void FromDamlValue_should_return_null_when_unwrapping_Unit_to_nullable_primitive()
    {
        DamlUnit.Instance.FromDamlValue<long?>().Should().BeNull();
    }

    [Fact]
    public void FromDamlValue_should_convert_a_DamlRecord_to_a_generated_record_type()
    {
        var record = DamlRecord.Create(new DamlField("count", new DamlInt64(7)), new DamlField("label", new DamlText("seven")));

        record.FromDamlValue<TestSplit>().Should().Be(new TestSplit(7, "seven"));
    }

    [Fact]
    public void FromDamlValue_should_let_a_generated_record_factory_failure_surface_unwrapped()
    {
        var action = () => DamlRecord.Create(new DamlField("count", new DamlInt64(7))).FromDamlValue<TestSplit>();

        action.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("label");
    }

    [Fact]
    public void FromDamlValue_should_throw_when_a_DamlRecord_targets_a_type_without_a_record_factory()
    {
        var action = () => DamlRecord.Create().FromDamlValue<long>();

        action.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void FromDamlValue_should_convert_an_empty_DamlOptional_to_nullable_long_null()
    {
        DamlOptional.None.FromDamlValue<long?>().Should().BeNull();
    }

    [Fact]
    public void FromDamlValue_should_convert_a_present_DamlOptional_to_the_carried_nullable_long()
    {
        DamlOptional.Some(new DamlInt64(5)).FromDamlValue<long?>().Should().Be(5L);
    }

    [Fact]
    public void FromDamlValue_should_convert_a_present_DamlOptional_to_the_carried_string()
    {
        DamlOptional.Some(new DamlText("carried")).FromDamlValue<string>().Should().Be("carried");
    }

    [Fact]
    public void FromDamlValue_should_convert_an_empty_DamlOptional_to_a_null_generated_record()
    {
        DamlOptional.None.FromDamlValue<TestSplit>().Should().BeNull();
    }

    [Fact]
    public void FromDamlValue_should_convert_a_present_DamlOptional_to_the_carried_generated_record()
    {
        var carried = DamlRecord.Create(new DamlField("count", new DamlInt64(1)), new DamlField("label", new DamlText("one")));

        DamlOptional.Some(carried).FromDamlValue<TestSplit>().Should().Be(new TestSplit(1, "one"));
    }

    [Fact]
    public void FromDamlValue_should_throw_when_an_empty_DamlOptional_targets_a_non_nullable_value_type()
    {
        var action = () => DamlOptional.None.FromDamlValue<long>();

        action.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void FromDamlValue_should_throw_when_a_present_DamlOptional_carries_a_value_of_the_wrong_type()
    {
        var action = () => DamlOptional.Some(new DamlText("nope")).FromDamlValue<long?>();

        action.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void FromDamlValue_should_return_the_DamlOptional_itself_when_the_target_is_DamlOptional()
    {
        var optional = DamlOptional.Some(new DamlInt64(5));

        optional.FromDamlValue<DamlOptional>().Should().BeSameAs(optional);
    }

    [Fact]
    public void FromDamlValue_should_convert_an_empty_DamlOptional_to_Optional_None()
    {
        DamlOptional.None.FromDamlValue<Optional<string>>().Should().Be(new Optional<string>.None());
    }

    [Fact]
    public void FromDamlValue_should_convert_a_present_DamlOptional_to_Optional_Some()
    {
        DamlOptional.Some(new DamlText("x")).FromDamlValue<Optional<string>>().Should().Be(new Optional<string>.Some("x"));
    }

    [Fact]
    public void FromDamlValue_should_convert_an_empty_DamlOptional_to_a_nested_Optional_None()
    {
        DamlOptional.None.FromDamlValue<Optional<Optional<string>>>()
            .Should().Be(new Optional<Optional<string>>.None());
    }

    [Fact]
    public void FromDamlValue_should_convert_a_Some_of_None_to_a_nested_Optional_Some_of_None()
    {
        DamlOptional.Some(DamlOptional.None).FromDamlValue<Optional<Optional<string>>>()
            .Should().Be(new Optional<Optional<string>>.Some(new Optional<string>.None()));
    }

    [Fact]
    public void FromDamlValue_should_convert_a_Some_of_Some_to_a_nested_Optional_Some_of_Some()
    {
        DamlOptional.Some(DamlOptional.Some(new DamlText("x"))).FromDamlValue<Optional<Optional<string>>>()
            .Should().Be(new Optional<Optional<string>>.Some(new Optional<string>.Some("x")));
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlOptionalChain_levels_to_a_nested_Optional()
    {
        DamlOptionalChain.Some(DamlOptionalChain.Some(new DamlText("x"))).FromDamlValue<Optional<Optional<string>>>()
            .Should().Be(new Optional<Optional<string>>.Some(new Optional<string>.Some("x")));
    }

    [Fact]
    public void FromDamlValue_should_unwrap_a_Some_of_Some_chain_to_the_carried_string()
    {
        DamlOptionalChain.Some(DamlOptionalChain.Some(new DamlText("x"))).FromDamlValue<string>()
            .Should().Be("x");
    }

    [Fact]
    public void FromDamlValue_should_convert_a_Some_of_None_chain_to_a_null_string()
    {
        DamlOptionalChain.Some(DamlOptionalChain.None).FromDamlValue<string>().Should().BeNull();
    }

    [Fact]
    public void FromDamlValue_should_convert_a_None_chain_to_a_null_string()
    {
        DamlOptionalChain.None.FromDamlValue<string>().Should().BeNull();
    }

    [Fact]
    public void FromDamlValue_should_unwrap_a_three_level_chain_to_the_carried_nullable_long()
    {
        var chain = DamlOptionalChain.Some(DamlOptionalChain.Some(DamlOptionalChain.Some(new DamlInt64(7))));

        chain.FromDamlValue<long?>().Should().Be(7L);
    }

    [Fact]
    public void FromDamlValue_should_convert_a_three_level_chain_with_an_inner_None_to_a_null_nullable_long()
    {
        var chain = DamlOptionalChain.Some(DamlOptionalChain.Some(DamlOptionalChain.None));

        chain.FromDamlValue<long?>().Should().BeNull();
    }

    [Fact]
    public void FromDamlValue_should_unwrap_a_chain_over_a_flat_optional_to_the_carried_string()
    {
        DamlOptionalChain.Some(DamlOptional.Some(new DamlText("x"))).FromDamlValue<string>()
            .Should().Be("x");
    }

    [Fact]
    public void FromDamlValue_should_convert_a_flat_Some_of_None_to_a_null_nullable_long()
    {
        DamlOptional.Some(DamlOptional.None).FromDamlValue<long?>().Should().BeNull();
    }

    [Fact]
    public void FromDamlValue_should_unwrap_a_Some_chain_over_a_generated_record()
    {
        var carried = DamlRecord.Create(new DamlField("count", new DamlInt64(1)), new DamlField("label", new DamlText("one")));

        DamlOptionalChain.Some(DamlOptionalChain.Some(carried)).FromDamlValue<TestSplit>()
            .Should().Be(new TestSplit(1, "one"));
    }

    [Fact]
    public void FromDamlValue_should_throw_when_a_None_chain_targets_a_non_nullable_value_type()
    {
        var action = () => DamlOptionalChain.None.FromDamlValue<long>();

        action.Should().Throw<NotSupportedException>()
            .WithMessage("Cannot convert an empty DamlOptional to value type System.Int64*");
    }

    [Fact]
    public void FromDamlValue_should_throw_when_an_inner_None_chain_level_targets_a_non_nullable_value_type()
    {
        var action = () => DamlOptionalChain.Some(DamlOptionalChain.None).FromDamlValue<long>();

        action.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void FromDamlValue_should_throw_when_a_chain_carries_a_value_of_the_wrong_type()
    {
        var action = () => DamlOptionalChain.Some(DamlOptionalChain.Some(new DamlText("nope"))).FromDamlValue<long?>();

        action.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void FromDamlValue_should_throw_when_a_None_chain_targets_DamlOptional()
    {
        var action = () => DamlOptionalChain.None.FromDamlValue<DamlOptional>();

        action.Should().Throw<NotSupportedException>().WithMessage(
            "Cannot convert Daml.Runtime.Data.DamlOptionalChain to Daml.Runtime.Data.DamlOptional. "
            + "Use a DamlValue-derived type as TResult for direct access.");
    }

    [Fact]
    public void FromDamlValue_should_throw_when_a_Some_of_None_chain_targets_DamlOptional()
    {
        var action = () => DamlOptionalChain.Some(DamlOptionalChain.None).FromDamlValue<DamlOptional>();

        action.Should().Throw<NotSupportedException>().WithMessage(
            "Cannot convert Daml.Runtime.Data.DamlOptionalChain to Daml.Runtime.Data.DamlOptional. "
            + "Use a DamlValue-derived type as TResult for direct access.");
    }

    [Fact]
    public void FromDamlValue_should_return_the_DamlOptionalChain_itself_when_the_target_is_DamlOptionalChain()
    {
        var chain = DamlOptionalChain.Some(new DamlInt64(5));

        chain.FromDamlValue<DamlOptionalChain>().Should().BeSameAs(chain);
    }

    [Fact]
    public void FromDamlValue_should_throw_when_an_Optional_target_receives_a_value_that_is_not_an_optional()
    {
        var action = () => new DamlText("x").FromDamlValue<Optional<string>>();

        action.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void FromDamlValue_should_decode_a_carried_record_through_the_target_types_own_json_reader()
    {
        var carried = new DamlUndecodedJson("{\"count\":\"7\",\"label\":\"seven\"}");

        carried.FromDamlValue<ReadableSplit>().Should().Be(new ReadableSplit(7, "seven"));
    }

    [Fact]
    public void FromDamlValue_should_return_the_carried_value_itself_when_asked_for_a_DamlUndecodedJson()
    {
        var carried = new DamlUndecodedJson("{\"count\":\"7\"}");

        carried.FromDamlValue<DamlUndecodedJson>().Should().BeSameAs(carried);
    }

    [Fact]
    public void FromDamlValue_should_decode_a_carried_text_to_a_string()
    {
        new DamlUndecodedJson("\"hello\"").FromDamlValue<string>().Should().Be("hello");
    }

    [Fact]
    public void FromDamlValue_should_decode_a_carried_int64_to_a_long()
    {
        new DamlUndecodedJson("\"42\"").FromDamlValue<long>().Should().Be(42L);
    }

    [Fact]
    public void FromDamlValue_should_decode_a_carried_bool_to_a_bool()
    {
        new DamlUndecodedJson("true").FromDamlValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void FromDamlValue_should_decode_a_carried_numeric_to_a_decimal()
    {
        new DamlUndecodedJson("\"1.5000000000\"").FromDamlValue<decimal>().Should().Be(1.5m);
    }

    [Fact]
    public void FromDamlValue_should_decode_a_carried_party_to_a_Party()
    {
        new DamlUndecodedJson("\"alice::ns\"").FromDamlValue<Party>().Value.Should().Be("alice::ns");
    }

    [Fact]
    public void FromDamlValue_should_decode_a_carried_contract_id_to_a_ContractId()
    {
        new DamlUndecodedJson("\"00abc\"").FromDamlValue<ContractId<TestTemplate>>()!.Value.Should().Be("00abc");
    }

    [Fact]
    public void FromDamlValue_should_decode_a_carried_json_null_to_null_for_a_reference_target()
    {
        new DamlUndecodedJson("null").FromDamlValue<string>().Should().BeNull();
    }

    [Fact]
    public void FromDamlValue_should_decode_a_carried_json_null_to_null_for_a_nullable_target()
    {
        new DamlUndecodedJson("null").FromDamlValue<long?>().Should().BeNull();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[[]]")]
    [InlineData("[[\"x\"]]")]
    public void FromDamlValue_should_refuse_carried_array_json_for_a_string_target(string lfJson)
    {
        var action = () => new DamlUndecodedJson(lfJson).FromDamlValue<string>();

        action.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[[]]")]
    [InlineData("[[\"7\"]]")]
    public void FromDamlValue_should_refuse_carried_array_json_for_a_nullable_long_target(string lfJson)
    {
        var action = () => new DamlUndecodedJson(lfJson).FromDamlValue<long?>();

        action.Should().Throw<JsonException>();
    }

    [Fact]
    public void FromDamlValue_should_still_refuse_carried_array_json_for_a_list_target()
    {
        var action = () => new DamlUndecodedJson("""["x"]""").FromDamlValue<List<string>>();

        action.Should().Throw<NotSupportedException>()
            .WithMessage("Cannot decode carried Daml-LF JSON to System.Collections.Generic.List`1[System.String].*");
    }

    [Fact]
    public void FromDamlValue_should_still_refuse_carried_array_json_for_an_Optional_target()
    {
        var action = () => new DamlUndecodedJson("[]").FromDamlValue<Optional<string>>();

        action.Should().Throw<NotSupportedException>()
            .WithMessage("Cannot decode carried Daml-LF JSON to Daml.Runtime.Stdlib.Optional`1[System.String].*");
    }

    [Fact]
    public void FromDamlValue_should_still_refuse_carried_array_json_for_a_DamlOptional_target()
    {
        var action = () => new DamlUndecodedJson("[]").FromDamlValue<DamlOptional>();

        action.Should().Throw<NotSupportedException>()
            .WithMessage("Cannot decode carried Daml-LF JSON to Daml.Runtime.Data.DamlOptional.*");
    }

    [Fact]
    public void FromDamlValue_should_throw_when_a_carried_json_null_meets_a_non_nullable_value_type()
    {
        var action = () => new DamlUndecodedJson("null").FromDamlValue<long>();

        action.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void FromDamlValue_should_surface_a_JsonException_when_the_carried_json_does_not_fit_the_target()
    {
        var action = () => new DamlUndecodedJson("\"not-a-number\"").FromDamlValue<long>();

        action.Should().Throw<JsonException>();
    }

    [Fact]
    public void FromDamlValue_should_throw_when_a_carried_value_meets_a_target_with_no_json_reader()
    {
        var action = () => new DamlUndecodedJson("{\"tag\":\"Left\",\"value\":\"x\"}").FromDamlValue<Either<string, long>>();

        action.Should().Throw<NotSupportedException>();
    }

    internal sealed record ReadableSplit(long Count, string Label) : IDamlRecord<ReadableSplit>
    {
        public DamlRecord ToRecord() =>
            DamlRecord.Create(new DamlField("count", new DamlInt64(Count)), new DamlField("label", new DamlText(Label)));

        public static ReadableSplit FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("count").As<DamlInt64>().Value, record.GetRequiredField("label").As<DamlText>().Value);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            var fields = new List<DamlField>();
            var root = DamlLfJsonDecoders.RequireObject(json, context);
            DamlLfJsonDecoders.AddFieldIfPresent(fields, root, "count", element => DamlLfJsonDecoders.ReadInt64(element, context));
            DamlLfJsonDecoders.AddFieldIfPresent(fields, root, "label", element => DamlLfJsonDecoders.ReadText(element, context));
            return new DamlRecord(null, fields);
        }
    }

    internal sealed record TestSplit(long Count, string Label) : IDamlRecord<TestSplit>
    {
        public DamlRecord ToRecord() =>
            DamlRecord.Create(new DamlField("count", new DamlInt64(Count)), new DamlField("label", new DamlText(Label)));

        public static TestSplit FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("count").As<DamlInt64>().Value, record.GetRequiredField("label").As<DamlText>().Value);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            throw new NotSupportedException();
    }

    internal sealed record TestTemplate(string Owner) : ITemplate
    {
        public static Identifier TemplateId { get; } = new("pkg", "Module", "Template");
        public static string PackageId => "pkg";
        public static string PackageName => "test-package";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() =>
            new(null, [new DamlField(nameof(Owner), new DamlParty(Owner))]);
    }
}
