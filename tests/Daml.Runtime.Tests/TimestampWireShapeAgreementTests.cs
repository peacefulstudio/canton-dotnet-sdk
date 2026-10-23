// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

public class TimestampWireShapeAgreementTests
{
    public static TheoryData<string, DateTimeOffset, TimeSpan> LedgerWireTimestampShapes() => new()
    {
        { "2026-01-01T12:34:56Z", new DateTimeOffset(2026, 1, 1, 12, 34, 56, TimeSpan.Zero), TimeSpan.Zero },
        { "2026-01-01T12:34:56.123456Z", new DateTimeOffset(2026, 1, 1, 12, 34, 56, TimeSpan.Zero).AddTicks(1_234_560), TimeSpan.Zero },
    };

    public static TheoryData<string> WireTimestampShapesOutsideTheLedgerGrammar() =>
    [
        "2026-01-01T12:34:56z",
        "2026-01-01T12:34:56.1234567Z",
        "2026-01-01T12:34:56.12345678Z",
        "2026-01-01T12:34:56+02:00",
        "2026-01-01T12:34:56.123456-05:00",
        "2026-01-01T12:34:56+00:00",
        "2026-01-01T12:34:56",
        "2026-01-01T12:34:56.123456",
        "2026-01-01",
        "2026-01-01 12:34:56",
        "2026-01-01T12:34Z",
        "2026-01-01T12:34:56+02",
        "2026-1-1T12:34:56Z",
        " 2026-01-01T12:34:56Z",
    ];

    [Theory]
    [MemberData(nameof(LedgerWireTimestampShapes))]
    public void ReadRecord_should_decode_every_ledger_wire_timestamp_shape(string wire, DateTimeOffset expectedInstant, TimeSpan expectedOffset)
    {
        var record = DamlLfJsonReader.ReadRecord<RecordedAtHolder>($$"""{"recordedAt":"{{wire}}"}""");

        var timestamp = record.GetRequiredField("recordedAt").Should().BeOfType<DamlTimestamp>().Which.Value;
        timestamp.Should().Be(expectedInstant);
        timestamp.Offset.Should().Be(expectedOffset);
    }

    [Theory]
    [MemberData(nameof(LedgerWireTimestampShapes))]
    public void DeserializeRecord_should_decode_every_ledger_wire_timestamp_shape(string wire, DateTimeOffset expectedInstant, TimeSpan expectedOffset)
    {
        var record = DamlJsonSerializer.DeserializeRecord($$"""{"recordedAt":"{{wire}}"}""");

        var timestamp = record.GetRequiredField("recordedAt").Should().BeOfType<DamlTimestamp>().Which.Value;
        timestamp.Should().Be(expectedInstant);
        timestamp.Offset.Should().Be(expectedOffset);
    }

    [Theory]
    [MemberData(nameof(WireTimestampShapesOutsideTheLedgerGrammar))]
    public void ReadRecord_should_reject_a_wire_timestamp_outside_the_ledger_grammar(string wire)
    {
        var act = () => DamlLfJsonReader.ReadRecord<RecordedAtHolder>($$"""{"recordedAt":"{{wire}}"}""");

        act.Should().Throw<JsonException>();
    }

    [Theory]
    [MemberData(nameof(WireTimestampShapesOutsideTheLedgerGrammar))]
    public void DeserializeRecord_should_not_infer_a_timestamp_outside_the_ledger_grammar(string wire)
    {
        var record = DamlJsonSerializer.DeserializeRecord($$"""{"recordedAt":"{{wire}}"}""");

        record.GetRequiredField("recordedAt").Should().NotBeOfType<DamlTimestamp>();
    }

    [Fact]
    public void ReadRecord_should_name_the_malformed_timestamp_when_it_carries_a_seventh_fractional_digit()
    {
        var act = () => DamlLfJsonReader.ReadRecord<RecordedAtHolder>("""{"recordedAt":"2026-01-01T12:34:56.1234567Z"}""");

        act.Should().Throw<JsonException>().WithMessage("*'2026-01-01T12:34:56.1234567Z'*is not a valid Daml Timestamp*");
    }

    [Fact]
    public void DeserializeRecord_should_keep_a_seven_digit_timestamp_as_text()
    {
        var record = DamlJsonSerializer.DeserializeRecord("""{"recordedAt":"2026-01-01T12:34:56.1234567Z"}""");

        record.GetRequiredField("recordedAt").Should().Be(new DamlText("2026-01-01T12:34:56.1234567Z"));
    }

    [Fact]
    public void DeserializeRecord_should_keep_an_offset_timestamp_as_text()
    {
        var record = DamlJsonSerializer.DeserializeRecord("""{"recordedAt":"2026-01-01T12:34:56+02:00"}""");

        record.GetRequiredField("recordedAt").Should().Be(new DamlText("2026-01-01T12:34:56+02:00"));
    }
}
