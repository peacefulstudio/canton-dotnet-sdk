// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime.Data;

namespace Canton.Ledger.Testing;

/// <summary>
/// An in-memory <see cref="IAdminClient"/> test double that serves canned participant, party,
/// user, and package data staged through the fluent <see cref="FakeAdminClientBuilder"/>. It lets
/// business logic that talks to the admin client be unit-tested without a live participant and
/// without a mocking framework.
/// </summary>
/// <remarks>
/// A query-style member you did not stage throws a descriptive <see cref="NotSupportedException"/>
/// naming the missing setup, so a test never silently exercises unconfigured behaviour. The
/// mutation-only members with no return payload — <see cref="GrantUserRightsAsync"/>,
/// <see cref="RevokeUserRightsAsync"/>, <see cref="UploadDarAsync(byte[], string?, CancellationToken)"/>,
/// <see cref="ValidateDarAsync(byte[], CancellationToken)"/>, <see cref="DeleteUserAsync"/>,
/// <see cref="UpdateUserIdentityProviderIdAsync"/>, <see cref="UpdatePartyIdentityProviderIdAsync"/>, <see cref="DeleteIdentityProviderConfigAsync"/>, <see cref="PruneAsync"/>
/// — are unconditional no-op successes instead, the same way <see cref="FakeLedgerClient.Dispose"/>
/// is: there is no return value to fake, so requiring staging first would add ceremony without
/// adding safety. <see cref="CreateUserAsync"/> and <see cref="AllocatePartyAsync"/> sit between
/// these two shapes: they do have a return payload, but rather than echoing it from the call
/// arguments they look up and replay the matching staged <see cref="UserDetails"/> / <see cref="PartyDetails"/>
/// verbatim, ignoring <c>primaryParty</c>/<c>rights</c> and <c>synchronizerId</c> respectively — stage
/// the exact result you expect back under the same id/hint rather than relying on the arguments a test's
/// system under test happens to pass in. <see cref="AllocatePartyAsync"/> in particular does not model
/// the multi-synchronizer allocation failure a live participant surfaces when <c>synchronizerId</c> is
/// omitted; a test exercising that path needs a different approach. The per-id stages
/// (<see cref="AllocatePartyAsync"/>'s and <see cref="CreateUserAsync"/>'s) are also independent from
/// the list-style stages (<see cref="ListKnownPartiesAsync"/>/<see cref="GetPartiesAsync"/> and
/// <see cref="ListUsersAsync"/>): staging an allocated party or a user does not automatically make it
/// appear in the corresponding known-parties/known-users list — unlike a live participant, where
/// allocating a party or creating a user makes it listable. Stage both sides explicitly if your test
/// exercises an allocate/create-then-list flow. Construct instances through <see cref="Create"/>.
/// </remarks>
public sealed partial class FakeAdminClient : IAdminClient
{
    private readonly string? _participantId;
    private readonly IReadOnlyDictionary<string, PartyDetails> _allocatedParties;
    private readonly IReadOnlyList<PartyDetails>? _knownParties;
    private readonly IReadOnlyDictionary<string, UserDetails> _users;
    private readonly IReadOnlyList<UserDetails>? _knownUsers;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<UserRight>> _userRights;
    private readonly IReadOnlyList<PackageDetails>? _knownPackages;
    private readonly IReadOnlyDictionary<string, PackageArchive> _packages;
    private readonly IReadOnlyList<VettedPackage>? _vettedPackages;
    private readonly IReadOnlyList<CommandStatus>? _commandStatuses;
    private readonly IReadOnlyList<IdentityProviderConfig>? _identityProviderConfigs;
    private readonly VettedPackagesUpdateResult? _vettedPackagesUpdateResult;
    private readonly ExternalPartyTopology? _externalPartyTopology;
    private readonly Party? _allocatedExternalParty;

    internal FakeAdminClient(
        string? participantId,
        IReadOnlyDictionary<string, PartyDetails> allocatedParties,
        IReadOnlyList<PartyDetails>? knownParties,
        IReadOnlyDictionary<string, UserDetails> users,
        IReadOnlyList<UserDetails>? knownUsers,
        IReadOnlyDictionary<string, IReadOnlyList<UserRight>> userRights,
        IReadOnlyList<PackageDetails>? knownPackages,
        IReadOnlyDictionary<string, PackageArchive> packages,
        IReadOnlyList<VettedPackage>? vettedPackages,
        IReadOnlyList<CommandStatus>? commandStatuses,
        IReadOnlyList<IdentityProviderConfig>? identityProviderConfigs,
        VettedPackagesUpdateResult? vettedPackagesUpdateResult,
        ExternalPartyTopology? externalPartyTopology,
        Party? allocatedExternalParty)
    {
        _participantId = participantId;
        _allocatedParties = allocatedParties;
        _knownParties = knownParties;
        _users = users;
        _knownUsers = knownUsers;
        _userRights = userRights;
        _knownPackages = knownPackages;
        _packages = packages;
        _vettedPackages = vettedPackages;
        _commandStatuses = commandStatuses;
        _identityProviderConfigs = identityProviderConfigs;
        _vettedPackagesUpdateResult = vettedPackagesUpdateResult;
        _externalPartyTopology = externalPartyTopology;
        _allocatedExternalParty = allocatedExternalParty;
    }

