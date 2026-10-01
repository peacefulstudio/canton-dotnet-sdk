// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// The changes <see cref="IAdminClient.UpdateUserAsync"/> applies to a user. Only the properties
/// that are set are sent to the participant as the update's field mask; every other property of
/// the user is left as it is.
/// </summary>
public sealed record UserUpdate
{
    /// <summary>Makes this party the user's primary party.</summary>
    public Party? PrimaryParty { get; init; }

    /// <summary>Removes the user's primary party. Mutually exclusive with <see cref="PrimaryParty"/>.</summary>
    public bool ClearPrimaryParty { get; init; }

    /// <summary>Denies the user all access to the Ledger API when <see langword="true"/>, restores it when <see langword="false"/>.</summary>
    public bool? IsDeactivated { get; init; }

    /// <summary>Lets the user authenticate by signing a party JWT with the primary party's signing key.</summary>
    public bool? PrimaryPartyAuthentication { get; init; }

    /// <summary>
    /// Annotations to add or overwrite; an entry whose value is the empty string removes that
    /// annotation. Annotations are write-only through this SDK: <see cref="UserDetails"/> does not
    /// carry them.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Annotations { get; init; }

    internal IReadOnlyList<string> UpdatePaths()
    {
        if (PrimaryParty is not null && ClearPrimaryParty)
            throw new ArgumentException("A user update cannot both set and clear the primary party.", "update");

        var paths = new List<string>();
        if (PrimaryParty is not null || ClearPrimaryParty)
            paths.Add("primary_party");
        if (IsDeactivated is not null)
            paths.Add("is_deactivated");
        if (PrimaryPartyAuthentication is not null)
            paths.Add("primary_party_authentication");
        if (Annotations is not null)
            paths.Add("metadata.annotations");

        if (paths.Count == 0)
            throw new ArgumentException("A user update must change at least one property.", "update");

        return paths;
    }
}

/// <summary>
/// The changes <see cref="IAdminClient.UpdatePartyDetailsAsync"/> applies to a party's
/// participant-local details. Only the properties that are set are sent to the participant as the
/// update's field mask.
/// </summary>
public sealed record PartyUpdate
{
    /// <summary>
    /// Annotations to add or overwrite; an entry whose value is the empty string removes that
    /// annotation. Annotations are write-only through this SDK: <see cref="PartyDetails"/> does not
    /// carry them.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Annotations { get; init; }

    internal IReadOnlyList<string> UpdatePaths() =>
        Annotations is null
            ? throw new ArgumentException("A party update must change at least one property.", "update")
            : ["local_metadata.annotations"];
}

/// <summary>The lifecycle state of a command the participant tracks.</summary>
public enum CommandState
{
    /// <summary>Matches every state when used in a query.</summary>
    Unspecified,

    /// <summary>The command is in flight.</summary>
    Pending,

    /// <summary>The command was accepted.</summary>
    Succeeded,

    /// <summary>The command was rejected or failed.</summary>
    Failed,
}

/// <summary>
/// A summary of one command the participant's command inspection tracks.
/// </summary>
/// <param name="CommandId">The id the submitter gave the command.</param>
/// <param name="State">Where the command is in its lifecycle.</param>
/// <param name="Started">When the participant started processing the command.</param>
/// <param name="Completed">When the participant finished processing it, or <see langword="null"/> while it is pending.</param>
/// <param name="SynchronizerId">The synchronizer the command ran on, or an empty string when none is known yet.</param>
public sealed record CommandStatus(
    string CommandId,
    CommandState State,
    DateTimeOffset? Started,
    DateTimeOffset? Completed,
    string SynchronizerId);

/// <summary>
/// An identity provider configuration a participant trusts to issue JWTs.
/// </summary>
/// <param name="IdentityProviderId">The configuration's unique id.</param>
/// <param name="IsDeactivated">Whether the participant currently rejects tokens from this provider.</param>
/// <param name="Issuer">The <c>iss</c> claim value the provider's tokens carry.</param>
/// <param name="JwksUrl">Where the participant fetches the provider's signing keys.</param>
/// <param name="Audience">The required <c>aud</c> claim value, or an empty string when none is required.</param>
public sealed record IdentityProviderConfig(
    string IdentityProviderId,
    bool IsDeactivated,
    string Issuer,
    string JwksUrl,
    string Audience);

/// <summary>
/// The changes <see cref="IAdminClient.UpdateIdentityProviderConfigAsync"/> applies to an identity
/// provider configuration. Only the properties that are set are sent to the participant as the
/// update's field mask.
/// </summary>
public sealed record IdentityProviderConfigUpdate
{
    /// <summary>Deactivates the configuration when <see langword="true"/>, reactivates it when <see langword="false"/>.</summary>
    public bool? IsDeactivated { get; init; }

    /// <summary>The new issuer.</summary>
    public string? Issuer { get; init; }

    /// <summary>The new JWKS URL.</summary>
    public string? JwksUrl { get; init; }

    /// <summary>The new audience; an empty string removes the audience requirement.</summary>
    public string? Audience { get; init; }

