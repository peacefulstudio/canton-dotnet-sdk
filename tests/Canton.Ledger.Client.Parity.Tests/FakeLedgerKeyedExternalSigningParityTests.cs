// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class FakeLedgerKeyedExternalSigningParityTests : LedgerKeyedExternalSigningParityTests
{
    protected override Task<CapabilityLane<ExternalSigningCapability>> OpenKeyedExternalSigningAsync(
        ExternalSigningScenario scenario, CancellationToken cancellationToken)
    {
        var party = new Party($"{scenario.PartyHint}::1220fake");
        var updateId = "fake-keyed-update-1";
        var admin = FakeAdminClient.Create()
            .WithExternalPartyTopology(new ExternalPartyTopology(
                party, "1220fake", [new byte[] { 1 }, new byte[] { 2 }], new byte[] { 3, 4 }))
            .WithAllocatedExternalParty(party)
            .WithParties(new PartyDetails(party, IsLocal: true))
            .Build();
        var creditedAccount = new CreatedContract(
            "0",
            "fake-account-cid",
            Account.TemplateId,
            new Account(party, scenario.PartyHint, 150).ToRecord(),
            [party],
            [party],
            []);
        var transaction = new TransactionResult(
            updateId, LedgerOffset.At(6), [creditedAccount], [], new CommandId("fake-command"));
        var client = FakeLedgerClient.Create()
            .WithLedgerEnd(LedgerOffset.At(5))
            .WithConnectedSynchronizers(new ConnectedSynchronizer("global", "fake-sync", SynchronizerPermissionLevel.Submission))
            .WithPreparedSubmission(new PreparedSubmission(
                Convert.FromHexString("120b2a0966616b652d75756964"),
                Convert.FromHexString("5d384e4e4fee4d431fca69896b94e7033f4cf34a8a1ee806f4e8572299f6f636"),
                HashingSchemeVersion.V3,
                null,
                null))
            .WithExecutedSubmission(new ExecutedSubmission(updateId, LedgerOffset.At(6)))
            .WithExecutedTransaction(transaction)
            .Build();

        return Task.FromResult(new CapabilityLane<ExternalSigningCapability>(
            new ExternalSigningCapability(admin, client, (_, _) => Task.CompletedTask),
            client.DisposeAsync));
    }
}
