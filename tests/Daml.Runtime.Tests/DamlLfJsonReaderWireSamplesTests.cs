// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using Xunit;

namespace Daml.Runtime.Tests;

public partial class DamlLfJsonReaderWireSamplesTests
{
    private static readonly string[] FactConsumedWireSamples =
    [
        "acs_wildcard_verbose_true.json",
        "acs_wildcard_verbose_false.json",
        "probe_nested_optional_matrix.json",
    ];

    [Theory]
    [MemberData(nameof(SupportedWireSamplePayloads))]
    public void ReadRecord_should_decode_captured_payloads_inside_the_reader_type_mapping(
        string fileName, string payloadPath, string shapeName, (string Label, Type DamlType)[] expectedFields)
    {
        using var document = LoadWireSample(fileName);
        var payload = ResolvePayload(document.RootElement, payloadPath);

        var record = DeclaredShapes[shapeName](payload);

        record.RecordId.Should().BeNull();
        record.Fields.Select(field => (field.Label, DamlType: field.Value.GetType()))
            .Should().Equal(expectedFields);
    }

    [Fact]
    public void ReadVariant_should_decode_the_captured_exercise_result_as_its_variant_arm()
    {
        using var document = LoadWireSample("exercise_describe.json");
        var payload = ResolvePayload(
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
        using var document = LoadWireSample("exercise_ping.json");
        var payload = ResolvePayload(
            document.RootElement, "response/transaction/events/0/ExercisedEvent/exerciseResult");

        DamlLfJsonDecoders.ReadUnit(payload, DamlLfJsonDecodeContext.Root("Unit"))
            .Should().BeSameAs(DamlUnit.Instance);
    }

    [Fact]
    public void ReadRecord_should_decode_the_captured_tuple_key_as_a_stdlib_tuple_field()
    {
        using var document = LoadWireSample("create_keyed_contract_key.json");
        var capturedKey = ResolvePayload(
            document.RootElement, "response/transaction/events/0/CreatedEvent/contractKey");

        var record = DamlLfJsonReader.ReadRecord<TupleKeyHolder>(
            """{"key":""" + capturedKey.GetRawText() + "}");

        record.GetRequiredField("key").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("_1", new DamlParty(capturedKey.GetProperty("_1").GetString()!)),
            new DamlField("_2", new DamlText(capturedKey.GetProperty("_2").GetString()!)));
    }

    [Fact]
    public void WireSamplesCorpus_should_show_identical_contract_entries_for_verbose_true_and_false()
    {
        using var verboseTrue = LoadWireSample("acs_wildcard_verbose_true.json");
        using var verboseFalse = LoadWireSample("acs_wildcard_verbose_false.json");

        var trueEntries = verboseTrue.RootElement.GetProperty("response").EnumerateArray()
            .Select(entry => entry.GetProperty("contractEntry")).ToList();
        var falseEntries = verboseFalse.RootElement.GetProperty("response").EnumerateArray()
            .Select(entry => entry.GetProperty("contractEntry")).ToList();

        trueEntries.Should().HaveSameCount(falseEntries);
        foreach (var (verboseEntry, terseEntry) in trueEntries.Zip(falseEntries))
        {
            JsonElement.DeepEquals(verboseEntry, terseEntry).Should().BeTrue(
                "the corpus README records that the verbose flag leaves ACS payloads untouched");
        }
    }

    [Fact]
    public void WireSamplesCorpus_should_keep_the_nested_optional_probe_matrix_self_consistent()
    {
        using var document = LoadWireSample("probe_nested_optional_matrix.json");

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
        var consumed = FileNamesOf(SupportedWireSamplePayloads)
            .Concat(FactConsumedWireSamples)
            .Distinct();

        var captured = Directory.EnumerateFiles(CorpusDirectory, "*.json").Select(Path.GetFileName);

        captured.Should().BeEquivalentTo(
            consumed,
            "every capture under tests/wire-samples/data must be exercised by a reader test");
    }

    [Fact]
    public void ReadRecord_should_decode_the_captured_nested_optional_encodings_as_a_chain()
    {
        using var matrix = LoadWireSample("probe_nested_optional_matrix.json");
        var someNone = ResolvePayload(matrix.RootElement, "response/array_of_empty_array/sent");

        var record = DamlLfJsonReader.ReadRecord<NestedNoteHolder>(
            """{"nestedNote":""" + someNone.GetRawText() + "}");

        record.GetRequiredField("nestedNote")
            .Should().Be(DamlOptionalChain.Some(DamlOptionalChain.None));
    }

