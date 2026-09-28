// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Stdlib;
using Daml.Runtime.Streams;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Live round-trip parity over the <c>richtypes.dar</c> corpus: a <see cref="RichRecord"/> and a
/// <see cref="TypeCorners"/> are created through each transport and read back from the active
/// contract set, and the payload read back has to equal the one submitted. TypeCorners carries
/// the shapes a transport is most likely to drop: two <c>DA.Map</c> fields and an
/// <c>Optional (Optional Text)</c> in each of its three states.
/// </summary>
public abstract class RichTypesRoundTripParityTests
{
    private static readonly TimeSpan ReadBackBudget = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Why this lane cannot decode a <see cref="TypeCorners"/> it reads back, or
    /// <see langword="null"/> when it can. Only the TypeCorners rows skip with this reason.
    /// </summary>
    protected virtual string? TypeCornersDecodeQuarantine => null;

    /// <summary>
    /// Opens a lane over this provider's <see cref="ICantonLedgerClient"/> for one test, with a
    /// party the client may act as and <c>richtypes.dar</c> uploaded.
    /// </summary>
    protected abstract Task<CapabilityLane<(ICantonLedgerClient Client, Party Owner)>> OpenClientAsync(
        CancellationToken cancellationToken);

    public static TheoryData<string> MaybeMaybeNoteStates => ["None", "Some None", "Some (Some deep)"];

    [Fact]
    public async Task RichRecord_round_trips_every_typed_field_through_the_ledger()
    {
        await using var lane = await OpenClientAsync(TestContext.Current.CancellationToken);
        var (client, owner) = lane.Capability;
        var markerCid = await CreateAsync(client, owner, new Marker(owner));
        var firstHolding = new ContractId<IHolding>((await CreateAsync(client, owner, new Asset(owner, 100m))).Value);
        var secondHolding = new ContractId<IHolding>((await CreateAsync(client, owner, new Asset(owner, 250m))).Value);
        var submitted = new RichRecord(
            Owner: owner,
            Count: 42L,
            Amount: 12.34m,
            Label: "initial",
            Active: true,
            AsOf: new DateOnly(2026, 5, 29),
            ObservedAt: new DateTimeOffset(2026, 5, 29, 13, 30, 0, TimeSpan.Zero),
            Note: "hello",
            Tags: ["alpha", "beta"],
            Attributes: new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" },
            Marker: markerCid,
            HoldingCid: firstHolding,
            HoldingCids: [firstHolding, secondHolding],
            Profile: new Profile(Nickname: "cdg", Level: 7L),
            Outcome: new Outcome.Win(new Outcome_Win(Prize: 250.50m, Tier: "gold")),
            Suit: Suit.Hearts,
            Fee: 0.05m);

        var createdCid = await CreateAsync(client, owner, submitted);
        var readBack = await ReadBackAsync(client, owner, createdCid);

        readBack.Should().Be(submitted);
        readBack.Tags.Should().Equal("alpha", "beta");
        readBack.Attributes.Should().BeEquivalentTo(new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" });
        readBack.Outcome.Should().Be(new Outcome.Win(new Outcome_Win(Prize: 250.50m, Tier: "gold")));
    }

    [Theory]
    [MemberData(nameof(MaybeMaybeNoteStates))]
    public async Task TypeCorners_round_trips_both_Maps_and_the_nested_Optional_through_the_ledger(string maybeMaybeNoteState)
    {
        if (TypeCornersDecodeQuarantine is { } quarantine)
        {
            Assert.Skip(quarantine);
        }

        await using var lane = await OpenClientAsync(TestContext.Current.CancellationToken);
        var (client, owner) = lane.Capability;
        var submitted = TypeCornersOwnedBy(owner, MaybeMaybeNote(maybeMaybeNoteState));

        var createdCid = await CreateAsync(client, owner, submitted);
        var readBack = await ReadBackAsync(client, owner, createdCid);

        readBack.Should().Be(submitted);
        readBack.MaybeMaybeNote.Should().Be(MaybeMaybeNote(maybeMaybeNoteState));
        readBack.QuotaByParty.Should().BeEquivalentTo(new Dictionary<Party, long> { [owner] = 7L });
        readBack.LabelByRank.Should().BeEquivalentTo(new Dictionary<long, string> { [1L] = "gold", [2L] = "silver" });
    }

    private static Optional<Optional<string>> MaybeMaybeNote(string state) => state switch
    {
        "None" => new Optional<Optional<string>>.None(),
        "Some None" => new Optional<Optional<string>>.Some(new Optional<string>.None()),
        "Some (Some deep)" => new Optional<Optional<string>>.Some(new Optional<string>.Some("deep")),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "not a MaybeMaybeNoteStates row"),
    };

    private static TypeCorners TypeCornersOwnedBy(Party owner, Optional<Optional<string>> maybeMaybeNote) => new(
        Owner: owner,
        BoxedText: new Box<string>("boxed"),
        BoxedProfile: new Box<Profile>(new Profile("ace", 7L)),
        Slot: new Slot<long>.Filled(11L),
        NestedNote: new Box<Optional<string>>(new Optional<string>.Some("inner")),
        MaybeMaybeNote: maybeMaybeNote,
        Crate: new Crate<string>(new Optional<string>.Some("crated")),
        QuotaByParty: new Dictionary<Party, long> { [owner] = 7L },
        LabelByRank: new Dictionary<long, string> { [1L] = "gold", [2L] = "silver" },
        RankOrLabel: new Either<long, string>.Right("runner-up"),
        NoteOrRank: new Either<Optional<string>, long>.Left(new Optional<string>.Some("noted")),
        Pair: new Tuple2<string, long>("pair", 3L),
        Triple: new Tuple3<string, long, bool>("triple", 4L, true),
        Branch: new Branch("root", [new Branch("leaf", [])]),
        Whole: 42m,
        Finest: 0.5m);

    private static async Task<ContractId<T>> CreateAsync<T>(ICantonLedgerClient client, Party owner, T template)
        where T : ITemplate, IDamlRecord<T>
    {
        var outcome = await client.TryCreateAsync(
            template, owner, cancellationToken: TestContext.Current.CancellationToken);
        return outcome.Should().BeOfType<ExerciseOutcome<ContractId<T>>.One>(
            "the create has to commit and decode, and the transport reported {0}", outcome).Subject.Result;
    }

    private static async Task<T> ReadBackAsync<T>(ICantonLedgerClient client, Party owner, ContractId<T> createdCid)
        where T : ITemplate, IDamlRecord<T>
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        budget.CancelAfter(ReadBackBudget);
        await foreach (var entry in client.SubscribeActiveAsync<T>(owner, cancellationToken: budget.Token))
        {
            if (entry is AcsSnapshotEntry<T>.Created created && created.ContractId.Value == createdCid.Value)
            {
                return created.Payload;
            }
        }
        throw new Xunit.Sdk.XunitException(
            $"The active contract set of {owner.Value} held no {typeof(T).Name} {createdCid.Value}.");
    }
}
