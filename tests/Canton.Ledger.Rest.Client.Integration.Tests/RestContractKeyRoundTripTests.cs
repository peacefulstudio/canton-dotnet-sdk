// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Rest.Client.Raw;
using Daml.Codegen.Testing.Conformance;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// LocalNet coverage for the REST transport's read-back of a keyed create: the transaction that
/// created a record-keyed <c>Account</c> and a bare-<c>Party</c>-keyed <c>Steward</c> comes back from
/// <c>TryCreateAsync</c> with the new contract id. The key population behind
/// <c>SubscribeAsync</c> is covered across both transports by
/// <c>Canton.Ledger.Client.Parity.Tests.LedgerContractKeyRoundTripParityTests</c>.
/// </summary>
[Trait("Category", "Integration")]
public class RestContractKeyRoundTripTests
{
    [Fact]
    public async Task TryCreateAsync_reads_back_the_transaction_that_created_a_record_keyed_Account()
    {
        await using var lane = await RestLiveLane.OpenAsync(TestContext.Current.CancellationToken);
        var custodian = await NewSignatoryAsync(lane, TestContext.Current.CancellationToken);

        var outcome = await lane.LedgerClient.TryCreateAsync(
            new Account(custodian, NewAccountLabel(), 100L),
            custodian,
            cancellationToken: TestContext.Current.CancellationToken);

        var accountCid = AssertReadBack(outcome);
        Assert.False(string.IsNullOrWhiteSpace(accountCid.Value), "the participant returned an empty contract id");
    }

    [Fact]
    public async Task TryCreateAsync_reads_back_the_transaction_that_created_a_bare_Party_keyed_Steward()
    {
        await using var lane = await RestLiveLane.OpenAsync(TestContext.Current.CancellationToken);
        var steward = await NewSignatoryAsync(lane, TestContext.Current.CancellationToken);

        var outcome = await lane.LedgerClient.TryCreateAsync(
            new Steward(steward, "charter"), steward, cancellationToken: TestContext.Current.CancellationToken);

        var stewardCid = AssertReadBack(outcome);
        Assert.False(string.IsNullOrWhiteSpace(stewardCid.Value), "the participant returned an empty contract id");
    }

    private static string NewAccountLabel() => $"account-{Guid.NewGuid():N}";

    private static ContractId<T> AssertReadBack<T>(ExerciseOutcome<ContractId<T>> outcome)
        where T : ITemplate
    {
        if (outcome is ExerciseOutcome<ContractId<T>>.InfraError rejection)
        {
            Assert.Fail(
                $"the {typeof(T).Name} create committed but its transaction did not read back: "
                + $"{rejection.Status} {rejection.Message}");
        }

        return Assert.IsType<ExerciseOutcome<ContractId<T>>.One>(outcome).Result;
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

        var party = await lane.Fixture.AllocatePartyAsync("rest-contract-key", cancellationToken: cancellationToken);
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
