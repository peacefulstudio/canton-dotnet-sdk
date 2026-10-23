// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing.Localnet;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// An <see cref="ICantonLedgerClient"/> with a party it may act as, <c>contractkeys.dar</c> uploaded,
/// and the hook that reads the <c>contractKeyHash</c> the participant sent for a contract through
/// the provider's raw event-query endpoint.
/// </summary>
/// <param name="Client">The provider's ledger client.</param>
/// <param name="Signatory">A party the client may act as.</param>
/// <param name="ReadWireKeyHashAsync">
/// Reads the base64 contract key hash the participant sent for a contract as seen by a reader, or
/// <see langword="null"/> when it sent none.
/// </param>
public sealed record ContractKeyCapability(
    ICantonLedgerClient Client,
    Party Signatory,
    Func<Party, string, CancellationToken, Task<string?>> ReadWireKeyHashAsync);

/// <summary>
/// Live parity suite over how <c>Created.Key</c> is populated: a keyed contract created through the
/// transport comes back through <c>SubscribeAsync</c> carrying a key whose value decodes to the key
/// the create used, and whose <c>KeyHash</c> equals the <c>contractKeyHash</c> the participant sent
/// for the same contract. Covers a record key, a bare <c>Party</c> key and the same bare key seen
/// through the <c>IStewardship</c> interface view.
/// </summary>
/// <remarks>
/// Only <c>Created</c> and <c>Assigned</c> carry a key, so the archive half of each template round
/// trip correlates through the contract id captured at create. Canton 3.x does not make contract
/// keys unique, so nothing here asserts that a key identifies at most one active contract.
/// </remarks>
public abstract class LedgerContractKeyRoundTripParityTests
{
    private static readonly TimeSpan SubscribeBudget = TimeSpan.FromSeconds(60);

    /// <summary>Opens a lane over this provider's contract-key capability for one test.</summary>
    protected abstract Task<CapabilityLane<ContractKeyCapability>> OpenContractKeysAsync(
        CancellationToken cancellationToken);

    [Fact]
    public async Task SubscribeAsync_populates_Created_Key_with_the_record_key_of_an_Account()
    {
        await using var lane = await OpenContractKeysAsync(TestContext.Current.CancellationToken);
        var (client, custodian, readWireKeyHash) = lane.Capability;
        var label = $"account-{Guid.NewGuid():N}";
        var startOffset = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        var accountCid = await CreateAsync(client, custodian, new Account(custodian, label, 100L));
        await ArchiveAsync(client, accountCid.ArchiveCommand(), custodian);

        var created = await ReadRoundTripAsync<Account>(lane, custodian, accountCid.Value, startOffset);

        var key = created.Key;
        key.Should().NotBeNull();
        key.Value.Should().BeOfType<DamlRecord>();
        var contract = created.ToContract<Account, AccountKey>();
        contract.Key.Value.Should().Be(new AccountKey(custodian, label));
        key.TemplateId.Should().NotBeNull();
        key.TemplateId.ModuleName.Should().Be("ContractKeys");
        key.TemplateId.EntityName.Should().Be("Account");
        var wireKeyHash = await readWireKeyHash(custodian, accountCid.Value, TestContext.Current.CancellationToken);
        wireKeyHash.Should().NotBeNullOrWhiteSpace("the participant has to send a contract key hash");
        key.KeyHash.Should().Be(wireKeyHash);
        contract.Key.Hash.Should().Be(wireKeyHash);
    }

