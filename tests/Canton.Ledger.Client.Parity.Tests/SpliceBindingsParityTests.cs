// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Splice.Api.Token.HoldingV1;
using Splice.ValidatorLicense;
using Splice.Wallet.Payment;
using Splice.Wallet.TransferOffer;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Parity suite over the Splice bindings this checkout's codegen generates, at build time, from the
/// <c>splice-wallet</c> DAR vendored in <c>Canton.Ledger.Client.Parity.SpliceBindings</c>,
/// run against every transport that reaches a live LocalNet participant.
/// Most lanes read the contracts Splice itself created when the validator onboarded, proving the
/// generated binding decodes real Splice payloads over that transport; the
/// <c>Splice.Wallet</c> <see cref="TransferOffer"/> lane also submits a create and a choice through
/// the generated binding, proving the write path over that transport.
/// </summary>
public abstract class SpliceBindingsParityTests
{
    private static readonly TimeSpan TapProjectionTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TapProjectionPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Opens a lane over this transport's <see cref="ICantonLedgerClient"/>, paired with the
    /// validator operator party that Splice onboarding issued the <see cref="ValidatorLicense"/> to,
    /// and with that operator's validator wallet to fund it through.
    /// </summary>
    private protected abstract Task<CapabilityLane<(
        ICantonLedgerClient Client,
        Party Operator,
        LocalnetValidatorWallet OperatorWallet)>>
        OpenOperatorLaneAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the operator's current Amulet holding contract IDs, so a caller can later tell which
    /// holdings a subsequent tap produced without relying on the tapped amount as identity: Splice's
    /// merge automation can combine a fresh tap with another holding before the next snapshot,
    /// leaving the merged holding's amount different from the amount that was tapped.
    /// </summary>
    private static async Task<HashSet<ContractId<IHolding>>> SnapshotAmuletHoldingContractIdsAsync(
        CapabilityLane<(ICantonLedgerClient Client, Party Operator, LocalnetValidatorWallet OperatorWallet)> lane,
        CancellationToken cancellationToken)
    {
        var (client, @operator, _) = lane.Capability;
        var contractIds = new HashSet<ContractId<IHolding>>();
        using var streamHold = await lane.HoldStreamAsync(cancellationToken);
        await foreach (var entry in client.SubscribeActiveAsync(
            IHolding.View, @operator, cancellationToken: cancellationToken))
        {
            if (entry is InterfaceAcsSnapshotEntry<IHolding, HoldingView>.Created created &&
                created.Payload.InstrumentId.Id == "Amulet")
            {
                contractIds.Add(created.ContractId);
            }
        }

        return contractIds;
    }

