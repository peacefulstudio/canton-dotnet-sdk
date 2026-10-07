// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Stdlib;

namespace Daml.Codegen.Testing.Conformance.Tests;

internal static partial class GeneratedStjSampleTable
{
    internal static RichRecord RichRecordSample(Outcome outcome) => new(
        Owner: Alice,
        Count: 42,
        Amount: 19.95m,
        Label: "first",
        Active: true,
        AsOf: new DateOnly(2026, 6, 4),
        ObservedAt: new DateTimeOffset(2026, 6, 4, 12, 30, 0, TimeSpan.Zero),
        Note: "hello",
        Tags: ["a", "b"],
        Attributes: new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" },
        Marker: new ContractId<Marker>("marker-cid"),
        HoldingCid: new ContractId<IHolding>("00aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899"),
        HoldingCids:
        [
            new ContractId<IHolding>("0011112222333344445555666677778888999900001111222233334444555566aa"),
            new ContractId<IHolding>("00bbbb2222333344445555666677778888999900001111222233334444555566bb"),
        ],
        Profile: new Profile("ace", 7),
        Outcome: outcome,
        Suit: Suit.Hearts,
        Fee: 1.5m);

    internal static TypeCorners TypeCornersSample() => new(
        Owner: Alice,
        BoxedText: new Box<string>("boxed"),
        BoxedProfile: new Box<Profile>(new Profile("ace", 7)),
        Slot: new Slot<long>.Filled(11),
        NestedNote: new Box<Optional<string>>(new Optional<string>.Some("inner")),
        MaybeMaybeNote: new Optional<Optional<string>>.Some(new Optional<string>.Some("nested")),
        Crate: new Crate<string>(new Optional<string>.Some("crated")),
        QuotaByParty: new Dictionary<Party, long> { [Alice] = 1, [Bob] = 2 },
        LabelByRank: new Dictionary<long, string> { [1] = "gold", [2] = "silver" },
        RankOrLabel: new Either<long, string>.Right("runner-up"),
        NoteOrRank: new Either<Optional<string>, long>.Left(new Optional<string>.Some("noted")),
        Pair: new Tuple2<string, long>("pair", 3),
        Triple: new Tuple3<string, long, bool>("triple", 4, true),
        Branch: new Branch("root", [new Branch("left", []), new Branch("right", [new Branch("leaf", [])])]),
        Whole: 42m,
        Finest: 0.5m);

    private static Optional<Optional<string>> SomeSome(string value) =>
        new Optional<Optional<string>>.Some(new Optional<string>.Some(value));

    private static Optional<Optional<string>> SomeNone() =>
        new Optional<Optional<string>>.Some(new Optional<string>.None());

    private static Optional<Optional<string>> NoneOuter() => new Optional<Optional<string>>.None();

