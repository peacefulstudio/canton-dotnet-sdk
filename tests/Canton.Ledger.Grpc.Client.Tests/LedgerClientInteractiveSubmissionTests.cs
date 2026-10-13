// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Resilience;
using Daml.Runtime;
using Daml.Runtime.Data;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;
using Interactive = Com.Daml.Ledger.Api.V2.Interactive;
using RuntimeCommands = Daml.Runtime.Commands;
using Status = Grpc.Core.Status;
using Wire = Com.Daml.Ledger.Api.V2;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class LedgerClientInteractiveSubmissionTests : IDisposable
{
    private static readonly Party Alice = new("alice::1220");

    private readonly LedgerClientOptions _options;
    private readonly GrpcChannel _channel;
    private readonly Interactive.InteractiveSubmissionService.InteractiveSubmissionServiceClient _service;
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    public LedgerClientInteractiveSubmissionTests()
    {
        _options = new LedgerClientOptions
        {
            GrpcAddress = "https://localhost:5001",
            UserId = "test-user",
        };
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);
        _service = Substitute.ForPartsOf<Interactive.InteractiveSubmissionService.InteractiveSubmissionServiceClient>(
            Substitute.For<CallInvoker>());
    }

    public void Dispose() => _channel.Dispose();

    private LedgerClient CreateClient() => new(
        _options,
        _channel,
        Substitute.ForPartsOf<Wire.CommandService.CommandServiceClient>(Substitute.For<CallInvoker>()),
        Substitute.ForPartsOf<Wire.UpdateService.UpdateServiceClient>(Substitute.For<CallInvoker>()),
        Substitute.ForPartsOf<Wire.StateService.StateServiceClient>(Substitute.For<CallInvoker>()),
        Substitute.ForPartsOf<Wire.CommandSubmissionService.CommandSubmissionServiceClient>(Substitute.For<CallInvoker>()),
        Substitute.ForPartsOf<Wire.CommandCompletionService.CommandCompletionServiceClient>(Substitute.For<CallInvoker>()),
        _tokenProvider,
        interactiveSubmissionService: _service);

    private void EnableRetry() =>
        _options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 2, Delay = TimeSpan.Zero };

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

    private static AsyncUnaryCall<TResponse> Faulted<TResponse>(RpcException exception) =>
        new(
            Task.FromException<TResponse>(exception),
            Task.FromResult(new Metadata()),
            () => exception.Status,
            () => new Metadata(),
            () => { });

    private static RuntimeCommands.CommandsSubmission Submission() =>
        RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(
                new Identifier("pkg", "Module", "Template"),
                new DamlRecord(null, [])))
            .WithActAs(Alice)
            .WithCommandId(new RuntimeCommands.CommandId("cmd-1"));

    private static Interactive.PrepareSubmissionResponse PreparedResponse() =>
        new()
        {
            PreparedTransaction = new Interactive.PreparedTransaction(),
            PreparedTransactionHash = ByteString.CopyFrom(Convert.FromHexString(EmptyTransactionV2Hash)),
            HashingSchemeVersion = Interactive.HashingSchemeVersion.V2,
        };

    private static byte[] PreparedTransactionBytes() =>
        new Interactive.PreparedTransaction { Metadata = new Interactive.Metadata { TransactionUuid = "uuid-1" } }
            .ToByteArray();

    private static SignedSubmission Signed(
        string submissionId = "sub-1",
        RuntimeCommands.DeduplicationPeriod? deduplication = null,
        RuntimeCommands.MinLedgerTime? minLedgerTime = null,
        byte[]? preparedTransaction = null,
        HashingSchemeVersion version = HashingSchemeVersion.V3,
        SigningAlgorithm algorithm = SigningAlgorithm.EcDsaSha256) =>
        new(
            new PreparedSubmission(
                preparedTransaction ?? PreparedTransactionBytes(),
                new byte[] { 9, 9 },
                version,
                null,
                null),
            [
                new PartySignatures(
                    Alice,
                    [new LedgerSignature(SignatureFormat.Der, new byte[] { 1, 2, 3 }, "fp-1", algorithm)]),
            ],
            submissionId,
            deduplication,
            minLedgerTime);

    private static RuntimeCommands.SubmitterInfo Submitter() => new(new HashSet<Party> { Alice });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Wire.Transaction CommittedTransaction = new() { UpdateId = "u-2", Offset = 9L };

    [Fact]
    public async Task PrepareSubmissionAsync_maps_the_submission_without_asking_for_cost_estimation()
    {
        Interactive.PrepareSubmissionRequest? captured = null;
        _service.PrepareSubmissionAsync(
                Arg.Do<Interactive.PrepareSubmissionRequest>(r => captured = r),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Ok(PreparedResponse()));

        await CreateClient().PrepareSubmissionAsync(Submission(), cancellationToken: Ct);

        captured.Should().NotBeNull();
        captured!.UserId.Should().Be("test-user");
        captured.CommandId.Should().Be("cmd-1");
        captured.ActAs.Should().Equal("alice::1220");
        captured.Commands.Should().ContainSingle();
        captured.EstimateTrafficCost.Should().BeNull();
    }

    [Fact]
    public async Task PrepareSubmissionAsync_maps_an_absolute_min_ledger_time()
    {
        Interactive.PrepareSubmissionRequest? captured = null;
        StubPrepare(r => captured = r);
        var bound = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

        await CreateClient().PrepareSubmissionAsync(
            Submission().WithMinLedgerTime(new RuntimeCommands.MinLedgerTime.Absolute(bound)),
            cancellationToken: Ct);

        captured!.MinLedgerTime.MinLedgerTimeAbs.ToDateTimeOffset().Should().Be(bound);
        captured.MinLedgerTime.MinLedgerTimeRel.Should().BeNull();
    }

    [Fact]
    public async Task PrepareSubmissionAsync_maps_a_relative_min_ledger_time()
    {
        Interactive.PrepareSubmissionRequest? captured = null;
        StubPrepare(r => captured = r);

        await CreateClient().PrepareSubmissionAsync(
            Submission().WithMinLedgerTime(new RuntimeCommands.MinLedgerTime.Relative(TimeSpan.FromSeconds(90))),
            cancellationToken: Ct);

        captured!.MinLedgerTime.MinLedgerTimeRel.ToTimeSpan().Should().Be(TimeSpan.FromSeconds(90));
        captured.MinLedgerTime.MinLedgerTimeAbs.Should().BeNull();
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_maps_a_relative_min_ledger_time()
    {
        Interactive.PrepareSubmissionRequest? captured = null;
        StubPrepare(r => captured = r);

        await CreateClient().EstimateTrafficCostAsync(
            Submission().WithMinLedgerTime(new RuntimeCommands.MinLedgerTime.Relative(TimeSpan.FromSeconds(90))),
            cancellationToken: Ct);

        captured!.MinLedgerTime.MinLedgerTimeRel.ToTimeSpan().Should().Be(TimeSpan.FromSeconds(90));
    }

    private void StubPrepare(Action<Interactive.PrepareSubmissionRequest> capture) =>
        _service.PrepareSubmissionAsync(
                Arg.Do(capture),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Ok(PreparedResponse()));

    private const string EmptyTransactionV2Hash = "d5acdc7f600596f7f03c12ff6dda5c48c7f479bf1fa73de82ecfa62e9dbc61e3";
    private const string Uuid7TransactionV3Hash = "d6369ee1b1d5eb324a7e781c94e1f4407a284db0c969d578f924b4ffaf2f1763";
    private const string EmptyTransactionV3Hash = "ff796e29747acf79ead358a3156a18b95730e8b7492669a124cb78752edf53ef";

    private void StubPrepareOfEmptyTransaction(string hashHex, int wireScheme) =>
        StubPrepare(new Interactive.PrepareSubmissionResponse
        {
            PreparedTransaction = new Interactive.PreparedTransaction(),
            PreparedTransactionHash = ByteString.CopyFrom(Convert.FromHexString(hashHex)),
            HashingSchemeVersion = (Interactive.HashingSchemeVersion)wireScheme,
        });

    [Theory]
    [InlineData(EmptyTransactionV2Hash, 2)]
    [InlineData(EmptyTransactionV3Hash, 3)]
    public async Task PrepareSubmissionAsync_returns_a_prepared_submission_whose_hash_matches_its_transaction(
        string hashHex, int wireScheme)
    {
        StubPrepareOfEmptyTransaction(hashHex, wireScheme);

        var result = await CreateClient().PrepareSubmissionAsync(Submission(), cancellationToken: Ct);

        Convert.ToHexStringLower(result.Hash.Span).Should().Be(hashHex);
    }

    [Fact]
    public async Task PrepareSubmissionAsync_refuses_a_hash_that_does_not_match_its_transaction_as_undecodable()
    {
        StubPrepareOfEmptyTransaction(EmptyTransactionV3Hash, 2);

        var act = () => CreateClient().PrepareSubmissionAsync(Submission(), cancellationToken: Ct);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        var mismatch = thrown.InnerException.Should().BeOfType<PreparedHashMismatchException>().Subject;
        Convert.ToHexStringLower(mismatch.ReportedHash.Span).Should().Be(EmptyTransactionV3Hash);
        Convert.ToHexStringLower(mismatch.RecomputedHash.Span).Should().Be(EmptyTransactionV2Hash);
    }

    [Fact]
    public async Task PrepareSubmissionAsync_refuses_a_scheme_it_cannot_recompute_as_undecodable()
    {
        StubPrepareOfEmptyTransaction(EmptyTransactionV3Hash, 4);

        var act = () => CreateClient().PrepareSubmissionAsync(Submission(), cancellationToken: Ct);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>()
            .Which.Detail.Should().Contain("scheme V4, which this SDK cannot recompute");
    }

    [Fact]
    public async Task PrepareSubmissionAsync_projects_every_field_of_the_response()
    {
        var prepared = new Interactive.PreparedTransaction { Metadata = new Interactive.Metadata { TransactionUuid = "uuid-7" } };
        StubPrepare(new Interactive.PrepareSubmissionResponse
        {
            PreparedTransaction = prepared,
            PreparedTransactionHash = ByteString.CopyFrom(Convert.FromHexString(Uuid7TransactionV3Hash)),
            HashingSchemeVersion = Interactive.HashingSchemeVersion.V3,
            HashingDetails = "details",
            CostEstimation = new Interactive.CostEstimation { TotalTrafficCostEstimation = 4096UL },
        });

        var result = await CreateClient().PrepareSubmissionAsync(Submission(), cancellationToken: Ct);

        Convert.ToHexStringLower(result.Hash.Span).Should().Be(Uuid7TransactionV3Hash);
        result.HashingSchemeVersion.Should().Be((HashingSchemeVersion)3);
        result.HashingDetails.Should().Be("details");
        result.CostEstimate!.TotalCost.Should().Be(4096L);
        Interactive.PreparedTransaction.Parser.ParseFrom(result.PreparedTransaction.ToArray())
            .Metadata.TransactionUuid.Should().Be("uuid-7");
    }

    [Fact]
    public async Task PrepareSubmissionAsync_leaves_details_and_estimate_null_when_the_participant_sends_none()
    {
        StubPrepare(PreparedResponse());

        var result = await CreateClient().PrepareSubmissionAsync(Submission(), cancellationToken: Ct);

        result.HashingDetails.Should().BeNull();
        result.CostEstimate.Should().BeNull();
        result.HashingSchemeVersion.Should().Be(HashingSchemeVersion.V2);
    }

    [Theory]
    [InlineData(2, HashingSchemeVersion.V2, EmptyTransactionV2Hash)]
    [InlineData(3, HashingSchemeVersion.V3, EmptyTransactionV3Hash)]
    public async Task PrepareSubmissionAsync_maps_the_wire_hashing_scheme_version_to_the_public_member(
        int wireValue, HashingSchemeVersion expected, string hashHex)
    {
        StubPrepareOfEmptyTransaction(hashHex, wireValue);

        var result = await CreateClient().PrepareSubmissionAsync(Submission(), cancellationToken: Ct);

        result.HashingSchemeVersion.Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(99)]
    public async Task PrepareSubmissionAsync_rejects_a_wire_hashing_scheme_version_it_does_not_know_as_undecodable(
        int wireValue)
    {
        StubPrepare(new Interactive.PrepareSubmissionResponse
        {
            PreparedTransaction = new Interactive.PreparedTransaction(),
            PreparedTransactionHash = ByteString.CopyFrom(new byte[32]),
            HashingSchemeVersion = (Interactive.HashingSchemeVersion)wireValue,
        });

        var act = () => CreateClient().PrepareSubmissionAsync(Submission(), cancellationToken: Ct);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Message.Should().Contain($"HashingSchemeVersion value {wireValue}");
    }

    [Theory]
    [InlineData(HashingSchemeVersion.V2, 2, EmptyTransactionV2Hash)]
    [InlineData(HashingSchemeVersion.V3, 3, EmptyTransactionV3Hash)]
    public async Task PrepareSubmissionAsync_sends_the_requested_hashing_scheme_as_its_wire_value(
        HashingSchemeVersion requested, int expectedWireValue, string hashHex)
    {
        Interactive.PrepareSubmissionRequest? captured = null;
        _service.PrepareSubmissionAsync(
                Arg.Do<Interactive.PrepareSubmissionRequest>(r => captured = r),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Ok(new Interactive.PrepareSubmissionResponse
            {
                PreparedTransaction = new Interactive.PreparedTransaction(),
                PreparedTransactionHash = ByteString.CopyFrom(Convert.FromHexString(hashHex)),
                HashingSchemeVersion = (Interactive.HashingSchemeVersion)expectedWireValue,
            }));

        var result = await CreateClient().PrepareSubmissionAsync(Submission(), requested, cancellationToken: Ct);

        ((int)captured!.HashingSchemeVersion).Should().Be(expectedWireValue);
        result.HashingSchemeVersion.Should().Be(requested);
    }

    [Fact]
    public async Task PrepareSubmissionAsync_sends_a_requested_V4_as_wire_value_4_and_refuses_the_answer_it_cannot_recompute()
    {
        Interactive.PrepareSubmissionRequest? captured = null;
        _service.PrepareSubmissionAsync(
                Arg.Do<Interactive.PrepareSubmissionRequest>(r => captured = r),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Ok(new Interactive.PrepareSubmissionResponse
            {
                PreparedTransaction = new Interactive.PreparedTransaction(),
                PreparedTransactionHash = ByteString.CopyFrom(Convert.FromHexString(EmptyTransactionV3Hash)),
                HashingSchemeVersion = Interactive.HashingSchemeVersion.V4,
            }));

        var act = () => CreateClient().PrepareSubmissionAsync(Submission(), HashingSchemeVersion.V4, cancellationToken: Ct);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        ((int)captured!.HashingSchemeVersion).Should().Be(4);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>()
            .Which.Detail.Should().Contain("scheme V4, which this SDK cannot recompute");
    }

    [Fact]
    public async Task PrepareSubmissionAsync_rejects_a_requested_hashing_scheme_it_cannot_name_before_any_call()
    {
        var act = () => CreateClient().PrepareSubmissionAsync(Submission(), (HashingSchemeVersion)99, cancellationToken: Ct);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("hashingSchemeVersion");
        _service.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareSubmissionAsync_leaves_the_wire_hashing_scheme_unspecified_when_none_is_requested()
    {
        Interactive.PrepareSubmissionRequest? captured = null;
        StubPrepare(r => captured = r);

        await CreateClient().PrepareSubmissionAsync(Submission(), cancellationToken: Ct);

        ((int)captured!.HashingSchemeVersion).Should().Be(0);
    }

    [Fact]
    public async Task EstimateTrafficCostAsync_leaves_the_wire_hashing_scheme_unspecified()
    {
        Interactive.PrepareSubmissionRequest? captured = null;
        StubPrepare(r => captured = r);

        await CreateClient().EstimateTrafficCostAsync(Submission(), cancellationToken: Ct);

        ((int)captured!.HashingSchemeVersion).Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public async Task PrepareSubmissionAsync_rejects_a_hash_that_is_not_32_bytes_as_undecodable(int hashLength)
    {
        StubPrepare(new Interactive.PrepareSubmissionResponse
        {
            PreparedTransaction = new Interactive.PreparedTransaction(),
            PreparedTransactionHash = ByteString.CopyFrom(new byte[hashLength]),
            HashingSchemeVersion = Interactive.HashingSchemeVersion.V2,
        });

        var act = () => CreateClient().PrepareSubmissionAsync(Submission(), cancellationToken: Ct);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Message.Should().Contain($"{hashLength} bytes").And.Contain("32");
    }

    [Fact]
    public async Task PrepareSubmissionAsync_rejects_a_response_scheme_that_differs_from_the_requested_one_as_undecodable()
    {
        StubPrepare(new Interactive.PrepareSubmissionResponse
        {
            PreparedTransaction = new Interactive.PreparedTransaction(),
            PreparedTransactionHash = ByteString.CopyFrom(new byte[32]),
            HashingSchemeVersion = Interactive.HashingSchemeVersion.V2,
        });

        var act = () => CreateClient().PrepareSubmissionAsync(Submission(), HashingSchemeVersion.V3, cancellationToken: Ct);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Message.Should().Contain("V3").And.Contain("V2");
    }

    [Fact]
    public async Task PrepareSubmissionAsync_accepts_any_scheme_the_participant_chooses_when_none_is_requested()
    {
        StubPrepare(new Interactive.PrepareSubmissionResponse
        {
            PreparedTransaction = new Interactive.PreparedTransaction(),
            PreparedTransactionHash = ByteString.CopyFrom(Convert.FromHexString(EmptyTransactionV3Hash)),
            HashingSchemeVersion = Interactive.HashingSchemeVersion.V3,
        });

        var result = await CreateClient().PrepareSubmissionAsync(Submission(), cancellationToken: Ct);

        result.HashingSchemeVersion.Should().Be(HashingSchemeVersion.V3);
    }

    [Fact]
    public async Task PrepareSubmissionAsync_throws_InvalidOperationException_when_the_response_has_no_prepared_transaction()
    {
        StubPrepare(new Interactive.PrepareSubmissionResponse());

        var act = () => CreateClient().PrepareSubmissionAsync(Submission(), cancellationToken: Ct);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*without a prepared transaction*");
    }

    [Fact]
    public async Task PrepareSubmissionAsync_throws_ArgumentNullException_for_a_null_submission()
    {
        var act = () => CreateClient().PrepareSubmissionAsync(null!, cancellationToken: Ct);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PrepareSubmissionAsync_surfaces_a_participant_rejection_as_a_not_committed_ledger_operation_failure()
    {
        _service.PrepareSubmissionAsync(
                Arg.Any<Interactive.PrepareSubmissionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<Interactive.PrepareSubmissionResponse>(StatusCode.InvalidArgument));

        var act = () => CreateClient().PrepareSubmissionAsync(Submission(), cancellationToken: Ct);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.InvalidArgument));
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeOfType<RpcException>();
    }

    [Fact]
    public async Task PrepareSubmissionAsync_surfaces_caller_cancellation_as_OperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        _service.PrepareSubmissionAsync(
                Arg.Do<Interactive.PrepareSubmissionRequest>(_ => cts.Cancel()),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<Interactive.PrepareSubmissionResponse>(StatusCode.Cancelled));

        var act = () => CreateClient().PrepareSubmissionAsync(Submission(), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PrepareSubmissionAsync_retries_a_transient_failure_when_retry_is_enabled()
    {
        EnableRetry();
        _service.PrepareSubmissionAsync(
                Arg.Any<Interactive.PrepareSubmissionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Faulted<Interactive.PrepareSubmissionResponse>(StatusCode.Unavailable),
                _ => Ok(PreparedResponse()));

        await CreateClient().PrepareSubmissionAsync(Submission(), cancellationToken: Ct);

        _ = _service.Received(2).PrepareSubmissionAsync(
            Arg.Any<Interactive.PrepareSubmissionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_maps_the_signed_submission_onto_the_request()
    {
        Interactive.ExecuteSubmissionRequest? captured = null;
        StubExecute(r => captured = r);

        await CreateClient().ExecuteSubmissionAsync(
            Signed(
                deduplication: new RuntimeCommands.DeduplicationPeriod.Offset(LedgerOffset.At(42)),
                minLedgerTime: new RuntimeCommands.MinLedgerTime.Absolute(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero))),
            cancellationToken: Ct);

        captured.Should().NotBeNull();
        captured!.UserId.Should().Be("test-user");
        captured.SubmissionId.Should().Be("sub-1");
        captured.HashingSchemeVersion.Should().Be((Interactive.HashingSchemeVersion)3);
        captured.PreparedTransaction.Metadata.TransactionUuid.Should().Be("uuid-1");
        captured.DeduplicationPeriodCase.Should().Be(Interactive.ExecuteSubmissionRequest.DeduplicationPeriodOneofCase.DeduplicationOffset);
        captured.DeduplicationOffset.Should().Be(42L);
        captured.MinLedgerTime.MinLedgerTimeAbs.Should().Be(Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)));
        var party = captured.PartySignatures.Signatures.Should().ContainSingle().Subject;
        party.Party.Should().Be("alice::1220");
        var signature = party.Signatures.Should().ContainSingle().Subject;
        signature.Format.Should().Be(Wire.SignatureFormat.Der);
        signature.Signature_.ToByteArray().Should().Equal(1, 2, 3);
        signature.SignedBy.Should().Be("fp-1");
        signature.SigningAlgorithmSpec.Should().Be(Wire.SigningAlgorithmSpec.EcDsaSha256);
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_sends_hashing_scheme_version_4_as_wire_number_4()
    {
        Interactive.ExecuteSubmissionRequest? captured = null;
        StubExecute(r => captured = r);

        await CreateClient().ExecuteSubmissionAsync(Signed(version: HashingSchemeVersion.V4), cancellationToken: Ct);

        ((int)captured!.HashingSchemeVersion).Should().Be(4);
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_sends_the_ML_DSA_65_signing_algorithm_as_wire_number_4()
    {
        Interactive.ExecuteSubmissionRequest? captured = null;
        StubExecute(r => captured = r);

        await CreateClient().ExecuteSubmissionAsync(Signed(algorithm: SigningAlgorithm.MlDsa65), cancellationToken: Ct);

        var signature = captured!.PartySignatures.Signatures.Single().Signatures.Single();
        ((int)signature.SigningAlgorithmSpec).Should().Be(4);
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_maps_a_duration_deduplication_and_a_relative_min_ledger_time()
    {
        Interactive.ExecuteSubmissionRequest? captured = null;
        StubExecute(r => captured = r);

        await CreateClient().ExecuteSubmissionAsync(
            Signed(
                deduplication: new RuntimeCommands.DeduplicationPeriod.Duration(TimeSpan.FromSeconds(30)),
                minLedgerTime: new RuntimeCommands.MinLedgerTime.Relative(TimeSpan.FromSeconds(5))),
            cancellationToken: Ct);

        captured!.DeduplicationDuration.Should().Be(Duration.FromTimeSpan(TimeSpan.FromSeconds(30)));
        captured.MinLedgerTime.MinLedgerTimeRel.Should().Be(Duration.FromTimeSpan(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_sets_no_deduplication_or_min_ledger_time_when_none_is_given()
    {
        Interactive.ExecuteSubmissionRequest? captured = null;
        StubExecute(r => captured = r);

        await CreateClient().ExecuteSubmissionAsync(Signed(), cancellationToken: Ct);

        captured!.DeduplicationPeriodCase.Should().Be(Interactive.ExecuteSubmissionRequest.DeduplicationPeriodOneofCase.None);
        captured.MinLedgerTime.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_throws_ArgumentNullException_for_a_null_submission()
    {
        var act = () => CreateClient().ExecuteSubmissionAsync(null!, cancellationToken: Ct);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteSubmissionAsync_throws_ArgumentException_for_a_blank_submission_id(string submissionId)
    {
        var act = () => CreateClient().ExecuteSubmissionAsync(Signed(submissionId), cancellationToken: Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_throws_ArgumentException_for_bytes_that_are_not_a_prepared_transaction()
    {
        var act = () => CreateClient().ExecuteSubmissionAsync(
            Signed(preparedTransaction: [0xFF, 0xFF, 0xFF]), cancellationToken: Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_surfaces_a_participant_rejection_as_an_unknown_commit_ledger_operation_failure()
    {
        _service.ExecuteSubmissionAsync(
                Arg.Any<Interactive.ExecuteSubmissionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<Interactive.ExecuteSubmissionResponse>(StatusCode.FailedPrecondition));

        var act = () => CreateClient().ExecuteSubmissionAsync(Signed(), cancellationToken: Ct);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.FailedPrecondition));
        thrown.CommitState.Should().Be(CommitState.Unknown);
        thrown.InnerException.Should().BeOfType<RpcException>();
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_surfaces_caller_cancellation_as_OperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        _service.ExecuteSubmissionAsync(
                Arg.Do<Interactive.ExecuteSubmissionRequest>(_ => cts.Cancel()),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<Interactive.ExecuteSubmissionResponse>(StatusCode.Cancelled));

        var act = () => CreateClient().ExecuteSubmissionAsync(Signed(), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteSubmissionAndWaitAsync_maps_the_request_and_projects_update_id_and_offset()
    {
        Interactive.ExecuteSubmissionAndWaitRequest? captured = null;
        _service.ExecuteSubmissionAndWaitAsync(
                Arg.Do<Interactive.ExecuteSubmissionAndWaitRequest>(r => captured = r),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Ok(new Interactive.ExecuteSubmissionAndWaitResponse { UpdateId = "u-1", CompletionOffset = 7L }));

        var result = await CreateClient().ExecuteSubmissionAndWaitAsync(
            Signed(deduplication: new RuntimeCommands.DeduplicationPeriod.Offset(LedgerOffset.At(3))),
            cancellationToken: Ct);

        result.UpdateId.Should().Be("u-1");
        result.CompletionOffset.Value.Should().Be(7L);
        captured!.UserId.Should().Be("test-user");
        captured.SubmissionId.Should().Be("sub-1");
        captured.DeduplicationOffset.Should().Be(3L);
        captured.PartySignatures.Signatures.Should().ContainSingle();
    }

    [Fact]
    public async Task ExecuteSubmissionAndWaitAsync_throws_ArgumentException_for_a_blank_submission_id()
    {
        var act = () => CreateClient().ExecuteSubmissionAndWaitAsync(Signed(" "), cancellationToken: Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteSubmissionAndWaitForTransactionAsync_requests_a_format_for_the_submitter_and_projects_the_transaction()
    {
        Interactive.ExecuteSubmissionAndWaitForTransactionRequest? captured = null;
        _service.ExecuteSubmissionAndWaitForTransactionAsync(
                Arg.Do<Interactive.ExecuteSubmissionAndWaitForTransactionRequest>(r => captured = r),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Ok(new Interactive.ExecuteSubmissionAndWaitForTransactionResponse { Transaction = CommittedTransaction }));

        var result = await CreateClient().ExecuteSubmissionAndWaitForTransactionAsync(
            Signed(), Submitter(), cancellationToken: Ct);

        result.UpdateId.Should().Be("u-2");
        result.CompletionOffset.Value.Should().Be(9L);
        captured!.SubmissionId.Should().Be("sub-1");
        captured.UserId.Should().Be("test-user");
        captured.TransactionFormat.EventFormat.FiltersByParty.Keys.Should().Equal("alice::1220");
        captured.TransactionFormat.TransactionShape.Should().Be(Wire.TransactionShape.LedgerEffects);
    }

    [Fact]
    public async Task ExecuteSubmissionAndWaitForTransactionAsync_surfaces_a_participant_rejection_as_an_unknown_commit_ledger_operation_failure()
    {
        _service.ExecuteSubmissionAndWaitForTransactionAsync(
                Arg.Any<Interactive.ExecuteSubmissionAndWaitForTransactionRequest>(),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<Interactive.ExecuteSubmissionAndWaitForTransactionResponse>(StatusCode.NotFound));

        var act = () => CreateClient().ExecuteSubmissionAndWaitForTransactionAsync(Signed(), Submitter(), cancellationToken: Ct);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.NotFound));
        thrown.CommitState.Should().Be(CommitState.Unknown);
        thrown.InnerException.Should().BeOfType<RpcException>();
    }

    [Fact]
    public async Task ExecuteSubmission_variants_are_not_retried_when_retry_is_enabled()
    {
        EnableRetry();
        _service.ExecuteSubmissionAsync(
                Arg.Any<Interactive.ExecuteSubmissionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<Interactive.ExecuteSubmissionResponse>(StatusCode.Unavailable));
        _service.ExecuteSubmissionAndWaitAsync(
                Arg.Any<Interactive.ExecuteSubmissionAndWaitRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<Interactive.ExecuteSubmissionAndWaitResponse>(StatusCode.Unavailable));
        _service.ExecuteSubmissionAndWaitForTransactionAsync(
                Arg.Any<Interactive.ExecuteSubmissionAndWaitForTransactionRequest>(),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<Interactive.ExecuteSubmissionAndWaitForTransactionResponse>(StatusCode.Unavailable));
        var client = CreateClient();

        await ((Func<Task>)(() => client.ExecuteSubmissionAsync(Signed(), cancellationToken: Ct))).Should().ThrowAsync<LedgerOperationException>();
        await ((Func<Task>)(() => client.ExecuteSubmissionAndWaitAsync(Signed(), cancellationToken: Ct))).Should().ThrowAsync<LedgerOperationException>();
        await ((Func<Task>)(() => client.ExecuteSubmissionAndWaitForTransactionAsync(Signed(), Submitter(), cancellationToken: Ct)))
            .Should().ThrowAsync<LedgerOperationException>();

        _ = _service.Received(1).ExecuteSubmissionAsync(
            Arg.Any<Interactive.ExecuteSubmissionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
        _ = _service.Received(1).ExecuteSubmissionAndWaitAsync(
            Arg.Any<Interactive.ExecuteSubmissionAndWaitRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
        _ = _service.Received(1).ExecuteSubmissionAndWaitForTransactionAsync(
            Arg.Any<Interactive.ExecuteSubmissionAndWaitForTransactionRequest>(),
            Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPreferredPackagesAsync_maps_requirements_and_projects_references()
    {
        Interactive.GetPreferredPackagesRequest? captured = null;
        _service.GetPreferredPackagesAsync(
                Arg.Do<Interactive.GetPreferredPackagesRequest>(r => captured = r),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Ok(new Interactive.GetPreferredPackagesResponse
            {
                PackageReferences =
                {
                    new Wire.PackageReference { PackageId = "pid-1", PackageName = "splice-amulet", PackageVersion = "0.1.5" },
                },
                SynchronizerId = "sync::1",
            }));

        var result = await CreateClient().GetPreferredPackagesAsync(
            [new PackageVettingRequirement([Alice], "splice-amulet")],
            new SynchronizerId("sync::1"),
            new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
            cancellationToken: Ct);

        captured!.SynchronizerId.Should().Be("sync::1");
        captured.VettingValidAt.Should().Be(Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero)));
        var requirement = captured.PackageVettingRequirements.Should().ContainSingle().Subject;
        requirement.PackageName.Should().Be("splice-amulet");
        requirement.Parties.Should().Equal("alice::1220");
        result.SynchronizerId.Value.Should().Be("sync::1");
        result.PackageReferences.Should().Equal(new PackageReference("pid-1", "splice-amulet", "0.1.5"));
    }

    [Fact]
    public async Task GetPreferredPackagesAsync_leaves_synchronizer_and_validity_unset_when_not_given()
    {
        Interactive.GetPreferredPackagesRequest? captured = null;
        _service.GetPreferredPackagesAsync(
                Arg.Do<Interactive.GetPreferredPackagesRequest>(r => captured = r),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Ok(new Interactive.GetPreferredPackagesResponse { SynchronizerId = "sync::1" }));

        await CreateClient().GetPreferredPackagesAsync([], cancellationToken: Ct);

        captured!.SynchronizerId.Should().BeEmpty();
        captured.VettingValidAt.Should().BeNull();
    }

    [Fact]
    public async Task GetPreferredPackagesAsync_throws_ArgumentNullException_for_null_requirements()
    {
        var act = () => CreateClient().GetPreferredPackagesAsync(null!, cancellationToken: Ct);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetPreferredPackagesAsync_retries_a_transient_failure_when_retry_is_enabled()
    {
        EnableRetry();
        _service.GetPreferredPackagesAsync(
                Arg.Any<Interactive.GetPreferredPackagesRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Faulted<Interactive.GetPreferredPackagesResponse>(StatusCode.Unavailable),
                _ => Ok(new Interactive.GetPreferredPackagesResponse { SynchronizerId = "sync::1" }));

        await CreateClient().GetPreferredPackagesAsync([], cancellationToken: Ct);

        _ = _service.Received(2).GetPreferredPackagesAsync(
            Arg.Any<Interactive.GetPreferredPackagesRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPreferredPackagesAsync_surfaces_a_participant_rejection_as_a_not_committed_ledger_operation_failure()
    {
        _service.GetPreferredPackagesAsync(
                Arg.Any<Interactive.GetPreferredPackagesRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<Interactive.GetPreferredPackagesResponse>(StatusCode.FailedPrecondition));

        var act = () => CreateClient().GetPreferredPackagesAsync([], cancellationToken: Ct);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.FailedPrecondition));
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeOfType<RpcException>();
    }

    [Fact]
    public async Task GetPreferredPackagesAsync_raises_NO_PREFERRED_PACKAGES_FOUND_instead_of_an_empty_result_when_no_version_is_vetted()
    {
        _service.GetPreferredPackagesAsync(
                Arg.Any<Interactive.GetPreferredPackagesRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<Interactive.GetPreferredPackagesResponse>(CategorisedRpcException.WithCategory(
                StatusCode.FailedPrecondition,
                "NO_PREFERRED_PACKAGES_FOUND",
                "No synchronizer satisfies the vetting requirements",
                "9")));

        var act = () => CreateClient().GetPreferredPackagesAsync(
            [new PackageVettingRequirement([Alice], "unvetted-pkg")], cancellationToken: Ct);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.ErrorId.Should().Be("NO_PREFERRED_PACKAGES_FOUND");
        thrown.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateOther);
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.FailedPrecondition));
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
    }

    private void StubPrepare(Interactive.PrepareSubmissionResponse response) =>
        _service.PrepareSubmissionAsync(
                Arg.Any<Interactive.PrepareSubmissionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Ok(response));

    private void StubExecute(Action<Interactive.ExecuteSubmissionRequest> capture) =>
        _service.ExecuteSubmissionAsync(
                Arg.Do(capture), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Ok(new Interactive.ExecuteSubmissionResponse()));

    [Fact]
    public async Task PrepareSubmissionAsync_maps_the_package_id_selection_preference()
    {
        Interactive.PrepareSubmissionRequest? captured = null;
        _service.PrepareSubmissionAsync(
                Arg.Do<Interactive.PrepareSubmissionRequest>(r => captured = r),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Ok(PreparedResponse()));

        await CreateClient().PrepareSubmissionAsync(
            Submission().WithPackageIdSelectionPreference("pkg-b", "pkg-a"), cancellationToken: Ct);

        captured!.PackageIdSelectionPreference.Should().Equal("pkg-b", "pkg-a");
    }

    [Fact]
    public async Task PrepareSubmissionAsync_leaves_the_package_id_selection_preference_empty_when_the_submission_sets_none()
    {
        Interactive.PrepareSubmissionRequest? captured = null;
        _service.PrepareSubmissionAsync(
                Arg.Do<Interactive.PrepareSubmissionRequest>(r => captured = r),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Ok(PreparedResponse()));

        await CreateClient().PrepareSubmissionAsync(Submission(), cancellationToken: Ct);

        captured!.PackageIdSelectionPreference.Should().BeEmpty();
    }
}
