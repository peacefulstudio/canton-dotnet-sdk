// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Behavioral parity suite over preparing, signing and executing a transaction that touches a
/// contract key (LF 2.3) with hashing scheme V3, run through one shared set of bodies. A participant
/// hashing under its default V2 refuses a keyed node, so each body asks for V3 explicitly and
/// asserts the prepared submission came back hashed under V3. Both bodies use the
/// <c>contractkeys</c> corpus <c>Account</c> template, keyed on its custodian and label.
/// </summary>
public abstract class LedgerKeyedExternalSigningParityTests
{
    private const long OpeningBalance = 100;
    private const long Credited = 50;

    /// <summary>Opens a lane over this provider's external-signing capability, with the <c>contractkeys</c> corpus available to it.</summary>
    protected abstract Task<CapabilityLane<ExternalSigningCapability>> OpenKeyedExternalSigningAsync(
        ExternalSigningScenario scenario, CancellationToken cancellationToken);

    [Fact]
    public async Task PrepareSubmissionAsync_with_V3_prepares_and_commits_a_keyed_create_signed_by_the_external_party()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = ExternalSigningScenario.CreateUnique();
        await using var lane = await OpenKeyedExternalSigningAsync(scenario, cancellationToken);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (generated, party) = await ExternalSigningSteps.OnboardAsync(lane.Capability, scenario, key, cancellationToken);
        await lane.Capability.GrantActAsAsync(party, cancellationToken);
        var client = lane.Capability.Client;

        var prepared = await PrepareV3Async(
            client, party, CreateCommand.For(new Account(party, scenario.PartyHint, OpeningBalance)), cancellationToken);
        var executed = await client.ExecuteSubmissionAndWaitAsync(
            Sign(prepared, party, key, generated, scenario), cancellationToken: cancellationToken);

        prepared.HashingSchemeVersion.Should().Be(HashingSchemeVersion.V3);
        ExternalSigningSteps.RecomputedHashHex(prepared).Should().Be(Convert.ToHexStringLower(prepared.Hash.Span));
        executed.UpdateId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PrepareSubmissionAsync_with_V3_prepares_and_commits_an_exercise_by_key_signed_by_the_external_party()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = ExternalSigningScenario.CreateUnique();
        await using var lane = await OpenKeyedExternalSigningAsync(scenario, cancellationToken);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (generated, party) = await ExternalSigningSteps.OnboardAsync(lane.Capability, scenario, key, cancellationToken);
        await lane.Capability.GrantActAsAsync(party, cancellationToken);
        var client = lane.Capability.Client;
        var keyedCreate = await PrepareV3Async(
            client, party, CreateCommand.For(new Account(party, scenario.PartyHint, OpeningBalance)), cancellationToken);
        await client.ExecuteSubmissionAndWaitAsync(
            Sign(keyedCreate, party, key, generated, scenario with { SubmissionId = scenario.SubmissionId + "-create" }),
            cancellationToken: cancellationToken);

        var prepared = await PrepareV3Async(
            client,
            party,
            Account.CreditByKeyCommand(new AccountKey(party, scenario.PartyHint), new Account.Credit(Credited)),
            cancellationToken);
        var transaction = await client.ExecuteSubmissionAndWaitForTransactionAsync(
            Sign(prepared, party, key, generated, scenario), new SubmitterInfo(party), cancellationToken: cancellationToken);

        prepared.HashingSchemeVersion.Should().Be(HashingSchemeVersion.V3);
        ExternalSigningSteps.RecomputedHashHex(prepared).Should().Be(Convert.ToHexStringLower(prepared.Hash.Span));
        transaction.UpdateId.Should().NotBeNullOrWhiteSpace();
        var credited = transaction.CreatedContracts.Should().ContainSingle(
            created => created.TemplateId.EntityName == "Account").Subject;
        Account.FromRecord(credited.Payload).Should().Be(new Account(party, scenario.PartyHint, 150));
    }

    private static Task<PreparedSubmission> PrepareV3Async(
        ICantonLedgerClient client, Party party, ICommand command, CancellationToken cancellationToken) =>
        client.PrepareSubmissionAsync(
            CommandsSubmission.Single(command)
                .WithActAs(party)
                .WithCommandId(new CommandId(Guid.NewGuid().ToString())),
            HashingSchemeVersion.V3,
            cancellationToken: cancellationToken);

    private static SignedSubmission Sign(
        PreparedSubmission prepared,
        Party party,
        ECDsa key,
        ExternalPartyTopology generated,
        ExternalSigningScenario scenario) =>
        new(
            prepared,
            [new PartySignatures(party, [ExternalSigningSteps.Sign(key, prepared.Hash, generated.PublicKeyFingerprint)])],
            scenario.SubmissionId);
}