    private static void AddRichTypes(Dictionary<Type, object[]> samples)
    {
        samples[typeof(AnnotationView)] =
        [
            new AnnotationView("annotated", SomeSome("deep")),
            new AnnotationView("inner-absent", SomeNone()),
            new AnnotationView("outer-absent", NoneOuter()),
        ];
        samples[typeof(Asset)] = [new Asset(Alice, 19.95m)];
        samples[typeof(Box<string>)] = [new Box<string>("boxed")];
        samples[typeof(Box<Profile>)] = [new Box<Profile>(new Profile("ace", 7))];
        samples[typeof(Box<Optional<string>>)] =
        [
            new Box<Optional<string>>(new Optional<string>.Some("inner")),
            new Box<Optional<string>>(new Optional<string>.None()),
        ];
        samples[typeof(Branch)] =
        [
            new Branch("root", [new Branch("left", []), new Branch("right", [new Branch("leaf", [])])]),
        ];
        samples[typeof(Crate<string>)] =
        [
            new Crate<string>(new Optional<string>.Some("crated")),
            new Crate<string>(new Optional<string>.None()),
        ];
        samples[typeof(Describe)] = [new Describe("prefix")];
        samples[typeof(GenericResults)] = [new GenericResults(Alice)];
        samples[typeof(Grade)] = [new Grade()];
        samples[typeof(HoldingView)] = [new HoldingView(5.5m)];
        samples[typeof(Marker)] = [new Marker(Alice)];
        samples[typeof(NestedOptionalShapes)] =
        [
            new NestedOptionalShapes(
                Alice,
                new Box<Optional<Optional<string>>>(SomeSome("boxed")),
                new Tuple2<string, Optional<Optional<string>>>("trailing", SomeSome("tail"))),
            new NestedOptionalShapes(
                Bob,
                new Box<Optional<Optional<string>>>(NoneOuter()),
                new Tuple2<string, Optional<Optional<string>>>("trailing", SomeNone())),
        ];
        samples[typeof(NestedOptionalTails)] =
        [
            new NestedOptionalTails(Alice, SomeSome("mid"), SomeSome("tail")),
            new NestedOptionalTails(Bob, NoneOuter(), SomeNone()),
        ];
        samples[typeof(OptionalCounts)] =
        [
            new OptionalCounts(Alice, "counted", 7),
            new OptionalCounts(Bob, "uncounted", null),
        ];
        samples[typeof(OptionalTails)] =
        [
            new OptionalTails(Alice, "mid", new TrailingNote("inner", "remark"), "tail"),
            new OptionalTails(Bob, null, new TrailingNote("inner", null), null),
        ];
        samples[typeof(Outcome)] =
        [
            new Outcome.Win(new Outcome_Win(Prize: 12.34m, Tier: "gold")),
            new Outcome.Pending(),
        ];
        samples[typeof(Outcome_Win)] = [new Outcome_Win(Prize: 12.34m, Tier: "gold")];
        samples[typeof(Profile)] = [new Profile("ace", 7)];
        samples[typeof(Reissue)] = [new Reissue(3.25m)];
        samples[typeof(RichRecord)] =
        [
            RichRecordSample(new Outcome.Win(new Outcome_Win(Prize: 12.34m, Tier: "gold"))),
            RichRecordSample(new Outcome.Pending()) with { Note = null },
        ];
        samples[typeof(Slot<long>)] = [new Slot<long>.Filled(11), new Slot<long>.Vacant()];
        samples[typeof(Split)] = [new Split(4)];
        samples[typeof(Suit)] = [Suit.Spades];
        samples[typeof(TrailingNote)] =
        [
            new TrailingNote("text", "remark"),
            new TrailingNote("text", null),
        ];
        samples[typeof(TypeCorners)] =
        [
            TypeCornersSample(),
            TypeCornersSample() with { NestedNote = null, MaybeMaybeNote = NoneOuter() },
        ];
    }

    private static void AddRichTypeChoiceArguments(Dictionary<Type, object[]> samples)
    {
        samples[typeof(GenericResults.ReturnBox)] = [new GenericResults.ReturnBox()];
        samples[typeof(GenericResults.ReturnContractIds)] = [new GenericResults.ReturnContractIds()];
        samples[typeof(GenericResults.ReturnEither)] = [new GenericResults.ReturnEither(true)];
        samples[typeof(GenericResults.ReturnGenMap)] = [new GenericResults.ReturnGenMap()];
        samples[typeof(GenericResults.ReturnNestedOptional)] = [new GenericResults.ReturnNestedOptional(true, true)];
        samples[typeof(GenericResults.ReturnNonEmpty)] = [new GenericResults.ReturnNonEmpty()];
        samples[typeof(GenericResults.ReturnOptionalSuit)] = [new GenericResults.ReturnOptionalSuit()];
        samples[typeof(GenericResults.ReturnOptionalText)] = [new GenericResults.ReturnOptionalText(true)];
        samples[typeof(GenericResults.ReturnOutcome)] = [new GenericResults.ReturnOutcome(true)];
        samples[typeof(GenericResults.ReturnOutcomes)] = [new GenericResults.ReturnOutcomes()];
        samples[typeof(GenericResults.ReturnProfiles)] = [new GenericResults.ReturnProfiles()];
        samples[typeof(GenericResults.ReturnSet)] = [new GenericResults.ReturnSet()];
        samples[typeof(GenericResults.ReturnSlot)] = [new GenericResults.ReturnSlot()];
        samples[typeof(GenericResults.ReturnSuit)] = [new GenericResults.ReturnSuit()];
        samples[typeof(GenericResults.ReturnTextMap)] = [new GenericResults.ReturnTextMap()];
        samples[typeof(GenericResults.ReturnTuple)] = [new GenericResults.ReturnTuple()];
        samples[typeof(NestedOptionalTails.EchoNestedOptionalTails)] = [new NestedOptionalTails.EchoNestedOptionalTails()];
        samples[typeof(OptionalTails.EchoOptionalTails)] = [new OptionalTails.EchoOptionalTails()];
        samples[typeof(RichRecord.Relabel)] = [new RichRecord.Relabel("relabelled")];
        samples[typeof(TypeCorners.Rebox)] = [new TypeCorners.Rebox(new Box<string>("reboxed"))];
    }
}
