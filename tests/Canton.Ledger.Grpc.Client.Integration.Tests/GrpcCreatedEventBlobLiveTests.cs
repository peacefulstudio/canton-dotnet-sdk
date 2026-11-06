// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Canton.Ledger.Testing.Localnet;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

/// <summary>
/// LocalNet coverage for <c>CreatedEventBlob.Decode(...).ReadContract</c>: the
/// <c>created_event_blob</c> a participant hands out for a keyed <c>Account</c> decodes into the
/// payload, the contract key and the same key hash the Ledger API's <c>CreatedEvent</c> carries.
/// </summary>
[Trait("Category", "Integration")]
public class GrpcCreatedEventBlobLiveTests
{
    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this integration test.";

    [Fact]
    public async Task ReadContract_decodes_the_payload_key_and_Ledger_API_key_hash_of_a_keyed_Account_blob_the_participant_disclosed()
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(SkipMessage);
        }

        await using var fixture = LocalnetFixture.FromEnvironment();
        var darOutcome = await fixture.UploadDarAsync(ContractKeysDar.Path, TestContext.Current.CancellationToken);
        Assert.True(
            darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
            $"Unexpected DAR upload outcome: {darOutcome}");

        var party = await fixture.AllocatePartyAsync("grpc-created-event-blob", cancellationToken: TestContext.Current.CancellationToken);
        var custodian = new Party(party.PartyId);
        await using var actAsRights = ActAsRightsLease.ForValidator(fixture);
        await actAsRights.GrantAsync(party.PartyId, TestContext.Current.CancellationToken);

        await using var services = LocalnetLedgerServices.ForValidator(fixture, fixture.ValidatorUserId);
        var client = services.GetRequiredService<ICantonLedgerClient>();
        var label = $"account-{Guid.NewGuid():N}";
        var created = await client.TryCreateAsync(
            new Account(custodian, label, 100L),
            custodian,
            cancellationToken: TestContext.Current.CancellationToken);
        var accountId = Assert.IsType<ExerciseOutcome<ContractId<Account>>.One>(created).Result;

        var submitter = new RuntimeCommands.SubmitterInfo(new HashSet<Party> { custodian }, new HashSet<Party>());
        var disclosure = await client.GetDisclosureAsync(
            accountId, submitter, cancellationToken: TestContext.Current.CancellationToken);

        var decoded = CreatedEventBlob.Decode(Assert.IsType<RuntimeCommands.DisclosedContract>(disclosure).CreatedEventBlob);
        var contract = decoded.ReadContract<Account, AccountKey>();

        Assert.Equal(accountId.Value, contract.Id.Value);
        Assert.Equal(new Account(custodian, label, 100L), contract.Data);
        Assert.Equal(new AccountKey(custodian, label), contract.Key.Value);
        var lifecycle = await client.GetEventsByContractIdAsync(
            accountId, submitter, cancellationToken: TestContext.Current.CancellationToken);
        var ledgerApiKeyHash = Assert.IsType<ContractStreamEvent<Account>.Created>(lifecycle.Created).Key?.KeyHash;
        Assert.False(string.IsNullOrEmpty(ledgerApiKeyHash));
        Assert.Equal(ledgerApiKeyHash, contract.Key.Hash);
        Assert.Equal(new[] { party.PartyId }, decoded.Signatories.ToArray());
    }
}
