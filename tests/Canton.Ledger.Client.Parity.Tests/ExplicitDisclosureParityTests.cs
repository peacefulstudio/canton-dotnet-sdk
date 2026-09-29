// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.Disclosure;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Behavioral parity suite for explicit disclosure, run against every transport with a participant.
/// An issuer creates an <see cref="Offer"/> it is the only stakeholder of; a reader party that is no
/// stakeholder then exercises <c>Inspect</c> on it. The participant cannot find the contract for the
/// reader until the submission discloses it, and the disclosure is the one the issuer's typed
/// active-contract read handed back.
/// </summary>
public abstract class ExplicitDisclosureParityTests
{
    private const long OfferPrice = 4217L;

    /// <summary>Opens a lane with an issuer and a reader party the client may act as, for one test.</summary>
    protected abstract Task<CapabilityLane<(ICantonLedgerClient Client, Party Issuer, Party Reader)>>
        OpenDisclosureLaneAsync(CancellationToken cancellationToken);

    [Fact]
    public async Task A_non_stakeholder_exercising_Inspect_on_an_undisclosed_Offer_is_told_the_contract_was_not_found()
    {
        await using var lane = await OpenDisclosureLaneAsync(TestContext.Current.CancellationToken);
        var (client, issuer, reader) = lane.Capability;

        var offerCid = await CreateOfferAsync(client, issuer);

        var inspected = await client.TrySubmitAndWaitForTransactionAsync(
            InspectBy(reader, offerCid),
            reader,
            cancellationToken: TestContext.Current.CancellationToken);

        inspected.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>(
            "the reader is no stakeholder of the Offer, so without a disclosure the participant cannot see it")
            .Which.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
    }

    [Fact]
    public async Task A_non_stakeholder_exercises_Inspect_on_an_Offer_disclosed_from_the_issuers_typed_active_contract_read()
    {
        await using var lane = await OpenDisclosureLaneAsync(TestContext.Current.CancellationToken);
        var (client, issuer, reader) = lane.Capability;

        var offerCid = await CreateOfferAsync(client, issuer);
        var activeOffer = await ActiveOfferAsync(lane, client, issuer, offerCid);

        var disclosure = activeOffer.Disclosure.Should().NotBeNull().And.Subject.As<DisclosedContract>();
        disclosure.ContractId.Should().Be(offerCid.Value);
        disclosure.TemplateId.Should().Be(Offer.TemplateId);
        disclosure.SynchronizerId.Should().Be(activeOffer.SynchronizerId);

        var inspected = await client.TrySubmitAndWaitForTransactionTreeAsync(
            InspectBy(reader, offerCid).WithDisclosedContracts(disclosure),
            reader,
            cancellationToken: TestContext.Current.CancellationToken);

        inspected.Should().BeOfType<ExerciseOutcome<TransactionTree>.One>(
            "the disclosure lets a party outside the Offer's stakeholders use it")
            .Which.Result.RootEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TreeEvent.Exercised>()
            .Which.ExerciseResult.Should().Be(new DamlInt64(4217L));
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_commits_a_non_stakeholders_Inspect_on_an_Offer_disclosed_from_the_issuers_typed_active_contract_read()
    {
        await using var lane = await OpenDisclosureLaneAsync(TestContext.Current.CancellationToken);
        var (client, issuer, reader) = lane.Capability;

        var offerCid = await CreateOfferAsync(client, issuer);
        var disclosure = (await ActiveOfferAsync(lane, client, issuer, offerCid))
            .Disclosure.Should().NotBeNull().And.Subject.As<DisclosedContract>();

        var inspected = await client.TrySubmitAndWaitForTransactionAsync(
            InspectBy(reader, offerCid).WithDisclosedContracts(disclosure),
            reader,
            cancellationToken: TestContext.Current.CancellationToken);

        inspected.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>(
            "the disclosure lets a party outside the Offer's stakeholders use it");
    }

    private static async Task<ContractId<Offer>> CreateOfferAsync(ICantonLedgerClient client, Party issuer)
    {
        var created = await client.TryCreateAsync(
            new Offer(issuer, OfferPrice), cancellationToken: TestContext.Current.CancellationToken);
        return created.Should().BeOfType<ExerciseOutcome<ContractId<Offer>>.One>().Which.Result;
    }

    private static CommandsSubmission InspectBy(Party reader, ContractId<Offer> offerCid) =>
        CommandsSubmission.Single(offerCid.InspectCommand(new Offer.Inspect(reader)));

    private static async Task<AcsSnapshotEntry<Offer>.Created> ActiveOfferAsync(
        CapabilityLane<(ICantonLedgerClient Client, Party Issuer, Party Reader)> lane,
        ICantonLedgerClient client,
        Party issuer,
        ContractId<Offer> offerCid)
    {
        var ledgerEnd = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        var entries = new List<AcsSnapshotEntry<Offer>>();
        using var streamHold = await lane.HoldStreamAsync(TestContext.Current.CancellationToken);
        await foreach (var entry in client.SubscribeActiveAsync<Offer>(
            issuer, ledgerEnd, includeDisclosure: true, TestContext.Current.CancellationToken))
        {
            entries.Add(entry);
        }

        return entries.OfType<AcsSnapshotEntry<Offer>.Created>()
            .Should().ContainSingle(created => created.ContractId.Equals(offerCid)).Subject;
    }
}