    [Fact]
    public async Task SubscribeAsync_populates_Created_Key_with_the_bare_Party_key_of_a_Steward()
    {
        await using var lane = await OpenContractKeysAsync(TestContext.Current.CancellationToken);
        var (client, steward, readWireKeyHash) = lane.Capability;
        var startOffset = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        var stewardCid = await CreateAsync(client, steward, new Steward(steward, "charter"));
        await ArchiveAsync(client, stewardCid.ArchiveCommand(), steward);

        var created = await ReadRoundTripAsync<Steward>(lane, steward, stewardCid.Value, startOffset);

        var key = created.Key;
        key.Should().NotBeNull();
        key.Value.Should().BeOfType<DamlParty>();
        Steward.Key.KeyDecoder(key.Value).Should().Be(steward);
        key.TemplateId.Should().NotBeNull();
        key.TemplateId.ModuleName.Should().Be("ContractKeys");
        key.TemplateId.EntityName.Should().Be("Steward");
        var wireKeyHash = await readWireKeyHash(steward, stewardCid.Value, TestContext.Current.CancellationToken);
        wireKeyHash.Should().NotBeNullOrWhiteSpace("the participant has to send a contract key hash");
        key.KeyHash.Should().Be(wireKeyHash);
    }

    [Fact]
    public async Task SubscribeAsync_populates_Created_Key_on_the_IStewardship_view_of_a_keyed_Steward()
    {
        await using var lane = await OpenContractKeysAsync(TestContext.Current.CancellationToken);
        var (client, steward, readWireKeyHash) = lane.Capability;
        var startOffset = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        var stewardCid = await CreateAsync(client, steward, new Steward(steward, "charter"));

        var created = await ReadCreatedThroughStewardshipAsync(lane, steward, stewardCid.Value, startOffset);

        var key = created.Key;
        key.Should().NotBeNull();
        key.Value.Should().BeOfType<DamlParty>();
        Steward.Key.KeyDecoder(key.Value).Should().Be(steward);
        key.TemplateId.Should().NotBeNull();
        key.TemplateId.ModuleName.Should().Be("ContractKeys");
        key.TemplateId.EntityName.Should().Be("Steward");
        var wireKeyHash = await readWireKeyHash(steward, stewardCid.Value, TestContext.Current.CancellationToken);
        wireKeyHash.Should().NotBeNullOrWhiteSpace("the participant has to send a contract key hash");
        key.KeyHash.Should().Be(wireKeyHash);
    }

    private static async Task<ContractId<T>> CreateAsync<T>(ICantonLedgerClient client, Party submitter, T template)
        where T : ITemplate, IDamlRecord<T>
    {
        var outcome = await client.TryCreateAsync(
            template, submitter, cancellationToken: TestContext.Current.CancellationToken);
        return outcome.Should().BeOfType<ExerciseOutcome<ContractId<T>>.One>(
            "the {0} create has to commit and its transaction has to read back, and the transport reported {1}",
            typeof(T).Name,
            outcome).Subject.Result;
    }

    private static async Task ArchiveAsync(ICantonLedgerClient client, ICommand archive, Party submitter)
    {
        var result = await client.SubmitAndWaitAsync(
            CommandsSubmission.Single(archive, submitter),
            submitter,
            cancellationToken: TestContext.Current.CancellationToken);

        result.UpdateId.Should().NotBeNullOrWhiteSpace("the archive submission has to return an update id");
    }

    private static async Task<ContractStreamEvent<T>.Created> ReadRoundTripAsync<T>(
        CapabilityLane<ContractKeyCapability> lane, Party reader, string contractIdValue, LedgerOffset fromExclusive)
        where T : ITemplate, IDamlRecord<T>
    {
        var client = lane.Capability.Client;
        var endOffset = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        ContractStreamEvent<T>.Created? created = null;
        ContractStreamEvent<T>.Archived? archived = null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(SubscribeBudget);
        using var streamHold = await lane.HoldStreamAsync(TestContext.Current.CancellationToken);
        try
        {
            await foreach (var streamEvent in client.SubscribeAsync<T>(reader, fromExclusive, endOffset, cts.Token))
            {
                FailOnStreamFault(streamEvent, contractIdValue);

                switch (streamEvent)
                {
                    case ContractStreamEvent<T>.Created candidate
                        when candidate.ContractId.Value == contractIdValue:
                        created ??= candidate;
                        break;
                    case ContractStreamEvent<T>.Archived candidate
                        when candidate.ContractId.Value == contractIdValue:
                        archived ??= candidate;
                        break;
                }

                if (created is not null && archived is not null) return created;
            }
        }
        catch (OperationCanceledException) when (
            cts.IsCancellationRequested && !TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            Assert.Fail(
                $"the {typeof(T).Name} subscribe stream over ({fromExclusive}, {endOffset}] did not deliver both a "
                + $"Created and an Archived event for {contractIdValue} within {SubscribeBudget.TotalSeconds:0}s");
        }

        Assert.Fail(
            $"the {typeof(T).Name} subscribe stream over ({fromExclusive}, {endOffset}] completed with the Created "
            + $"event {(created is null ? "missing" : "present")} and the Archived event "
            + $"{(archived is null ? "missing" : "present")} for {contractIdValue}");
        return default!;
    }

