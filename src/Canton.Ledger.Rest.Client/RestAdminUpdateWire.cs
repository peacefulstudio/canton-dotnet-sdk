// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace Canton.Ledger.Rest.Client;

internal sealed class WireFieldMask(IReadOnlyList<string> paths)
{
    [JsonPropertyName("paths")]
    public IReadOnlyList<string> Paths { get; } = paths;

    [JsonPropertyName("unknownFields")]
    public WireUnknownFields UnknownFields { get; } = new();
}

internal sealed class WireUnknownFields
{
    [JsonPropertyName("fields")]
    public IDictionary<string, object> Fields { get; } = new Dictionary<string, object>();
}

internal sealed class WireUpdateUserRequest(Raw.User user, WireFieldMask updateMask)
{
    [JsonPropertyName("user")]
    public Raw.User User { get; } = user;

    [JsonPropertyName("updateMask")]
    public WireFieldMask UpdateMask { get; } = updateMask;
}

internal sealed class WireUpdatePartyDetailsRequest(Raw.PartyDetails partyDetails, WireFieldMask updateMask)
{
    [JsonPropertyName("partyDetails")]
    public Raw.PartyDetails PartyDetails { get; } = partyDetails;

    [JsonPropertyName("updateMask")]
    public WireFieldMask UpdateMask { get; } = updateMask;
}

internal sealed class WireUpdateIdentityProviderConfigRequest(Raw.IdentityProviderConfig config, WireFieldMask updateMask)
{
    [JsonPropertyName("identityProviderConfig")]
    public Raw.IdentityProviderConfig IdentityProviderConfig { get; } = config;

    [JsonPropertyName("updateMask")]
    public WireFieldMask UpdateMask { get; } = updateMask;
}

internal sealed class WireUpdateVettedPackagesRequest(
    IReadOnlyList<Raw.VettedPackagesChange> changes,
    bool dryRun,
    string? synchronizerId,
    Raw.PriorTopologySerial? expectedTopologySerial,
    IReadOnlyList<string> forceFlags)
{
    [JsonPropertyName("changes")]
    public IReadOnlyList<Raw.VettedPackagesChange> Changes { get; } = changes;

    [JsonPropertyName("dryRun")]
    public bool DryRun { get; } = dryRun;

    [JsonPropertyName("synchronizerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SynchronizerId { get; } = synchronizerId;

    [JsonPropertyName("expectedTopologySerial")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Raw.PriorTopologySerial? ExpectedTopologySerial { get; } = expectedTopologySerial;

    [JsonPropertyName("updateVettedPackagesForceFlags")]
    public IReadOnlyList<string> UpdateVettedPackagesForceFlags { get; } = forceFlags;
}
