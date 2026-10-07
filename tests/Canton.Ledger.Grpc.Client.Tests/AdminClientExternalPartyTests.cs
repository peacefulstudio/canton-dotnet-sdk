// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Resilience;
using Com.Daml.Ledger.Api.V2.Admin;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using Status = Grpc.Core.Status;
using Wire = Com.Daml.Ledger.Api.V2;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class AdminClientExternalPartyTests : IDisposable
{
    private readonly LedgerClientOptions _options = new() { GrpcAddress = "https://localhost:5001" };
    private readonly GrpcChannel _channel;
    private readonly PartyManagementService.PartyManagementServiceClient _partyService;
    private readonly UserManagementService.UserManagementServiceClient _userService;
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    public AdminClientExternalPartyTests()
    {
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);
        var callInvoker = Substitute.For<CallInvoker>();
        _partyService = Substitute.ForPartsOf<PartyManagementService.PartyManagementServiceClient>(callInvoker);
        _userService = Substitute.ForPartsOf<UserManagementService.UserManagementServiceClient>(callInvoker);
    }

    public void Dispose() => _channel.Dispose();

    private AdminClient CreateClient() => new(_options, _channel, _partyService, _userService, _tokenProvider);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AsyncUnaryCall<TResponse> Ok<TResponse>(TResponse response) =>
        new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    private static AsyncUnaryCall<TResponse> Faulted<TResponse>(StatusCode code) =>
        new(
            Task.FromException<TResponse>(new RpcException(new Status(code, "boom"))),
            Task.FromResult(new Metadata()),
            () => new Status(code, "boom"),
            () => new Metadata(),
            () => { });

    private static ExternalPartyTopologyRequest TopologyRequest() =>
        new(
            new SynchronizerId("sync::1"),
            "carol",
            new SigningPublicKey(PublicKeyFormat.DerX509SubjectPublicKeyInfo, new byte[] { 4, 5, 6 }, SigningKeySpec.EcCurve25519),
            LocalParticipantObservationOnly: true,
            OtherConfirmingParticipantUids: ["PAR::a::1", "PAR::b::2"],
            ConfirmationThreshold: 2,
            ObservingParticipantUids: ["PAR::c::3"]);

    private static ExternalPartyAllocation Allocation(
        bool? waitForAllocation = null,
        string? identityProviderId = null,
        string? userId = null) =>
        new(
            new SynchronizerId("sync::1"),
            [
                new SignedTopologyTransaction(
                    new byte[] { 1, 1 },
                    [new LedgerSignature(SignatureFormat.Raw, new byte[] { 7 }, "fp-a", SigningAlgorithm.Ed25519)]),
            ],
            [new LedgerSignature(SignatureFormat.Concat, new byte[] { 8, 9 }, "fp-b", SigningAlgorithm.EcDsaSha384)],
            waitForAllocation,
            identityProviderId,
            userId);

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_maps_every_request_field()
    {
        GenerateExternalPartyTopologyRequest? captured = null;
        StubGenerate(new GenerateExternalPartyTopologyResponse { PartyId = "carol::fp" }, r => captured = r);

        await CreateClient().GenerateExternalPartyTopologyAsync(TopologyRequest(), Ct);

        captured!.Synchronizer.Should().Be("sync::1");
        captured.PartyHint.Should().Be("carol");
        captured.PublicKey.Format.Should().Be(Wire.CryptoKeyFormat.DerX509SubjectPublicKeyInfo);
        captured.PublicKey.KeyData.ToByteArray().Should().Equal(4, 5, 6);
        captured.PublicKey.KeySpec.Should().Be(Wire.SigningKeySpec.EcCurve25519);
        captured.LocalParticipantObservationOnly.Should().BeTrue();
        captured.OtherConfirmingParticipantUids.Should().Equal("PAR::a::1", "PAR::b::2");
        captured.ConfirmationThreshold.Should().Be(2u);
        captured.ObservingParticipantUids.Should().Equal("PAR::c::3");
    }

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_projects_the_response()
    {
        StubGenerate(new GenerateExternalPartyTopologyResponse
        {
            PartyId = "carol::fp",
            PublicKeyFingerprint = "fp",
            TopologyTransactions = { ByteString.CopyFrom(1, 2), ByteString.CopyFrom(3) },
            MultiHash = ByteString.CopyFrom(9, 8, 7),
        });

        var topology = await CreateClient().GenerateExternalPartyTopologyAsync(TopologyRequest(), Ct);

        topology.Party.Value.Should().Be("carol::fp");
        topology.PublicKeyFingerprint.Should().Be("fp");
        topology.TopologyTransactions.Select(t => t.ToArray()).Should().SatisfyRespectively(
            first => first.Should().Equal(1, 2),
            second => second.Should().Equal(3));
        topology.MultiHash.ToArray().Should().Equal(9, 8, 7);
    }

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_leaves_optional_lists_empty_when_not_given()
    {
        GenerateExternalPartyTopologyRequest? captured = null;
        StubGenerate(new GenerateExternalPartyTopologyResponse { PartyId = "carol::fp" }, r => captured = r);

        await CreateClient().GenerateExternalPartyTopologyAsync(
            TopologyRequest() with { OtherConfirmingParticipantUids = null, ObservingParticipantUids = null }, Ct);

        captured!.OtherConfirmingParticipantUids.Should().BeEmpty();
        captured.ObservingParticipantUids.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_throws_ArgumentNullException_for_a_null_request()
    {
        var act = () => CreateClient().GenerateExternalPartyTopologyAsync(null!, Ct);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task GenerateExternalPartyTopologyAsync_throws_ArgumentException_for_a_blank_party_hint(string hint)
    {
        var act = () => CreateClient().GenerateExternalPartyTopologyAsync(TopologyRequest() with { PartyIdHint = hint }, Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_throws_LedgerOperationException_for_a_participant_rejection()
    {
        _partyService.GenerateExternalPartyTopologyAsync(
                Arg.Any<GenerateExternalPartyTopologyRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<GenerateExternalPartyTopologyResponse>(StatusCode.InvalidArgument));

        var act = () => CreateClient().GenerateExternalPartyTopologyAsync(TopologyRequest(), Ct);

        (await act.Should().ThrowAsync<LedgerOperationException>()).Which.Status
            .Should().Be(new TransportStatus.Grpc(GrpcStatusCode.InvalidArgument));
    }

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_surfaces_caller_cancellation_as_OperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        _partyService.GenerateExternalPartyTopologyAsync(
                Arg.Do<GenerateExternalPartyTopologyRequest>(_ => cts.Cancel()),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<GenerateExternalPartyTopologyResponse>(StatusCode.Cancelled));

        var act = () => CreateClient().GenerateExternalPartyTopologyAsync(TopologyRequest(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_retries_a_transient_failure_when_retry_is_enabled()
    {
        EnableRetry();
        _partyService.GenerateExternalPartyTopologyAsync(
                Arg.Any<GenerateExternalPartyTopologyRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Faulted<GenerateExternalPartyTopologyResponse>(StatusCode.Unavailable),
                _ => Ok(new GenerateExternalPartyTopologyResponse { PartyId = "carol::fp" }));

        await CreateClient().GenerateExternalPartyTopologyAsync(TopologyRequest(), Ct);

        _ = _partyService.Received(2).GenerateExternalPartyTopologyAsync(
            Arg.Any<GenerateExternalPartyTopologyRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AllocateExternalPartyAsync_maps_every_allocation_field()
    {
        AllocateExternalPartyRequest? captured = null;
        StubAllocate(new AllocateExternalPartyResponse { PartyId = "carol::fp" }, r => captured = r);

        await CreateClient().AllocateExternalPartyAsync(
            Allocation(waitForAllocation: false, identityProviderId: "idp-1", userId: "user-1"), Ct);

        captured!.Synchronizer.Should().Be("sync::1");
        captured.IdentityProviderId.Should().Be("idp-1");
        captured.UserId.Should().Be("user-1");
        captured.HasWaitForAllocation.Should().BeTrue();
        captured.WaitForAllocation.Should().BeFalse();
        var onboarding = captured.OnboardingTransactions.Should().ContainSingle().Subject;
        onboarding.Transaction.ToByteArray().Should().Equal(1, 1);
        var signature = onboarding.Signatures.Should().ContainSingle().Subject;
        signature.Format.Should().Be(Wire.SignatureFormat.Raw);
        signature.Signature_.ToByteArray().Should().Equal(7);
        signature.SignedBy.Should().Be("fp-a");
        signature.SigningAlgorithmSpec.Should().Be(Wire.SigningAlgorithmSpec.Ed25519);
        var multiHash = captured.MultiHashSignatures.Should().ContainSingle().Subject;
        multiHash.Format.Should().Be(Wire.SignatureFormat.Concat);
        multiHash.Signature_.ToByteArray().Should().Equal(8, 9);
        multiHash.SigningAlgorithmSpec.Should().Be(Wire.SigningAlgorithmSpec.EcDsaSha384);
    }

    [Fact]
    public async Task AllocateExternalPartyAsync_leaves_optional_fields_unset_when_not_given()
    {
        AllocateExternalPartyRequest? captured = null;
        StubAllocate(new AllocateExternalPartyResponse { PartyId = "carol::fp" }, r => captured = r);

        await CreateClient().AllocateExternalPartyAsync(Allocation(), Ct);

        captured!.HasWaitForAllocation.Should().BeFalse();
        captured.IdentityProviderId.Should().BeEmpty();
        captured.UserId.Should().BeEmpty();
    }

    [Fact]
    public async Task AllocateExternalPartyAsync_returns_the_allocated_party()
    {
        StubAllocate(new AllocateExternalPartyResponse { PartyId = "carol::fp" });

        var party = await CreateClient().AllocateExternalPartyAsync(Allocation(), Ct);

        party.Value.Should().Be("carol::fp");
    }

    [Fact]
    public async Task AllocateExternalPartyAsync_raises_an_undecodable_body_failure_that_committed_when_the_response_names_no_party()
    {
        StubAllocate(new AllocateExternalPartyResponse());

        var act = () => CreateClient().AllocateExternalPartyAsync(Allocation(), Ct);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.Committed);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>()
            .Which.InnerException.Should().BeOfType<ArgumentException>();
    }

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_response_names_no_party()
    {
        StubGenerate(new GenerateExternalPartyTopologyResponse());

        var act = () => CreateClient().GenerateExternalPartyTopologyAsync(TopologyRequest(), Ct);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>()
            .Which.InnerException.Should().BeOfType<ArgumentException>();
    }

    [Fact]
    public async Task AllocateExternalPartyAsync_throws_ArgumentNullException_for_a_null_allocation()
    {
        var act = () => CreateClient().AllocateExternalPartyAsync(null!, Ct);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task AllocateExternalPartyAsync_throws_LedgerOperationException_for_a_participant_rejection()
    {
        _partyService.AllocateExternalPartyAsync(
                Arg.Any<AllocateExternalPartyRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<AllocateExternalPartyResponse>(StatusCode.AlreadyExists));

        var act = () => CreateClient().AllocateExternalPartyAsync(Allocation(), Ct);

        (await act.Should().ThrowAsync<LedgerOperationException>()).Which.Status
            .Should().Be(new TransportStatus.Grpc(GrpcStatusCode.AlreadyExists));
    }

    [Fact]
    public async Task AllocateExternalPartyAsync_surfaces_caller_cancellation_as_OperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        _partyService.AllocateExternalPartyAsync(
                Arg.Do<AllocateExternalPartyRequest>(_ => cts.Cancel()),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<AllocateExternalPartyResponse>(StatusCode.Cancelled));

        var act = () => CreateClient().AllocateExternalPartyAsync(Allocation(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AllocateExternalPartyAsync_is_not_retried_when_retry_is_enabled()
    {
        EnableRetry();
        _partyService.AllocateExternalPartyAsync(
                Arg.Any<AllocateExternalPartyRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<AllocateExternalPartyResponse>(StatusCode.Unavailable));

        var act = () => CreateClient().AllocateExternalPartyAsync(Allocation(), Ct);

        await act.Should().ThrowAsync<LedgerOperationException>();
        _ = _partyService.Received(1).AllocateExternalPartyAsync(
            Arg.Any<AllocateExternalPartyRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    private void EnableRetry() =>
        _options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 2, Delay = TimeSpan.Zero };

    private void StubGenerate(
        GenerateExternalPartyTopologyResponse response,
        Action<GenerateExternalPartyTopologyRequest>? capture = null) =>
        _partyService.GenerateExternalPartyTopologyAsync(
                Arg.Do<GenerateExternalPartyTopologyRequest>(r => capture?.Invoke(r)),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Ok(response));

    private void StubAllocate(
        AllocateExternalPartyResponse response,
        Action<AllocateExternalPartyRequest>? capture = null) =>
        _partyService.AllocateExternalPartyAsync(
                Arg.Do<AllocateExternalPartyRequest>(r => capture?.Invoke(r)),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Ok(response));
}
