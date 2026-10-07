// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Holds the writer answerable to real participant output: every capture the typed reader decodes
/// is read back and re-serialized, and the result must be the payload the participant sent.
/// </summary>
public class SerializedWireShapeMatchesCaptureTests
{
    [Theory]
    [MemberData(
        nameof(DamlLfJsonReaderWireSamplesTests.DecodedWireSamplePayloads),
        MemberType = typeof(DamlLfJsonReaderWireSamplesTests))]
    public void Serialize_should_reproduce_the_captured_payload(
        string fileName, string payloadPath, string shapeName)
    {
        using var document = DamlLfJsonReaderWireSamplesTests.LoadWireSample(fileName);
        var captured = DamlLfJsonReaderWireSamplesTests.ResolvePayload(document.RootElement, payloadPath);

        var record = DamlLfJsonReaderWireSamplesTests.DeclaredShapes[shapeName](captured);
        using var written = JsonDocument.Parse(DamlJsonSerializer.Serialize(record));

        CapturedPayloadDisagreements.Between(captured, written.RootElement, shapeName).Should().BeEmpty(
            "the participant's own payload is the wire grammar the writer has to speak");
    }

    [Theory]
    [MemberData(
        nameof(DamlLfJsonReaderWireSamplesTests.AcceptedNestedOptionalCandidates),
        MemberType = typeof(DamlLfJsonReaderWireSamplesTests))]
    public void Serialize_should_reproduce_every_nested_optional_encoding_the_participant_accepted(string candidate)
    {
        using var document = DamlLfJsonReaderWireSamplesTests.LoadWireSample(DamlLfJsonReaderWireSamplesTests.NestedOptionalProbe);
        var echoed = document.RootElement.GetProperty("response").GetProperty(candidate).GetProperty("echoed");

        var nestedNote = DamlLfJsonReaderWireSamplesTests.ReadOptionalOptionalText(echoed);
        using var written = JsonDocument.Parse(DamlJsonSerializer.Serialize(nestedNote));

        CapturedPayloadDisagreements.Between(echoed, written.RootElement, candidate).Should().BeEmpty(
            "the participant accepted and echoed this encoding of an Optional (Optional Text)");
    }

    [Fact]
    public void AcceptedNestedOptionalCandidates_should_cover_the_three_accepted_encodings() =>
        FirstColumnOf(DamlLfJsonReaderWireSamplesTests.AcceptedNestedOptionalCandidates)
            .Should().Equal("empty_array", "array_of_empty_array", "array_of_array_of_text");

    /// <summary>
    /// Anchored on the corpus directory, not on the member this class's theory data is projected
    /// from: comparing a projection against its own source can never fail. Payload-level totality
    /// is structural instead — <see cref="DecodedWireSamplePayloads"/> loops over every supported
    /// row with no filter — so what needs a gate is a capture landing on disk that no writer
    /// assertion ever reaches.
    /// </summary>
    [Fact]
    public void SerializedWireShapeMatchesCapture_should_hold_the_writer_against_every_capture_on_disk()
    {
        var capturesTheWriterIsHeldAgainst = FirstColumnOf(DamlLfJsonReaderWireSamplesTests.DecodedWireSamplePayloads)
            .Append(DamlLfJsonReaderWireSamplesTests.NestedOptionalProbe)
            .Distinct();

        capturesTheWriterIsHeldAgainst.Should().BeEquivalentTo(
            Directory.EnumerateFiles(DamlLfJsonReaderWireSamplesTests.CorpusDirectory, "*.json")
                .Select(Path.GetFileName),
            "a capture the writer is never held against would let the two halves of the "
            + "grammar drift apart again");
    }

    private static IEnumerable<string> FirstColumnOf(IEnumerable<ITheoryDataRow> rows) =>
        rows.Select(row => (string)row.GetData()[0]!);
}
