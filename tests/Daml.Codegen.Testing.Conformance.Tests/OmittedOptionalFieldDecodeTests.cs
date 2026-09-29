// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Nodes;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// Decodes the payloads the Ledger API returned for the richtypes and contractkeys corpus,
/// whose trailing None fields it leaves out. Over JSON it omits a record field whose value is a trailing flat-Optional None, keeps
/// a mid-record None as <c>null</c>, and always writes a nested Optional's None as <c>[]</c>. Over
/// gRPC it omits every trailing None field, nested Optionals included, and keeps a mid-record
/// None as an empty <c>optional</c>.
/// </summary>
public class OmittedOptionalFieldDecodeTests
{
    private const string Owner = "alice::1220";

    private static OptionalTails DecodeOptionalTails(string json) =>
        OptionalTails.FromRecord(DamlLfJsonReader.ReadRecord<OptionalTails>(json));

    private static NestedOptionalTails DecodeNestedOptionalTails(string json) =>
        NestedOptionalTails.FromRecord(DamlLfJsonReader.ReadRecord<NestedOptionalTails>(json));

    [Fact]
    public void OptionalTails_decodes_the_omitted_trailing_None_the_ledger_returns()
    {
        var decoded = DecodeOptionalTails(
            """{"owner":"alice::1220","midNote":"mid","inner":{"text":"inner","remark":"remark"}}""");

        decoded.Should().Be(new OptionalTails(new Party(Owner), "mid", new TrailingNote("inner", "remark"), null));
    }

    [Fact]
    public void OptionalTails_decodes_the_null_mid_record_None_the_ledger_returns()
    {
        var decoded = DecodeOptionalTails(
            """{"owner":"alice::1220","midNote":null,"inner":{"text":"inner","remark":"remark"},"tailNote":"tail"}""");

        decoded.Should().Be(new OptionalTails(new Party(Owner), null, new TrailingNote("inner", "remark"), "tail"));
    }

    [Fact]
    public void OptionalTails_decodes_the_omitted_trailing_None_inside_a_nested_record()
    {
        var decoded = DecodeOptionalTails(
            """{"owner":"alice::1220","midNote":"mid","inner":{"text":"inner"},"tailNote":"tail"}""");

        decoded.Should().Be(new OptionalTails(new Party(Owner), "mid", new TrailingNote("inner", null), "tail"));
    }

    [Fact]
    public void OptionalTails_decodes_the_all_None_payload_the_ledger_returns()
    {
        var decoded = DecodeOptionalTails("""{"owner":"alice::1220","midNote":null,"inner":{"text":"inner"}}""");

        decoded.Should().Be(new OptionalTails(new Party(Owner), null, new TrailingNote("inner", null), null));
    }

