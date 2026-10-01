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
        _options = new LedgerClientOptions { GrpcAddress = "https://localhost:5001", UserId = "test-user" };
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

    private static RuntimeCommands.CommandsSubmission Submission() =>
        RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(
                new Identifier("pkg", "Module", "Template"),
                new DamlRecord(null, [])))
            .WithActAs(Alice)
            .WithCommandId(new RuntimeCommands.CommandId("cmd-1"));

    private static Interactive.PrepareSubmissionResponse PreparedResponse() =>
        new() { PreparedTransaction = new Interactive.PreparedTransaction() };

    private static byte[] PreparedTransactionBytes() =>
        new Interactive.PreparedTransaction { Metadata = new Interactive.Metadata { TransactionUuid = "uuid-1" } }
            .ToByteArray();

    private static SignedSubmission Signed(
        string submissionId = "sub-1",
        RuntimeCommands.DeduplicationPeriod? deduplication = null,
        RuntimeCommands.MinLedgerTime? minLedgerTime = null,
        byte[]? preparedTransaction = null) =>
        new(
            new PreparedSubmission(
                preparedTransaction ?? PreparedTransactionBytes(),
                new byte[] { 9, 9 },
                HashingSchemeVersion.V3,
                null,
                null),
            [
                new PartySignatures(
                    Alice,
                    [new LedgerSignature(SignatureFormat.Der, new byte[] { 1, 2, 3 }, "fp-1", SigningAlgorithm.EcDsaSha256)]),
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

    [Fact]
    public async Task PrepareSubmissionAsync_projects_every_field_of_the_response()
    {
        var prepared = new Interactive.PreparedTransaction { Metadata = new Interactive.Metadata { TransactionUuid = "uuid-7" } };
        StubPrepare(new Interactive.PrepareSubmissionResponse
        {
            PreparedTransaction = prepared,
            PreparedTransactionHash = ByteString.CopyFrom(7, 8, 9),
            HashingSchemeVersion = Interactive.HashingSchemeVersion.V3,
            HashingDetails = "details",
            CostEstimation = new Interactive.CostEstimation { TotalTrafficCostEstimation = 4096UL },
        });

        var result = await CreateClient().PrepareSubmissionAsync(Submission(), cancellationToken: Ct);

        result.Hash.ToArray().Should().Equal(7, 8, 9);
        result.HashingSchemeVersion.Should().Be((HashingSchemeVersion)3);
        result.HashingDetails.Should().Be("details");
        result.CostEstimate!.TotalCost.Should().Be(4096L);
        Interactive.PreparedTransaction.Parser.ParseFrom(result.PreparedTransaction.ToArray())
            .Metadata.TransactionUuid.Should().Be("uuid-7");
    }

    [Fact]
    public async Task PrepareSubmissionAsync_leaves_details_and_estimate_null_when_the_participant_sends_none()
    {
        StubPrepare(new Interactive.PrepareSubmissionResponse
        {
            PreparedTransaction = new Interactive.PreparedTransaction(),
            HashingSchemeVersion = (Interactive.HashingSchemeVersion)9,
        });

        var result = await CreateClient().PrepareSubmissionAsync(Submission(), cancellationToken: Ct);

        result.HashingDetails.Should().BeNull();
        result.CostEstimate.Should().BeNull();
        ((int)result.HashingSchemeVersion).Should().Be(9);
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
    public async Task PrepareSubmissionAsync_surfaces_a_participant_rejection_as_RpcException()
    {
        _service.PrepareSubmissionAsync(
                Arg.Any<Interactive.PrepareSubmissionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<Interactive.PrepareSubmissionResponse>(StatusCode.InvalidArgument));

        var act = () => CreateClient().PrepareSubmissionAsync(Submission(), cancellationToken: Ct);

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
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
    public async Task ExecuteSubmissionAsync_surfaces_a_participant_rejection_as_RpcException()
    {
        _service.ExecuteSubmissionAsync(
                Arg.Any<Interactive.ExecuteSubmissionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<Interactive.ExecuteSubmissionResponse>(StatusCode.FailedPrecondition));

        var act = () => CreateClient().ExecuteSubmissionAsync(Signed(), cancellationToken: Ct);

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
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
    public async Task ExecuteSubmissionAndWaitForTransactionAsync_surfaces_a_participant_rejection_as_RpcException()
    {
        _service.ExecuteSubmissionAndWaitForTransactionAsync(
                Arg.Any<Interactive.ExecuteSubmissionAndWaitForTransactionRequest>(),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<Interactive.ExecuteSubmissionAndWaitForTransactionResponse>(StatusCode.NotFound));

        var act = () => CreateClient().ExecuteSubmissionAndWaitForTransactionAsync(Signed(), Submitter(), cancellationToken: Ct);

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.NotFound);
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

        await ((Func<Task>)(() => client.ExecuteSubmissionAsync(Signed(), cancellationToken: Ct))).Should().ThrowAsync<RpcException>();
        await ((Func<Task>)(() => client.ExecuteSubmissionAndWaitAsync(Signed(), cancellationToken: Ct))).Should().ThrowAsync<RpcException>();
        await ((Func<Task>)(() => client.ExecuteSubmissionAndWaitForTransactionAsync(Signed(), Submitter(), cancellationToken: Ct)))
            .Should().ThrowAsync<RpcException>();

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
    public async Task GetPreferredPackagesAsync_surfaces_a_participant_rejection_as_RpcException()
    {
        _service.GetPreferredPackagesAsync(
                Arg.Any<Interactive.GetPreferredPackagesRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<Interactive.GetPreferredPackagesResponse>(StatusCode.FailedPrecondition));

        var act = () => CreateClient().GetPreferredPackagesAsync([], cancellationToken: Ct);

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
    }

    [Fact]
    public async Task GetPreferredPackageVersionAsync_maps_the_request_and_projects_the_preference()
    {
        Interactive.GetPreferredPackageVersionRequest? captured = null;
        _service.GetPreferredPackageVersionAsync(
                Arg.Do<Interactive.GetPreferredPackageVersionRequest>(r => captured = r),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Ok(new Interactive.GetPreferredPackageVersionResponse
            {
                PackagePreference = new Interactive.PackagePreference
                {
                    PackageReference = new Wire.PackageReference { PackageId = "pid-2", PackageName = "my-pkg", PackageVersion = "1.2.3" },
                    SynchronizerId = "sync::2",
                },
            }));

        var result = await CreateClient().GetPreferredPackageVersionAsync(
            [Alice], "my-pkg", new SynchronizerId("sync::2"), cancellationToken: Ct);

        captured!.PackageName.Should().Be("my-pkg");
        captured.Parties.Should().Equal("alice::1220");
        captured.SynchronizerId.Should().Be("sync::2");
        result!.Package.Should().Be(new PackageReference("pid-2", "my-pkg", "1.2.3"));
        result.SynchronizerId.Value.Should().Be("sync::2");
    }

    [Fact]
    public async Task GetPreferredPackageVersionAsync_returns_null_when_the_response_has_no_preference()
    {
        _service.GetPreferredPackageVersionAsync(
                Arg.Any<Interactive.GetPreferredPackageVersionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Ok(new Interactive.GetPreferredPackageVersionResponse()));

        var result = await CreateClient().GetPreferredPackageVersionAsync([Alice], "my-pkg", cancellationToken: Ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPreferredPackageVersionAsync_throws_ArgumentNullException_for_null_parties()
    {
        var act = () => CreateClient().GetPreferredPackageVersionAsync(null!, "my-pkg", cancellationToken: Ct);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task GetPreferredPackageVersionAsync_throws_ArgumentException_for_a_blank_package_name(string? packageName)
    {
        var act = () => CreateClient().GetPreferredPackageVersionAsync([Alice], packageName!, cancellationToken: Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private void StubPrepare(Interactive.PrepareSubmissionResponse response) =>
        _service.PrepareSubmissionAsync(
                Arg.Any<Interactive.PrepareSubmissionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Ok(response));

    private void StubExecute(Action<Interactive.ExecuteSubmissionRequest> capture) =>
        _service.ExecuteSubmissionAsync(
                Arg.Do(capture), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Ok(new Interactive.ExecuteSubmissionResponse()));
}