    /// <summary>
    /// Re-reads the operator's Amulet-holding ACS snapshot until it contains a holding the given
    /// pre-tap set does not, or <see cref="TapProjectionTimeout"/> elapses. The validator wallet
    /// tap endpoint's response confirms only that the HTTP request succeeded, not that the ledger
    /// has committed the resulting transaction, so a snapshot taken immediately after the tap can
    /// still miss it.
    /// </summary>
    private static async Task<(
        List<InterfaceAcsSnapshotEntry<IHolding, HoldingView>> Entries,
        List<InterfaceAcsSnapshotEntry<IHolding, HoldingView>.Created> TappedAmuletHoldings)>
        PollForTappedAmuletHoldingsAsync(
            CapabilityLane<(ICantonLedgerClient Client, Party Operator, LocalnetValidatorWallet OperatorWallet)> lane,
            HashSet<ContractId<IHolding>> preTapAmuletContractIds,
            CancellationToken cancellationToken)
    {
        var (client, @operator, _) = lane.Capability;
        var deadline = DateTimeOffset.UtcNow.Add(TapProjectionTimeout);
        while (true)
        {
            var entries = new List<InterfaceAcsSnapshotEntry<IHolding, HoldingView>>();
            using (await lane.HoldStreamAsync(cancellationToken))
            {
                await foreach (var entry in client.SubscribeActiveAsync(
                    IHolding.View, @operator, cancellationToken: cancellationToken))
                {
                    entries.Add(entry);
                }
            }

            var tappedAmuletHoldings = entries
                .OfType<InterfaceAcsSnapshotEntry<IHolding, HoldingView>.Created>()
                .Where(created => created.Payload.InstrumentId.Id == "Amulet"
                    && !preTapAmuletContractIds.Contains(created.ContractId))
                .ToList();

            if (tappedAmuletHoldings.Count > 0 || DateTimeOffset.UtcNow >= deadline)
            {
                return (entries, tappedAmuletHoldings);
            }

            await Task.Delay(TapProjectionPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task SubscribeActiveAsync_decodes_the_operator_ValidatorLicense_through_the_generated_Splice_binding()
    {
        await using var lane = await OpenOperatorLaneAsync(TestContext.Current.CancellationToken);
        var (client, @operator, _) = lane.Capability;

        var entries = new List<AcsSnapshotEntry<ValidatorLicense>>();
        using var streamHold = await lane.HoldStreamAsync(TestContext.Current.CancellationToken);
        await foreach (var entry in client.SubscribeActiveAsync<ValidatorLicense>(
            @operator, cancellationToken: TestContext.Current.CancellationToken))
        {
            entries.Add(entry);
        }

        var license = entries.OfType<AcsSnapshotEntry<ValidatorLicense>.Created>().Should().ContainSingle().Subject;
        @operator.Value.Should().StartWith("a-validator-1::");
        license.Payload.Validator.Should().Be(@operator);
        license.Payload.Sponsor.Should().Be(@operator);
        license.Payload.Dso.Value.Should().StartWith("DSO::");
        license.Payload.Metadata.Should().NotBeNull();
        license.Payload.LastActiveAt.Should().NotBeNull();
        entries[^1].Should().BeOfType<AcsSnapshotEntry<ValidatorLicense>.Checkpoint>();
    }

    [Fact]
    public async Task SubscribeActiveAsync_decodes_the_operator_Amulet_holdings_through_the_generated_Splice_Token_Holding_binding()
    {
        await using var lane = await OpenOperatorLaneAsync(TestContext.Current.CancellationToken);
        var (client, @operator, operatorWallet) = lane.Capability;

        var preTapAmuletContractIds = await SnapshotAmuletHoldingContractIdsAsync(
            lane, TestContext.Current.CancellationToken);

        await operatorWallet.TapAsync("10.0", TestContext.Current.CancellationToken);

        var (entries, tappedAmuletHoldings) = await PollForTappedAmuletHoldingsAsync(
            lane, preTapAmuletContractIds, TestContext.Current.CancellationToken);

        var holdings = entries.OfType<InterfaceAcsSnapshotEntry<IHolding, HoldingView>.Created>().ToList();
        holdings.Should().NotBeEmpty();
        tappedAmuletHoldings.Should().NotBeEmpty();
        foreach (var amulet in tappedAmuletHoldings)
        {
            amulet.Payload.Owner.Should().Be(@operator);
            amulet.Payload.InstrumentId.Admin.Value.Should().StartWith("DSO::");
            amulet.Payload.Amount.Should().BeGreaterThan(0);
        }
        entries[^1].Should().BeOfType<InterfaceAcsSnapshotEntry<IHolding, HoldingView>.Checkpoint>();
    }

    [Fact]
    public async Task TransferOffer_created_and_withdrawn_through_the_generated_Splice_Wallet_binding_commits_and_reads_back()
    {
        await using var lane = await OpenOperatorLaneAsync(TestContext.Current.CancellationToken);
        var (client, @operator, _) = lane.Capability;
        var dso = (await ReadOperatorValidatorLicenseAsync(lane, TestContext.Current.CancellationToken)).Payload.Dso;
        var trackingId = $"parity-1511-{Guid.NewGuid():N}";
        var offer = new TransferOffer(
            Sender: @operator,
            Receiver: dso,
            Dso: dso,
            Amount: new PaymentAmount(12.5m, Unit.AmuletUnit),
            Description: "splice parity live write",
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(1),
            TrackingId: trackingId);

        var createOutcome = await client.TryCreateAsync(
            offer, @operator, cancellationToken: TestContext.Current.CancellationToken);

        var offerCid = createOutcome.Should().BeOfType<ExerciseOutcome<ContractId<TransferOffer>>.One>(
            "the sender alone signs a TransferOffer, so the create has to commit; got {0}", createOutcome).Subject.Result;
        var created = await FindTransferOfferAsync(lane, trackingId, TestContext.Current.CancellationToken);
        created.Should().NotBeNull("the committed create has to be in the sender's active contract set");
        created!.ContractId.Value.Should().Be(offerCid.Value);
        created.Payload.Sender.Should().Be(@operator);
        created.Payload.Receiver.Should().Be(dso);
        created.Payload.Amount.Should().Be(new PaymentAmount(12.5m, Unit.AmuletUnit));
        created.Payload.Description.Should().Be("splice parity live write");

        var withdrawOutcome = await offerCid.TryTransferOffer_WithdrawAsync(
            client,
            new TransferOffer.TransferOffer_Withdraw(Reason: "splice parity live write cleanup"),
            @operator,
            cancellationToken: TestContext.Current.CancellationToken);

        var withdrawn = withdrawOutcome.Should().BeOfType<ExerciseOutcome<TransferOffer_WithdrawResult>.One>(
            "the sender controls the withdrawal, so it has to commit; got {0}", withdrawOutcome).Subject.Result;
        withdrawn.TrackingInfo.Should().Be(new TransferOfferTrackingInfo(trackingId, @operator, dso));
        (await FindTransferOfferAsync(lane, trackingId, TestContext.Current.CancellationToken))
            .Should().BeNull("withdrawing the offer archives it");
    }

    private static async Task<AcsSnapshotEntry<ValidatorLicense>.Created> ReadOperatorValidatorLicenseAsync(
        CapabilityLane<(ICantonLedgerClient Client, Party Operator, LocalnetValidatorWallet OperatorWallet)> lane,
        CancellationToken cancellationToken)
    {
        var (client, @operator, _) = lane.Capability;
        var entries = new List<AcsSnapshotEntry<ValidatorLicense>>();
        using var streamHold = await lane.HoldStreamAsync(cancellationToken);
        await foreach (var entry in client.SubscribeActiveAsync<ValidatorLicense>(
            @operator, cancellationToken: cancellationToken))
        {
            entries.Add(entry);
        }

        return entries.OfType<AcsSnapshotEntry<ValidatorLicense>.Created>().Should().ContainSingle().Subject;
    }

    private static async Task<AcsSnapshotEntry<TransferOffer>.Created?> FindTransferOfferAsync(
        CapabilityLane<(ICantonLedgerClient Client, Party Operator, LocalnetValidatorWallet OperatorWallet)> lane,
        string trackingId,
        CancellationToken cancellationToken)
    {
        var (client, @operator, _) = lane.Capability;
        using var streamHold = await lane.HoldStreamAsync(cancellationToken);
        await foreach (var entry in client.SubscribeActiveAsync<TransferOffer>(
            @operator, cancellationToken: cancellationToken))
        {
            if (entry is AcsSnapshotEntry<TransferOffer>.Created created && created.Payload.TrackingId == trackingId)
            {
                return created;
            }
        }

        return null;
    }
}
