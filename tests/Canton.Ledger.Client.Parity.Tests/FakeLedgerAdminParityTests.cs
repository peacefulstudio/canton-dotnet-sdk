// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing;
using Daml.Runtime.Data;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class FakeLedgerAdminParityTests : LedgerAdminParityTests
{
    private const string VettedPackageId = "1220fakevettedpackagedeadbeefdeadbeefdeadbeefdeadbeefdeadbeef01";

    protected override Task<CapabilityLane<IAdminClient>> OpenAdminAsync(
        AdminParityScenario scenario, CancellationToken cancellationToken)
    {
        var party = new Party($"{scenario.PartyHint}::1220fake");
        var partyDetails = new PartyDetails(party, IsLocal: true);
        var synchronizerId = new SynchronizerId("synchronizer::1220fake");
        var vettedPackage = new VettedPackage(
            VettedPackageId, "fake-vetted-package", "1.0.0", "participant::1220fake", synchronizerId);
        var packageArchive = new PackageArchive(new byte[] { 1, 2, 3, 4 }, VettedPackageId, HashFunction.Sha256);
        var client = FakeAdminClient.Create()
            .WithParticipantId("participant::1220fake")
            .WithAllocatedParty(scenario.PartyHint, partyDetails)
            .WithParties(partyDetails)
            .WithUser(new UserDetails(scenario.UserId, party))
            .WithUserRights(scenario.UserId, new UserRight.ActAs(party))
            .WithVettedPackages(vettedPackage)
            .WithPackage(VettedPackageId, packageArchive)
            .Build();
        return Task.FromResult(new CapabilityLane<IAdminClient>(client, () => ValueTask.CompletedTask));
    }
}
