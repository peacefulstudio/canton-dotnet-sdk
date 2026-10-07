// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// Calls each throwing unary member of <see cref="ICantonLedgerClient"/> and <see cref="IAdminClient"/>
/// with fixed, valid arguments, so a transport's malformed-response sweep can aim any client at any
/// entry point by its key without writing the call twice.
/// </summary>
public static class EntryPointInvocations
{
    private static readonly Party Alice = new("alice::ns1");
    private static readonly RuntimeCommands.SubmitterInfo Submitter = new(Alice);
    private static readonly SynchronizerId Source = new("sync::a");
    private static readonly SynchronizerId Target = new("sync::b");

    /// <summary>
    /// Calls the member named by <paramref name="entryPoint"/> (the member name followed by its parameter
    /// count in parentheses) on <paramref name="ledger"/> or <paramref name="admin"/>, reading any
    /// contract as <typeparamref name="TContract"/>. A call that executes a prepared submission carries
    /// <paramref name="preparedTransaction"/> as the serialized transaction, in whatever encoding the
    /// transport under test requires.
    /// </summary>
    public static Task Invoke<TContract>(
        ICantonLedgerClient ledger,
        IAdminClient admin,
        string entryPoint,
        byte[] preparedTransaction,
        CancellationToken cancellationToken)
        where TContract : ITemplate, IDamlRecord<TContract> =>
        entryPoint switch
        {
        "SubmitAndWaitAsync(4)" => ledger.SubmitAndWaitAsync(Submission(), Submitter, cancellationToken: cancellationToken),
        "GetLedgerEndAsync(2)" => ledger.GetLedgerEndAsync(cancellationToken: cancellationToken),
        "SubmitAsync(3)" => ledger.SubmitAsync(Submission(), cancellationToken: cancellationToken),
        "SubmitReassignmentAsync(3)" => ledger.SubmitReassignmentAsync(
            ReassignmentSubmission.Of(new UnassignCommand("00abc", Source, Target), Alice), cancellationToken: cancellationToken),
        "GetConnectedSynchronizersAsync(4)" => ledger.GetConnectedSynchronizersAsync(Alice, cancellationToken: cancellationToken),
        "GetLedgerApiVersionAsync(2)" => ledger.GetLedgerApiVersionAsync(cancellationToken: cancellationToken),
        "GetUpdateByOffsetAsync(4)" => ledger.GetUpdateByOffsetAsync(LedgerOffset.At(5), Submitter, cancellationToken: cancellationToken),
        "GetUpdateByIdAsync(4)" => ledger.GetUpdateByIdAsync("u-1", Submitter, cancellationToken: cancellationToken),
        "GetUpdateTreeByOffsetAsync(4)" => ledger.GetUpdateTreeByOffsetAsync(LedgerOffset.At(5), Submitter, cancellationToken: cancellationToken),
        "EstimateTrafficCostAsync(3)" => ledger.EstimateTrafficCostAsync(Submission(), cancellationToken: cancellationToken),
        "GetContractAsync(4)" => ledger.GetContractAsync(new ContractId<TContract>("00abc"), Submitter, cancellationToken: cancellationToken),
        "GetEventsByContractIdAsync(4)" => ledger.GetEventsByContractIdAsync(new ContractId<TContract>("00abc"), Submitter, cancellationToken: cancellationToken),
        "GetDisclosureAsync(4)" => ledger.GetDisclosureAsync(new ContractId<TContract>("00abc"), Submitter, cancellationToken: cancellationToken),
        "GetActiveContractsPageAsync(7)" => ledger.GetActiveContractsPageAsync<TContract>(Submitter, LedgerOffset.At(5), includeDisclosure: true, cancellationToken: cancellationToken),
        "GetLatestPrunedOffsetsAsync(2)" => ledger.GetLatestPrunedOffsetsAsync(cancellationToken: cancellationToken),
        "GetUpdatesPageAsync(8)" => ledger.GetUpdatesPageAsync(Submitter, cancellationToken: cancellationToken),
        "PrepareSubmissionAsync(3)" => ledger.PrepareSubmissionAsync(Submission(), cancellationToken: cancellationToken),
        "ExecuteSubmissionAsync(3)" => ledger.ExecuteSubmissionAsync(Signed(preparedTransaction), cancellationToken: cancellationToken),
        "ExecuteSubmissionAndWaitAsync(3)" => ledger.ExecuteSubmissionAndWaitAsync(Signed(preparedTransaction), cancellationToken: cancellationToken),
        "ExecuteSubmissionAndWaitForTransactionAsync(4)" => ledger.ExecuteSubmissionAndWaitForTransactionAsync(Signed(preparedTransaction), Submitter, cancellationToken: cancellationToken),
        "GetPreferredPackagesAsync(5)" => ledger.GetPreferredPackagesAsync(
            [new PackageVettingRequirement([Alice], "pkg")], cancellationToken: cancellationToken),
        "GetPreferredPackageVersionAsync(6)" => ledger.GetPreferredPackageVersionAsync([Alice], "pkg", cancellationToken: cancellationToken),
        "GetParticipantIdAsync(1)" => admin.GetParticipantIdAsync(cancellationToken),
        "AllocatePartyAsync(3)" => admin.AllocatePartyAsync("alice", cancellationToken: cancellationToken),
        "GenerateExternalPartyTopologyAsync(2)" => admin.GenerateExternalPartyTopologyAsync(
            new ExternalPartyTopologyRequest(Source, "alice", new SigningPublicKey(PublicKeyFormat.Der, new byte[] { 1 }, SigningKeySpec.EcP256)), cancellationToken),
        "AllocateExternalPartyAsync(2)" => admin.AllocateExternalPartyAsync(
            new ExternalPartyAllocation(Source, [], []), cancellationToken),
        "GetPartiesAsync(2)" => admin.GetPartiesAsync([Alice], cancellationToken),
        "ListKnownPartiesAsync(1)" => admin.ListKnownPartiesAsync(cancellationToken),
        "CreateUserAsync(4)" => admin.CreateUserAsync("alice", Alice, cancellationToken: cancellationToken),
        "GetUserAsync(2)" => admin.GetUserAsync("alice", cancellationToken),
        "GrantUserRightsAsync(3)" => admin.GrantUserRightsAsync("alice", [new UserRight.ParticipantAdmin()], cancellationToken),
        "RevokeUserRightsAsync(3)" => admin.RevokeUserRightsAsync("alice", [new UserRight.ParticipantAdmin()], cancellationToken),
        "ListUserRightsAsync(2)" => admin.ListUserRightsAsync("alice", cancellationToken),
        "ListUsersAsync(1)" => admin.ListUsersAsync(cancellationToken),
        "ListKnownPackagesAsync(1)" => admin.ListKnownPackagesAsync(cancellationToken),
        "ListPackagesAsync(1)" => admin.ListPackagesAsync(cancellationToken),
        "GetPackageStatusAsync(2)" => admin.GetPackageStatusAsync("pkg-1", cancellationToken),
        "GetPackageAsync(2)" => admin.GetPackageAsync("pkg-1", cancellationToken),
        "ListVettedPackagesAsync(2)" => admin.ListVettedPackagesAsync(["pkg"], cancellationToken),
        "UploadDarAsync(3)" => admin.UploadDarAsync(new byte[] { 1 }, "sub-1", cancellationToken),
        "UploadDarAsync(4)" => admin.UploadDarAsync(new byte[] { 1 }, Source, "sub-1", cancellationToken),
        "ValidateDarAsync(2)" => admin.ValidateDarAsync(new byte[] { 1 }, cancellationToken),
        "ValidateDarAsync(3)" => admin.ValidateDarAsync(new byte[] { 1 }, Source, cancellationToken),
        "UpdateUserAsync(4)" => admin.UpdateUserAsync("alice", new UserUpdate { IsDeactivated = true }, cancellationToken: cancellationToken),
        "DeleteUserAsync(3)" => admin.DeleteUserAsync("alice", cancellationToken: cancellationToken),
        "UpdateUserIdentityProviderIdAsync(4)" => admin.UpdateUserIdentityProviderIdAsync("alice", null, "idp-1", cancellationToken),
        "UpdatePartyDetailsAsync(4)" => admin.UpdatePartyDetailsAsync(
            Alice, new PartyUpdate { Annotations = new Dictionary<string, string> { ["k"] = "v" } }, cancellationToken: cancellationToken),
        "UpdatePartyIdentityProviderIdAsync(4)" => admin.UpdatePartyIdentityProviderIdAsync(Alice, null, "idp-1", cancellationToken),
        "GetCommandStatusAsync(4)" => admin.GetCommandStatusAsync(cancellationToken: cancellationToken),
        "CreateIdentityProviderConfigAsync(2)" => admin.CreateIdentityProviderConfigAsync(
            new IdentityProviderConfig("idp-1", false, "https://issuer", "https://jwks", "aud"), cancellationToken),
        "GetIdentityProviderConfigAsync(2)" => admin.GetIdentityProviderConfigAsync("idp-1", cancellationToken),
        "ListIdentityProviderConfigsAsync(1)" => admin.ListIdentityProviderConfigsAsync(cancellationToken),
        "UpdateIdentityProviderConfigAsync(3)" => admin.UpdateIdentityProviderConfigAsync(
            "idp-1", new IdentityProviderConfigUpdate { IsDeactivated = true }, cancellationToken),
        "DeleteIdentityProviderConfigAsync(2)" => admin.DeleteIdentityProviderConfigAsync("idp-1", cancellationToken),
        "UpdateVettedPackagesAsync(6)" => admin.UpdateVettedPackagesAsync(
            [new VettedPackagesChange.Unvet([new PackageSelector("pkg-1")])], cancellationToken: cancellationToken),
        "PruneAsync(4)" => admin.PruneAsync(5, cancellationToken: cancellationToken),
        "GetTimeAsync(1)" => admin.GetTimeAsync(cancellationToken),
        "SetTimeAsync(3)" => admin.SetTimeAsync(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1), cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(entryPoint), entryPoint, "No invocation is written for this entry point."),
        };

    /// <summary>A valid single-create submission acting as the party every invocation uses.</summary>
    public static RuntimeCommands.CommandsSubmission Submission() =>
        RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(
                new Identifier("pkg", "Module", "Template"),
                new DamlRecord(null, [])))
            .WithActAs(Alice)
            .WithCommandId(new RuntimeCommands.CommandId("test-cmd"));

    private static SignedSubmission Signed(byte[] preparedTransaction) =>
        new(
            new PreparedSubmission(
                preparedTransaction,
                new byte[] { 9, 9 },
                HashingSchemeVersion.V3,
                null,
                null),
            [
                new PartySignatures(
                    Alice,
                    [new LedgerSignature(SignatureFormat.Der, new byte[] { 1, 2, 3 }, "fp-1", SigningAlgorithm.EcDsaSha256)]),
            ],
            "sub-1",
            null,
            null);
}
