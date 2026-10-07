// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Stdlib;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed record PqsFilterSeed(
    Party Owner,
    string RunId,
    ContractId<Marker> MarkerA,
    ContractId<Marker> MarkerB,
    ContractId<IHolding> Holding,
    ContractId<IHolding> OtherHolding)
{
    public string Key(string name) => $"{RunId}:{name}";

    public string NameOf(string key) => key[(RunId.Length + 1)..];
}

internal static class PqsFilterParitySeed
{
    public static readonly string[] RichRecordNames = ["win", "pending", "mid"];
    public static readonly string[] OptionalCountsNames = ["none", "zero", "seven", "neg"];
    public static readonly string[] TypeCornersNames = ["a", "b", "c"];

    public static IReadOnlyList<RichRecord> RichRecords(PqsFilterSeed seed) =>
    [
        new RichRecord(
            Owner: seed.Owner,
            Count: 42L,
            Amount: 12.34m,
            Label: seed.Key("win"),
            Active: true,
            AsOf: new DateOnly(2026, 5, 29),
            ObservedAt: new DateTimeOffset(2026, 5, 29, 13, 30, 0, TimeSpan.Zero),
            Note: "hello",
            Tags: ["urgent", "blue"],
            Attributes: new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" },
            Marker: seed.MarkerA,
            HoldingCid: seed.Holding,
            HoldingCids: [seed.Holding],
            Profile: new Profile(Nickname: "cdg", Level: 7L),
            Outcome: new Outcome.Win(new Outcome_Win(Prize: 250.50m, Tier: "gold")),
            Suit: Suit.Hearts,
            Fee: 0.05m),
        new RichRecord(
            Owner: seed.Owner,
            Count: 1L,
            Amount: 1m,
            Label: seed.Key("pending"),
            Active: false,
            AsOf: new DateOnly(2026, 6, 15),
            ObservedAt: new DateTimeOffset(2026, 6, 15, 9, 15, 30, TimeSpan.Zero).AddTicks(1_234_560),
            Note: null,
            Tags: [],
            Attributes: new Dictionary<string, string>(),
            Marker: seed.MarkerB,
            HoldingCid: seed.Holding,
            HoldingCids: [],
            Profile: new Profile(Nickname: "pending", Level: 1L),
            Outcome: new Outcome.Pending(),
            Suit: Suit.Clubs,
            Fee: 0.01m),
        new RichRecord(
            Owner: seed.Owner,
            Count: -5L,
            Amount: -0.5m,
            Label: seed.Key("mid"),
            Active: true,
            AsOf: new DateOnly(2026, 1, 1),
            ObservedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Note: "",
            Tags: ["blocked"],
            Attributes: new Dictionary<string, string> { ["k1"] = "other" },
            Marker: seed.MarkerB,
            HoldingCid: seed.Holding,
            HoldingCids: [seed.Holding, seed.OtherHolding],
            Profile: new Profile(Nickname: "x", Level: 0L),
            Outcome: new Outcome.Win(new Outcome_Win(Prize: 1.00m, Tier: "bronze")),
            Suit: Suit.Spades,
            Fee: 99.99m),
    ];

    public static IReadOnlyList<OptionalCounts> OptionalCountsRows(PqsFilterSeed seed) =>
    [
        new OptionalCounts(seed.Owner, seed.Key("none"), null),
        new OptionalCounts(seed.Owner, seed.Key("zero"), 0L),
        new OptionalCounts(seed.Owner, seed.Key("seven"), 7L),
        new OptionalCounts(seed.Owner, seed.Key("neg"), -3L),
    ];

    public static IReadOnlyList<TypeCorners> TypeCornersRows(PqsFilterSeed seed) =>
    [
        new TypeCorners(
            Owner: seed.Owner,
            BoxedText: new Box<string>("boxed"),
            BoxedProfile: new Box<Profile>(new Profile("ace", 7)),
            Slot: new Slot<long>.Filled(11),
            NestedNote: new Box<Optional<string>>(new Optional<string>.None()),
            MaybeMaybeNote: new Optional<Optional<string>>.Some(new Optional<string>.Some("deep")),
            Crate: new Crate<string>(new Optional<string>.Some("crated")),
            QuotaByParty: new Dictionary<Party, long> { [seed.Owner] = 5 },
            LabelByRank: new Dictionary<long, string> { [1] = "gold" },
            RankOrLabel: new Either<long, string>.Right("runner-up"),
            NoteOrRank: new Either<Optional<string>, long>.Left(new Optional<string>.Some("noted")),
            Pair: new Tuple2<string, long>(seed.Key("a"), 3),
            Triple: new Tuple3<string, long, bool>("triple", 4, true),
            Branch: new Branch("root", [new Branch("leaf", [])]),
            Whole: 42m,
            Finest: 0.5m),
        new TypeCorners(
            Owner: seed.Owner,
            BoxedText: new Box<string>("boxed"),
            BoxedProfile: new Box<Profile>(new Profile("ace", 7)),
            Slot: new Slot<long>.Vacant(),
            NestedNote: null,
            MaybeMaybeNote: new Optional<Optional<string>>.Some(new Optional<string>.None()),
            Crate: new Crate<string>(new Optional<string>.None()),
            QuotaByParty: new Dictionary<Party, long>(),
            LabelByRank: new Dictionary<long, string> { [1] = "gold", [2] = "silver" },
            RankOrLabel: new Either<long, string>.Left(5),
            NoteOrRank: new Either<Optional<string>, long>.Right(9),
            Pair: new Tuple2<string, long>(seed.Key("b"), 3),
            Triple: new Tuple3<string, long, bool>("t2", -1, false),
            Branch: new Branch("root", []),
            Whole: 7m,
            Finest: 0.1234567890123456789012345678m),
        new TypeCorners(
            Owner: seed.Owner,
            BoxedText: new Box<string>("boxed"),
            BoxedProfile: new Box<Profile>(new Profile("ace", 7)),
            Slot: new Slot<long>.Filled(3),
            NestedNote: new Box<Optional<string>>(new Optional<string>.Some("inner")),
            MaybeMaybeNote: new Optional<Optional<string>>.None(),
            Crate: new Crate<string>(new Optional<string>.Some("x")),
            QuotaByParty: new Dictionary<Party, long> { [seed.Owner] = 0 },
            LabelByRank: new Dictionary<long, string>(),
            RankOrLabel: new Either<long, string>.Left(6),
            NoteOrRank: new Either<Optional<string>, long>.Left(new Optional<string>.None()),
            Pair: new Tuple2<string, long>(seed.Key("c"), 4),
            Triple: new Tuple3<string, long, bool>("t3", 2, true),
            Branch: new Branch("root", [new Branch("leafy", []), new Branch("mid", [new Branch("leaf", [])])]),
            Whole: 42m,
            Finest: 0.1m),
    ];
}
