// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Tests;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

/// <summary>
/// Holds the REST submission writer answerable to real participant output, the same way
/// <c>SerializedWireShapeMatchesCaptureTests</c> holds the runtime writer: every capture is read
/// back, encoded through <see cref="RestValueEncoder"/> and the REST serializer options, and the
/// result must be the payload the participant sent.
/// </summary>
public class RestSerializedWireShapeMatchesCaptureTests
{
    private const string NestedOptionalProbe = "probe_nested_optional_matrix.json";

    [Theory]
    [MemberData(nameof(DecodedWireSamplePayloads))]
    public void WriteRecord_should_reproduce_the_captured_payload(string fileName, string payloadPath, string shapeName)
    {
        using var document = DamlLfJsonReaderWireSamplesTests.LoadWireSample(fileName);
        var captured = DamlLfJsonReaderWireSamplesTests.ResolvePayload(document.RootElement, payloadPath);

        var record = DamlLfJsonReaderWireSamplesTests.DeclaredShapes[shapeName](captured);
        using var written = JsonDocument.Parse(
            JsonSerializer.Serialize(RestValueEncoder.ToWireRecord(record), RestRefitSettings.SerializerOptions));

        CapturedPayloadDisagreements.Between(captured, written.RootElement, shapeName).Should().BeEmpty(
            "a REST submission has to speak the wire grammar the participant itself sends");
    }

    [Theory]
    [MemberData(nameof(AcceptedNestedOptionalCandidates))]
    public void WriteValue_should_reproduce_every_nested_optional_encoding_the_participant_accepted(string candidate)
    {
        using var document = DamlLfJsonReaderWireSamplesTests.LoadWireSample(NestedOptionalProbe);
        var echoed = document.RootElement.GetProperty("response").GetProperty(candidate).GetProperty("echoed");

        var nestedNote = ReadOptionalOptionalText(echoed);
        using var written = JsonDocument.Parse(
            JsonSerializer.Serialize(RestValueEncoder.ToWireValue(nestedNote), RestRefitSettings.SerializerOptions));

        CapturedPayloadDisagreements.Between(echoed, written.RootElement, candidate).Should().BeEmpty(
            "the participant accepted and echoed this encoding of an Optional (Optional Text)");
    }

    [Fact]
    public void RestSerializedWireShapeMatchesCapture_should_hold_the_writer_against_every_capture_on_disk()
    {
        var capturesTheWriterIsHeldAgainst = FirstColumnOf(DecodedWireSamplePayloads)
            .Append(NestedOptionalProbe)
            .Distinct();

        capturesTheWriterIsHeldAgainst.Should().BeEquivalentTo(
            Directory.EnumerateFiles(DamlLfJsonReaderWireSamplesTests.CorpusDirectory, "*.json").Select(Path.GetFileName),
            "a capture the REST writer is never held against would let it drift from the participant's grammar");
    }

    [Fact]
    public void AcceptedNestedOptionalCandidates_should_cover_the_three_accepted_encodings() =>
        FirstColumnOf(AcceptedNestedOptionalCandidates)
            .Should().Equal("empty_array", "array_of_empty_array", "array_of_array_of_text");

    public static TheoryData<string, string, string> DecodedWireSamplePayloads
    {
        get
        {
            var payloads = new TheoryData<string, string, string>();
            foreach (ITheoryDataRow row in DamlLfJsonReaderWireSamplesTests.SupportedWireSamplePayloads)
            {
                var data = row.GetData();
                payloads.Add((string)data[0]!, (string)data[1]!, (string)data[2]!);
            }
            return payloads;
        }
    }

    public static TheoryData<string> AcceptedNestedOptionalCandidates
    {
        get
        {
            using var document = DamlLfJsonReaderWireSamplesTests.LoadWireSample(NestedOptionalProbe);
            var candidates = new TheoryData<string>();
            foreach (var candidate in document.RootElement.GetProperty("response").EnumerateObject())
            {
                if (candidate.Value.GetProperty("accepted").GetBoolean())
                    candidates.Add(candidate.Name);
            }
            return candidates;
        }
    }

    private static IEnumerable<string> FirstColumnOf(IEnumerable<ITheoryDataRow> rows) =>
        rows.Select(row => (string)row.GetData()[0]!);

    private static DamlOptionalChain ReadOptionalOptionalText(JsonElement json) =>
        DamlLfJsonDecoders.ReadOptionalChain(
            json,
            DamlLfJsonDecodeContext.Root("nestedNote"),
            (outer, outerContext) => DamlLfJsonDecoders.ReadOptionalChain(
                outer, outerContext, (inner, innerContext) => DamlLfJsonDecoders.ReadText(inner, innerContext)));
}