    private static void FailOnStreamFault<T>(ContractStreamEvent<T> streamEvent, string contractIdValue)
        where T : ITemplate, IDamlRecord<T>
    {
        if (streamEvent is ContractStreamEvent<T>.StreamError error)
        {
            Assert.Fail(
                $"the {typeof(T).Name} subscribe stream faulted before the round trip of {contractIdValue} "
                + $"completed: status {error.Status}, category {error.Category?.ToString() ?? "none"} - "
                + error.Message);
        }

        if (streamEvent is ContractStreamEvent<T>.Unclassified unclassified)
        {
            Assert.Fail(
                $"the {typeof(T).Name} subscribe stream refused the event at offset {unclassified.Offset} as "
                + $"{unclassified.Kind} before the round trip of {contractIdValue} completed; a contract-key "
                + "decode failure lands here");
        }
    }

    private static async Task<InterfaceStreamEvent<IStewardship, StewardshipView>.Created>
        ReadCreatedThroughStewardshipAsync(
            CapabilityLane<ContractKeyCapability> lane,
            Party reader,
            string contractIdValue,
            LedgerOffset fromExclusive)
    {
        var client = lane.Capability.Client;
        var endOffset = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(SubscribeBudget);
        using var streamHold = await lane.HoldStreamAsync(TestContext.Current.CancellationToken);
        try
        {
            await foreach (var streamEvent in client.SubscribeAsync(
                IStewardship.View, reader, fromExclusive, endOffset, cts.Token))
            {
                FailOnInterfaceStreamFault(streamEvent, contractIdValue);

                if (streamEvent is InterfaceStreamEvent<IStewardship, StewardshipView>.Created created
                    && created.ContractId.Value == contractIdValue)
                {
                    return created;
                }
            }
        }
        catch (OperationCanceledException) when (
            cts.IsCancellationRequested && !TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            Assert.Fail(
                $"the IStewardship subscribe stream over ({fromExclusive}, {endOffset}] did not deliver a "
                + $"Created event for {contractIdValue} within {SubscribeBudget.TotalSeconds:0}s");
        }

        Assert.Fail(
            $"the IStewardship subscribe stream over ({fromExclusive}, {endOffset}] completed without a "
            + $"Created event for {contractIdValue}");
        return default!;
    }

    private static void FailOnInterfaceStreamFault(
        InterfaceStreamEvent<IStewardship, StewardshipView> streamEvent, string contractIdValue)
    {
        if (streamEvent is InterfaceStreamEvent<IStewardship, StewardshipView>.StreamError error)
        {
            Assert.Fail(
                $"the IStewardship subscribe stream faulted before {contractIdValue} arrived: status "
                + $"{error.Status}, category {error.Category?.ToString() ?? "none"} - {error.Message}");
        }

        if (streamEvent is InterfaceStreamEvent<IStewardship, StewardshipView>.Unclassified unclassified)
        {
            Assert.Fail(
                $"the IStewardship subscribe stream refused the event at offset {unclassified.Offset} as "
                + $"{unclassified.Kind} before {contractIdValue} arrived; an interface-stream key decode "
                + "failure lands here");
        }
    }
}
