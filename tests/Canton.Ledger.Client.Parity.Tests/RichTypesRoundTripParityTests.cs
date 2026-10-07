// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Stdlib;
using Daml.Runtime.Streams;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Codegen.Testing.Conformance.SubmitShapes;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;

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
    /// Opens a lane over this provider's <see cref="ICantonLedgerClient"/> for one test, with a
    /// party the client may act as and <c>richtypes.dar</c> uploaded.
    /// </summary>
    protected abstract Task<CapabilityLane<RichTypesSession>> OpenClientAsync(
        CancellationToken cancellationToken);

    public static TheoryData<string> MaybeMaybeNoteStates => ["None", "Some None", "Some (Some deep)"];

    public static TheoryData<string?, string?, string?> OptionalTailsStates => new()
    {
        { "mid", "remark", null },
        { null, "remark", "tail" },
        { "mid", null, "tail" },
        { null, null, null },
    };

    public static TheoryData<string, string> NestedOptionalTailsStates => new()
    {
        { "Some (Some deep)", "None" },
        { "Some (Some deep)", "Some None" },
        { "None", "Some (Some deep)" },
        { "Some None", "Some (Some deep)" },
        { "None", "None" },
    };

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
        var readBack = await ReadBackAsync(lane, createdCid);

        readBack.Should().Be(submitted);
        readBack.Tags.Should().Equal("alpha", "beta");
        readBack.Attributes.Should().BeEquivalentTo(new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" });
        readBack.Outcome.Should().Be(new Outcome.Win(new Outcome_Win(Prize: 250.50m, Tier: "gold")));
    }

    [Fact]
    public async Task Churn_returns_its_Decimal_result_through_the_generated_TryChurnAsync()
    {
        await using var lane = await OpenClientAsync(TestContext.Current.CancellationToken);
        var (client, owner) = lane.Capability;
        var factory = await CreateAsync(client, owner, new EphemeralFactory(owner));
        var retiring = await CreateAsync(client, owner, new Ephemeral(owner, "retiring"));

        var outcome = await factory.TryChurnAsync(
            client, new EphemeralFactory.Churn(retiring), owner,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<decimal>.One>(
            "the choice committed, so the wrapper has to return its result rather than throw; got {0}", outcome)
            .Which.Result.Should().Be(42.5m);
    }

    [Fact]
    public async Task Churn_flat_submit_reports_only_the_surviving_create_and_the_pre_existing_archive()
    {
        await using var lane = await OpenClientAsync(TestContext.Current.CancellationToken);
        var (client, owner) = lane.Capability;
        var factory = await CreateAsync(client, owner, new EphemeralFactory(owner));
        var retiring = await CreateAsync(client, owner, new Ephemeral(owner, "retiring"));

        var outcome = await client.TrySubmitSingleAsync(
            factory.ChurnCommand(new EphemeralFactory.Churn(retiring)), owner,
            cancellationToken: TestContext.Current.CancellationToken);

        var result = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>(
            "the choice committed; got {0}", outcome).Subject.Result;
        result.CreatedContracts.Select(created => Ephemeral.FromRecord(created.Payload).Tag)
            .Should().Equal("kept");
        result.ArchivedContractIds.Should().Equal(retiring.Value);
        result.ExercisedEvents.Select(exercised => exercised.ChoiceName.Value)
            .Should().Contain("Churn");
    }

    [Fact]
    public async Task Issue_flat_submit_reports_only_the_contract_its_submitter_is_a_stakeholder_of()
    {
        await using var lane = await OpenClientAsync(TestContext.Current.CancellationToken);
        var (client, patron) = lane.Capability;
        var issuer = lane.Capability.Counterparty;
        var desk = await CreateAsync(client, issuer, new TicketDesk(issuer, patron));

        var outcome = await client.TrySubmitSingleAsync(
            desk.IssueCommand(new TicketDesk.Issue()), patron,
            cancellationToken: TestContext.Current.CancellationToken);

        var result = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>(
            "the choice committed; got {0}", outcome).Subject.Result;
        result.CreatedContracts.Select(created => Ticket.FromRecord(created.Payload).Holder)
            .Should().Equal(patron);
    }

    [Fact]
    public async Task Issue_returns_the_ticket_contract_id_through_the_generated_TryIssueAsync()
    {
        await using var lane = await OpenClientAsync(TestContext.Current.CancellationToken);
        var (client, patron) = lane.Capability;
        var issuer = lane.Capability.Counterparty;
        var desk = await CreateAsync(client, issuer, new TicketDesk(issuer, patron));

        var outcome = await desk.TryIssueAsync(
            client, new TicketDesk.Issue(), patron,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<Ticket>>.One>(
            "the choice committed and returned the ticket's contract id, which the patron sees on its own exercise; got {0}", outcome);
    }

    [Fact]
    public async Task Reserve_returns_the_ticket_its_submitter_is_no_stakeholder_of_through_the_bare_Party_TryReserveAsync()
    {
        await using var lane = await OpenClientAsync(TestContext.Current.CancellationToken);
        var (client, patron) = lane.Capability;
        var issuer = lane.Capability.Counterparty;
        var desk = await CreateAsync(client, issuer, new TicketDesk(issuer, patron));

        var outcome = await desk.TryReserveAsync(
            client, new TicketDesk.Reserve(), patron,
            cancellationToken: TestContext.Current.CancellationToken);

        var reserved = outcome.Should().BeOfType<ExerciseOutcome<ContractId<Ticket>>.One>(
            "the choice committed and returned the ticket's contract id, which the patron sees on its own exercise "
            + "although it is no stakeholder of the ticket; got {0}", outcome).Subject.Result;
        (await ReadBackAsync(lane, reserved, issuer)).Should().Be(new Ticket(issuer, issuer));
    }

    [Fact]
    public async Task Reserve_returns_the_ticket_its_submitter_is_no_stakeholder_of_through_the_SubmitterInfo_TryReserveAsync()
    {
        await using var lane = await OpenClientAsync(TestContext.Current.CancellationToken);
        var (client, patron) = lane.Capability;
        var issuer = lane.Capability.Counterparty;
        var desk = await CreateAsync(client, issuer, new TicketDesk(issuer, patron));

        var outcome = await desk.TryReserveAsync(
            client, new TicketDesk.Reserve(), new RuntimeCommands.SubmitterInfo(patron),
            cancellationToken: TestContext.Current.CancellationToken);

        var reserved = outcome.Should().BeOfType<ExerciseOutcome<ContractId<Ticket>>.One>(
            "the choice committed and returned the ticket's contract id, which the patron sees on its own exercise "
            + "although it is no stakeholder of the ticket; got {0}", outcome).Subject.Result;
        (await ReadBackAsync(lane, reserved, issuer)).Should().Be(new Ticket(issuer, issuer));
    }

    [Fact]
    public async Task Pair_projects_the_ticket_and_no_ephemeral_when_the_ledger_omits_the_trailing_None()
    {
        await using var lane = await OpenClientAsync(TestContext.Current.CancellationToken);
        var (client, patron) = lane.Capability;
        var issuer = lane.Capability.Counterparty;
        var desk = await CreateAsync(client, issuer, new TicketDesk(issuer, patron));

        var outcome = await desk.TryPairAsync(
            client, new TicketDesk.Pair(false), patron,
            cancellationToken: TestContext.Current.CancellationToken);

        var pair = outcome.Should().BeOfType<ExerciseOutcome<Tuple2<ContractId<Ticket>, Optional<ContractId<Ephemeral>>>>.One>(
            "the choice committed and returned (ticket, None), whose trailing None the ledger may leave out; got {0}", outcome)
            .Subject.Result;
        (await ReadBackAsync(lane, pair._1, issuer)).Should().Be(new Ticket(issuer, issuer));
        pair._2.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task Pair_projects_the_ticket_and_the_ephemeral_when_the_trailing_Optional_is_present()
    {
        await using var lane = await OpenClientAsync(TestContext.Current.CancellationToken);
        var (client, patron) = lane.Capability;
        var issuer = lane.Capability.Counterparty;
        var desk = await CreateAsync(client, issuer, new TicketDesk(issuer, patron));

        var outcome = await desk.TryPairAsync(
            client, new TicketDesk.Pair(true), patron,
            cancellationToken: TestContext.Current.CancellationToken);

        var pair = outcome.Should().BeOfType<ExerciseOutcome<Tuple2<ContractId<Ticket>, Optional<ContractId<Ephemeral>>>>.One>(
            "the choice committed and returned (ticket, Some ephemeral); got {0}", outcome).Subject.Result;
        (await ReadBackAsync(lane, pair._1, issuer)).Should().Be(new Ticket(issuer, issuer));
        pair._2.HasValue.Should().BeTrue();
    }

    [Fact]
    public async Task Retire_flat_submit_reports_no_archive_of_a_contract_its_submitter_only_witnesses()
    {
        await using var lane = await OpenClientAsync(TestContext.Current.CancellationToken);
        var (client, patron) = lane.Capability;
        var issuer = lane.Capability.Counterparty;
        var desk = await CreateAsync(client, issuer, new TicketDesk(issuer, patron));
        var issuerOnlyTicket = await CreateAsync(client, issuer, new Ticket(issuer, issuer));
        var disclosedTicket = await lane.Capability.DiscloseAsync(
            issuerOnlyTicket.Value, issuer, TestContext.Current.CancellationToken);
        var submission = RuntimeCommands.CommandsSubmission
            .Single(desk.RetireCommand(new TicketDesk.Retire(issuerOnlyTicket)))
            .WithCommandId(new RuntimeCommands.CommandId(Guid.NewGuid().ToString()))
            .WithDisclosedContracts(disclosedTicket);

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            submission, patron, cancellationToken: TestContext.Current.CancellationToken);

        var result = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>(
            "the choice committed; got {0}", outcome).Subject.Result;
        result.ArchivedContractIds.Should().BeEmpty();
        result.ExercisedEvents.Select(exercised => exercised.ChoiceName.Value)
            .Should().Contain("Retire");
    }

    [Theory]
    [MemberData(nameof(MaybeMaybeNoteStates))]
    public async Task TypeCorners_round_trips_both_Maps_and_the_nested_Optional_through_the_ledger(string maybeMaybeNoteState)
    {
        await using var lane = await OpenClientAsync(TestContext.Current.CancellationToken);
        var (client, owner) = lane.Capability;
        var submitted = TypeCornersOwnedBy(owner, MaybeMaybeNote(maybeMaybeNoteState));

        var createdCid = await CreateAsync(client, owner, submitted);
        var readBack = await ReadBackAsync(lane, createdCid);

        readBack.Should().Be(submitted);
        readBack.MaybeMaybeNote.Should().Be(MaybeMaybeNote(maybeMaybeNoteState));
        readBack.QuotaByParty.Should().BeEquivalentTo(new Dictionary<Party, long> { [owner] = 7L });
        readBack.LabelByRank.Should().BeEquivalentTo(new Dictionary<long, string> { [1L] = "gold", [2L] = "silver" });
    }

    [Theory]
    [MemberData(nameof(OptionalTailsStates))]
    public async Task OptionalTails_round_trips_every_None_position_through_the_ledger(
        string? midNote, string? remark, string? tailNote)
    {
        await using var lane = await OpenClientAsync(TestContext.Current.CancellationToken);
        var (client, owner) = lane.Capability;
        var submitted = new OptionalTails(owner, midNote, new TrailingNote("inner", remark), tailNote);

        var createdCid = await CreateAsync(client, owner, submitted);
        var readBack = await ReadBackAsync(lane, createdCid);

        readBack.Should().Be(new OptionalTails(owner, midNote, new TrailingNote("inner", remark), tailNote));
    }

    [Theory]
    [MemberData(nameof(OptionalTailsStates))]
    public async Task EchoOptionalTails_result_decodes_every_None_position(
        string? midNote, string? remark, string? tailNote)
    {
        await using var lane = await OpenClientAsync(TestContext.Current.CancellationToken);
        var (client, owner) = lane.Capability;
        var createdCid = await CreateAsync(
            client, owner, new OptionalTails(owner, midNote, new TrailingNote("inner", remark), tailNote));

        var outcome = await createdCid.TryEchoOptionalTailsAsync(
            client, new OptionalTails.EchoOptionalTails(), owner,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<OptionalTails>.One>(
                "the exercise has to commit and decode, and the transport reported {0}", outcome)
            .Subject.Result.Should().Be(new OptionalTails(owner, midNote, new TrailingNote("inner", remark), tailNote));
    }

    [Theory]
    [MemberData(nameof(NestedOptionalTailsStates))]
    public async Task NestedOptionalTails_round_trips_every_nested_None_position_through_the_ledger(
        string midState, string tailState)
    {
        await using var lane = await OpenClientAsync(TestContext.Current.CancellationToken);
        var (client, owner) = lane.Capability;
        var submitted = new NestedOptionalTails(owner, MaybeMaybeNote(midState), MaybeMaybeNote(tailState));

        var createdCid = await CreateAsync(client, owner, submitted);
        var readBack = await ReadBackAsync(lane, createdCid);

        readBack.Should().Be(new NestedOptionalTails(owner, MaybeMaybeNote(midState), MaybeMaybeNote(tailState)));
    }

    [Theory]
    [MemberData(nameof(NestedOptionalTailsStates))]
    public async Task EchoNestedOptionalTails_result_decodes_every_nested_None_position(
        string midState, string tailState)
    {
        await using var lane = await OpenClientAsync(TestContext.Current.CancellationToken);
        var (client, owner) = lane.Capability;
        var createdCid = await CreateAsync(
            client, owner, new NestedOptionalTails(owner, MaybeMaybeNote(midState), MaybeMaybeNote(tailState)));

        var outcome = await createdCid.TryEchoNestedOptionalTailsAsync(
            client, new NestedOptionalTails.EchoNestedOptionalTails(), owner,
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<NestedOptionalTails>.One>(
                "the exercise has to commit and decode, and the transport reported {0}", outcome)
            .Subject.Result.Should().Be(
                new NestedOptionalTails(owner, MaybeMaybeNote(midState), MaybeMaybeNote(tailState)));
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

    private static Task<T> ReadBackAsync<T>(
        CapabilityLane<RichTypesSession> lane, ContractId<T> createdCid)
        where T : ITemplate, IDamlRecord<T> =>
        ReadBackAsync(lane, createdCid, lane.Capability.Owner);

    private static async Task<T> ReadBackAsync<T>(
        CapabilityLane<RichTypesSession> lane, ContractId<T> createdCid, Party owner)
        where T : ITemplate, IDamlRecord<T>
    {
        var client = lane.Capability.Client;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        budget.CancelAfter(ReadBackBudget);
        using var streamHold = await lane.HoldStreamAsync(TestContext.Current.CancellationToken);
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

/// <summary>
/// A client with two parties it may act as: the <see cref="Owner"/> most tests use, and a
/// <see cref="Counterparty"/> for the shapes where a second party signs or only observes.
/// <see cref="DiscloseAsync"/> reads a contract's created-event blob as a party that sees it, so
/// another party can submit against the contract without being a stakeholder.
/// </summary>
public readonly record struct RichTypesSession(
    ICantonLedgerClient Client,
    Party Owner,
    Party Counterparty,
    Func<string, Party, CancellationToken, Task<RuntimeCommands.DisclosedContract>> DiscloseAsync)
{
    public void Deconstruct(out ICantonLedgerClient client, out Party owner)
    {
        client = Client;
        owner = Owner;
    }
}
