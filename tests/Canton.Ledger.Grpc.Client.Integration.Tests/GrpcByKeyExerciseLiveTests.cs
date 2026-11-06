// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing.Localnet;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

/// <summary>
/// LocalNet coverage for the generated <c>TryCreditByKeyAsync</c> over gRPC: with two active
/// <c>Account</c> contracts sharing one key, the outcome names the contract the participant
/// resolved the key to, and the ledger archived exactly that one.
/// </summary>
[Trait("Category", "Integration")]
public class GrpcByKeyExerciseLiveTests
{
    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this integration test.";

    [Fact]
    public async Task TryCreditByKeyAsync_names_the_resolved_Account_when_two_Accounts_share_a_key()
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

        var party = await fixture.AllocatePartyAsync("grpc-by-key-exercise", cancellationToken: TestContext.Current.CancellationToken);
        var custodian = new Party(party.PartyId);
        await using var actAsRights = ActAsRightsLease.ForValidator(fixture);
        await actAsRights.GrantAsync(party.PartyId, TestContext.Current.CancellationToken);

        await using var services = LocalnetLedgerServices.ForValidator(fixture, fixture.ValidatorUserId);
        var client = services.GetRequiredService<ICantonLedgerClient>();
        var label = $"account-{Guid.NewGuid():N}";
        var first = await CreateAccountAsync(client, custodian, label);
        var second = await CreateAccountAsync(client, custodian, label);

        var outcome = await Account.TryCreditByKeyAsync(
            client,
            new AccountKey(custodian, label),
            new Account.Credit(5L),
            custodian,
            cancellationToken: TestContext.Current.CancellationToken);

        var exercise = Assert.IsType<ExerciseOutcome<ByKeyExercise<Account, ContractId<Account>>>.One>(outcome).Result;
        var resolved = exercise.ResolvedContractId.Value;
        Assert.Contains(resolved, new[] { first.Value, second.Value });
        var untouched = resolved == first.Value ? second.Value : first.Value;

        using var streamHold = await LedgerUserRightsGate.Shared.HoldStreamAsync(
            fixture.ValidatorUserId, TestContext.Current.CancellationToken);
        var active = await client.SnapshotAsync<Account>(custodian, cancellationToken: TestContext.Current.CancellationToken);
        var activeIds = active.Where(contract => contract.Data.Label == label).Select(contract => contract.Id.Value).ToList();
        Assert.DoesNotContain(resolved, activeIds);
        Assert.Contains(untouched, activeIds);
        Assert.Contains(exercise.Result.Value, activeIds);
    }

    private static async Task<ContractId<Account>> CreateAccountAsync(ICantonLedgerClient client, Party custodian, string label)
    {
        var outcome = await client.TryCreateAsync(
            new Account(custodian, label, 100L),
            custodian,
            cancellationToken: TestContext.Current.CancellationToken);
        return Assert.IsType<ExerciseOutcome<ContractId<Account>>.One>(outcome).Result;
    }
}