    [Fact]
    public void OptionalTails_keeps_a_non_Optional_field_required()
    {
        var act = () => DecodeOptionalTails("""{"owner":"alice::1220","midNote":null}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml field 'OptionalTails.inner' is missing from the JSON object");
    }

    [Fact]
    public void OptionalTails_keeps_a_non_Optional_field_of_a_nested_record_required()
    {
        var act = () => DecodeOptionalTails("""{"owner":"alice::1220","midNote":null,"inner":{"remark":"remark"}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml field 'OptionalTails.inner.text' is missing from the JSON object");
    }

    [Fact]
    public void NestedOptionalTails_decodes_the_array_forms_the_ledger_returns()
    {
        var decoded = DecodeNestedOptionalTails("""{"owner":"alice::1220","midMaybe":[["deep"]],"tailMaybe":[[]]}""");

        decoded.MidMaybe.GetValueOrThrow().GetValueOrThrow().Should().Be("deep");
        decoded.TailMaybe.GetValueOrThrow().HasValue.Should().BeFalse();
    }

    [Fact]
    public void NestedOptionalTails_decodes_an_omitted_trailing_nested_Optional_as_None()
    {
        var decoded = DecodeNestedOptionalTails("""{"owner":"alice::1220","midMaybe":[]}""");

        decoded.MidMaybe.HasValue.Should().BeFalse();
        decoded.TailMaybe.HasValue.Should().BeFalse();
    }

    [Fact]
    public void NestedOptionalTails_still_rejects_null_for_a_nested_Optional()
    {
        var act = () => DecodeNestedOptionalTails("""{"owner":"alice::1220","midMaybe":[],"tailMaybe":null}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Array at 'NestedOptionalTails.tailMaybe' but found Null");
    }

    [Fact]
    public void TypeCorners_decodes_the_omitted_None_item_of_an_instantiated_generic_record()
    {
        var payload = TypeCornersPayloadWith(new Box<Optional<string>>(new Optional<string>.Some("dropped")));
        payload["nestedNote"] = new JsonObject();
        payload["crate"] = new JsonObject();

        var decoded = TypeCorners.FromRecord(DamlLfJsonReader.ReadRecord<TypeCorners>(payload.ToJsonString()));

        decoded.NestedNote!.Item.HasValue.Should().BeFalse();
        decoded.Crate.Item.HasValue.Should().BeFalse();
    }

    [Fact]
    public void Enrollment_key_decodes_the_omitted_trailing_None_tuple_component_the_ledger_returns()
    {
        using var document = JsonDocument.Parse("""{"_1":"alice::1220"}""");

        var key = Enrollment.Key.KeyJsonDecoder(document.RootElement, DamlLfJsonDecodeContext.Root("key"));

        key.Should().Be(new Tuple2<Party, Optional<string>>(new Party(Owner), new Optional<string>.None()));
    }

    [Fact]
    public void Enrollment_key_keeps_the_non_Optional_tuple_component_required()
    {
        using var document = JsonDocument.Parse("""{"_2":"note"}""");

        var act = () => Enrollment.Key.KeyJsonDecoder(document.RootElement, DamlLfJsonDecodeContext.Root("key"));

        act.Should().Throw<JsonException>().WithMessage("Required Daml field 'key._1' is missing from the JSON object");
    }

    [Fact]
    public void OptionalTails_FromRecord_reads_the_trailing_None_fields_the_gRPC_ledger_omits()
    {
        var record = DamlRecord.Create(
            DamlField.Create("owner", new DamlParty(Owner)),
            DamlField.Create("midNote", DamlOptional.None),
            DamlField.Create("inner", DamlRecord.Create(DamlField.Create("text", new DamlText("inner")))));

        var decoded = OptionalTails.FromRecord(record);

        decoded.Should().Be(new OptionalTails(new Party(Owner), null, new TrailingNote("inner", null), null));
    }

    [Fact]
    public void NestedOptionalTails_FromRecord_reads_the_trailing_nested_None_the_gRPC_ledger_omits()
    {
        var record = DamlRecord.Create(
            DamlField.Create("owner", new DamlParty(Owner)),
            DamlField.Create("midMaybe", DamlOptionalChain.Some(DamlOptionalChain.Some(new DamlText("deep")))));

        var decoded = NestedOptionalTails.FromRecord(record);

        decoded.MidMaybe.GetValueOrThrow().GetValueOrThrow().Should().Be("deep");
        decoded.TailMaybe.HasValue.Should().BeFalse();
    }

    [Fact]
    public void TypeCorners_FromRecord_reads_the_None_item_the_gRPC_ledger_omits_from_an_instantiated_generic_record()
    {
        var sample = TypeCornersRecord();
        var record = sample with
        {
            Fields =
            [
                .. sample.Fields.Select(field => field.Label switch
                {
                    "nestedNote" => DamlField.Create("nestedNote", DamlOptional.Some(DamlRecord.Create())),
                    "crate" => DamlField.Create("crate", DamlRecord.Create()),
                    _ => field,
                }),
            ],
        };

        var decoded = TypeCorners.FromRecord(record);

        decoded.NestedNote!.Item.HasValue.Should().BeFalse();
        decoded.Crate.Item.HasValue.Should().BeFalse();
    }

    [Fact]
    public void Enrollment_key_FromRecord_reads_the_trailing_None_tuple_component_the_gRPC_ledger_omits()
    {
        var key = Enrollment.Key.KeyDecoder(DamlRecord.Create(DamlField.Create("_1", new DamlParty(Owner))));

        key.Should().Be(new Tuple2<Party, Optional<string>>(new Party(Owner), new Optional<string>.None()));
    }

    [Fact]
    public void OptionalTails_FromRecord_keeps_a_non_Optional_field_required()
    {
        var act = () => OptionalTails.FromRecord(DamlRecord.Create(DamlField.Create("owner", new DamlParty(Owner))));

        act.Should().Throw<InvalidOperationException>().WithMessage("Required field 'inner' not found in record.");
    }

    private static DamlRecord TypeCornersRecord() =>
        DamlLfJsonReader.ReadRecord<TypeCorners>(
            TypeCornersPayloadWith(new Box<Optional<string>>(new Optional<string>.Some("dropped"))).ToJsonString());

    private static JsonObject TypeCornersPayloadWith(Box<Optional<string>> nestedNote)
    {
        var sample = new TypeCorners(
            Owner: new Party(Owner),
            BoxedText: new Box<string>("boxed"),
            BoxedProfile: new Box<Profile>(new Profile("ace", 7)),
            Slot: new Slot<long>.Filled(11),
            NestedNote: nestedNote,
            MaybeMaybeNote: new Optional<Optional<string>>.None(),
            Crate: new Crate<string>(new Optional<string>.Some("crated")),
            QuotaByParty: new Dictionary<Party, long> { [new Party(Owner)] = 1 },
            LabelByRank: new Dictionary<long, string> { [1] = "gold" },
            RankOrLabel: new Either<long, string>.Right("runner-up"),
            NoteOrRank: new Either<Optional<string>, long>.Left(new Optional<string>.Some("noted")),
            Pair: new Tuple2<string, long>("pair", 3),
            Triple: new Tuple3<string, long, bool>("triple", 4, true),
            Branch: new Branch("root", new List<Branch>()),
            Whole: 42m,
            Finest: 0.5m);
        return JsonNode.Parse(DamlJsonSerializer.Serialize(sample.ToRecord()))!.AsObject();
    }
}
