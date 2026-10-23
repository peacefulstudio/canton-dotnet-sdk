// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// Holds the writer answerable to real participant output: every capture the typed reader decodes
/// is read back and re-serialized, and the result must be the payload the participant sent.
/// </summary>
public class SerializedWireShapeMatchesCaptureTests
{
    [Theory]
    [MemberData(
        nameof(WireCaptures.DecodedWireSamplePayloads),
        MemberType = typeof(WireCaptures))]
    public void Serialize_should_reproduce_the_captured_payload(
        string fileName, string payloadPath, string shapeName)
    {
        using var document = WireCaptures.LoadWireSample(fileName);
        var captured = WireCaptures.ResolvePayload(document.RootElement, payloadPath);

        var record = WireCaptures.DeclaredShapes[shapeName](captured);
        using var written = JsonDocument.Parse(DamlJsonSerializer.Serialize(record));

        CapturedPayloadDisagreements.Between(captured, written.RootElement, shapeName).Should().BeEmpty(
            "the participant's own payload is the wire grammar the writer has to speak");
    }

    [Theory]
    [MemberData(
        nameof(WireCaptures.AcceptedNestedOptionalCandidates),
        MemberType = typeof(WireCaptures))]
    public void Serialize_should_reproduce_every_nested_optional_encoding_the_participant_accepted(string candidate)
    {
        using var document = WireCaptures.LoadWireSample(WireCaptures.NestedOptionalProbe);
        var echoed = document.RootElement.GetProperty("response").GetProperty(candidate).GetProperty("echoed");

        var nestedNote = WireCaptures.ReadOptionalOptionalText(echoed);
        using var written = JsonDocument.Parse(DamlJsonSerializer.Serialize(nestedNote));

        CapturedPayloadDisagreements.Between(echoed, written.RootElement, candidate).Should().BeEmpty(
            "the participant accepted and echoed this encoding of an Optional (Optional Text)");
    }

    [Fact]
    public void AcceptedNestedOptionalCandidates_should_cover_the_three_accepted_encodings() =>
        FirstColumnOf(WireCaptures.AcceptedNestedOptionalCandidates)
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
        var capturesTheWriterIsHeldAgainst = FirstColumnOf(WireCaptures.DecodedWireSamplePayloads)
            .Append(WireCaptures.NestedOptionalProbe)
            .Distinct();

        capturesTheWriterIsHeldAgainst.Should().BeEquivalentTo(
            Directory.EnumerateFiles(WireCaptures.CorpusDirectory, "*.json")
                .Select(Path.GetFileName),
            "a capture the writer is never held against would let the two halves of the "
            + "grammar drift apart again");
    }

    [Fact]
    public void Serialize_should_write_every_flat_nested_shape_as_the_encoding_the_participant_accepted()
    {
        using var matrix = WireCaptures.LoadWireSample(WireCaptures.NestedOptionalProbe);
        var acceptedSomeNone = WireCaptures.ResolvePayload(
            matrix.RootElement, "response/array_of_empty_array/sent");
        var acceptedSomeSome = WireCaptures.ResolvePayload(
            matrix.RootElement, "response/array_of_array_of_text/sent");

        SameJson(DamlJsonSerializer.Serialize(DamlOptional.Some(DamlOptional.None)), acceptedSomeNone)
            .Should().BeTrue();
        SameJson(
                DamlJsonSerializer.Serialize(DamlOptional.Some(DamlOptional.Some(new DamlText("deep")))),
                acceptedSomeSome)
            .Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(FlatNestedShapesWithTheirChainEquivalent))]
    public void Serialize_then_ReadRecord_should_recover_the_chain_equivalent_of_a_flat_nested_optional(
        DamlValue flat, DamlValue expectedChain)
    {
        var json = DamlJsonSerializer.Serialize(new DamlRecord(null,
        [
            new DamlField("owner", new DamlParty("Alice::1220")),
            new DamlField("midMaybe", flat),
        ]));

        var read = DamlLfJsonReader.ReadRecord<NestedOptionalTails>(json);

        read.GetRequiredField("midMaybe").Should().Be(expectedChain);
    }

    public static TheoryData<DamlValue, DamlValue> FlatNestedShapesWithTheirChainEquivalent => new()
    {
        { DamlOptional.Some(DamlOptional.None), DamlOptionalChain.Some(DamlOptionalChain.None) },
        {
            DamlOptional.Some(DamlOptional.Some(new DamlText("deep"))),
            DamlOptionalChain.Some(DamlOptionalChain.Some(new DamlText("deep")))
        },
    };

    [Theory]
    [MemberData(nameof(WireCaptures.RejectedNestedOptionalCandidates), MemberType = typeof(WireCaptures))]
    public void Serialize_should_never_emit_a_nested_optional_encoding_the_participant_rejected(string candidate)
    {
        using var capture = WireCaptures.LoadWireSample(WireCaptures.NestedOptionalProbe);
        var rejected = JsonNode.Parse(WireCaptures.SentNestedOptionalCandidate(capture, candidate).GetRawText());

        var emitted = new[]
        {
            DamlJsonSerializer.Serialize(DamlOptionalChain.None),
            DamlJsonSerializer.Serialize(DamlOptionalChain.Some(DamlOptionalChain.None)),
            DamlJsonSerializer.Serialize(
                DamlOptionalChain.Some(DamlOptionalChain.Some(new DamlText("deep")))),
        };

        emitted.Should().HaveCount(3,
            "a shorter array would satisfy the disjointness below without serializing anything");
        emitted.Select(json => JsonNode.DeepEquals(JsonNode.Parse(json), rejected))
            .Should().AllBeEquivalentTo(false,
                $"the participant answered HTTP 500 to candidate '{candidate}' in a nested position");
    }

    private static bool SameJson(string written, JsonElement captured) =>
        JsonNode.DeepEquals(JsonNode.Parse(written), JsonNode.Parse(captured.GetRawText()));

    private static IEnumerable<string> FirstColumnOf(IEnumerable<ITheoryDataRow> rows) =>
        rows.Select(row => (string)row.GetData()[0]!);
}