    internal IReadOnlyList<string> UpdatePaths()
    {
        var paths = new List<string>();
        if (IsDeactivated is not null)
            paths.Add("is_deactivated");
        if (Issuer is not null)
            paths.Add("issuer");
        if (JwksUrl is not null)
            paths.Add("jwks_url");
        if (Audience is not null)
            paths.Add("audience");

        if (paths.Count == 0)
            throw new ArgumentException("An identity provider config update must change at least one property.", "update");

        return paths;
    }
}

/// <summary>
/// Identifies packages to vet or unvet. Every property left empty is not used to match.
/// </summary>
/// <param name="PackageId">The package id, or empty to match by name and version.</param>
/// <param name="PackageName">The package name, or empty to match by id.</param>
/// <param name="PackageVersion">The package version, or empty to match by id.</param>
public sealed record PackageSelector(string PackageId = "", string PackageName = "", string PackageVersion = "");

/// <summary>
/// One change <see cref="IAdminClient.UpdateVettedPackagesAsync"/> applies to the vetting state.
/// </summary>
public abstract record VettedPackagesChange
{
    private VettedPackagesChange()
    {
    }

    /// <summary>Vets the referenced packages, optionally within a validity window.</summary>
    /// <param name="Packages">The packages to vet.</param>
    /// <param name="ValidFromInclusive">The start of the validity window, or <see langword="null"/> for no lower bound; re-vetting a package overwrites and so clears any bound it already had.</param>
    /// <param name="ValidUntilExclusive">The end of the validity window, or <see langword="null"/> for no upper bound; re-vetting a package overwrites and so clears any bound it already had.</param>
    public sealed record Vet(
        IReadOnlyList<PackageSelector> Packages,
        DateTimeOffset? ValidFromInclusive = null,
        DateTimeOffset? ValidUntilExclusive = null) : VettedPackagesChange;

    /// <summary>Unvets the referenced packages.</summary>
    /// <param name="Packages">The packages to unvet.</param>
    public sealed record Unvet(IReadOnlyList<PackageSelector> Packages) : VettedPackagesChange;
}

/// <summary>Safety checks <see cref="IAdminClient.UpdateVettedPackagesAsync"/> can be told to skip.</summary>
[Flags]
public enum VettingOverrides
{
    /// <summary>Every check applies.</summary>
    None = 0,

    /// <summary>Allows vetting a package that is upgrade-incompatible with other vetted packages.</summary>
    AllowVetIncompatibleUpgrades = 1,

    /// <summary>Allows vetting a package without vetting one or more of its dependencies.</summary>
    AllowUnvettedDependencies = 2,
}

/// <summary>
/// The topology serial an update expects the vetting state to be at, so a concurrent change makes
/// the update fail instead of overwriting it.
/// </summary>
public sealed record ExpectedTopologySerial
{
    private ExpectedTopologySerial(uint? prior) => Prior = prior;

    /// <summary>The serial the vetting state is expected to be at, or <see langword="null"/> when none is expected to exist yet.</summary>
    public uint? Prior { get; }

    /// <summary>Expects no vetting state to exist yet.</summary>
    public static ExpectedTopologySerial NoPrior { get; } = new((uint?)null);

    /// <summary>Expects the vetting state to be at <paramref name="serial"/>.</summary>
    public static ExpectedTopologySerial At(uint serial) => new(serial);
}

/// <summary>A package's vetting entry with its validity window.</summary>
/// <param name="PackageId">The package id.</param>
/// <param name="PackageName">The package name.</param>
/// <param name="PackageVersion">The package version.</param>
/// <param name="ValidFromInclusive">When the vetting starts, or <see langword="null"/> when it always has.</param>
/// <param name="ValidUntilExclusive">When the vetting ends, or <see langword="null"/> when it never does.</param>
public sealed record VettedPackageEntry(
    string PackageId,
    string PackageName,
    string PackageVersion,
    DateTimeOffset? ValidFromInclusive,
    DateTimeOffset? ValidUntilExclusive);

/// <summary>A participant's vetting state on one synchronizer at one topology serial.</summary>
/// <param name="Packages">The vetted packages.</param>
/// <param name="ParticipantId">The participant the state belongs to.</param>
/// <param name="SynchronizerId">The synchronizer the state applies to.</param>
/// <param name="TopologySerial">The topology serial the state was read at.</param>
public sealed record VettedPackagesSnapshot(
    IReadOnlyList<VettedPackageEntry> Packages,
    string ParticipantId,
    string SynchronizerId,
    uint TopologySerial);

/// <summary>The vetting state before and after <see cref="IAdminClient.UpdateVettedPackagesAsync"/>.</summary>
/// <param name="Past">The state before the update, or <see langword="null"/> when the participant did not report it.</param>
/// <param name="New">The state after the update, or <see langword="null"/> when the participant did not report it.</param>
public sealed record VettedPackagesUpdateResult(VettedPackagesSnapshot? Past, VettedPackagesSnapshot? New);
