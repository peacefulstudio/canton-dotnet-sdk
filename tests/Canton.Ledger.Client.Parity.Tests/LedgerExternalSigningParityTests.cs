// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>The ids one external-signing parity test uses, unique per test.</summary>
/// <param name="PartyHint">The hint the external party is onboarded under.</param>
/// <param name="SubmissionId">The submission id the signed submission is executed under.</param>
public sealed record ExternalSigningScenario(string PartyHint, string SubmissionId)
{
    /// <summary>Creates a scenario whose ids are unique to this call.</summary>
    public static ExternalSigningScenario CreateUnique()
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new ExternalSigningScenario($"ext-signing-{suffix}", $"ext-signing-submission-{suffix}");
    }
}

/// <summary>
/// The admin and ledger clients of one provider, plus the hook that gives the lane's ledger user
/// act-as rights over a freshly onboarded external party.
/// </summary>
/// <param name="Admin">The provider's admin client.</param>
/// <param name="Client">The provider's ledger client.</param>
/// <param name="GrantActAsAsync">Grants the lane's ledger user act-as rights over the party.</param>
public sealed record ExternalSigningCapability(
    IAdminClient Admin,
    ICantonLedgerClient Client,
    Func<Party, CancellationToken, Task> GrantActAsAsync);

/// <summary>
/// Behavioral parity suite over the external-signing flow — generate topology, allocate an external
/// party, prepare, execute — run through one shared set of bodies. The suite holds the private key
/// itself, as an external signer does; a Fake lane cannot verify signatures, so on it the bodies
/// assert only what the contract promises, while the live lanes have the participant verify every
/// signature.
/// </summary>
/// <remarks>
/// Keys are ECDSA P-256. Per the Canton external-signing tutorials the signature covers the hash
/// bytes as data under ECDSA with SHA-256 (the hash is hashed again), encoded as an ASN.1 DER
/// sequence, and <see cref="LedgerSignature.SignedBy"/> is the key fingerprint returned by
/// <see cref="IAdminClient.GenerateExternalPartyTopologyAsync"/>.
/// </remarks>
public abstract class LedgerExternalSigningParityTests
{
    private const string GlobalSynchronizerAlias = "global";
    private static readonly TimeSpan CompletionBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StaleAuthorizationBackoff = TimeSpan.FromSeconds(2);
    private const int CompletionOpenAttempts = 4;

    /// <summary>Opens a lane over this provider's external-signing capability for one scenario.</summary>
    protected abstract Task<CapabilityLane<ExternalSigningCapability>> OpenExternalSigningAsync(
        ExternalSigningScenario scenario, CancellationToken cancellationToken);

    [Fact]
    public async Task AllocateExternalPartyAsync_allocates_the_generated_party_and_lists_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = ExternalSigningScenario.CreateUnique();
        await using var lane = await OpenExternalSigningAsync(scenario, cancellationToken);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var (generated, allocated) = await OnboardAsync(lane.Capability, scenario, key, cancellationToken);
        var known = await lane.Capability.Admin.ListKnownPartiesAsync(cancellationToken);

