// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime.Data;

namespace Canton.Ledger.Testing;

/// <summary>
/// Fluent builder that stages the participant/party/user/package data a
/// <see cref="FakeAdminClient"/> serves. Obtain one from <see cref="FakeAdminClient.Create"/>,
/// chain <c>With*</c> calls, then call <see cref="Build"/>.
/// </summary>
public sealed class FakeAdminClientBuilder
{
    private string? _participantId;
    private readonly Dictionary<string, PartyDetails> _allocatedParties = [];
    private PartyDetails[]? _knownParties;
    private readonly Dictionary<string, UserDetails> _users = [];
    private UserDetails[]? _knownUsers;
    private readonly Dictionary<string, IReadOnlyList<UserRight>> _userRights = [];
    private PackageDetails[]? _knownPackages;
    private readonly Dictionary<string, PackageArchive> _packages = [];
    private VettedPackage[]? _vettedPackages;
    private CommandStatus[]? _commandStatuses;
    private IdentityProviderConfig[]? _identityProviderConfigs;
    private VettedPackagesUpdateResult? _vettedPackagesUpdateResult;
    private ExternalPartyTopology? _externalPartyTopology;
    private Party? _allocatedExternalParty;
    private string[]? _packageIds;
    private readonly Dictionary<string, PackageStatus> _packageStatuses = [];
    private DateTimeOffset? _time;

    /// <summary>Stages the participant id that <see cref="FakeAdminClient.GetParticipantIdAsync"/> returns.</summary>
    /// <returns>The same builder, for chaining.</returns>
    public FakeAdminClientBuilder WithParticipantId(string participantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(participantId);
        _participantId = participantId;
        return this;
    }

