// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// Client interface for Canton participant administration.
/// Provides methods for managing parties, users, and packages.
/// </summary>
/// <remarks>
/// Every member rejects a <see langword="null"/> reference argument with an
/// <see cref="ArgumentNullException"/> naming the parameter, thrown synchronously. An identifier is
/// additionally rejected for being empty or whitespace only when its underlying request field is
/// Required and the Ledger API gives the empty string no meaning; where the field is Optional and the
/// empty string carries a documented meaning, it is accepted and that meaning applies.
/// <para>
/// A call the participant rejects surfaces as a
/// <see cref="Daml.Ledger.Abstractions.LedgerOperationException"/> classified the way
/// <see cref="ParsedLedgerError"/> classifies every other ledger failure, whatever the transport:
/// with the error's category, id and metadata when the participant attached a structured error,
/// and with the transport status code otherwise. A caller cancellation surfaces as an
/// <see cref="OperationCanceledException"/>.
/// </para>
/// <para>
/// <b>Failure contract.</b> It is the one the ledger client follows, on the gRPC and the JSON Ledger
/// API transport alike. Every call that does not succeed raises
/// <see cref="Daml.Ledger.Abstractions.LedgerOperationException"/> — no connection, no answer within the
/// deadline, a participant rejection, or a response body that cannot be decoded — carrying the
/// transport-native <see cref="Daml.Ledger.Abstractions.LedgerOperationException.Status"/>
/// (<c>Grpc</c> with <c>Unavailable</c> or <c>DeadlineExceeded</c> on gRPC, <c>NoResponse</c> on the JSON
/// Ledger API, <c>Http</c> for a JSON answer, <c>UndecodableBody</c> for an unreadable one), the
/// <see cref="Daml.Ledger.Abstractions.LedgerOperationException.Category"/>,
/// <see cref="Daml.Ledger.Abstractions.LedgerOperationException.ErrorId"/> and
/// <see cref="Daml.Ledger.Abstractions.LedgerOperationException.Metadata"/> when the participant sent them,
/// the <see cref="Daml.Ledger.Abstractions.LedgerOperationException.CommitState"/> the kind of call decides,
/// and the transport's own exception as <see cref="Exception.InnerException"/>. A call that reads — every
/// <c>Get*</c> and <c>List*</c>, <see cref="ValidateDarAsync(byte[], CancellationToken)"/> and a dry-run
/// <see cref="UpdateVettedPackagesAsync"/> — commits nothing, so its failure is always
/// <see cref="Daml.Ledger.Abstractions.CommitState.NotCommitted"/>. A call that changes participant state
/// reports <see cref="Daml.Ledger.Abstractions.CommitState.Unknown"/> when no answer arrived, takes the
/// state from the error when the participant answered with one
/// (<see cref="Daml.Ledger.Abstractions.CommitState.NotCommitted"/> for a structured rejection,
/// <see cref="Daml.Ledger.Abstractions.CommitState.Unknown"/> for a category of
/// <c>DeadlineExceededRequestStateUnknown</c> or <c>Unknown</c>, an error with no structured detail, or the
/// error id <c>SUBMISSION_ALREADY_IN_FLIGHT</c>, and <see cref="Daml.Ledger.Abstractions.CommitState.Committed"/>
/// for the error id <c>DUPLICATE_COMMAND</c> unless its <c>accepted</c> metadata is <c>"false"</c>), and
/// reports <see cref="Daml.Ledger.Abstractions.CommitState.Committed"/> when the participant answered 2xx
/// with a body the client could not decode. A lookup documented to return <see langword="null"/> for an
/// unknown entity returns it rather than throwing.
/// </para>
/// <para>
/// A caller error — a null or blank argument — throws its usual <see cref="ArgumentException"/> type
/// synchronously, outside this contract. A failure of the configured token provider propagates unchanged
/// on gRPC. On the JSON Ledger API the provider runs inside the HTTP pipeline, so an
/// <see cref="System.Net.Http.HttpRequestException"/> (including a token endpoint that is unreachable or answers
/// with a non-success status), a <see cref="TimeoutException"/>, or an <see cref="OperationCanceledException"/>
/// the caller did not cause is reported as <c>NoResponse</c> on a
/// <see cref="Daml.Ledger.Abstractions.LedgerOperationException"/>; any other provider exception propagates
/// unchanged. A member the JSON Ledger API serves no route for throws
/// <see cref="NotSupportedException"/> from the returned task on that transport.
/// </para>
/// </remarks>
public interface IAdminClient
{
    /// <summary>
    /// Gets the participant ID.
    /// </summary>
    Task<string> GetParticipantIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Allocates a new party on the ledger.
    /// </summary>
    /// <param name="partyIdHint">
    /// A hint for the party ID, which the participant may modify or ignore entirely. An empty string
    /// gives no hint and lets the participant choose the party ID.
    /// </param>
    /// <param name="synchronizerId">
    /// Optional id of the synchronizer to allocate the party on. Required when the participant
    /// is connected to more than one synchronizer — otherwise Canton rejects the request with
    /// <c>PARTY_ALLOCATION_CANNOT_DETERMINE_SYNCHRONIZER</c>. When <see langword="null"/> the
    /// participant falls back to its single connected synchronizer (the prior behaviour).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The allocated party details.</returns>
    Task<PartyDetails> AllocatePartyAsync(
        string partyIdHint,
        SynchronizerId? synchronizerId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the participant to generate the topology transactions that onboard an external party
    /// controlled by <see cref="ExternalPartyTopologyRequest.PublicKey"/>. Nothing is allocated: sign
    /// <see cref="ExternalPartyTopology.MultiHash"/> and pass the result to
    /// <see cref="AllocateExternalPartyAsync"/>.
    /// </summary>
    /// <param name="request">What to generate the topology for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ExternalPartyTopology> GenerateExternalPartyTopologyAsync(
        ExternalPartyTopologyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Allocates an external party from topology transactions its key has signed. The call is not
    /// retried by the opt-in retry pipeline, because replaying a committed allocation is not
    /// idempotent.
    /// </summary>
    /// <param name="allocation">The signed onboarding topology.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The allocated party.</returns>
    Task<Party> AllocateExternalPartyAsync(
        ExternalPartyAllocation allocation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets details for the specified parties. Only parties the participant knows come back, so a
    /// read of several parties can answer with fewer details than it asked for, and an unknown
    /// party is an absence rather than an error.
    /// </summary>
    /// <remarks>
    /// The two transports charge differently for this read. The gRPC Ledger API takes the parties as
    /// a repeated request field and serves them in one round trip; the JSON Ledger API serves
    /// <c>GET /v2/parties/{party}</c> one party at a time, so an implementation over it costs one
    /// round trip per party. The result is the same either way — only the traffic differs.
    /// </remarks>
    Task<IReadOnlyList<PartyDetails>> GetPartiesAsync(
        IEnumerable<Party> parties,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all known parties.
    /// Transparently follows server pagination and returns the complete result set.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<PartyDetails>> ListKnownPartiesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new user on the participant.
    /// </summary>
    /// <param name="userId">The id of the user to create.</param>
    /// <param name="primaryParty">
    /// The party the user reads and acts as by default, or <see langword="null"/> for a user
    /// without a primary party, such as a participant administrator.
    /// </param>
    /// <param name="rights">The rights to grant the user on creation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<UserDetails> CreateUserAsync(
        string userId,
        Party? primaryParty,
        IEnumerable<UserRight>? rights = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets details for a user.
    /// </summary>
    /// <param name="userId">
    /// The user whose details to retrieve. An empty string retrieves the authenticated user.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The user's details, or <see langword="null"/> when the user does not exist.
    /// </returns>
    Task<UserDetails?> GetUserAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Grants rights to a user.
    /// </summary>
    Task GrantUserRightsAsync(
        string userId,
        IEnumerable<UserRight> rights,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes rights from a user.
    /// </summary>
    Task RevokeUserRightsAsync(
        string userId,
        IEnumerable<UserRight> rights,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the rights granted to a user.
    /// </summary>
    /// <param name="userId">
    /// The user whose rights to list. An empty string lists the rights of the authenticated user.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The rights granted to the user, or <see langword="null"/> when the user does not exist —
    /// mirroring <see cref="GetUserAsync"/>.
    /// </returns>
    Task<IReadOnlyList<UserRight>?> ListUserRightsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all users.
    /// Transparently follows server pagination and returns the complete result set.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<UserDetails>> ListUsersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all Daml-LF packages known to the participant.
    /// </summary>
    /// <remarks>
    /// The JSON Ledger API serves no route for this, so the REST implementation throws
    /// <see cref="NotSupportedException"/> from the returned task; use <see cref="ListVettedPackagesAsync"/>
    /// or <see cref="ListPackagesAsync"/>, or the gRPC <see cref="IAdminClient"/>.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<PackageDetails>> ListKnownPackagesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the ids of the packages the participant serves through <c>PackageService.ListPackages</c>;
    /// <see cref="ListKnownPackagesAsync"/> lists the same packages with their metadata.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<string>> ListPackagesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the status of a single package through <c>PackageService.GetPackageStatus</c>.
    /// </summary>
    /// <param name="packageId">The ID of the requested package.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PackageStatus> GetPackageStatusAsync(
        string packageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the archive of a single package.
    /// </summary>
    /// <param name="packageId">The ID of the requested package.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The <c>daml_lf</c> archive payload together with its hash and hash function.</returns>
    Task<PackageArchive> GetPackageAsync(
        string packageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the packages vetted on the participant's connected synchronizers.
    /// Transparently follows server pagination and returns the complete result set.
    /// </summary>
    /// <param name="packageNamePrefixes">
    /// Optional package name prefixes to filter by; a vetted package matches when its name
    /// starts with at least one prefix. Null or empty returns all vetted packages.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<VettedPackage>> ListVettedPackagesAsync(
        IEnumerable<string>? packageNamePrefixes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a DAR file to the participant. By default the ledger also vets all packages
    /// in the DAR (the underlying request's <c>vetting_change</c> defaults to
    /// <c>VETTING_CHANGE_VET_ALL_PACKAGES</c>). The participant autodetects the synchronizer to
    /// vet on, which requires it to be connected to exactly one; on a participant connected to
    /// more than one, use the overload that takes a <see cref="SynchronizerId"/>.
    /// </summary>
    /// <param name="darFile">The DAR file contents.</param>
    /// <param name="submissionId">Optional unique submission identifier; the ledger generates one when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UploadDarAsync(
        byte[] darFile,
        string? submissionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a DAR file to the participant and vets its packages on <paramref name="synchronizerId"/>.
    /// By default the ledger also vets all packages in the DAR (the underlying request's
    /// <c>vetting_change</c> defaults to <c>VETTING_CHANGE_VET_ALL_PACKAGES</c>).
    /// </summary>
    /// <param name="darFile">The DAR file contents.</param>
    /// <param name="synchronizerId">
    /// The synchronizer to vet the DAR's packages on. Required when the participant is connected
    /// to more than one synchronizer — omitting it there fails with
    /// <c>PACKAGE_SERVICE_CANNOT_AUTODETECT_SYNCHRONIZER</c>.
    /// </param>
    /// <param name="submissionId">Optional unique submission identifier; the ledger generates one when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UploadDarAsync(
        byte[] darFile,
        SynchronizerId synchronizerId,
        string? submissionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a DAR file without persisting or vetting anything. A DAR the participant finds
    /// invalid surfaces as a <see cref="Daml.Ledger.Abstractions.LedgerOperationException"/>. The
    /// participant autodetects the synchronizer to check upgrade compatibility against, which
    /// requires it to be connected to exactly one; on a participant connected to more than one,
    /// use the overload that takes a <see cref="SynchronizerId"/>.
    /// </summary>
    /// <param name="darFile">The DAR file contents.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ValidateDarAsync(
        byte[] darFile,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a DAR file without persisting or vetting anything, checking upgrade compatibility
    /// against <paramref name="synchronizerId"/>. A DAR the participant finds invalid surfaces as a
    /// <see cref="Daml.Ledger.Abstractions.LedgerOperationException"/>.
    /// </summary>
    /// <param name="darFile">The DAR file contents.</param>
    /// <param name="synchronizerId">
    /// The synchronizer to check the DAR's packages for upgrade compatibility against. Required
    /// when the participant is connected to more than one synchronizer — omitting it there fails
    /// with <c>PACKAGE_SERVICE_CANNOT_AUTODETECT_SYNCHRONIZER</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ValidateDarAsync(
        byte[] darFile,
        SynchronizerId synchronizerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the properties of a user that <paramref name="update"/> sets. Never retried, even
    /// when retry is enabled: the call mutates participant state.
    /// </summary>
    /// <param name="userId">The user to update.</param>
    /// <param name="update">The changes to apply; it must change at least one property.</param>
    /// <param name="identityProviderId">
    /// The identity provider managing the user, or <see langword="null"/> for the default one.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user as the participant stored it after the update.</returns>
    Task<UserDetails> UpdateUserAsync(
        string userId,
        UserUpdate update,
        string? identityProviderId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a user. Never retried, even when retry is enabled: the call mutates participant state.
    /// </summary>
    /// <param name="userId">The user to delete.</param>
    /// <param name="identityProviderId">
    /// The identity provider managing the user, or <see langword="null"/> for the default one.
    /// The JSON Ledger API has no identity-provider-scoped user delete, so the REST client throws
    /// <see cref="NotSupportedException"/> for a non-default identity provider.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteUserAsync(
        string userId,
        string? identityProviderId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a user from one identity provider to another. Never retried, even when retry is
    /// enabled: the call mutates participant state.
    /// </summary>
    /// <param name="userId">The user to move.</param>
    /// <param name="sourceIdentityProviderId">The user's current identity provider; <see langword="null"/> is the default one.</param>
    /// <param name="targetIdentityProviderId">The identity provider to move the user to; <see langword="null"/> is the default one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateUserIdentityProviderIdAsync(
        string userId,
        string? sourceIdentityProviderId,
        string? targetIdentityProviderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the participant-local details of a party that <paramref name="update"/> sets. Never
    /// retried, even when retry is enabled: the call mutates participant state.
    /// </summary>
    /// <param name="party">The party to update.</param>
    /// <param name="update">The changes to apply; it must change at least one property.</param>
    /// <param name="identityProviderId">
    /// The identity provider managing the party, or <see langword="null"/> for the default one.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The party's details as the participant stored them after the update.</returns>
    Task<PartyDetails> UpdatePartyDetailsAsync(
        Party party,
        PartyUpdate update,
        string? identityProviderId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a party from one identity provider to another. The JSON Ledger API serves no route
    /// for this, so the REST implementation throws <see cref="NotSupportedException"/>; use the gRPC
    /// <see cref="IAdminClient"/>. Never retried, even when retry is enabled.
    /// </summary>
    /// <param name="party">The party to move.</param>
    /// <param name="sourceIdentityProviderId">The party's current identity provider; <see langword="null"/> is the default one.</param>
    /// <param name="targetIdentityProviderId">The identity provider to move the party to; <see langword="null"/> is the default one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdatePartyIdentityProviderIdAsync(
        Party party,
        string? sourceIdentityProviderId,
        string? targetIdentityProviderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the status of commands the participant's command inspection tracks. The JSON Ledger
    /// API serves no route for this, so the REST implementation throws
    /// <see cref="NotSupportedException"/>; use the gRPC <see cref="IAdminClient"/>.
    /// </summary>
    /// <param name="commandIdPrefix">Only commands whose id starts with this prefix; empty matches every command.</param>
    /// <param name="state">Only commands in this state; <see cref="CommandState.Unspecified"/> matches every state.</param>
    /// <param name="limit">The most statuses to return, or <see langword="null"/> for the participant's default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<CommandStatus>> GetCommandStatusAsync(
        string commandIdPrefix = "",
        CommandState state = CommandState.Unspecified,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers an identity provider configuration. Never retried, even when retry is enabled.
    /// </summary>
    /// <param name="config">The configuration to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The configuration as the participant stored it.</returns>
    Task<IdentityProviderConfig> CreateIdentityProviderConfigAsync(
        IdentityProviderConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one identity provider configuration.</summary>
    /// <param name="identityProviderId">The configuration's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The configuration, or <see langword="null"/> when the participant has none with that id.</returns>
    Task<IdentityProviderConfig?> GetIdentityProviderConfigAsync(
        string identityProviderId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every identity provider configuration.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<IdentityProviderConfig>> ListIdentityProviderConfigsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an identity provider configuration. Only the properties set on <paramref name="update"/>
    /// change. Never retried, even when retry is enabled.
    /// </summary>
    /// <param name="identityProviderId">The configuration to update.</param>
    /// <param name="update">The changes to apply; it must change at least one property.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The configuration as the participant stored it after the update.</returns>
    Task<IdentityProviderConfig> UpdateIdentityProviderConfigAsync(
        string identityProviderId,
        IdentityProviderConfigUpdate update,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an identity provider configuration. Never retried, even when retry is enabled.
    /// </summary>
    /// <param name="identityProviderId">The configuration to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteIdentityProviderConfigAsync(
        string identityProviderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Vets or unvets packages on a synchronizer. Never retried, even when retry is enabled.
    /// </summary>
    /// <param name="changes">The changes to apply, in order.</param>
    /// <param name="dryRun">When <see langword="true"/>, computes and returns the result without applying it.</param>
    /// <param name="synchronizerId">The synchronizer to update; <see langword="null"/> is only accepted when the participant is connected to exactly one.</param>
    /// <param name="expectedTopologySerial">The serial the vetting state must be at for the update to apply, or <see langword="null"/> for no check.</param>
    /// <param name="safetyOverrides">Safety checks to skip.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The vetting state before and after the update.</returns>
    Task<VettedPackagesUpdateResult> UpdateVettedPackagesAsync(
        IReadOnlyList<VettedPackagesChange> changes,
        bool dryRun = false,
        SynchronizerId? synchronizerId = null,
        ExpectedTopologySerial? expectedTopologySerial = null,
        VettingOverrides safetyOverrides = VettingOverrides.None,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prunes the participant's ledger up to an offset. Destructive and irreversible. The JSON Ledger
    /// API serves no route for this, so the REST implementation throws <see cref="NotSupportedException"/>;
    /// use the gRPC <see cref="IAdminClient"/>. Never retried, even when retry is enabled.
    /// </summary>
    /// <param name="pruneUpTo">The offset to prune up to, inclusive.</param>
    /// <param name="submissionId">A submission id for tracing, or <see langword="null"/> to have one generated.</param>
    /// <param name="pruneAllDivulgedContracts">Whether to also prune divulged contracts that were never archived.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PruneAsync(
        long pruneUpTo,
        string? submissionId = null,
        bool pruneAllDivulgedContracts = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the ledger's current time through <c>TimeService.GetTime</c>. Only a participant running
    /// in static-time mode serves the time service; a wall-clock participant answers
    /// <c>UNIMPLEMENTED</c>, which surfaces as a <see cref="Daml.Ledger.Abstractions.LedgerOperationException"/>.
    /// The JSON Ledger API serves no route for it, so the REST transport throws
    /// <see cref="NotSupportedException"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DateTimeOffset> GetTimeAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Advances the ledger's static time through <c>TimeService.SetTime</c>. The call mutates the
    /// participant, so it is sent exactly once even when <c>LedgerClientOptions.Retry</c> is
    /// enabled for the client's reads. The participant refuses a <paramref name="currentTime"/> that is
    /// not its current time, and a <paramref name="newTime"/> that is not later than it.
    /// The JSON Ledger API serves no route for it, so the REST transport throws
    /// <see cref="NotSupportedException"/>.
    /// </summary>
    /// <param name="currentTime">The time the caller believes the ledger is at.</param>
    /// <param name="newTime">The time to advance the ledger to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetTimeAsync(
        DateTimeOffset currentTime,
        DateTimeOffset newTime,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Details about a party.
/// </summary>
public sealed record PartyDetails(
    Party Party,
    bool IsLocal);

/// <summary>
/// Details about a user. Rights are not part of the underlying <c>User</c> proto;
/// read them back with <see cref="IAdminClient.ListUserRightsAsync"/>.
/// </summary>
/// <param name="UserId">The user's id.</param>
/// <param name="PrimaryParty">
/// The party the user reads and acts as by default, or <see langword="null"/> when the user has none.
/// </param>
public sealed record UserDetails(
    string UserId,
    Party? PrimaryParty);

/// <summary>
/// Details about a Daml-LF package known to the participant.
/// </summary>
public sealed record PackageDetails(
    string PackageId,
    string Name,
    string Version,
    long PackageSize,
    DateTimeOffset KnownSince);

/// <summary>
/// The hash function used to compute a <see cref="PackageArchive.Hash"/>.
/// </summary>
public enum HashFunction
{
    /// <summary>SHA-256.</summary>
    Sha256,

    /// <summary>
    /// A hash function reported by the participant that this SDK version does not recognise.
    /// </summary>
    Unrecognized,
}

/// <summary>
/// A package archive downloaded from the participant: the <c>daml_lf</c> payload
/// together with its hash and the hash function used to compute it.
/// </summary>
public sealed record PackageArchive(
    ReadOnlyMemory<byte> Payload,
    string Hash,
    HashFunction HashFunction)
{
    /// <inheritdoc />
    public bool Equals(PackageArchive? other) =>
        ReferenceEquals(this, other)
        || (other is not null
            && Payload.Span.SequenceEqual(other.Payload.Span)
            && Hash == other.Hash
            && HashFunction == other.HashFunction);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.AddBytes(Payload.Span);
        hashCode.Add(Hash);
        hashCode.Add(HashFunction);
        return hashCode.ToHashCode();
    }
}

/// <summary>
/// A package vetted on a participant and synchronizer.
/// </summary>
public sealed record VettedPackage(
    string PackageId,
    string PackageName,
    string PackageVersion,
    string ParticipantId,
    SynchronizerId SynchronizerId);

/// <summary>
/// A right that can be granted to a user.
/// </summary>
public abstract record UserRight
{
    /// <summary>
    /// Right to act as a party.
    /// </summary>
    public sealed record ActAs(Party Party) : UserRight;

    /// <summary>
    /// Right to read as a party.
    /// </summary>
    public sealed record ReadAs(Party Party) : UserRight;

    /// <summary>
    /// Right to administer the participant.
    /// </summary>
    public sealed record ParticipantAdmin : UserRight;

    /// <summary>
    /// Right to administer an identity provider.
    /// </summary>
    public sealed record IdentityProviderAdmin : UserRight;

    /// <summary>
    /// Right to read ledger data visible to any party on the participant.
    /// Intended for tools that consume the whole ledger, such as PQS.
    /// </summary>
    public sealed record ReadAsAnyParty : UserRight;

    /// <summary>
    /// Right to prepare and execute submissions as a party, without any read entitlement.
    /// Combine with <see cref="ReadAs"/> when reading is also required; <see cref="ActAs"/>
    /// implicitly contains this right.
    /// </summary>
    public sealed record ExecuteAs(Party Party) : UserRight;

    /// <summary>
    /// Right to prepare and execute submissions as any party on the participant.
    /// Intended for users that perform interactive submissions on behalf of many parties.
    /// </summary>
    public sealed record ExecuteAsAnyParty : UserRight;
}
