// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Abstractions.Tests;

/// <summary>
/// <see cref="IAdminClient"/>'s public data records carry no subtype of their own: each is a
/// closed shape a consumer reads back, never a base a consumer widens. Sealing keeps equality
/// closed and keeps a future variant from silently becoming an inheritance hierarchy.
/// </summary>
public class AdminSurfaceRecordsAreSealedTests
{
    public static TheoryData<Type> AdminSurfaceRecords() => new()
    {
        typeof(ConnectedSynchronizer),
        typeof(PartyDetails),
        typeof(UserDetails),
        typeof(PackageDetails),
        typeof(VettedPackage),
        typeof(UserRight.ActAs),
        typeof(UserRight.ReadAs),
        typeof(UserRight.ParticipantAdmin),
        typeof(UserRight.IdentityProviderAdmin),
        typeof(UserRight.ReadAsAnyParty),
        typeof(UserRight.ExecuteAs),
        typeof(UserRight.ExecuteAsAnyParty),
    };

    [Theory]
    [MemberData(nameof(AdminSurfaceRecords))]
    public void AdminSurfaceRecord_is_sealed(Type recordType)
    {
        recordType.IsSealed.Should().BeTrue(
            "{0} is a closed data shape with no intended subtype", recordType.Name);
    }
}