    /// <summary>
    /// Stages the <see cref="PartyDetails"/> that <see cref="FakeAdminClient.AllocatePartyAsync"/>
    /// returns for the given <paramref name="partyIdHint"/>.
    /// </summary>
    /// <returns>The same builder, for chaining.</returns>
    public FakeAdminClientBuilder WithAllocatedParty(string partyIdHint, PartyDetails details)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partyIdHint);
        ArgumentNullException.ThrowIfNull(details);
        _allocatedParties[partyIdHint] = details;
        return this;
    }

    /// <summary>
    /// Stages the known parties that back both <see cref="FakeAdminClient.GetPartiesAsync"/>
    /// (filtered to the requested party ids) and <see cref="FakeAdminClient.ListKnownPartiesAsync"/>
    /// (returned in full). Call with no arguments to explicitly stage "no known parties".
    /// </summary>
    /// <returns>The same builder, for chaining.</returns>
    public FakeAdminClientBuilder WithParties(params PartyDetails[] parties)
    {
        ArgumentNullException.ThrowIfNull(parties);
        _knownParties = parties.ToArray();
        return this;
    }

    /// <summary>
    /// Stages a <see cref="UserDetails"/> that <see cref="FakeAdminClient.GetUserAsync"/> and
    /// <see cref="FakeAdminClient.CreateUserAsync"/> return, keyed by <see cref="UserDetails.UserId"/>.
    /// </summary>
    /// <returns>The same builder, for chaining.</returns>
    public FakeAdminClientBuilder WithUser(UserDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        _users[details.UserId] = details;
        return this;
    }

    /// <summary>
    /// Stages the full list <see cref="FakeAdminClient.ListUsersAsync"/> returns. Call with no
    /// arguments to explicitly stage "no known users".
    /// </summary>
    /// <returns>The same builder, for chaining.</returns>
    public FakeAdminClientBuilder WithUsers(params UserDetails[] users)
    {
        ArgumentNullException.ThrowIfNull(users);
        _knownUsers = users.ToArray();
        return this;
    }

    /// <summary>
    /// Stages the rights that <see cref="FakeAdminClient.ListUserRightsAsync"/> returns for
    /// <paramref name="userId"/>. Call with no rights to stage a user with zero granted rights
    /// (distinct from an unstaged <paramref name="userId"/>, which returns <see langword="null"/>).
    /// </summary>
    /// <returns>The same builder, for chaining.</returns>
    public FakeAdminClientBuilder WithUserRights(string userId, params UserRight[] rights)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(rights);
        _userRights[userId] = rights.ToArray();
        return this;
    }

    /// <summary>
    /// Stages the full list <see cref="FakeAdminClient.ListKnownPackagesAsync"/> returns. Call
    /// with no arguments to explicitly stage "no known packages".
    /// </summary>
    /// <returns>The same builder, for chaining.</returns>
    public FakeAdminClientBuilder WithKnownPackages(params PackageDetails[] packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        _knownPackages = packages.ToArray();
        return this;
    }

    /// <summary>
    /// Stages the <see cref="PackageArchive"/> that <see cref="FakeAdminClient.GetPackageAsync"/>
    /// returns for <paramref name="packageId"/>.
    /// </summary>
    /// <returns>The same builder, for chaining.</returns>
    public FakeAdminClientBuilder WithPackage(string packageId, PackageArchive archive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(archive);
        _packages[packageId] = archive;
        return this;
    }

    /// <summary>
    /// Stages the vetted packages that <see cref="FakeAdminClient.ListVettedPackagesAsync"/>
    /// filters by package-name prefix. Call with no arguments to explicitly stage "no vetted
    /// packages".
    /// </summary>
    /// <returns>The same builder, for chaining.</returns>
    public FakeAdminClientBuilder WithVettedPackages(params VettedPackage[] packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        _vettedPackages = packages.ToArray();
        return this;
    }

    /// <summary>Stages the command statuses <see cref="FakeAdminClient.GetCommandStatusAsync"/> filters and serves.</summary>
    /// <param name="statuses">The statuses to serve, in order.</param>
    /// <returns>This builder.</returns>
    public FakeAdminClientBuilder WithCommandStatuses(params CommandStatus[] statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        _commandStatuses = statuses.ToArray();
        return this;
    }

    /// <summary>
    /// Stages the package ids <see cref="FakeAdminClient.ListPackagesAsync"/> returns. Call with no
    /// arguments to explicitly stage "no packages".
    /// </summary>
    /// <returns>The same builder, for chaining.</returns>
    public FakeAdminClientBuilder WithPackageIds(params string[] packageIds)
    {
        ArgumentNullException.ThrowIfNull(packageIds);
        _packageIds = packageIds.ToArray();
        return this;
    }

    /// <summary>
    /// Stages the topology <see cref="FakeAdminClient.GenerateExternalPartyTopologyAsync"/> returns
    /// for every request.
    /// </summary>
    /// <returns>The same builder, for chaining.</returns>
    public FakeAdminClientBuilder WithExternalPartyTopology(ExternalPartyTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);
        _externalPartyTopology = topology;
        return this;
    }

    /// <summary>
    /// Stages the <see cref="PackageStatus"/> that <see cref="FakeAdminClient.GetPackageStatusAsync"/>
    /// returns for <paramref name="packageId"/>.
    /// </summary>
    /// <returns>The same builder, for chaining.</returns>
    public FakeAdminClientBuilder WithPackageStatus(string packageId, PackageStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        _packageStatuses[packageId] = status;
        return this;
    }

    /// <summary>
    /// Stages the identity provider configurations <see cref="FakeAdminClient.ListIdentityProviderConfigsAsync"/> lists and
    /// <see cref="FakeAdminClient.GetIdentityProviderConfigAsync"/>, <see cref="FakeAdminClient.CreateIdentityProviderConfigAsync"/>
    /// and <see cref="FakeAdminClient.UpdateIdentityProviderConfigAsync"/> replay by id.
    /// </summary>
    /// <param name="configs">The configurations to serve.</param>
    /// <returns>This builder.</returns>
    public FakeAdminClientBuilder WithIdentityProviderConfigs(params IdentityProviderConfig[] configs)
    {
        ArgumentNullException.ThrowIfNull(configs);
        _identityProviderConfigs = configs.ToArray();
        return this;
    }

    /// <summary>Stages the result <see cref="FakeAdminClient.UpdateVettedPackagesAsync"/> returns.</summary>
    /// <param name="result">The result to serve.</param>
    /// <returns>This builder.</returns>
    public FakeAdminClientBuilder WithVettedPackagesUpdateResult(VettedPackagesUpdateResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _vettedPackagesUpdateResult = result;
        return this;
    }

    /// <summary>
    /// Stages the party <see cref="FakeAdminClient.AllocateExternalPartyAsync"/> returns for every
    /// allocation. Like <see cref="WithAllocatedParty"/>, it does not make the party appear in the
    /// <see cref="WithParties"/> list.
    /// </summary>
    /// <returns>The same builder, for chaining.</returns>
    public FakeAdminClientBuilder WithAllocatedExternalParty(Party party)
    {
        _allocatedExternalParty = party;
        return this;
    }

    /// <summary>
    /// Stages the participant's current time: <see cref="FakeAdminClient.GetTimeAsync"/> returns it and
    /// <see cref="FakeAdminClient.SetTimeAsync"/> moves it forward, as against a static-time ledger.
    /// </summary>
    /// <returns>The same builder, for chaining.</returns>
    public FakeAdminClientBuilder WithTime(DateTimeOffset time)
    {
        _time = time;
        return this;
    }

    /// <summary>Builds a <see cref="FakeAdminClient"/> from the currently staged data.</summary>
    /// <returns>
    /// A fake whose behaviour is a snapshot of this builder; later mutation of the builder does
    /// not affect an already-built client.
    /// </returns>
    public FakeAdminClient Build() => new(
        _participantId,
        new Dictionary<string, PartyDetails>(_allocatedParties),
        _knownParties?.ToArray(),
        new Dictionary<string, UserDetails>(_users),
        _knownUsers?.ToArray(),
        new Dictionary<string, IReadOnlyList<UserRight>>(_userRights),
        _knownPackages?.ToArray(),
        new Dictionary<string, PackageArchive>(_packages),
        _vettedPackages?.ToArray(),
        _commandStatuses?.ToArray(),
        _identityProviderConfigs?.ToArray(),
        _vettedPackagesUpdateResult,
        _externalPartyTopology,
        _allocatedExternalParty)
    {
        PackagesAndTime = new FakePackagesAndTime(
            _packageIds?.ToArray(), new Dictionary<string, PackageStatus>(_packageStatuses), _time),
    };
}
