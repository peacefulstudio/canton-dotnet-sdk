// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Data;
using Google.Protobuf;
using Xunit;
using Interactive = Com.Daml.Ledger.Api.V2.Interactive;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class GrpcExternalSigningEncodingTests
{
    private static readonly Party Alice = new("alice::1220");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExecuteSubmissionAsync_rejects_an_undefined_hashing_scheme_version_before_sending()
    {
        var (clients, invoker) = ClientsOverRecordingInvoker();

        var act = () => clients.Ledger.ExecuteSubmissionAsync(
            Signed(version: (HashingSchemeVersion)99), cancellationToken: Ct);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("HashingSchemeVersion value 99");
        invoker.RecordedCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_rejects_an_undefined_signature_format_before_sending()
    {
        var (clients, invoker) = ClientsOverRecordingInvoker();

        var act = () => clients.Ledger.ExecuteSubmissionAsync(
            Signed(format: (SignatureFormat)99), cancellationToken: Ct);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("SignatureFormat value 99");
        invoker.RecordedCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_rejects_an_undefined_signing_algorithm_before_sending()
    {
        var (clients, invoker) = ClientsOverRecordingInvoker();

        var act = () => clients.Ledger.ExecuteSubmissionAsync(
            Signed(algorithm: (SigningAlgorithm)99), cancellationToken: Ct);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("SigningAlgorithm value 99");
        invoker.RecordedCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_rejects_an_undefined_signing_key_spec_before_sending()
    {
        var (clients, invoker) = ClientsOverRecordingInvoker();

        var act = () => clients.Admin.GenerateExternalPartyTopologyAsync(
            TopologyRequest(keySpec: (SigningKeySpec)99), Ct);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("SigningKeySpec value 99");
        invoker.RecordedCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_rejects_an_undefined_public_key_format_before_sending()
    {
        var (clients, invoker) = ClientsOverRecordingInvoker();

        var act = () => clients.Admin.GenerateExternalPartyTopologyAsync(
            TopologyRequest(keyFormat: (PublicKeyFormat)99), Ct);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("PublicKeyFormat value 99");
        invoker.RecordedCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task AllocateExternalPartyAsync_rejects_an_undefined_signature_format_before_sending()
    {
        var (clients, invoker) = ClientsOverRecordingInvoker();
        var allocation = new ExternalPartyAllocation(
            new SynchronizerId("sync::1"),
            [],
            [new LedgerSignature((SignatureFormat)99, new byte[] { 8 }, "fp-b", SigningAlgorithm.Ed25519)],
            null,
            null,
            null);

        var act = () => clients.Admin.AllocateExternalPartyAsync(allocation, Ct);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("SignatureFormat value 99");
        invoker.RecordedCalls.Should().BeEmpty();
    }

    private static (GrpcClients Clients, CannedResponseCallInvoker Invoker) ClientsOverRecordingInvoker()
    {
        var invoker = CannedResponseCallInvoker.Answering(new Interactive.ExecuteSubmissionResponse());
        return (GrpcClientHarness.CreateClients(invoker), invoker);
    }

    private static SignedSubmission Signed(
        HashingSchemeVersion version = HashingSchemeVersion.V3,
        SignatureFormat format = SignatureFormat.Der,
        SigningAlgorithm algorithm = SigningAlgorithm.EcDsaSha256) =>
        new(
            new PreparedSubmission(
                new Interactive.PreparedTransaction { Metadata = new Interactive.Metadata { TransactionUuid = "uuid-1" } }
                    .ToByteArray(),
                new byte[] { 9, 9 },
                version,
                null,
                null),
            [new PartySignatures(Alice, [new LedgerSignature(format, new byte[] { 1, 2, 3 }, "fp-1", algorithm)])],
            "sub-1",
            null,
            null);

    private static ExternalPartyTopologyRequest TopologyRequest(
        SigningKeySpec keySpec = SigningKeySpec.EcP256,
        PublicKeyFormat keyFormat = PublicKeyFormat.Der) =>
        new(
            new SynchronizerId("sync::1"),
            "carol",
            new SigningPublicKey(keyFormat, new byte[] { 4, 5, 6 }, keySpec),
            LocalParticipantObservationOnly: false,
            OtherConfirmingParticipantUids: null,
            ConfirmationThreshold: 1,
            ObservingParticipantUids: null);
}
