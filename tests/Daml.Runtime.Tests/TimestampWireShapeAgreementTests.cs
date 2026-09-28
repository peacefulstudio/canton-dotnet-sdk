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
    public static TheoryData<string, DateTimeOffset, TimeSpan> ToleratedWireTimestampShapes() => new()
    {
        { "2026-01-01T12:34:56Z", new DateTimeOffset(2026, 1, 1, 12, 34, 56, TimeSpan.Zero), TimeSpan.Zero },
        { "2026-01-01T12:34:56.123456Z", new DateTimeOffset(2026, 1, 1, 12, 34, 56, TimeSpan.Zero).AddTicks(1_234_560), TimeSpan.Zero },
        { "2026-01-01T12:34:56.1234567Z", new DateTimeOffset(2026, 1, 1, 12, 34, 56, TimeSpan.Zero).AddTicks(1_234_567), TimeSpan.Zero },
        { "2026-01-01T12:34:56+02:00", new DateTimeOffset(2026, 1, 1, 10, 34, 56, TimeSpan.Zero), TimeSpan.Zero },
        { "2026-01-01T12:34:56.123456-05:00", new DateTimeOffset(2026, 1, 1, 17, 34, 56, TimeSpan.Zero).AddTicks(1_234_560), TimeSpan.Zero },
        { "2026-01-01T12:34:56", new DateTimeOffset(2026, 1, 1, 12, 34, 56, TimeSpan.Zero), TimeSpan.Zero },
    };

    public static TheoryData<string> WireTimestampShapesOutsideTheGrammar() =>
    [
        "2026-01-01T12:34:56z",
        "2026-01-01T12:34:56.12345678Z",
        "2026-01-01",
        "2026-01-01 12:34:56",
        "2026-01-01T12:34Z",
        "2026-01-01T12:34:56+02",
        "2026-1-1T12:34:56Z",
        " 2026-01-01T12:34:56Z",
    ];

    [Theory]
    [MemberData(nameof(ToleratedWireTimestampShapes))]
    public void ReadRecord_should_decode_every_tolerated_wire_timestamp_shape(string wire, DateTimeOffset expectedInstant, TimeSpan expectedOffset)
    {
        var record = DamlLfJsonReader.ReadRecord<RecordedAtHolder>($$"""{"recordedAt":"{{wire}}"}""");

        var timestamp = record.GetRequiredField("recordedAt").Should().BeOfType<DamlTimestamp>().Which.Value;
        timestamp.Should().Be(expectedInstant);
        timestamp.Offset.Should().Be(expectedOffset);
    }

    [Theory]
    [MemberData(nameof(ToleratedWireTimestampShapes))]
    public void DeserializeRecord_should_decode_every_tolerated_wire_timestamp_shape(string wire, DateTimeOffset expectedInstant, TimeSpan expectedOffset)
    {
        var record = DamlJsonSerializer.DeserializeRecord($$"""{"recordedAt":"{{wire}}"}""");

        var timestamp = record.GetRequiredField("recordedAt").Should().BeOfType<DamlTimestamp>().Which.Value;
        timestamp.Should().Be(expectedInstant);
        timestamp.Offset.Should().Be(expectedOffset);
    }

    [Theory]
    [MemberData(nameof(WireTimestampShapesOutsideTheGrammar))]
    public void ReadRecord_should_reject_a_wire_timestamp_outside_the_tolerated_shapes(string wire)
    {
        var act = () => DamlLfJsonReader.ReadRecord<RecordedAtHolder>($$"""{"recordedAt":"{{wire}}"}""");

        act.Should().Throw<JsonException>();
    }

    [Theory]
    [MemberData(nameof(WireTimestampShapesOutsideTheGrammar))]
    public void DeserializeRecord_should_not_infer_a_timestamp_outside_the_tolerated_shapes(string wire)
    {
        var record = DamlJsonSerializer.DeserializeRecord($$"""{"recordedAt":"{{wire}}"}""");

        record.GetRequiredField("recordedAt").Should().NotBeOfType<DamlTimestamp>();
    }
}
