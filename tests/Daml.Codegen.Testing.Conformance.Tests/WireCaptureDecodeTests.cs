// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class WireCaptureDecodeTests
{
    private static readonly string[] FactConsumedWireSamples =
    [
        "acs_wildcard_verbose_true.json",
        "acs_wildcard_verbose_false.json",
        "probe_nested_optional_matrix.json",
    ];

    [Theory]
    [MemberData(nameof(WireCaptures.SupportedWireSamplePayloads), MemberType = typeof(WireCaptures))]
    public void ReadRecord_should_decode_captured_payloads_inside_the_reader_type_mapping(
        string fileName, string payloadPath, string shapeName, (string Label, Type DamlType)[] expectedFields)
    {
        using var document = WireCaptures.LoadWireSample(fileName);
        var payload = WireCaptures.ResolvePayload(document.RootElement, payloadPath);

        var record = WireCaptures.DeclaredShapes[shapeName](payload);

        record.RecordId.Should().BeNull();
        record.Fields.Select(field => (field.Label, DamlType: field.Value.GetType()))
            .Should().Equal(expectedFields);
    }

    [Fact]
    public void ReadVariant_should_decode_the_captured_exercise_result_as_its_variant_arm()
    {
        using var document = WireCaptures.LoadWireSample("exercise_describe.json");
        var payload = WireCaptures.ResolvePayload(
            document.RootElement, "response/transaction/events/0/ExercisedEvent/exerciseResult");

        var variant = DamlLfJsonDecoders.ReadVariant<Outcome>(payload, DamlLfJsonDecodeContext.Root(nameof(Outcome)));

        variant.Constructor.Should().Be("Win");
        var details = variant.GetValue<DamlRecord>();
        details.Fields.Select(field => field.Label).Should().Equal("prize", "tier");
        details.GetRequiredField("prize").Should().BeOfType<DamlNumeric>().Which.Value.Should().Be(1.25m);
        details.GetRequiredField("tier").Should().Be(new DamlText("gold"));
    }

    [Fact]
    public void ReadUnit_should_decode_the_captured_unit_exercise_result()
    {
        using var document = WireCaptures.LoadWireSample("exercise_ping.json");
        var payload = WireCaptures.ResolvePayload(
            document.RootElement, "response/transaction/events/0/ExercisedEvent/exerciseResult");

        DamlLfJsonDecoders.ReadUnit(payload, DamlLfJsonDecodeContext.Root("Unit"))
            .Should().BeSameAs(DamlUnit.Instance);
    }

    [Fact]
    public void KeyJsonReader_should_decode_the_captured_tuple_key_as_a_stdlib_tuple_record()
    {
        using var document = WireCaptures.LoadWireSample("create_keyed_contract_key.json");
        var capturedKey = WireCaptures.ResolvePayload(
            document.RootElement, "response/transaction/events/0/CreatedEvent/contractKey");

        var key = Membership.Key.KeyJsonReader(capturedKey, DamlLfJsonDecodeContext.Root("Membership.Key"));

        key.Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("_1", new DamlParty(capturedKey.GetProperty("_1").GetString()!)),
            new DamlField("_2", new DamlText(capturedKey.GetProperty("_2").GetString()!)));
    }

    [Fact]
    public void WireSamplesCorpus_should_show_identical_contract_entries_for_verbose_true_and_false()
    {
        using var verboseTrue = WireCaptures.LoadWireSample("acs_wildcard_verbose_true.json");
        using var verboseFalse = WireCaptures.LoadWireSample("acs_wildcard_verbose_false.json");

        var trueEntries = verboseTrue.RootElement.GetProperty("response").EnumerateArray()
            .Select(entry => entry.GetProperty("contractEntry")).ToList();
        var falseEntries = verboseFalse.RootElement.GetProperty("response").EnumerateArray()
            .Select(entry => entry.GetProperty("contractEntry")).ToList();

        trueEntries.Should().HaveSameCount(falseEntries);
        foreach (var (verboseEntry, terseEntry) in trueEntries.Zip(falseEntries))
        {
            JsonElement.DeepEquals(verboseEntry, terseEntry).Should().BeTrue(
                "the conformance README records that the verbose flag leaves ACS payloads untouched");
        }
    }

    [Fact]
    public void WireSamplesCorpus_should_keep_the_nested_optional_probe_matrix_self_consistent()
    {
        using var document = WireCaptures.LoadWireSample("probe_nested_optional_matrix.json");

        var acceptedCandidates = new List<string>();
        foreach (var candidate in document.RootElement.GetProperty("response").EnumerateObject())
        {
            if (candidate.Value.GetProperty("accepted").GetBoolean())
            {
                acceptedCandidates.Add(candidate.Name);
                JsonElement.DeepEquals(
                        candidate.Value.GetProperty("sent"),
                        candidate.Value.GetProperty("echoed"))
                    .Should().BeTrue($"the participant echoed accepted candidate '{candidate.Name}' back verbatim");
            }
            else
            {
                candidate.Value.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
                candidate.Value.TryGetProperty("echoed", out _).Should().BeFalse();
            }
        }

        acceptedCandidates.Should().Equal("empty_array", "array_of_empty_array", "array_of_array_of_text");
    }

    [Fact]
    public void WireSamplesCorpus_should_fail_when_a_capture_is_not_consumed_by_these_tests()
    {
        var consumed = FileNamesOf(WireCaptures.SupportedWireSamplePayloads)
            .Concat(FactConsumedWireSamples)
            .Distinct();

        var captured = Directory.EnumerateFiles(WireCaptures.CorpusDirectory, "*.json").Select(Path.GetFileName);

        captured.Should().BeEquivalentTo(
            consumed,
            "every capture under conformance/*/wire must be exercised by a reader test");
    }

    [Fact]
    public void ReadRecord_should_decode_the_captured_nested_optional_encodings_as_a_chain()
    {
        using var matrix = WireCaptures.LoadWireSample("probe_nested_optional_matrix.json");
        var someNone = WireCaptures.ResolvePayload(matrix.RootElement, "response/array_of_empty_array/sent");

        var record = DamlLfJsonReader.ReadRecord<NestedOptionalTails>(TailsWithMidMaybe(someNone.GetRawText()));

        record.GetRequiredField("midMaybe")
            .Should().Be(DamlOptionalChain.Some(DamlOptionalChain.None));
    }

    [Fact]
    public void ReadRecord_should_distinguish_the_three_accepted_nested_optional_encodings()
    {
        DamlLfJsonReader.ReadRecord<NestedOptionalTails>(TailsWithMidMaybe("[]"))
            .GetRequiredField("midMaybe").Should().Be(DamlOptionalChain.None);
        DamlLfJsonReader.ReadRecord<NestedOptionalTails>(TailsWithMidMaybe("[[]]"))
            .GetRequiredField("midMaybe").Should().Be(DamlOptionalChain.Some(DamlOptionalChain.None));
        DamlLfJsonReader.ReadRecord<NestedOptionalTails>(TailsWithMidMaybe("""[["deep"]]"""))
            .GetRequiredField("midMaybe")
            .Should().Be(DamlOptionalChain.Some(DamlOptionalChain.Some(new DamlText("deep"))));
    }

    [Theory]
    [MemberData(nameof(WireCaptures.NestedOptionalCandidatesOnCapture), MemberType = typeof(WireCaptures))]
    public void ReadRecord_should_accept_or_reject_each_nested_optional_encoding_as_the_participant_did(string candidate)
    {
        using var capture = WireCaptures.LoadWireSample(WireCaptures.NestedOptionalProbe);
        var sent = WireCaptures.SentNestedOptionalCandidate(capture, candidate);
        var accepted = WireCaptures.ParticipantAccepted(capture, candidate);

        var act = () => DamlLfJsonReader.ReadRecord<NestedOptionalTails>(TailsWithMidMaybe(sent.GetRawText()));

        if (accepted)
        {
            act.Should().NotThrow($"the participant accepted and echoed candidate '{candidate}'");
        }
        else
        {
            act.Should().Throw<JsonException>($"the participant rejected candidate '{candidate}' with HTTP 500")
                .Which.Message.Should().StartWith("Expected JSON Array at 'NestedOptionalTails.midMaybe");
        }
    }

    [Fact]
    public void ReadRecord_should_reject_a_nested_optional_level_carrying_more_than_one_element()
    {
        var act = () => DamlLfJsonReader.ReadRecord<NestedOptionalTails>(TailsWithMidMaybe("[[],[]]"));

        act.Should().Throw<JsonException>().WithMessage(
            "A nested Daml Optional at 'NestedOptionalTails.midMaybe' encodes as an array of at most "
            + "one element but found 2");
    }

    [Fact]
    public void ReadRecord_should_name_the_chain_level_that_failed()
    {
        var outerLevel = () => DamlLfJsonReader.ReadRecord<ThreeDeepOptionals>(ThreeDeepWithDeepNote("[null]"));
        var innerLevel = () => DamlLfJsonReader.ReadRecord<ThreeDeepOptionals>(ThreeDeepWithDeepNote("[[null]]"));

        outerLevel.Should().Throw<JsonException>().WithMessage(
            "Expected JSON Array at 'ThreeDeepOptionals.deepNote[0]' but found Null");
        innerLevel.Should().Throw<JsonException>().WithMessage(
            "Expected JSON Array at 'ThreeDeepOptionals.deepNote[0][0]' but found Null");
    }

    [Fact]
    public void ReadRecord_should_name_the_chain_level_carrying_more_than_one_element()
    {
        var act = () => DamlLfJsonReader.ReadRecord<ThreeDeepOptionals>(ThreeDeepWithDeepNote("[[[],[]]]"));

        act.Should().Throw<JsonException>().WithMessage(
            "A nested Daml Optional at 'ThreeDeepOptionals.deepNote[0]' encodes as an array of at most "
            + "one element but found 2");
    }

    [Fact]
    public void ReadRecord_should_read_every_level_of_a_chain_as_a_chain_level()
    {
        var innerAbsent = DamlLfJsonReader.ReadRecord<NestedOptionalTails>(TailsWithMidMaybe("[[]]"))
            .GetRequiredField("midMaybe").As<DamlOptionalChain>();

        innerAbsent.Value.Should().BeOfType<DamlOptionalChain>(
            "the interior level of a chain is CLR-identical to a flat wrapper and can only be "
            + "reached by recursion from the root; deciding it structurally reads it as flat");
    }

    internal static string TailsWithMidMaybe(string midMaybeJson) =>
        """{"owner":"Alice::1220","midMaybe":""" + midMaybeJson + "}";

    private static string ThreeDeepWithDeepNote(string deepNoteJson) =>
        """{"owner":"Alice::1220","deepNote":""" + deepNoteJson + "}";

    private static IEnumerable<string> FileNamesOf(IEnumerable<ITheoryDataRow> rows) =>
        rows.Select(row => (string)row.GetData()[0]!);
}