    /// <summary>Starts a new fluent builder for a <see cref="FakeAdminClient"/>.</summary>
    /// <returns>An empty builder; stage data on it, then call <see cref="FakeAdminClientBuilder.Build"/>.</returns>
    public static FakeAdminClientBuilder Create() => new();

    /// <inheritdoc />
    public Task<string> GetParticipantIdAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_participantId ?? throw StagingMissing(
            "participant id", "one", "WithParticipantId(...)"));

    /// <inheritdoc />
    public Task<PartyDetails> AllocatePartyAsync(
        string partyIdHint,
        SynchronizerId? synchronizerId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(partyIdHint);

        if (_allocatedParties.TryGetValue(partyIdHint, out var details))
        {
            return Task.FromResult(details);
        }

        throw StagingMissing(
            "allocated party", "one", $"WithAllocatedParty(\"{partyIdHint}\", ...)",
            $" for party id hint '{partyIdHint}'");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PartyDetails>> GetPartiesAsync(
        IEnumerable<Party> parties,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parties);
        var requested = new HashSet<Party>(parties);
        return Task.FromResult<IReadOnlyList<PartyDetails>>(
            KnownParties().Where(p => requested.Contains(p.Party)).ToList());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PartyDetails>> ListKnownPartiesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(KnownParties());

    /// <inheritdoc />
    public Task<UserDetails> CreateUserAsync(
        string userId,
        Party? primaryParty,
        IEnumerable<UserRight>? rights = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (_users.TryGetValue(userId, out var details))
        {
            return Task.FromResult(details);
        }

        throw StagingMissing(
            "user", "one", $"WithUser(new UserDetails(\"{userId}\", ...))", $" for user id '{userId}'");
    }

    /// <inheritdoc />
    public Task<UserDetails?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);
        return Task.FromResult(_users.TryGetValue(userId, out var details) ? details : null);
    }

    /// <inheritdoc />
    public Task GrantUserRightsAsync(
        string userId,
        IEnumerable<UserRight> rights,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(rights);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RevokeUserRightsAsync(
        string userId,
        IEnumerable<UserRight> rights,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(rights);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UserRight>?> ListUserRightsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);
        return Task.FromResult(_userRights.TryGetValue(userId, out var rights) ? rights : null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UserDetails>> ListUsersAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_knownUsers as IReadOnlyList<UserDetails> ?? throw StagingMissing(
            "known users", "them", "WithUsers(...)"));

    /// <inheritdoc />
    public Task<UserDetails> UpdateUserAsync(
        string userId,
        UserUpdate update,
        string? identityProviderId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(update);
        _ = update.UpdatePaths();

        if (_users.TryGetValue(userId, out var details))
        {
            return Task.FromResult(details);
        }

        throw StagingMissing(
            "user", "one", $"WithUser(new UserDetails(\"{userId}\", ...))", $" for user id '{userId}'");
    }

    /// <inheritdoc />
    public Task DeleteUserAsync(
        string userId,
        string? identityProviderId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateUserIdentityProviderIdAsync(
        string userId,
        string? sourceIdentityProviderId,
        string? targetIdentityProviderId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PartyDetails> UpdatePartyDetailsAsync(
        Party party,
        PartyUpdate update,
        string? identityProviderId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        _ = update.UpdatePaths();

        return Task.FromResult(KnownParties().FirstOrDefault(details => details.Party == party)
            ?? throw StagingMissing(
                "known party", "it", "WithParties(...)", $" for party '{party.Value}'"));
    }

    /// <inheritdoc />
    public Task UpdatePartyIdentityProviderIdAsync(
        Party party,
        string? sourceIdentityProviderId,
        string? targetIdentityProviderId,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task<IReadOnlyList<CommandStatus>> GetCommandStatusAsync(
        string commandIdPrefix = "",
        CommandState state = CommandState.Unspecified,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandIdPrefix);

        var statuses = _commandStatuses ?? throw StagingMissing(
            "command statuses", "them", "WithCommandStatuses(...)");
        var matching = statuses
            .Where(status => status.CommandId.StartsWith(commandIdPrefix, StringComparison.Ordinal))
            .Where(status => state == CommandState.Unspecified || status.State == state);
        return Task.FromResult<IReadOnlyList<CommandStatus>>(
            (limit is > 0 ? matching.Take(limit.Value) : matching).ToList());
    }

    /// <inheritdoc />
    public Task<IdentityProviderConfig> CreateIdentityProviderConfigAsync(
        IdentityProviderConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        return Task.FromResult(StagedIdentityProviderConfig(config.IdentityProviderId));
    }

    /// <inheritdoc />
    public Task<IdentityProviderConfig?> GetIdentityProviderConfigAsync(
        string identityProviderId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityProviderId);
        return Task.FromResult(StagedIdentityProviderConfigs()
            .FirstOrDefault(config => config.IdentityProviderId == identityProviderId));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IdentityProviderConfig>> ListIdentityProviderConfigsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StagedIdentityProviderConfigs());

    /// <inheritdoc />
    public Task<IdentityProviderConfig> UpdateIdentityProviderConfigAsync(
        string identityProviderId,
        IdentityProviderConfigUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityProviderId);
        ArgumentNullException.ThrowIfNull(update);
        _ = update.UpdatePaths();
        return Task.FromResult(StagedIdentityProviderConfig(identityProviderId));
    }

    /// <inheritdoc />
    public Task DeleteIdentityProviderConfigAsync(
        string identityProviderId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityProviderId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<VettedPackagesUpdateResult> UpdateVettedPackagesAsync(
        IReadOnlyList<VettedPackagesChange> changes,
        bool dryRun = false,
        SynchronizerId? synchronizerId = null,
        ExpectedTopologySerial? expectedTopologySerial = null,
        VettingOverrides safetyOverrides = VettingOverrides.None,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return Task.FromResult(_vettedPackagesUpdateResult ?? throw StagingMissing(
            "vetted packages update result", "it", "WithVettedPackagesUpdateResult(...)"));
    }

    /// <inheritdoc />
    public Task PruneAsync(
        long pruneUpTo,
        string? submissionId = null,
        bool pruneAllDivulgedContracts = false,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    private IReadOnlyList<IdentityProviderConfig> StagedIdentityProviderConfigs() =>
        _identityProviderConfigs ?? throw StagingMissing(
            "identity provider configs", "them", "WithIdentityProviderConfigs(...)");

    private IdentityProviderConfig StagedIdentityProviderConfig(string identityProviderId) =>
        StagedIdentityProviderConfigs().FirstOrDefault(config => config.IdentityProviderId == identityProviderId)
        ?? throw StagingMissing(
            "identity provider config", "one", "WithIdentityProviderConfigs(...)", $" for id '{identityProviderId}'");

    /// <inheritdoc />
    public Task<IReadOnlyList<PackageDetails>> ListKnownPackagesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_knownPackages as IReadOnlyList<PackageDetails> ?? throw StagingMissing(
            "known packages", "them", "WithKnownPackages(...)"));

    /// <inheritdoc />
    public Task<PackageArchive> GetPackageAsync(string packageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        if (_packages.TryGetValue(packageId, out var archive))
        {
            return Task.FromResult(archive);
        }

        throw StagingMissing(
            "package archive", "one", $"WithPackage(\"{packageId}\", ...)", $" for package id '{packageId}'");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<VettedPackage>> ListVettedPackagesAsync(
        IEnumerable<string>? packageNamePrefixes = null,
        CancellationToken cancellationToken = default)
    {
        if (_vettedPackages is null)
        {
            throw StagingMissing("vetted packages", "them", "WithVettedPackages(...)");
        }

        var prefixes = packageNamePrefixes?.ToList();
        if (prefixes is not { Count: > 0 })
        {
            return Task.FromResult<IReadOnlyList<VettedPackage>>(_vettedPackages);
        }

        return Task.FromResult<IReadOnlyList<VettedPackage>>(
            _vettedPackages.Where(p => prefixes.Any(prefix => p.PackageName.StartsWith(prefix, StringComparison.Ordinal))).ToList());
    }

    /// <inheritdoc />
    public Task UploadDarAsync(
        byte[] darFile,
        string? submissionId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrEmpty(darFile);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UploadDarAsync(
        byte[] darFile,
        SynchronizerId synchronizerId,
        string? submissionId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrEmpty(darFile);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ValidateDarAsync(
        byte[] darFile,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrEmpty(darFile);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ValidateDarAsync(
        byte[] darFile,
        SynchronizerId synchronizerId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrEmpty(darFile);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ExternalPartyTopology> GenerateExternalPartyTopologyAsync(
        ExternalPartyTopologyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(_externalPartyTopology ?? throw StagingMissing(
            "external party topology", "one", "WithExternalPartyTopology(...)"));
    }

    /// <inheritdoc />
    public Task<Party> AllocateExternalPartyAsync(
        ExternalPartyAllocation allocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        return Task.FromResult(_allocatedExternalParty ?? throw StagingMissing(
            "allocated external party", "one", "WithAllocatedExternalParty(...)"));
    }

    private static void ThrowIfNullOrEmpty(byte[] darFile)
    {
        ArgumentNullException.ThrowIfNull(darFile);
        if (darFile.Length == 0)
            throw new ArgumentException("DAR file must not be empty.", nameof(darFile));
    }

    private IReadOnlyList<PartyDetails> KnownParties() =>
        _knownParties ?? throw StagingMissing("known parties", "them", "WithParties(...)");

    private static NotSupportedException StagingMissing(
        string what, string stageVerb, string builderCall, string context = "") =>
        new($"FakeAdminClient has no {what} staged{context}. Stage {stageVerb} with " +
            $"FakeAdminClient.Create().{builderCall}.Build() before exercising this path.");
}
