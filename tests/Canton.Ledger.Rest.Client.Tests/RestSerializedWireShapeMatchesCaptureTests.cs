// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Serialization;
using Daml.Codegen.Testing.Conformance.Tests;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

/// <summary>
/// Holds the REST submission path to the runtime writer: for every capture the participant sent, the
/// <c>createArguments</c> or <c>choiceArgument</c> bytes of the request are exactly the bytes
/// <see cref="DamlJsonSerializer"/> writes. The wire grammar itself is pinned once, by
/// <c>SerializedWireShapeMatchesCaptureTests</c> in the conformance suite.
/// </summary>
public sealed class RestSerializedWireShapeMatchesCaptureTests : IDisposable
{
    private readonly SubmissionRecorder _recorder = new();

    public void Dispose() => _recorder.Dispose();

    [Theory]
    [MemberData(
        nameof(WireCaptures.DecodedWireSamplePayloads),
        MemberType = typeof(WireCaptures))]
    public async Task SubmitAndWaitAsync_writes_the_runtime_writers_bytes_for_every_captured_payload(
        string fileName, string payloadPath, string shapeName)
    {
        using var document = WireCaptures.LoadWireSample(fileName);
        var captured = WireCaptures.ResolvePayload(document.RootElement, payloadPath);
        var record = WireCaptures.DeclaredShapes[shapeName](captured);

        var submitted = await _recorder.CreateArgumentsOnTheWire(record);

        submitted.Should().Be(DamlJsonSerializer.Serialize(record));
    }

    [Theory]
    [MemberData(
        nameof(WireCaptures.AcceptedNestedOptionalCandidates),
        MemberType = typeof(WireCaptures))]
    public async Task SubmitAndWaitAsync_writes_the_runtime_writers_bytes_for_every_accepted_nested_optional(
        string candidate)
    {
        using var document = WireCaptures.LoadWireSample(
            WireCaptures.NestedOptionalProbe);
        var echoed = document.RootElement.GetProperty("response").GetProperty(candidate).GetProperty("echoed");
        var nestedNote = WireCaptures.ReadOptionalOptionalText(echoed);

        var submitted = await _recorder.ChoiceArgumentOnTheWire(nestedNote);

        submitted.Should().Be(DamlJsonSerializer.Serialize(nestedNote));
    }

    [Fact]
    public void RestSerializedWireShapeMatchesCapture_should_hold_the_request_against_every_capture_on_disk()
    {
        var capturesTheRequestIsHeldAgainst = FirstColumnOf(WireCaptures.DecodedWireSamplePayloads)
            .Append(WireCaptures.NestedOptionalProbe)
            .Distinct();

        capturesTheRequestIsHeldAgainst.Should().BeEquivalentTo(
            Directory.EnumerateFiles(WireCaptures.CorpusDirectory, "*.json").Select(Path.GetFileName),
            "a capture the REST request is never held against would let it drift from the runtime writer");
    }

    private static IEnumerable<string> FirstColumnOf(IEnumerable<ITheoryDataRow> rows) =>
        rows.Select(row => (string)row.GetData()[0]!);
}