        allocated.Should().Be(generated.Party);
        generated.Party.Value.Should().StartWith(scenario.PartyHint + "::");
        generated.PublicKeyFingerprint.Should().NotBeNullOrWhiteSpace();
        generated.MultiHash.Length.Should().BeGreaterThan(0);
        known.Select(details => details.Party).Should().Contain(allocated);
    }

    [Fact]
    public async Task ExecuteSubmissionAndWaitAsync_commits_a_submission_signed_by_the_external_party()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = ExternalSigningScenario.CreateUnique();
        await using var lane = await OpenExternalSigningAsync(scenario, cancellationToken);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (generated, party) = await OnboardAsync(lane.Capability, scenario, key, cancellationToken);
        await lane.Capability.GrantActAsAsync(party, cancellationToken);

        var signed = await PrepareAndSignAsync(lane.Capability.Client, party, key, generated, scenario, cancellationToken);
        var executed = await lane.Capability.Client.ExecuteSubmissionAndWaitAsync(
            signed, cancellationToken: cancellationToken);
        var transaction = await lane.Capability.Client.GetUpdateByIdAsync(
            executed.UpdateId, new SubmitterInfo(party), cancellationToken: cancellationToken);

        executed.UpdateId.Should().NotBeNullOrWhiteSpace();
        transaction.UpdateId.Should().Be(executed.UpdateId);
        transaction.CreatedContracts.Select(created => created.TemplateId.EntityName).Should().Contain("Marker");
    }

    [Fact]
    public async Task ExecuteSubmissionAndWaitForTransactionAsync_returns_the_created_marker()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = ExternalSigningScenario.CreateUnique();
        await using var lane = await OpenExternalSigningAsync(scenario, cancellationToken);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (generated, party) = await OnboardAsync(lane.Capability, scenario, key, cancellationToken);
        await lane.Capability.GrantActAsAsync(party, cancellationToken);

        var signed = await PrepareAndSignAsync(lane.Capability.Client, party, key, generated, scenario, cancellationToken);
        var transaction = await lane.Capability.Client.ExecuteSubmissionAndWaitForTransactionAsync(
            signed, new SubmitterInfo(party), cancellationToken: cancellationToken);

        transaction.UpdateId.Should().NotBeNullOrWhiteSpace();
        transaction.CreatedContracts.Select(created => created.TemplateId.EntityName).Should().Contain("Marker");
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_is_accepted_and_its_completion_reaches_the_stream()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = ExternalSigningScenario.CreateUnique();
        await using var lane = await OpenExternalSigningAsync(scenario, cancellationToken);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (generated, party) = await OnboardAsync(lane.Capability, scenario, key, cancellationToken);
        await lane.Capability.GrantActAsAsync(party, cancellationToken);
        var client = lane.Capability.Client;
        var signed = await PrepareAndSignAsync(client, party, key, generated, scenario, cancellationToken);
        var endBeforeExecute = await client.GetLedgerEndAsync(cancellationToken: cancellationToken);

        await client.ExecuteSubmissionAsync(signed, cancellationToken: cancellationToken);
        var accepted = await FindAcceptedCompletionAsync(
            lane, party, endBeforeExecute, scenario.SubmissionId, cancellationToken);

        accepted.Should().NotBeNull(
            "the participant echoes the execution's submission id on its accepted completion");
        accepted!.UpdateId.Should().NotBeNullOrWhiteSpace();
    }

    private static async Task<(ExternalPartyTopology Generated, Party Allocated)> OnboardAsync(
        ExternalSigningCapability capability,
        ExternalSigningScenario scenario,
        ECDsa key,
        CancellationToken cancellationToken)
    {
        var synchronizer = await ResolveSynchronizerAsync(capability.Client, cancellationToken);
        var publicKey = new SigningPublicKey(
            PublicKeyFormat.DerX509SubjectPublicKeyInfo, key.ExportSubjectPublicKeyInfo(), SigningKeySpec.EcP256);

        var generated = await capability.Admin.GenerateExternalPartyTopologyAsync(
            new ExternalPartyTopologyRequest(synchronizer, scenario.PartyHint, publicKey), cancellationToken);
        var allocated = await capability.Admin.AllocateExternalPartyAsync(
            new ExternalPartyAllocation(
                synchronizer,
                generated.TopologyTransactions
                    .Select(transaction => new SignedTopologyTransaction(transaction, []))
                    .ToList(),
                [Sign(key, generated.MultiHash, generated.PublicKeyFingerprint)]),
            cancellationToken);

        return (generated, allocated);
    }

    private static async Task<SignedSubmission> PrepareAndSignAsync(
        ICantonLedgerClient client,
        Party party,
        ECDsa key,
        ExternalPartyTopology generated,
        ExternalSigningScenario scenario,
        CancellationToken cancellationToken)
    {
        var prepared = await client.PrepareSubmissionAsync(
            CommandsSubmission.Single(CreateCommand.For(new Marker(party)))
                .WithActAs(party)
                .WithCommandId(new CommandId(Guid.NewGuid().ToString())),
            cancellationToken: cancellationToken);

        prepared.Hash.Length.Should().BeGreaterThan(0);
        prepared.PreparedTransaction.Length.Should().BeGreaterThan(0);

        return new SignedSubmission(
            prepared,
            [new PartySignatures(party, [Sign(key, prepared.Hash, generated.PublicKeyFingerprint)])],
            scenario.SubmissionId);
    }

    private static async Task<SynchronizerId> ResolveSynchronizerAsync(
        ICantonLedgerClient client, CancellationToken cancellationToken)
    {
        var connected = await client.GetConnectedSynchronizersAsync(cancellationToken: cancellationToken);
        var chosen = connected.FirstOrDefault(
                synchronizer => synchronizer.SynchronizerAlias == GlobalSynchronizerAlias)
            ?? connected[0];
        return new SynchronizerId(chosen.SynchronizerId);
    }

    private static LedgerSignature Sign(ECDsa key, ReadOnlyMemory<byte> hash, string fingerprint) => new(
        SignatureFormat.Der,
        key.SignData(hash.Span, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence),
        fingerprint,
        SigningAlgorithm.EcDsaSha256);

    private static async Task<CompletionStreamEvent.CommandAccepted?> FindAcceptedCompletionAsync(
        CapabilityLane<ExternalSigningCapability> lane,
        Party party,
        LedgerOffset beginExclusive,
        string submissionId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= CompletionOpenAttempts; attempt++)
        {
            var window = await DrainWindowAsync(lane, party, beginExclusive, submissionId, cancellationToken);
            if (window.Accepted is not null || !window.SawStreamError)
            {
                return window.Accepted;
            }

            await Task.Delay(StaleAuthorizationBackoff, cancellationToken);
        }

        return null;
    }

    private static async Task<(CompletionStreamEvent.CommandAccepted? Accepted, bool SawStreamError)> DrainWindowAsync(
        CapabilityLane<ExternalSigningCapability> lane,
        Party party,
        LedgerOffset beginExclusive,
        string submissionId,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CompletionBudget);
        using var streamHold = await lane.HoldStreamAsync(cancellationToken);
        try
        {
            await foreach (var streamEvent in lane.Capability.Client.CompletionStreamAsync(
                new SubmitterInfo(party), beginExclusive, budget.Token))
            {
                switch (streamEvent)
                {
                    case CompletionStreamEvent.CommandAccepted accepted
                        when accepted.Completion.SubmissionId == submissionId:
                        return (accepted, false);
                    case CompletionStreamEvent.StreamError:
                        return (null, true);
                }
            }
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
        }

        return (null, false);
    }
}
