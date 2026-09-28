// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlLfJsonReaderScalarTests
{
    public sealed record TextHolder([property: DamlFieldAttribute("name")] string Name) : IDamlRecord<TextHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("name", new DamlText(Name)));

        public static TextHolder FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("name").As<DamlText>().Value);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(DamlField.Create("name", DamlLfJsonDecoders.ReadText(
                DamlLfJsonDecoders.RequireField(json, context, "name"), context.Field("name"))));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_text_field()
    {
        var record = DamlLfJsonReader.ReadRecord<TextHolder>("""{"name":"hello"}""");

        record.GetRequiredField("name").Should().BeOfType<DamlText>().Which.Value.Should().Be("hello");
    }

    [Fact]
    public void ReadRecord_should_decode_an_empty_text_field()
    {
        var record = DamlLfJsonReader.ReadRecord<TextHolder>("""{"name":""}""");

        record.GetRequiredField("name").Should().BeOfType<DamlText>().Which.Value.Should().Be("");
    }

    public sealed record FlagHolder([property: DamlFieldAttribute("flag")] bool Flag) : IDamlRecord<FlagHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("flag", new DamlBool(Flag)));

        public static FlagHolder FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("flag").As<DamlBool>().Value);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(DamlField.Create("flag", DamlLfJsonDecoders.ReadBool(
                DamlLfJsonDecoders.RequireField(json, context, "flag"), context.Field("flag"))));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_bool_field()
    {
        var record = DamlLfJsonReader.ReadRecord<FlagHolder>("""{"flag":true}""");

        record.GetRequiredField("flag").Should().BeOfType<DamlBool>().Which.Value.Should().BeTrue();
    }

    [Fact]
    public void ReadRecord_should_decode_a_false_bool_field()
    {
        var record = DamlLfJsonReader.ReadRecord<FlagHolder>("""{"flag":false}""");

        record.GetRequiredField("flag").Should().BeOfType<DamlBool>().Which.Value.Should().BeFalse();
    }

    [Fact]
    public void ReadRecord_should_reject_a_bool_field_encoded_as_a_json_number()
    {
        var act = () => DamlLfJsonReader.ReadRecord<FlagHolder>("""{"flag":1}""");

        act.Should().Throw<JsonException>().WithMessage("Expected JSON boolean at 'FlagHolder.flag' but found Number");
    }

    [Fact]
    public void ReadRecord_should_name_a_json_boolean_when_rejecting_a_bool_field_encoded_as_a_string()
    {
        var act = () => DamlLfJsonReader.ReadRecord<FlagHolder>("""{"flag":"true"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON boolean at 'FlagHolder.flag' but found String");
    }

    public sealed record CountHolder([property: DamlFieldAttribute("count")] long Count) : IDamlRecord<CountHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("count", new DamlInt64(Count)));

        public static CountHolder FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("count").As<DamlInt64>().Value);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(DamlField.Create("count", DamlLfJsonDecoders.ReadInt64(
                DamlLfJsonDecoders.RequireField(json, context, "count"), context.Field("count"))));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_positive_int64_field_from_its_wire_string_form()
    {
        var record = DamlLfJsonReader.ReadRecord<CountHolder>("""{"count":"42"}""");

        record.GetRequiredField("count").Should().BeOfType<DamlInt64>().Which.Value.Should().Be(42L);
    }

    [Fact]
    public void ReadRecord_should_decode_a_negative_int64_field_from_its_wire_string_form()
    {
        var record = DamlLfJsonReader.ReadRecord<CountHolder>("""{"count":"-1"}""");

        record.GetRequiredField("count").Should().BeOfType<DamlInt64>().Which.Value.Should().Be(-1L);
    }

    [Fact]
    public void ReadRecord_should_reject_an_int64_field_encoded_as_a_json_number_instead_of_the_observed_wire_string()
    {
        var act = () => DamlLfJsonReader.ReadRecord<CountHolder>("""{"count":42}""");

        act.Should().Throw<JsonException>().WithMessage("Expected JSON String at 'CountHolder.count' but found Number");
    }

    [Fact]
    public void ReadRecord_should_reject_a_malformed_int64_field()
    {
        var act = () => DamlLfJsonReader.ReadRecord<CountHolder>("""{"count":"not-a-number"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Value 'not-a-number' at 'CountHolder.count' is not a valid Daml Int64");
    }

    [Fact]
    public void ReadRecord_should_decode_a_zero_int64_field()
    {
        var record = DamlLfJsonReader.ReadRecord<CountHolder>("""{"count":"0"}""");

        record.GetRequiredField("count").Should().BeOfType<DamlInt64>().Which.Value.Should().Be(0L);
    }

    [Fact]
    public void ReadRecord_should_reject_an_int64_field_with_a_leading_plus_sign()
    {
        var act = () => DamlLfJsonReader.ReadRecord<CountHolder>("""{"count":"+42"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Value '+42' at 'CountHolder.count' is not a valid Daml Int64");
    }

    [Fact]
    public void ReadRecord_should_reject_an_int64_field_with_leading_zeroes()
    {
        var act = () => DamlLfJsonReader.ReadRecord<CountHolder>("""{"count":"007"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Value '007' at 'CountHolder.count' is not a valid Daml Int64");
    }

    [Fact]
    public void ReadRecord_should_echo_only_a_bounded_prefix_of_an_oversized_malformed_value()
    {
        var oversizedValue = new string('9', 5_000);

        var act = () => DamlLfJsonReader.ReadRecord<CountHolder>($$"""{"count":"{{oversizedValue}}"}""");

        act.Should().Throw<JsonException>()
            .WithMessage($"Value '{new string('9', 64)}…' at 'CountHolder.count' is not a valid Daml Int64");
    }

    [Fact]
    public void ReadRecord_should_not_split_a_surrogate_pair_when_eliding_an_oversized_malformed_value()
    {
        var surrogateStraddlingValue = new string('9', 63) + "😀😀";

        var act = () => DamlLfJsonReader.ReadRecord<CountHolder>($$"""{"count":"{{surrogateStraddlingValue}}"}""");

        act.Should().Throw<JsonException>()
            .WithMessage($"Value '{new string('9', 63)}…' at 'CountHolder.count' is not a valid Daml Int64");
    }

    public sealed record AmountHolder([property: DamlFieldAttribute("amount")] decimal Amount) : IDamlRecord<AmountHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("amount", new DamlNumeric(Amount)));

        public static AmountHolder FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("amount").As<DamlNumeric>().Value);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(DamlField.Create("amount", DamlLfJsonDecoders.ReadNumeric(
                DamlLfJsonDecoders.RequireField(json, context, "amount"), context.Field("amount"))));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_numeric_field_at_scale_ten()
    {
        var record = DamlLfJsonReader.ReadRecord<AmountHolder>("""{"amount":"42.5000000000"}""");

        record.GetRequiredField("amount").Should().BeOfType<DamlNumeric>()
            .Which.Value.Should().Be(42.5m);
    }

    [Fact]
    public void ReadRecord_should_decode_a_numeric_field_at_scale_two()
    {
        var record = DamlLfJsonReader.ReadRecord<AmountHolder>("""{"amount":"1.25"}""");

        record.GetRequiredField("amount").Should().BeOfType<DamlNumeric>()
            .Which.Value.Should().Be(1.25m);
    }

    [Fact]
    public void ReadRecord_should_reject_a_malformed_numeric_field()
    {
        var act = () => DamlLfJsonReader.ReadRecord<AmountHolder>("""{"amount":"not-a-number"}""");

        act.Should().Throw<JsonException>();
    }

    public sealed record IssuedHolder([property: DamlFieldAttribute("issued")] DateOnly Issued) : IDamlRecord<IssuedHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("issued", new DamlDate(Issued)));

        public static IssuedHolder FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("issued").As<DamlDate>().Value);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(DamlField.Create("issued", DamlLfJsonDecoders.ReadDate(
                DamlLfJsonDecoders.RequireField(json, context, "issued"), context.Field("issued"))));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_date_field()
    {
        var record = DamlLfJsonReader.ReadRecord<IssuedHolder>("""{"issued":"2026-07-31"}""");

        record.GetRequiredField("issued").Should().BeOfType<DamlDate>()
            .Which.Value.Should().Be(new DateOnly(2026, 7, 31));
    }

    [Fact]
    public void ReadRecord_should_reject_a_malformed_date_field()
    {
        var act = () => DamlLfJsonReader.ReadRecord<IssuedHolder>("""{"issued":"31-07-2026"}""");

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void ReadRecord_should_decode_a_timestamp_field_with_a_microsecond_fraction()
    {
        var record = DamlLfJsonReader.ReadRecord<RecordedAtHolder>(
            """{"recordedAt":"2026-07-31T12:34:56.123456Z"}""");

        var expected = new DateTimeOffset(2026, 7, 31, 12, 34, 56, TimeSpan.Zero).AddTicks(1_234_560);
        record.GetRequiredField("recordedAt").Should().BeOfType<DamlTimestamp>()
            .Which.Value.Should().Be(expected);
    }

    [Fact]
    public void ReadRecord_should_decode_a_timestamp_field_without_a_fraction()
    {
        var record = DamlLfJsonReader.ReadRecord<RecordedAtHolder>("""{"recordedAt":"2026-07-31T12:34:56Z"}""");

        var expected = new DateTimeOffset(2026, 7, 31, 12, 34, 56, TimeSpan.Zero);
        record.GetRequiredField("recordedAt").Should().BeOfType<DamlTimestamp>()
            .Which.Value.Should().Be(expected);
    }

    [Fact]
    public void ReadRecord_should_decode_a_timestamp_field_with_a_millisecond_fraction()
    {
        var record = DamlLfJsonReader.ReadRecord<RecordedAtHolder>("""{"recordedAt":"2023-06-15T12:30:45.123Z"}""");

        var expected = new DateTimeOffset(2023, 6, 15, 12, 30, 45, TimeSpan.Zero).AddTicks(1_230_000);
        record.GetRequiredField("recordedAt").Should().BeOfType<DamlTimestamp>()
            .Which.Value.Should().Be(expected);
    }

    [Fact]
    public void ReadRecord_should_reject_a_malformed_timestamp_field()
    {
        var act = () => DamlLfJsonReader.ReadRecord<RecordedAtHolder>("""{"recordedAt":"not-a-timestamp"}""");

        act.Should().Throw<JsonException>();
    }

    public sealed record ReferencedTemplate(long Placeholder) : ITemplate
    {
        public static Identifier TemplateId => new("test-package-id", "Test.Module", nameof(ReferencedTemplate));
        public static string PackageId => "test-package-id";
        public static string PackageName => "test-package-name";
        public static Version PackageVersion => new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("placeholder", new DamlInt64(Placeholder)));
    }

    public sealed record ReferenceHolder(
        [property: DamlFieldAttribute("reference")] ContractId<ReferencedTemplate> Reference)
        : IDamlRecord<ReferenceHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("reference", new DamlContractId(Reference.Value)));

        public static ReferenceHolder FromRecord(DamlRecord record) =>
            new(new ContractId<ReferencedTemplate>(record.GetRequiredField("reference").As<DamlContractId>().Value));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(DamlField.Create("reference", DamlLfJsonDecoders.ReadContractId(
                DamlLfJsonDecoders.RequireField(json, context, "reference"), context.Field("reference"))));
        }
    }

    private const string ReferenceContractId =
        "00e658d5467611d5231f1a4efafd299efb6e78f9f3971f4fb270773c65f8da64e7ca121220f1dcf68bdb90b3eb833c1ff85f6efc86e701399b3e88b358a8d24abc40fa9612";

    [Fact]
    public void ReadRecord_should_decode_a_contract_id_field()
    {
        var record = DamlLfJsonReader.ReadRecord<ReferenceHolder>($$"""{"reference":"{{ReferenceContractId}}"}""");

        record.GetRequiredField("reference").Should().BeOfType<DamlContractId>()
            .Which.Value.Should().Be(ReferenceContractId);
    }

    [Fact]
    public void ReadRecord_should_reject_a_contract_id_field_encoded_as_a_json_number()
    {
        var act = () => DamlLfJsonReader.ReadRecord<ReferenceHolder>("""{"reference":123}""");

        act.Should().Throw<JsonException>().WithMessage("Expected JSON String at 'ReferenceHolder.reference' but found Number");
    }

    private const string TopLevelParty = "alice::1220ab";

    [Fact]
    public void DamlLfJsonDecoders_ReadParty_should_decode_a_top_level_party()
    {
        using var document = JsonDocument.Parse($"\"{TopLevelParty}\"");

        DamlLfJsonDecoders.ReadParty(document.RootElement, DamlLfJsonDecodeContext.Root("Party"))
            .Should().Be(new DamlParty(TopLevelParty));
    }

    [Fact]
    public void DamlLfJsonDecoders_ReadText_should_decode_a_top_level_text()
    {
        using var document = JsonDocument.Parse("\"hello\"");

        DamlLfJsonDecoders.ReadText(document.RootElement, DamlLfJsonDecodeContext.Root("Text"))
            .Should().Be(new DamlText("hello"));
    }

    [Fact]
    public void DamlLfJsonDecoders_ReadBool_should_decode_a_top_level_bool()
    {
        using var document = JsonDocument.Parse("true");

        DamlLfJsonDecoders.ReadBool(document.RootElement, DamlLfJsonDecodeContext.Root("Bool"))
            .Should().Be(new DamlBool(true));
    }

    [Fact]
    public void DamlLfJsonDecoders_ReadInt64_should_decode_a_top_level_int64()
    {
        using var document = JsonDocument.Parse("\"42\"");

        DamlLfJsonDecoders.ReadInt64(document.RootElement, DamlLfJsonDecodeContext.Root("Int64"))
            .Should().Be(new DamlInt64(42));
    }

    [Fact]
    public void DamlLfJsonDecoders_ReadNumeric_should_decode_a_top_level_numeric()
    {
        using var document = JsonDocument.Parse("\"1.25\"");

        DamlLfJsonDecoders.ReadNumeric(document.RootElement, DamlLfJsonDecodeContext.Root("Numeric"))
            .Should().Be(new DamlNumeric(1.25m));
    }

    [Fact]
    public void DamlLfJsonDecoders_ReadDate_should_decode_a_top_level_date()
    {
        using var document = JsonDocument.Parse("\"2026-09-05\"");

        DamlLfJsonDecoders.ReadDate(document.RootElement, DamlLfJsonDecodeContext.Root("Date"))
            .Should().Be(new DamlDate(new DateOnly(2026, 9, 5)));
    }

    [Fact]
    public void DamlLfJsonDecoders_ReadTimestamp_should_decode_a_top_level_timestamp()
    {
        using var document = JsonDocument.Parse("\"2026-09-05T12:34:56Z\"");

        DamlLfJsonDecoders.ReadTimestamp(document.RootElement, DamlLfJsonDecodeContext.Root("Timestamp"))
            .Should().Be(new DamlTimestamp(new DateTimeOffset(2026, 9, 5, 12, 34, 56, TimeSpan.Zero)));
    }

    [Fact]
    public void DamlLfJsonDecoders_ReadUnit_should_decode_a_top_level_unit()
    {
        using var document = JsonDocument.Parse("{}");

        DamlLfJsonDecoders.ReadUnit(document.RootElement, DamlLfJsonDecodeContext.Root("Unit"))
            .Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void DamlLfJsonDecoders_ReadContractId_should_decode_a_top_level_contract_id()
    {
        using var document = JsonDocument.Parse($"\"{ReferenceContractId}\"");

        DamlLfJsonDecoders.ReadContractId(document.RootElement, DamlLfJsonDecodeContext.Root("ContractId"))
            .Should().Be(new DamlContractId(ReferenceContractId));
    }

    [Fact]
    public void DamlLfJsonDecoders_ReadParty_should_reject_a_top_level_value_whose_json_shape_does_not_match()
    {
        using var document = JsonDocument.Parse("123");
        var act = () => DamlLfJsonDecoders.ReadParty(document.RootElement, DamlLfJsonDecodeContext.Root("Party"));

        act.Should().Throw<JsonException>().WithMessage("Expected JSON String at 'Party' but found Number");
    }

    [Fact]
    public void DamlLfJsonDecoders_ReadInt64_should_reject_a_malformed_top_level_value()
    {
        using var document = JsonDocument.Parse("\"twelve\"");
        var act = () => DamlLfJsonDecoders.ReadInt64(document.RootElement, DamlLfJsonDecodeContext.Root("Int64"));

        act.Should().Throw<JsonException>().WithMessage("Value 'twelve' at 'Int64' is not a valid Daml Int64");
    }

    [Fact]
    public void DayOfWeekExtensions_ReadDamlLfJson_should_decode_a_stdlib_enum_constructor()
    {
        using var document = JsonDocument.Parse("\"Monday\"");

        Stdlib.DayOfWeekExtensions.__ReadDamlLfJson(document.RootElement, DamlLfJsonDecodeContext.Root("DayOfWeek"))
            .Should().Be(DamlEnum.Create("Monday"));
    }

    [Fact]
    public void DirectionExtensions_ReadDamlLfJson_should_decode_a_top_level_generated_enum()
    {
        using var document = JsonDocument.Parse("\"Forward\"");

        DirectionExtensions.__ReadDamlLfJson(document.RootElement, DamlLfJsonDecodeContext.Root("Direction"))
            .Should().Be(DamlEnum.Create("Forward"));
    }

    [Fact]
    public void DirectionExtensions_ReadDamlLfJson_should_reject_a_top_level_enum_constructor_the_generated_enum_does_not_declare()
    {
        using var document = JsonDocument.Parse("\"Merit\"");

        var act = () => DirectionExtensions.__ReadDamlLfJson(document.RootElement, DamlLfJsonDecodeContext.Root("Direction"));

        act.Should().Throw<JsonException>().WithMessage(
            "Unknown Daml enum constructor 'Merit' at 'Direction'; expected one of Forward, U$u0020Turn");
    }
}
