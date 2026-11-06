// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Rest.Client.Raw;
using Daml.Codegen.Testing.Conformance;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// LocalNet coverage for the generated <c>TryCreditByKeyAsync</c> over the JSON Ledger API: with two
/// active <c>Account</c> contracts sharing one key, the outcome names the contract the participant
/// resolved the key to, and the ledger archived exactly that one.
/// </summary>
[Trait("Category", "Integration")]
public class RestByKeyExerciseLiveTests
{
    [Fact]
    public async Task TryCreditByKeyAsync_names_the_resolved_Account_when_two_Accounts_share_a_key()
    {
        await using var lane = await RestLiveLane.OpenAsync(TestContext.Current.CancellationToken);
        var custodian = await NewSignatoryAsync(lane, TestContext.Current.CancellationToken);
        var label = $"account-{Guid.NewGuid():N}";
        var first = await CreateAccountAsync(lane, custodian, label);
        var second = await CreateAccountAsync(lane, custodian, label);

        var outcome = await Account.TryCreditByKeyAsync(
            lane.LedgerClient,
            new AccountKey(custodian, label),
            new Account.Credit(5L),
            custodian,
            cancellationToken: TestContext.Current.CancellationToken);

        var exercise = Assert.IsType<ExerciseOutcome<ByKeyExercise<Account, ContractId<Account>>>.One>(outcome).Result;
        var resolved = exercise.ResolvedContractId.Value;
        Assert.Contains(resolved, new[] { first.Value, second.Value });
        var untouched = resolved == first.Value ? second.Value : first.Value;

        using var streamHold = await lane.HoldStreamAsync(TestContext.Current.CancellationToken);
        var active = await lane.LedgerClient.SnapshotAsync<Account>(
            custodian, cancellationToken: TestContext.Current.CancellationToken);
        var activeIds = active.Where(contract => contract.Data.Label == label).Select(contract => contract.Id.Value).ToList();
        Assert.DoesNotContain(resolved, activeIds);
        Assert.Contains(untouched, activeIds);
        Assert.Contains(exercise.Result.Value, activeIds);
    }

    private static async Task<ContractId<Account>> CreateAccountAsync(RestLiveLane lane, Party custodian, string label)
    {
        var outcome = await lane.LedgerClient.TryCreateAsync(
            new Account(custodian, label, 100L),
            custodian,
            cancellationToken: TestContext.Current.CancellationToken);
        return Assert.IsType<ExerciseOutcome<ContractId<Account>>.One>(outcome).Result;
    }

    private static async Task<Party> NewSignatoryAsync(RestLiveLane lane, CancellationToken cancellationToken)
    {
        var darPath = await ContractKeysDarPathAsync(cancellationToken);
        try
        {
            var darOutcome = await lane.Fixture.UploadDarAsync(darPath, cancellationToken);
            Assert.True(
                darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
                $"Unexpected DAR upload outcome: {darOutcome}");
        }
        finally
        {
            File.Delete(darPath);
        }

        var party = await lane.Fixture.AllocatePartyAsync("rest-by-key-exercise", cancellationToken: cancellationToken);
        await lane.GrantActAsAsync(party.PartyId, cancellationToken);
        return new Party(party.PartyId);
    }

    private static async Task<string> ContractKeysDarPathAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"contractkeys-{Guid.NewGuid():N}.dar");
        await using var dar = ConformanceCorpus.OpenDar(ConformancePackage.ContractKeys);
        await using var file = File.Create(path);
        try
        {
            await dar.CopyToAsync(file, cancellationToken);
        }
        catch
        {
            await file.DisposeAsync();
            File.Delete(path);
            throw;
        }

        return path;
    }
}