    [Fact]
    public void ReadRecord_should_distinguish_the_three_accepted_nested_optional_encodings()
    {
        DamlLfJsonReader.ReadRecord<NestedNoteHolder>("""{"nestedNote":[]}""")
            .GetRequiredField("nestedNote").Should().Be(DamlOptionalChain.None);
        DamlLfJsonReader.ReadRecord<NestedNoteHolder>("""{"nestedNote":[[]]}""")
            .GetRequiredField("nestedNote").Should().Be(DamlOptionalChain.Some(DamlOptionalChain.None));
        DamlLfJsonReader.ReadRecord<NestedNoteHolder>("""{"nestedNote":[["deep"]]}""")
            .GetRequiredField("nestedNote")
            .Should().Be(DamlOptionalChain.Some(DamlOptionalChain.Some(new DamlText("deep"))));
    }

    [Fact]
    public void ReadRecord_should_reject_every_nested_optional_encoding_the_participant_rejected()
    {
        var rejected = new[]
        {
            ("null", "Expected JSON Array at 'NestedNoteHolder.nestedNote' but found Null"),
            ("[null]", "Expected JSON Array at 'NestedNoteHolder.nestedNote[0]' but found Null"),
            ("""["deep"]""", "Expected JSON Array at 'NestedNoteHolder.nestedNote[0]' but found String"),
        };

        foreach (var (encoding, message) in rejected)
        {
            var act = () => DamlLfJsonReader.ReadRecord<NestedNoteHolder>(
                """{"nestedNote":""" + encoding + "}");

            act.Should().Throw<JsonException>($"the participant rejected {encoding} with HTTP 500")
                .WithMessage(message);
        }
    }

    [Fact]
    public void ReadRecord_should_reject_a_nested_optional_level_carrying_more_than_one_element()
    {
        var act = () => DamlLfJsonReader.ReadRecord<NestedNoteHolder>("""{"nestedNote":[[],[]]}""");

        act.Should().Throw<JsonException>().WithMessage(
            "A nested Daml Optional at 'NestedNoteHolder.nestedNote' encodes as an array of at most "
            + "one element but found 2");
    }

    [Fact]
    public void ReadRecord_should_name_the_chain_level_that_failed()
    {
        var outerLevel = () => DamlLfJsonReader.ReadRecord<DeepNoteHolder>("""{"deepNote":[null]}""");
        var innerLevel = () => DamlLfJsonReader.ReadRecord<DeepNoteHolder>("""{"deepNote":[[null]]}""");

        outerLevel.Should().Throw<JsonException>().WithMessage(
            "Expected JSON Array at 'DeepNoteHolder.deepNote[0]' but found Null");
        innerLevel.Should().Throw<JsonException>().WithMessage(
            "Expected JSON Array at 'DeepNoteHolder.deepNote[0][0]' but found Null");
    }

    [Fact]
    public void ReadRecord_should_name_the_chain_level_carrying_more_than_one_element()
    {
        var act = () => DamlLfJsonReader.ReadRecord<DeepNoteHolder>("""{"deepNote":[[[],[]]]}""");

        act.Should().Throw<JsonException>().WithMessage(
            "A nested Daml Optional at 'DeepNoteHolder.deepNote[0]' encodes as an array of at most "
            + "one element but found 2");
    }

    [Fact]
    public void ReadRecord_should_keep_a_flat_wrapper_slot_on_the_flat_encoding()
    {
        DamlLfJsonReader.ReadRecord<FlatNoteHolder>("""{"note":null}""")
            .GetRequiredField("note").Should().Be(DamlOptional.None);
        DamlLfJsonReader.ReadRecord<FlatNoteHolder>("""{"note":"present"}""")
            .GetRequiredField("note").Should().Be(DamlOptional.Some(new DamlText("present")));
    }

    [Fact]
    public void ReadRecord_should_read_a_wrapper_reached_through_a_generic_at_exactly_its_own_level()
    {
        var item = DamlLfJsonReader.ReadRecord<BoxedNoteHolder>("""{"boxed":{"item":"deep"}}""")
            .GetRequiredField("boxed").As<DamlRecord>()
            .GetRequiredField("item");

        item.Should().Be(
            DamlOptional.Some(new DamlText("deep")),
            "the notnull constraint the emitter puts on every generated type parameter keeps the "
            + "slot's read-state non-nullable, so only the wrapper the field actually carries is read");

        Optional<string>.FromValue(item, value => value.As<DamlText>().Value)
            .Should().Be(new Optional<string>.Some("deep"),
                "the generated FromRecord a caller hands this to has to be able to consume it");
    }

    [Fact]
    public void ReadRecord_should_read_every_level_of_a_chain_as_a_chain_level()
    {
        var innerAbsent = DamlLfJsonReader.ReadRecord<NestedNoteHolder>("""{"nestedNote":[[]]}""")
            .GetRequiredField("nestedNote").As<DamlOptionalChain>();

        innerAbsent.Value.Should().BeOfType<DamlOptionalChain>(
            "the interior level of a chain is CLR-identical to a flat wrapper and can only be "
            + "reached by recursion from the root; deciding it structurally reads it as flat");
    }

    private static IEnumerable<string> FileNamesOf(IEnumerable<ITheoryDataRow> rows) =>
        rows.Select(row => (string)row.GetData()[0]!);
}
