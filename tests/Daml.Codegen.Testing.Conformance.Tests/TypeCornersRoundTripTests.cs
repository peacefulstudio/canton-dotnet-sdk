// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class TypeCornersRoundTripTests
{
    private static TypeCorners Sample(
        Box<Optional<string>>? nestedNote = null,
        Optional<Optional<string>>? maybeMaybeNote = null) => new(
        Owner: new Party("alice"),
        BoxedText: new Box<string>("boxed"),
        BoxedProfile: new Box<Profile>(new Profile("ace", 7)),
        Slot: new Slot<long>.Filled(11),
        NestedNote: nestedNote,
        MaybeMaybeNote: maybeMaybeNote ?? new Optional<Optional<string>>.None(),
        Crate: new Crate<string>(new Optional<string>.Some("crated")),
        QuotaByParty: new Dictionary<Party, long>
        {
            [new Party("alice")] = 1,
            [new Party("bob")] = 2,
        },
        LabelByRank: new Dictionary<long, string> { [1] = "gold", [2] = "silver" },
        RankOrLabel: new Either<long, string>.Right("runner-up"),
        NoteOrRank: new Either<Optional<string>, long>.Left(new Optional<string>.Some("noted")),
        Pair: new Tuple2<string, long>("pair", 3),
        Triple: new Tuple3<string, long, bool>("triple", 4, true),
        Branch: new Branch("root", new List<Branch>
        {
            new Branch("left", new List<Branch>()),
            new Branch("right", new List<Branch> { new Branch("leaf", new List<Branch>()) }),
        }),
        Whole: 42m,
        Finest: 0.5m);

    private static DamlRecord SampleRecordWith(string label, DamlValue value)
    {
        var record = Sample().ToRecord();
        record.GetField(label).Should().NotBeNull();
        return record with
        {
            Fields = [.. record.Fields.Select(field => field.Label == label ? DamlField.Create(label, value) : field)],
        };
    }

    [Fact]
    public void TypeCornersRoundTrip_round_trips_every_corner_field()
    {
        var original = Sample(nestedNote: new Box<Optional<string>>(new Optional<string>.Some("inner")));

        var restored = TypeCorners.FromRecord(original.ToRecord());

        restored.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void TypeCornersRoundTrip_round_trips_the_parameterized_record_and_variant()
    {
        var restored = TypeCorners.FromRecord(Sample().ToRecord());

        restored.BoxedText.Item.Should().Be("boxed");
        restored.BoxedProfile.Item.Should().Be(new Profile("ace", 7));
        restored.Slot.Should().BeOfType<Slot<long>.Filled>().Subject.Value.Should().Be(11);
    }

    [Fact]
    public void ToRecord_wires_the_parameterized_variant_as_a_daml_variant()
    {
        var slot = Sample().ToRecord().GetRequiredField("slot").As<DamlVariant>();

        slot.Constructor.Should().Be("Filled");
        slot.Value.As<DamlInt64>().Value.Should().Be(11);
    }

    [Fact]
    public void TypeCornersRoundTrip_round_trips_the_vacant_constructor_of_the_parameterized_variant()
    {
        var original = Sample() with { Slot = new Slot<long>.Vacant() };

        var restored = TypeCorners.FromRecord(original.ToRecord());

        restored.Slot.Should().BeOfType<Slot<long>.Vacant>();
    }

    [Fact]
    public void ToRecord_serializes_non_text_keyed_maps_as_gen_maps()
    {
        var record = Sample().ToRecord();

        var quotaByParty = record.GetRequiredField("quotaByParty").As<DamlGenMap>();
        quotaByParty.Entries.Select(e => e.Key.As<DamlParty>().Value).Should().BeEquivalentTo("alice", "bob");

        var labelByRank = record.GetRequiredField("labelByRank").As<DamlGenMap>();
        labelByRank.Entries.Select(e => e.Key.As<DamlInt64>().Value).Should().BeEquivalentTo([1L, 2L]);
    }

    [Fact]
    public void TypeCornersRoundTrip_round_trips_an_optional_nested_inside_an_optional_through_the_wrapper_record()
    {
        var outerAndInnerPresent = TypeCorners.FromRecord(
            Sample(new Box<Optional<string>>(new Optional<string>.Some("inner"))).ToRecord());
        var outerPresentInnerAbsent = TypeCorners.FromRecord(
            Sample(new Box<Optional<string>>(new Optional<string>.None())).ToRecord());
        var outerAbsent = TypeCorners.FromRecord(Sample(nestedNote: null).ToRecord());

        outerAndInnerPresent.NestedNote!.Item.GetValueOrThrow().Should().Be("inner");
        outerPresentInnerAbsent.NestedNote!.Item.HasValue.Should().BeFalse();
        outerAbsent.NestedNote.Should().BeNull();
    }

    [Fact]
    public void ToRecord_distinguishes_an_absent_wrapper_from_a_wrapper_holding_none()
    {
        var outerAbsent = Sample(nestedNote: null)
            .ToRecord().GetRequiredField("nestedNote").As<DamlOptional>();
        var innerAbsent = Sample(new Box<Optional<string>>(new Optional<string>.None()))
            .ToRecord().GetRequiredField("nestedNote").As<DamlOptional>();

        outerAbsent.HasValue.Should().BeFalse();
        innerAbsent.HasValue.Should().BeTrue();
        innerAbsent.Value!.As<DamlRecord>().GetRequiredField("item").As<DamlOptional>()
            .HasValue.Should().BeFalse();
    }

    [Fact]
    public void ToRecord_keeps_the_wrapped_optional_on_the_flat_wire_encoding()
    {
        var innerPresent = Sample(new Box<Optional<string>>(new Optional<string>.Some("inner")))
            .ToRecord().GetRequiredField("nestedNote").As<DamlOptional>();

        innerPresent.Value!.As<DamlRecord>().GetRequiredField("item")
            .Should().BeOfType<DamlOptional>();
    }

    public sealed record NestedNoteBox(
        [property: DamlFieldAttribute("item")] Optional<string> Item) : IDamlRecord<NestedNoteBox>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("item", Item.ToValue(value => new DamlText(value))));

        public static NestedNoteBox FromRecord(DamlRecord record) =>
            new(Optional<string>.FromValue(record.GetRequiredField("item"), value => value.As<DamlText>().Value));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(
                DamlField.Create("item", DamlLfJsonDecoders.ReadOptional(
                    DamlLfJsonDecoders.OptionalField(json, "item"), context.Field("item"),
                    (itemJson, itemContext) => DamlLfJsonDecoders.ReadText(itemJson, itemContext))));
        }
    }

    [Fact]
    public void TypeCornersRoundTrip_decodes_the_wrapped_optional_field_from_its_flat_json_payload()
    {
        var carried = DamlLfJsonReader.ReadRecord<NestedNoteBox>("""{"item":"deep"}""");
        var absent = DamlLfJsonReader.ReadRecord<NestedNoteBox>("""{"item":null}""");

        var innerPresent = TypeCorners.FromRecord(SampleRecordWith("nestedNote", DamlOptional.Some(carried)));
        var innerAbsent = TypeCorners.FromRecord(SampleRecordWith("nestedNote", DamlOptional.Some(absent)));

        innerPresent.NestedNote!.Item.GetValueOrThrow().Should().Be("deep");
        innerAbsent.NestedNote!.Item.HasValue.Should().BeFalse();
    }

    [Fact]
    public void TypeCornersRoundTrip_rejects_the_array_form_for_the_wrapped_optional_field()
    {
        var readArrayForm = () => DamlLfJsonReader.ReadRecord<NestedNoteBox>("""{"item":["deep"]}""");

        readArrayForm.Should().Throw<JsonException>(
            "the Daml type under this wrapper is a flat Optional Text, so the array form belongs "
            + "to a nested chain and must not be accepted here");
    }

    [Fact]
    public void ToRecord_renders_the_wrapped_optional_as_flat_json_rather_than_the_array_form()
    {
        var innerPresent = DamlJsonSerializer.Serialize(
            Sample(new Box<Optional<string>>(new Optional<string>.Some("inner"))).ToRecord());
        var innerAbsent = DamlJsonSerializer.Serialize(
            Sample(new Box<Optional<string>>(new Optional<string>.None())).ToRecord());

        innerPresent.Should().Contain("""{"item":"inner"}""").And.NotContain("""["inner"]""");
        innerAbsent.Should().Contain("""{"item":null}""").And.NotContain("""{"item":[]}""");
    }

    [Fact]
    public void TypeCornersRoundTrip_keeps_the_three_inhabitants_of_a_nested_optional_distinct()
    {
        var outerAbsent = TypeCorners.FromRecord(
            Sample(maybeMaybeNote: new Optional<Optional<string>>.None()).ToRecord());
        var innerAbsent = TypeCorners.FromRecord(
            Sample(maybeMaybeNote: new Optional<Optional<string>>.Some(new Optional<string>.None())).ToRecord());
        var present = TypeCorners.FromRecord(
            Sample(maybeMaybeNote: new Optional<Optional<string>>.Some(new Optional<string>.Some("deep"))).ToRecord());

        outerAbsent.MaybeMaybeNote.HasValue.Should().BeFalse();
        innerAbsent.MaybeMaybeNote.GetValueOrThrow().HasValue.Should().BeFalse();
        present.MaybeMaybeNote.GetValueOrThrow().GetValueOrThrow().Should().Be("deep");
    }

    [Fact]
    public void ToRecord_encodes_a_nested_optional_on_the_array_form_the_participant_accepts()
    {
        DamlJsonSerializer.Serialize(
            Sample(maybeMaybeNote: new Optional<Optional<string>>.None())
                .ToRecord().GetRequiredField("maybeMaybeNote")).Should().Be("[]");
        DamlJsonSerializer.Serialize(
            Sample(maybeMaybeNote: new Optional<Optional<string>>.Some(new Optional<string>.None()))
                .ToRecord().GetRequiredField("maybeMaybeNote")).Should().Be("[[]]");
        DamlJsonSerializer.Serialize(
            Sample(maybeMaybeNote: new Optional<Optional<string>>.Some(new Optional<string>.Some("deep")))
                .ToRecord().GetRequiredField("maybeMaybeNote")).Should().Be("""[["deep"]]""");
    }

    [Fact]
    public void ToRecord_leaves_the_case_c_nested_note_on_the_flat_encoding()
    {
        DamlJsonSerializer.Serialize(Sample().ToRecord().GetRequiredField("nestedNote"))
            .Should().NotStartWith("[",
                "nestedNote is Optional of Box of Optional Text - the Box between the two Optionals makes "
                + "the inner one a flat optional, and the array form there would be a wire regression");
    }

    [Fact]
    public void TypeCornersRoundTrip_records_the_short_write_codegen_refuses_to_emit()
    {
        var substituted = new Crate<Optional<string>>(
            new Optional<Optional<string>>.Some(new Optional<string>.None()));

        var written = DamlJsonSerializer.Serialize(
            substituted.ToRecord(item => item.ToValue(text => new DamlText(text)))
                .GetRequiredField("item"));

        written.Should().Be("null",
            "Crate's field converter is emitted once from its declaration and writes the flat form, so "
            + "composing it with an Optional type argument lands one array level short of the chain the "
            + "participant accepts - which is why codegen refuses to emit that substitution and this "
            + "composition can only be reached by hand");

        var decodingTheAcceptedForm = () => Crate<Optional<string>>.FromRecord(
            DamlRecord.Create(DamlField.Create(
                "item", DamlOptionalChain.Some(DamlOptionalChain.None))),
            value => Optional<string>.FromValue(value, text => text.As<DamlText>().Value));

        decodingTheAcceptedForm.Should().Throw<InvalidCastException>(
                "the strict As of DamlOptional in Optional.FromValue makes the gap loud rather than silent")
            .WithMessage("*DamlOptionalChain*DamlOptional*");
    }

    [Fact]
    public void TypeCornersRoundTrip_round_trips_an_optional_over_a_type_variable_through_the_wrapper()
    {
        var carried = TypeCorners.FromRecord(Sample().ToRecord());
        var absent = TypeCorners.FromRecord(
            (Sample() with { Crate = new Crate<string>(new Optional<string>.None()) }).ToRecord());

        carried.Crate.Item.GetValueOrThrow().Should().Be("crated");
        absent.Crate.Item.HasValue.Should().BeFalse();
    }

    [Fact]
    public void ToRecord_keeps_an_optional_over_a_type_variable_on_the_flat_wire_encoding()
    {
        var crate = Sample().ToRecord().GetRequiredField("crate").As<DamlRecord>();

        crate.GetRequiredField("item").Should().BeOfType<DamlOptional>();
    }

    [Fact]
    public void TypeCornersRoundTrip_round_trips_either_in_both_branches()
    {
        var right = TypeCorners.FromRecord(Sample().ToRecord());
        var left = TypeCorners.FromRecord((Sample() with { RankOrLabel = new Either<long, string>.Left(9) }).ToRecord());

        right.RankOrLabel.Should().BeOfType<Either<long, string>.Right>().Subject.Value.Should().Be("runner-up");
        left.RankOrLabel.Should().BeOfType<Either<long, string>.Left>().Subject.Value.Should().Be(9);
    }

    [Fact]
    public void TypeCornersRoundTrip_round_trips_an_optional_carried_by_an_either_arm()
    {
        var carried = TypeCorners.FromRecord(Sample().ToRecord());
        var absent = TypeCorners.FromRecord((Sample() with
        {
            NoteOrRank = new Either<Optional<string>, long>.Left(new Optional<string>.None()),
        }).ToRecord());
        var otherArm = TypeCorners.FromRecord((Sample() with
        {
            NoteOrRank = new Either<Optional<string>, long>.Right(5),
        }).ToRecord());

        carried.NoteOrRank.Should().BeOfType<Either<Optional<string>, long>.Left>()
            .Subject.Value.GetValueOrThrow().Should().Be("noted");
        absent.NoteOrRank.Should().BeOfType<Either<Optional<string>, long>.Left>()
            .Subject.Value.HasValue.Should().BeFalse();
        otherArm.NoteOrRank.Should().BeOfType<Either<Optional<string>, long>.Right>()
            .Subject.Value.Should().Be(5);
    }

    [Fact]
    public void ToRecord_keeps_an_optional_carried_by_an_either_arm_on_the_flat_wire_encoding()
    {
        var noteOrRank = Sample().ToRecord().GetRequiredField("noteOrRank").As<DamlVariant>();

        noteOrRank.Constructor.Should().Be("Left");
        noteOrRank.Value.Should().BeOfType<DamlOptional>(
            "the Optional sits directly under an Either arm, so the array form of a nested chain "
            + "there would be a wire regression");
        noteOrRank.Value.As<DamlOptional>().Value!.As<DamlText>().Value.Should().Be("noted");
    }

    [Fact]
    public void ToRecord_wires_tuples_as_positional_records()
    {
        var record = Sample().ToRecord();

        var pair = record.GetRequiredField("pair").As<DamlRecord>();
        pair.GetRequiredField("_1").As<DamlText>().Value.Should().Be("pair");
        pair.GetRequiredField("_2").As<DamlInt64>().Value.Should().Be(3);

        var triple = record.GetRequiredField("triple").As<DamlRecord>();
        triple.GetRequiredField("_3").As<DamlBool>().Value.Should().BeTrue();
    }

    [Fact]
    public void TypeCornersRoundTrip_round_trips_a_recursive_record_to_its_full_depth()
    {
        var restored = TypeCorners.FromRecord(Sample().ToRecord());

        restored.Branch.Children.Should().HaveCount(2);
        restored.Branch.Children[1].Children.Should().ContainSingle()
            .Which.Label.Should().Be("leaf");
    }

    [Fact]
    public void TypeCornersRoundTrip_round_trips_the_scale_zero_corner_from_integral_canonical_text()
    {
        const string integralCanonicalText = "7";
        DamlNumeric.TryParseCanonical(integralCanonicalText, out var wire).Should().BeTrue();

        var restored = TypeCorners.FromRecord(SampleRecordWith("whole", wire));

        restored.Whole.Should().Be(7m);

        const string scaleZeroShapeLostBecauseDecimalCarriesNoScale = "7.0";
        restored.ToRecord().GetRequiredField("whole").As<DamlNumeric>().ToCanonicalString()
            .Should().Be(scaleZeroShapeLostBecauseDecimalCarriesNoScale);
    }

    [Fact]
    public void TypeCornersRoundTrip_round_trips_the_numeric_37_corner_up_to_decimal_precision()
    {
        const string twentyEightFractionalDigits = "0.1234567890123456789012345678";
        DamlNumeric.TryParseCanonical(twentyEightFractionalDigits, out var wire).Should().BeTrue();

        var restored = TypeCorners.FromRecord(SampleRecordWith("finest", wire));

        restored.Finest.Should().Be(0.1234567890123456789012345678m);
        restored.ToRecord().GetRequiredField("finest").As<DamlNumeric>().ToCanonicalString()
            .Should().Be(twentyEightFractionalDigits);
    }

    [Fact]
    public void TypeCornersRoundTrip_overflows_on_the_numeric_37_corner_beyond_decimal_precision()
    {
        var thirtySevenFractionalDigits = "0." + new string('0', 36) + "1";
        DamlNumeric.TryParseCanonical(thirtySevenFractionalDigits, out var wire).Should().BeTrue();
        wire.ToCanonicalString().Should().Be(thirtySevenFractionalDigits,
            "the wire type carries the full Numeric 37 corner losslessly, so any loss below is the generated model's");

        var restore = () => TypeCorners.FromRecord(SampleRecordWith("finest", wire));

        restore.Should().Throw<OverflowException>(
                "the emitter maps every Numeric scale to decimal, so the Numeric 37 corner is only "
                + "round-trippable up to decimal's 28 fractional digits")
            .WithMessage("*" + thirtySevenFractionalDigits + "*");
    }

    [Fact]
    public void Rebox_choice_argument_carries_the_parameterized_record()
    {
        var argument = new TypeCorners.Rebox(new Box<string>("replaced"));

        var restored = TypeCorners.Rebox.FromRecord(argument.ToRecord());

        restored.Replacement.Item.Should().Be("replaced");
        restored.Should().Be(argument);
    }

    [Fact]
    public void Rebox_ArgumentJsonDecoder_decodes_the_instantiated_generic_field_directly_from_the_generated_choice()
    {
        using var document = JsonDocument.Parse("""{"replacement":{"item":"new-value"}}""");

        var argument = TypeCorners.ChoiceRebox.ArgumentJsonDecoder(document.RootElement, DamlLfJsonDecodeContext.Root("Rebox"));

        argument.Should().Be(new TypeCorners.Rebox(new Box<string>("new-value")));
    }
}
