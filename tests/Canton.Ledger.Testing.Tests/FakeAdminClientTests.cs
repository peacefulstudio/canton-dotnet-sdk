// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using AwesomeAssertions;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class FakeAdminClientTests
{
    [Fact]
    public async Task GetParticipantIdAsync_returns_the_staged_participant_id()
    {
        var client = FakeAdminClient.Create().WithParticipantId("participant1").Build();

        var participantId = await client.GetParticipantIdAsync(TestContext.Current.CancellationToken);

        participantId.Should().Be("participant1");
    }

    [Fact]
    public async Task GetParticipantIdAsync_for_unstaged_client_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.GetParticipantIdAsync();

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithParticipantId");
    }

    [Fact]
    public async Task AllocatePartyAsync_returns_the_staged_PartyDetails_for_the_hint()
    {
        var details = new PartyDetails(new Party("alice::1220"), IsLocal: true);
        var client = FakeAdminClient.Create().WithAllocatedParty("alice", details).Build();

        var result = await client.AllocatePartyAsync("alice", cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be(details);
    }

    [Fact]
    public async Task AllocatePartyAsync_for_unstaged_hint_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create()
            .WithAllocatedParty("alice", new PartyDetails(new Party("alice::1220"), true))
            .Build();

        var act = () => client.AllocatePartyAsync("bob");

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithAllocatedParty").And.Contain("bob");
    }

    [Fact]
    public async Task GetPartiesAsync_returns_the_staged_parties_matching_the_requested_ids()
    {
        var alice = new PartyDetails(new Party("alice"), true);
        var bob = new PartyDetails(new Party("bob"), true);
        var client = FakeAdminClient.Create().WithParties(alice, bob).Build();

        var result = await client.GetPartiesAsync([new Party("bob")], TestContext.Current.CancellationToken);

        result.Should().ContainSingle().Which.Should().Be(bob);
    }

    [Fact]
    public async Task ListKnownPartiesAsync_returns_all_staged_parties()
    {
        var alice = new PartyDetails(new Party("alice"), true);
        var bob = new PartyDetails(new Party("bob"), true);
        var client = FakeAdminClient.Create().WithParties(alice, bob).Build();

        var result = await client.ListKnownPartiesAsync(cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Equal(alice, bob);
    }

    [Fact]
    public async Task ListKnownPartiesAsync_for_unstaged_client_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.ListKnownPartiesAsync();

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithParties");
    }

    [Fact]
    public async Task GetPartiesAsync_for_unstaged_client_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.GetPartiesAsync([new Party("alice")]);

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithParties");
    }

    [Fact]
    public async Task ListKnownPartiesAsync_staged_with_zero_parties_returns_empty()
    {
        var client = FakeAdminClient.Create().WithParties().Build();

        var result = await client.ListKnownPartiesAsync(cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserAsync_returns_the_staged_UserDetails()
    {
        var user = new UserDetails("alice", new Party("alice"));
        var client = FakeAdminClient.Create().WithUser(user).Build();

        var result = await client.GetUserAsync("alice", TestContext.Current.CancellationToken);

        result.Should().Be(user);
    }

    [Fact]
    public async Task GetUserAsync_returns_null_for_unstaged_userId()
    {
        var client = FakeAdminClient.Create().WithUser(new UserDetails("alice", new Party("alice"))).Build();

        var result = await client.GetUserAsync("bob", TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateUserAsync_returns_the_staged_UserDetails_for_the_userId()
    {
        var user = new UserDetails("alice", new Party("alice"));
        var client = FakeAdminClient.Create().WithUser(user).Build();

        var result = await client.CreateUserAsync("alice", new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be(user);
    }

    [Fact]
    public async Task CreateUserAsync_without_a_primary_party_returns_the_staged_UserDetails()
    {
        var admin = new UserDetails("participant_admin", null);
        var client = FakeAdminClient.Create().WithUser(admin).Build();

        var result = await client.CreateUserAsync(
            "participant_admin", null, cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be(new UserDetails("participant_admin", null));
    }

    [Fact]
    public async Task CreateUserAsync_for_unstaged_userId_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.CreateUserAsync("alice", new Party("alice"));

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithUser").And.Contain("alice");
    }

    [Fact]
    public async Task ListUsersAsync_returns_all_staged_users()
    {
        var alice = new UserDetails("alice", new Party("alice"));
        var bob = new UserDetails("bob", new Party("bob"));
        var client = FakeAdminClient.Create().WithUsers(alice, bob).Build();

        var result = await client.ListUsersAsync(cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Equal(alice, bob);
    }

    [Fact]
    public async Task ListUsersAsync_for_unstaged_client_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.ListUsersAsync();

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithUsers");
    }

    [Fact]
    public async Task ListUserRightsAsync_returns_the_staged_rights()
    {
        var rights = new UserRight[] { new UserRight.ParticipantAdmin() };
        var client = FakeAdminClient.Create().WithUserRights("alice", rights).Build();

        var result = await client.ListUserRightsAsync("alice", TestContext.Current.CancellationToken);

        result.Should().Equal(rights);
    }

    [Fact]
    public async Task ListUserRightsAsync_returns_null_for_unstaged_userId()
    {
        var client = FakeAdminClient.Create().WithUserRights("alice", new UserRight.ParticipantAdmin()).Build();

        var result = await client.ListUserRightsAsync("bob", TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GrantUserRightsAsync_succeeds_without_any_staging()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.GrantUserRightsAsync("alice", [new UserRight.ParticipantAdmin()]);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RevokeUserRightsAsync_succeeds_without_any_staging()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.RevokeUserRightsAsync("alice", [new UserRight.ParticipantAdmin()]);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ListKnownPackagesAsync_returns_all_staged_packages()
    {
        var package = new PackageDetails("pkg1", "name", "1.0.0", 123L, DateTimeOffset.UnixEpoch);
        var client = FakeAdminClient.Create().WithKnownPackages(package).Build();

        var result = await client.ListKnownPackagesAsync(TestContext.Current.CancellationToken);

        result.Should().Equal(package);
    }

    [Fact]
    public async Task ListKnownPackagesAsync_for_unstaged_client_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.ListKnownPackagesAsync();

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithKnownPackages");
    }

    [Fact]
    public async Task GetPackageAsync_returns_the_staged_PackageArchive()
    {
        var archive = new PackageArchive(new byte[] { 1, 2, 3 }, "hash1", HashFunction.Sha256);
        var client = FakeAdminClient.Create().WithPackage("pkg1", archive).Build();

        var result = await client.GetPackageAsync("pkg1", TestContext.Current.CancellationToken);

        result.Should().Be(archive);
    }

    [Fact]
    public async Task GetPackageAsync_for_unstaged_packageId_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.GetPackageAsync("pkg1");

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithPackage").And.Contain("pkg1");
    }

    [Fact]
    public async Task ListVettedPackagesAsync_with_no_prefixes_returns_all_staged_packages()
    {
        var alpha = new VettedPackage("pkg1", "alpha-service", "1.0.0", "participant1", new SynchronizerId("sync1"));
        var beta = new VettedPackage("pkg2", "beta-service", "1.0.0", "participant1", new SynchronizerId("sync1"));
        var client = FakeAdminClient.Create().WithVettedPackages(alpha, beta).Build();

        var result = await client.ListVettedPackagesAsync(cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Equal(alpha, beta);
    }

    [Fact]
    public async Task ListVettedPackagesAsync_filters_by_package_name_prefix()
    {
        var alpha = new VettedPackage("pkg1", "alpha-service", "1.0.0", "participant1", new SynchronizerId("sync1"));
        var beta = new VettedPackage("pkg2", "beta-service", "1.0.0", "participant1", new SynchronizerId("sync1"));
        var client = FakeAdminClient.Create().WithVettedPackages(alpha, beta).Build();

        var result = await client.ListVettedPackagesAsync(["alpha"], TestContext.Current.CancellationToken);

        result.Should().ContainSingle().Which.Should().Be(alpha);
    }

    [Fact]
    public async Task ListVettedPackagesAsync_for_unstaged_client_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.ListVettedPackagesAsync();

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithVettedPackages");
    }

    [Fact]
    public async Task UploadDarAsync_succeeds_without_any_staging()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.UploadDarAsync([1, 2, 3]);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateDarAsync_succeeds_without_any_staging()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.ValidateDarAsync([1, 2, 3]);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Build_snapshots_staged_users_so_later_builder_mutation_is_ignored()
    {
        var builder = FakeAdminClient.Create().WithUser(new UserDetails("alice", new Party("alice")));
        var client = builder.Build();
        builder.WithUser(new UserDetails("bob", new Party("bob")));

        var alice = await client.GetUserAsync("alice", TestContext.Current.CancellationToken);
        var bob = await client.GetUserAsync("bob", TestContext.Current.CancellationToken);

        alice.Should().NotBeNull();
        bob.Should().BeNull();
    }

    [Fact]
    public async Task ListPackagesAsync_returns_the_staged_package_ids()
    {
        var client = FakeAdminClient.Create().WithPackageIds("pkg-a", "pkg-b").Build();

        var ids = await client.ListPackagesAsync(TestContext.Current.CancellationToken);

        ids.Should().Equal("pkg-a", "pkg-b");
    }

    [Fact]
    public async Task ListPackagesAsync_unstaged_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.ListPackagesAsync();

        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("WithPackageIds");
    }

    [Fact]
    public async Task GetPackageStatusAsync_returns_the_staged_status()
    {
        var client = FakeAdminClient.Create().WithPackageStatus("pkg-a", PackageStatus.Registered).Build();

        var status = await client.GetPackageStatusAsync("pkg-a", TestContext.Current.CancellationToken);

        status.Should().Be(PackageStatus.Registered);
    }

    [Fact]
    public async Task GetPackageStatusAsync_for_an_unstaged_package_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().WithPackageStatus("pkg-a", PackageStatus.Registered).Build();

        var act = () => client.GetPackageStatusAsync("pkg-b");

        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("pkg-b").And.Contain("WithPackageStatus");
    }

    [Fact]
    public async Task GetTimeAsync_returns_the_staged_time()
    {
        var time = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var client = FakeAdminClient.Create().WithTime(time).Build();

        var result = await client.GetTimeAsync(TestContext.Current.CancellationToken);

        result.Should().Be(time);
    }

    [Fact]
    public async Task SetTimeAsync_advances_the_staged_time()
    {
        var start = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var later = start.AddHours(1);
        var client = FakeAdminClient.Create().WithTime(start).Build();

        await client.SetTimeAsync(start, later, TestContext.Current.CancellationToken);

        (await client.GetTimeAsync(TestContext.Current.CancellationToken)).Should().Be(later);
    }

    [Fact]
    public async Task SetTimeAsync_with_a_stale_current_time_throws_LedgerOperationException()
    {
        var start = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var client = FakeAdminClient.Create().WithTime(start).Build();

        var act = () => client.SetTimeAsync(start.AddMinutes(1), start.AddHours(1));

        await act.Should().ThrowAsync<Daml.Ledger.Abstractions.LedgerOperationException>();
    }

    [Fact]
    public async Task SetTimeAsync_with_a_new_time_not_later_throws_LedgerOperationException()
    {
        var start = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var client = FakeAdminClient.Create().WithTime(start).Build();

        var act = () => client.SetTimeAsync(start, start);

        await act.Should().ThrowAsync<Daml.Ledger.Abstractions.LedgerOperationException>();
    }

    [Fact]
    public async Task GetTimeAsync_unstaged_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.GetTimeAsync();

        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("WithTime");
    }

    private static ExternalPartyTopology Topology() => new(
        new Party("ext::1220ab"), "1220ab", [new byte[] { 1 }, new byte[] { 2 }], new byte[] { 3 });

    private static ExternalPartyTopologyRequest TopologyRequest() => new(
        new SynchronizerId("sync-1"),
        "ext",
        new SigningPublicKey(PublicKeyFormat.DerX509SubjectPublicKeyInfo, new byte[] { 7 }, SigningKeySpec.EcP256));

    private static ExternalPartyAllocation Allocation() => new(new SynchronizerId("sync-1"), [], []);

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_returns_the_staged_topology()
    {
        var topology = Topology();
        var client = FakeAdminClient.Create().WithExternalPartyTopology(topology).Build();

        var result = await client.GenerateExternalPartyTopologyAsync(TopologyRequest(), TestContext.Current.CancellationToken);

        result.Should().BeSameAs(topology);
    }

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_without_a_staged_topology_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.GenerateExternalPartyTopologyAsync(TopologyRequest());

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithExternalPartyTopology");
    }

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_rejects_a_null_request()
    {
        var client = FakeAdminClient.Create().WithExternalPartyTopology(Topology()).Build();

        var act = () => client.GenerateExternalPartyTopologyAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task AllocateExternalPartyAsync_returns_the_staged_party()
    {
        var client = FakeAdminClient.Create().WithAllocatedExternalParty(new Party("ext::1220ab")).Build();

        var result = await client.AllocateExternalPartyAsync(Allocation(), TestContext.Current.CancellationToken);

        result.Should().Be(new Party("ext::1220ab"));
    }

    [Fact]
    public async Task AllocateExternalPartyAsync_without_a_staged_party_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.AllocateExternalPartyAsync(Allocation());

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithAllocatedExternalParty");
    }

    [Fact]
    public async Task AllocateExternalPartyAsync_rejects_a_null_allocation()
    {
        var client = FakeAdminClient.Create().WithAllocatedExternalParty(new Party("ext::1220ab")).Build();

        var act = () => client.AllocateExternalPartyAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}

public class FakeAdminClientUpdateTests
{
    [Fact]
    public async Task UpdateUserAsync_returns_the_staged_user()
    {
        var user = new UserDetails("alice", new Party("alice::1220"));
        var client = FakeAdminClient.Create().WithUser(user).Build();

        var result = await client.UpdateUserAsync(
            "alice", new UserUpdate { IsDeactivated = true }, cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be(user);
    }

    [Fact]
    public async Task UpdateUserAsync_for_an_unstaged_user_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.UpdateUserAsync("alice", new UserUpdate { IsDeactivated = true });

        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("WithUser");
    }

    [Fact]
    public async Task UpdateUserAsync_rejects_an_update_that_changes_nothing()
    {
        var client = FakeAdminClient.Create().WithUser(new UserDetails("alice", null)).Build();

        var act = () => client.UpdateUserAsync("alice", new UserUpdate());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpdatePartyDetailsAsync_returns_the_staged_known_party()
    {
        var details = new PartyDetails(new Party("alice::1220"), IsLocal: true);
        var client = FakeAdminClient.Create().WithParties(details).Build();

        var result = await client.UpdatePartyDetailsAsync(
            details.Party,
            new PartyUpdate { Annotations = new Dictionary<string, string>() },
            cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be(details);
    }

    [Fact]
    public async Task GetCommandStatusAsync_filters_the_staged_statuses_by_prefix_state_and_limit()
    {
        var pendingOne = new CommandStatus("cmd-1", CommandState.Pending, null, null, "sync");
        var pendingTwo = new CommandStatus("cmd-2", CommandState.Pending, null, null, "sync");
        var failed = new CommandStatus("cmd-3", CommandState.Failed, null, null, "sync");
        var other = new CommandStatus("other-1", CommandState.Pending, null, null, "sync");
        var client = FakeAdminClient.Create().WithCommandStatuses(pendingOne, pendingTwo, failed, other).Build();

        var result = await client.GetCommandStatusAsync(
            "cmd-", CommandState.Pending, 1, TestContext.Current.CancellationToken);

        result.Should().Equal(pendingOne);
    }

    [Fact]
    public async Task GetCommandStatusAsync_for_unstaged_client_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.GetCommandStatusAsync();

        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("WithCommandStatuses");
    }

    [Fact]
    public async Task DeleteUserAsync_completes_without_staging()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.DeleteUserAsync("alice");

        await act.Should().NotThrowAsync();
    }
}

public class FakeAdminClientIdpVettingTests
{
    private static readonly IdentityProviderConfig Config = new("idp-1", false, "https://issuer.invalid", "https://issuer.invalid/jwks", "aud");

    [Fact]
    public async Task IdentityProviderConfigs_are_served_from_the_staged_list()
    {
        var client = FakeAdminClient.Create().WithIdentityProviderConfigs(Config).Build();
        var cancellationToken = TestContext.Current.CancellationToken;

        (await client.ListIdentityProviderConfigsAsync(cancellationToken)).Should().Equal(Config);
        (await client.GetIdentityProviderConfigAsync("idp-1", cancellationToken)).Should().Be(Config);
        (await client.GetIdentityProviderConfigAsync("other", cancellationToken)).Should().BeNull();
        (await client.CreateIdentityProviderConfigAsync(Config, cancellationToken)).Should().Be(Config);
        (await client.UpdateIdentityProviderConfigAsync(
            "idp-1", new IdentityProviderConfigUpdate { Audience = "x" }, cancellationToken)).Should().Be(Config);
    }

    [Fact]
    public async Task ListIdentityProviderConfigsAsync_for_unstaged_client_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.ListIdentityProviderConfigsAsync();

        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("WithIdentityProviderConfigs");
    }

    [Fact]
    public async Task UpdateVettedPackagesAsync_returns_the_staged_result()
    {
        var result = new VettedPackagesUpdateResult(null, new VettedPackagesSnapshot([], "participant", "sync", 2));
        var client = FakeAdminClient.Create().WithVettedPackagesUpdateResult(result).Build();

        var returned = await client.UpdateVettedPackagesAsync([], cancellationToken: TestContext.Current.CancellationToken);

        returned.Should().Be(result);
    }

    [Fact]
    public async Task UpdateVettedPackagesAsync_for_unstaged_client_throws_descriptive_NotSupportedException()
    {
        var client = FakeAdminClient.Create().Build();

        var act = () => client.UpdateVettedPackagesAsync([]);

        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("WithVettedPackagesUpdateResult");
    }

    [Fact]
    public async Task PruneAsync_and_DeleteIdentityProviderConfigAsync_complete_without_staging()
    {
        var client = FakeAdminClient.Create().Build();

        var prune = () => client.PruneAsync(1);
        var delete = () => client.DeleteIdentityProviderConfigAsync("idp-1");

        await prune.Should().NotThrowAsync();
        await delete.Should().NotThrowAsync();
    }
}
