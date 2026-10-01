// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class FakeLedgerExternalSigningParityTests : LedgerExternalSigningParityTests
{
    protected override Task<CapabilityLane<ExternalSigningCapability>> OpenExternalSigningAsync(
        ExternalSigningScenario scenario, CancellationToken cancellationToken)
    {
        var party = new Party($"{scenario.PartyHint}::1220fake");
        var updateId = "fake-update-1";
        var admin = FakeAdminClient.Create()
            .WithExternalPartyTopology(new ExternalPartyTopology(
                party, "1220fake", [new byte[] { 1 }, new byte[] { 2 }], new byte[] { 3, 4 }))
            .WithAllocatedExternalParty(party)
            .WithParties(new PartyDetails(party, IsLocal: true))
            .Build();
        var createdMarker = new CreatedContract(
            "0", "fake-marker-cid", Marker.TemplateId, new Marker(party).ToRecord(), [party], [party], []);
        var transaction = new TransactionResult(
            updateId, LedgerOffset.At(6), [createdMarker], [], new CommandId("fake-command"));
        var client = FakeLedgerClient.Create()
            .WithLedgerEnd(LedgerOffset.At(5))
            .WithConnectedSynchronizers(new ConnectedSynchronizer("global", "fake-sync", SynchronizerPermissionLevel.Submission))
            .WithPreparedSubmission(new PreparedSubmission(
                new byte[] { 5, 6 }, new byte[] { 7, 8 }, HashingSchemeVersion.V2, null, null))
            .WithExecutedSubmission(new ExecutedSubmission(updateId, LedgerOffset.At(6)))
            .WithExecutedTransaction(transaction)
            .WithUpdateById(updateId, transaction)
            .WithCompletionEvents(new CompletionStreamEvent.CommandAccepted(
                new Completion(
                    new CommandId("fake-command"),
                    LedgerOffset.At(6),
                    [party],
                    new SynchronizerTime("fake-sync", DateTimeOffset.UnixEpoch),
                    scenario.SubmissionId,
                    UserId: null,
                    DeduplicationPeriod: null,
                    PaidTrafficCost: 0L,
                    TraceContext: null),
                updateId))
            .Build();

        return Task.FromResult(new CapabilityLane<ExternalSigningCapability>(
            new ExternalSigningCapability(admin, client, (_, _) => Task.CompletedTask),
            client.DisposeAsync));
    }
}
