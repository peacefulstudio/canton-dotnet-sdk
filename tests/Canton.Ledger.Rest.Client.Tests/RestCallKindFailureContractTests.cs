// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestCallKindFailureContractTests : IDisposable
{
    private static readonly Party Alice = new("party::alice");

    private static readonly string[] ReadsWithADecodedBody =
    [
        nameof(RestLedgerClient.GetLedgerEndAsync),
        nameof(RestLedgerClient.GetConnectedSynchronizersAsync),
        nameof(RestLedgerClient.GetLedgerApiVersionAsync),
        nameof(RestLedgerClient.GetUpdateByOffsetAsync),
        nameof(RestLedgerClient.GetUpdateByIdAsync),
        nameof(RestLedgerClient.GetUpdateTreeByOffsetAsync),
        nameof(RestLedgerClient.GetLatestPrunedOffsetsAsync),
        nameof(RestLedgerClient.EstimateTrafficCostAsync),
        nameof(RestLedgerClient.PrepareSubmissionAsync),
        nameof(RestLedgerClient.GetPreferredPackagesAsync),
        nameof(RestLedgerClient.GetPreferredPackageVersionAsync),
        nameof(RestAdminClient.GetParticipantIdAsync),
        nameof(RestAdminClient.ListPackagesAsync),
        nameof(RestAdminClient.GetPackageStatusAsync),
        nameof(RestAdminClient.ListUsersAsync),
        nameof(RestAdminClient.ListKnownPartiesAsync),
        nameof(RestAdminClient.ListIdentityProviderConfigsAsync),
        nameof(RestAdminClient.ListVettedPackagesAsync),
    ];

    private static readonly string[] ReadsWithAnIgnoredBody =
    [
        nameof(RestAdminClient.ValidateDarAsync),
    ];

    private static readonly string[] EffectAppliedWritesWithADecodedBody =
    [
        nameof(RestLedgerClient.SubmitAndWaitAsync),
        nameof(RestLedgerClient.ExecuteSubmissionAndWaitAsync),
        nameof(RestLedgerClient.ExecuteSubmissionAndWaitForTransactionAsync),
        nameof(RestAdminClient.AllocatePartyAsync),
        nameof(RestAdminClient.CreateUserAsync),
        nameof(RestAdminClient.GrantUserRightsAsync),
        nameof(RestAdminClient.UpdateVettedPackagesAsync),
    ];

    private static readonly string[] EffectAppliedWritesWithAnIgnoredBody =
    [
        nameof(RestAdminClient.DeleteUserAsync),
        nameof(RestAdminClient.UploadDarAsync),
        nameof(RestAdminClient.DeleteIdentityProviderConfigAsync),
    ];

    private static readonly string[] AcceptedOnlyWrites =
    [
        nameof(RestLedgerClient.SubmitAsync),
        nameof(RestLedgerClient.SubmitReassignmentAsync),
        nameof(RestLedgerClient.ExecuteSubmissionAsync),
    ];

    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    public static TheoryData<string> HandRolledOperations() => Rows(
    [
        nameof(RestLedgerClient.GetLedgerEndAsync),
        nameof(RestLedgerClient.GetConnectedSynchronizersAsync),
        nameof(RestLedgerClient.GetLedgerApiVersionAsync),
        nameof(RestLedgerClient.GetUpdateByOffsetAsync),
        nameof(RestLedgerClient.GetUpdateByIdAsync),
        nameof(RestLedgerClient.GetUpdateTreeByOffsetAsync),
        nameof(RestLedgerClient.SubmitAsync),
        nameof(RestLedgerClient.SubmitReassignmentAsync),
    ]);

    public static TheoryData<string> AllReads() => Rows([.. ReadsWithADecodedBody, .. ReadsWithAnIgnoredBody]);

    public static TheoryData<string> DecodedReads() => Rows(ReadsWithADecodedBody);

    public static TheoryData<string> DecodedEffectAppliedWrites() => Rows(EffectAppliedWritesWithADecodedBody);

    public static TheoryData<string> AllEffectAppliedWrites() =>
        Rows([.. EffectAppliedWritesWithADecodedBody, .. EffectAppliedWritesWithAnIgnoredBody]);

    public static TheoryData<string> AllWrites() =>
        Rows([.. EffectAppliedWritesWithADecodedBody, .. EffectAppliedWritesWithAnIgnoredBody, .. AcceptedOnlyWrites]);

    [Theory]
    [MemberData(nameof(DecodedReads))]
    public async Task A_read_whose_success_body_cannot_be_read_reports_NotCommitted(string operation)
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{ not json");

        var thrown = await ThrownAsync(operation, transport);

        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.InnerException.Should().BeOfType<JsonException>();
    }

    [Theory]
    [MemberData(nameof(AllReads))]
    public async Task A_read_the_participant_rejected_reports_NotCommitted(string operation)
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.ServiceUnavailable, """{"code": 14, "message": "participant unavailable"}""");

        var thrown = await ThrownAsync(operation, transport);

        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Status.Should().Be(new TransportStatus.Http(HttpStatusCode.ServiceUnavailable));
    }

    [Theory]
    [MemberData(nameof(AllReads))]
    public async Task A_read_the_participant_never_answered_reports_NotCommitted(string operation)
    {
        var transport = new RecordingHttpHandler().WithTransportException(new HttpRequestException("connection refused"));

        var thrown = await ThrownAsync(operation, transport);

        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Status.Should().Be(new TransportStatus.NoResponse());
    }

    [Theory]
    [MemberData(nameof(AllReads))]
    public async Task A_read_whose_pipeline_timed_out_before_any_answer_reports_NotCommitted(string operation)
    {
        var raised = new TimeoutException("token acquisition timed out");
        var transport = new RecordingHttpHandler().WithTransportException(raised);

        var thrown = await ThrownAsync(operation, transport);

        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Status.Should().Be(new TransportStatus.NoResponse());
        thrown.InnerException.Should().BeSameAs(raised);
        thrown.Message.Should().Be("token acquisition timed out");
    }

    [Theory]
    [MemberData(nameof(AllWrites))]
    public async Task A_write_whose_pipeline_timed_out_before_any_answer_reports_Unknown(string operation)
    {
        var raised = new TimeoutException("token acquisition timed out");
        var transport = new RecordingHttpHandler().WithTransportException(raised);

        var thrown = await ThrownAsync(operation, transport);

        thrown.CommitState.Should().Be(CommitState.Unknown);
        thrown.Status.Should().Be(new TransportStatus.NoResponse());
        thrown.InnerException.Should().BeSameAs(raised);
        thrown.Message.Should().Be("token acquisition timed out");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_reports_a_pipeline_timeout_before_any_answer_as_NoResponse()
    {
        var raised = new TimeoutException("token acquisition timed out");
        var client = LedgerClientWith(new RecordingHttpHandler().WithTransportException(raised));

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            Submission(), cancellationToken: TestContext.Current.CancellationToken);

        var infraError = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.InfraError>().Subject;
        infraError.Status.Should().Be(new TransportStatus.NoResponse());
        infraError.Message.Should().Be("token acquisition timed out");
        infraError.SourceException.Should().BeSameAs(raised);
    }

    [Theory]
    [MemberData(nameof(DecodedEffectAppliedWrites))]
    public async Task An_effect_applied_write_whose_success_body_cannot_be_read_reports_Committed(string operation)
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{ not json");

        var thrown = await ThrownAsync(operation, transport);

        thrown.CommitState.Should().Be(CommitState.Committed);
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.InnerException.Should().BeOfType<JsonException>();
    }

    [Fact]
    public async Task A_dry_run_vetting_update_whose_success_body_cannot_be_read_reports_NotCommitted()
    {
        var admin = AdminClientWith(new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{ not json"));

        var act = () => admin.UpdateVettedPackagesAsync(
            [], dryRun: true, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
    }

    [Fact]
    public async Task A_dry_run_vetting_update_the_participant_never_answered_reports_NotCommitted()
    {
        var admin = AdminClientWith(
            new RecordingHttpHandler().WithTransportException(new HttpRequestException("connection refused")));

        var act = () => admin.UpdateVettedPackagesAsync(
            [], dryRun: true, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Status.Should().Be(new TransportStatus.NoResponse());
    }

    [Theory]
    [MemberData(nameof(DecodedEffectAppliedWrites))]
    public async Task An_effect_applied_write_whose_success_body_fails_in_transit_reports_Committed(string operation)
    {
        var raised = new HttpRequestException("connection reset while reading the body");

        var thrown = await ThrownAsync(operation, new SuccessWithABodyThatFailsHandler(raised));

        thrown.CommitState.Should().Be(CommitState.Committed);
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.Message.Should().Be("connection reset while reading the body");
        thrown.InnerException.Should().BeSameAs(raised);
    }

    [Theory]
    [MemberData(nameof(DecodedReads))]
    public async Task A_read_whose_success_body_fails_in_transit_reports_NotCommitted_as_no_response(string operation)
    {
        var raised = new HttpRequestException("connection reset while reading the body");

        var thrown = await ThrownAsync(operation, new SuccessWithABodyThatFailsHandler(raised));

        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Status.Should().Be(new TransportStatus.NoResponse());
        thrown.InnerException.Should().BeSameAs(raised);
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_reports_a_success_body_that_fails_in_transit_as_CommittedUndecodable()
    {
        var raised = new HttpRequestException("connection reset while reading the body");
        var client = LedgerClientWith(new SuccessWithABodyThatFailsHandler(raised));

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            Submission(), cancellationToken: TestContext.Current.CancellationToken);

        var undecodable = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.CommittedUndecodable>().Subject;
        undecodable.Message.Should().Be("connection reset while reading the body");
        undecodable.SourceException.Should().BeSameAs(raised);
    }

    [Fact(Timeout = 10_000)]
    public async Task An_effect_applied_write_without_a_call_timeout_is_bounded_by_the_HttpClient_timeout_while_reading_the_success_body()
    {
        var factory = new StubHttpClientFactory(new SuccessWithABodyThatNeverArrivesHandler(), TimeSpan.FromMilliseconds(200));
        _factories.Add(factory);
        var client = new RestLedgerClient(
            factory, Options.Create(new RestLedgerClientOptions { HttpAddress = "http://localhost:7575" }));

        var act = () => client.SubmitAndWaitAsync(Submission(), timeout: null, TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.CommitState.Should().Be(CommitState.Committed);
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.Message.Should().Be("Request exceeded the HttpClient default deadline while reading the response body.");
    }

    [Fact(Timeout = 10_000)]
    public async Task A_read_without_a_call_timeout_is_bounded_by_the_HttpClient_timeout_while_reading_the_success_body()
    {
        var factory = new StubHttpClientFactory(new SuccessWithABodyThatNeverArrivesHandler(), TimeSpan.FromMilliseconds(200));
        _factories.Add(factory);
        var admin = new RestAdminClient(factory);

        var act = () => admin.ListUsersAsync(TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Status.Should().Be(new TransportStatus.NoResponse());
        thrown.Message.Should().Be("Request exceeded the HttpClient default deadline while reading the response body.");
    }

    [Theory]
    [MemberData(nameof(HandRolledOperations))]
    public async Task A_hand_rolled_operation_the_participant_never_answered_raises_NoResponse_carrying_the_connection_failure(
        string operation)
    {
        var raised = new HttpRequestException("connection refused");
        var transport = new RecordingHttpHandler().WithTransportException(raised);

        var thrown = await ThrownAsync(operation, transport);

        thrown.Status.Should().Be(new TransportStatus.NoResponse());
        thrown.Message.Should().Be("connection refused");
        thrown.InnerException.Should().BeSameAs(raised);
    }

    [Theory]
    [MemberData(nameof(AllWrites))]
    public async Task A_write_the_participant_never_answered_reports_Unknown(string operation)
    {
        var transport = new RecordingHttpHandler().WithTransportException(new HttpRequestException("connection refused"));

        var thrown = await ThrownAsync(operation, transport);

        thrown.CommitState.Should().Be(CommitState.Unknown);
        thrown.Status.Should().Be(new TransportStatus.NoResponse());
    }

    [Theory]
    [MemberData(nameof(AllWrites))]
    public async Task A_write_rejected_with_a_structured_error_reports_from_its_category(string operation)
    {
        var rejected = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.NotFound,
            StructuredError("CONTRACT_NOT_FOUND", "InvalidGivenCurrentSystemStateResourceMissing"));
        var stateUnknown = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.GatewayTimeout,
            StructuredError("REQUEST_TIME_OUT", "DeadlineExceededRequestStateUnknown"));

        var rejectedWrite = await ThrownAsync(operation, rejected);
        var unknownWrite = await ThrownAsync(operation, stateUnknown);

        rejectedWrite.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing);
        rejectedWrite.CommitState.Should().Be(CommitState.NotCommitted);
        unknownWrite.Category.Should().Be(DamlErrorCategory.DeadlineExceededRequestStateUnknown);
        unknownWrite.CommitState.Should().Be(CommitState.Unknown);
    }

    [Theory]
    [MemberData(nameof(AllWrites))]
    public async Task A_write_rejected_without_a_structured_error_reports_Unknown(string operation)
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.ServiceUnavailable, """{"code": 14, "message": "participant unavailable"}""");

        var thrown = await ThrownAsync(operation, transport);

        thrown.CommitState.Should().Be(CommitState.Unknown);
        thrown.Status.Should().Be(new TransportStatus.Http(HttpStatusCode.ServiceUnavailable));
    }

    [Theory]
    [MemberData(nameof(AllReads))]
    [MemberData(nameof(AllWrites))]
    public async Task A_failure_before_any_response_that_is_not_a_transport_failure_propagates_unchanged(string operation)
    {
        var raised = new InvalidOperationException("handler bug");
        var transport = new RecordingHttpHandler().WithTransportException(raised);

        var act = () => InvokeAsync(operation, transport);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().BeSameAs(raised);
    }

    [Theory]
    [MemberData(nameof(AllReads))]
    [MemberData(nameof(AllWrites))]
    public async Task A_rejection_whose_error_body_cannot_be_read_propagates_unchanged_and_is_never_reported_as_committed(
        string operation)
    {
        var act = () => InvokeAsync(operation, new RejectionWithAnUnreadableErrorBodyHandler());

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().NotBeOfType<LedgerOperationException>();
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_does_not_report_CommittedUndecodable_for_a_rejection_with_an_unreadable_error_body()
    {
        var client = LedgerClientWith(new RejectionWithAnUnreadableErrorBodyHandler());

        var act = () => client.TrySubmitAndWaitForTransactionAsync(
            Submission(), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SubmitAndWaitAsync_raises_the_caller_cancellation_that_interrupts_the_success_body_read()
    {
        using var callerCancellation = new CancellationTokenSource();
        var client = LedgerClientWith(new CallerCancelsWhileTheBodyIsPendingHandler(callerCancellation));

        var act = () => client.SubmitAndWaitAsync(Submission(), timeout: null, callerCancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private async Task<LedgerOperationException> ThrownAsync(string operation, HttpMessageHandler transport)
    {
        var act = () => InvokeAsync(operation, transport);
        return (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
    }

    private Task InvokeAsync(string operation, HttpMessageHandler transport)
    {
        var ledger = LedgerClientWith(transport);
        var admin = AdminClientWith(transport);
        var token = TestContext.Current.CancellationToken;
        var darFile = new byte[] { 0x01 };
        return operation switch
        {
            nameof(RestLedgerClient.GetLatestPrunedOffsetsAsync) => ledger.GetLatestPrunedOffsetsAsync(cancellationToken: token),
            nameof(RestLedgerClient.EstimateTrafficCostAsync) => ledger.EstimateTrafficCostAsync(Submission(), cancellationToken: token),
            nameof(RestLedgerClient.PrepareSubmissionAsync) => ledger.PrepareSubmissionAsync(Submission(), cancellationToken: token),
            nameof(RestLedgerClient.GetPreferredPackagesAsync) => ledger.GetPreferredPackagesAsync(
                [new PackageVettingRequirement([Alice], "pkg-name")], cancellationToken: token),
            nameof(RestLedgerClient.GetPreferredPackageVersionAsync) =>
                ledger.GetPreferredPackageVersionAsync([Alice], "pkg-name", cancellationToken: token),
            nameof(RestLedgerClient.GetLedgerEndAsync) => ledger.GetLedgerEndAsync(cancellationToken: token),
            nameof(RestLedgerClient.GetConnectedSynchronizersAsync) =>
                ledger.GetConnectedSynchronizersAsync(cancellationToken: token),
            nameof(RestLedgerClient.GetLedgerApiVersionAsync) => ledger.GetLedgerApiVersionAsync(cancellationToken: token),
            nameof(RestLedgerClient.GetUpdateByOffsetAsync) =>
                ledger.GetUpdateByOffsetAsync(LedgerOffset.At(7), AliceSubmitter(), cancellationToken: token),
            nameof(RestLedgerClient.GetUpdateByIdAsync) =>
                ledger.GetUpdateByIdAsync("upd-1", AliceSubmitter(), cancellationToken: token),
            nameof(RestLedgerClient.GetUpdateTreeByOffsetAsync) =>
                ledger.GetUpdateTreeByOffsetAsync(LedgerOffset.At(7), AliceSubmitter(), cancellationToken: token),
            nameof(RestLedgerClient.SubmitAsync) => ledger.SubmitAsync(Submission(), cancellationToken: token),
            nameof(RestLedgerClient.SubmitReassignmentAsync) =>
                ledger.SubmitReassignmentAsync(Reassignment(), cancellationToken: token),
            nameof(RestLedgerClient.SubmitAndWaitAsync) => ledger.SubmitAndWaitAsync(Submission(), cancellationToken: token),
            nameof(RestLedgerClient.ExecuteSubmissionAsync) => ledger.ExecuteSubmissionAsync(Signed(), cancellationToken: token),
            nameof(RestLedgerClient.ExecuteSubmissionAndWaitAsync) =>
                ledger.ExecuteSubmissionAndWaitAsync(Signed(), cancellationToken: token),
            nameof(RestLedgerClient.ExecuteSubmissionAndWaitForTransactionAsync) =>
                ledger.ExecuteSubmissionAndWaitForTransactionAsync(
                    Signed(), new RuntimeCommands.SubmitterInfo(new HashSet<Party> { Alice }, new HashSet<Party>()), cancellationToken: token),
            nameof(RestAdminClient.GetParticipantIdAsync) => admin.GetParticipantIdAsync(token),
            nameof(RestAdminClient.ListPackagesAsync) => admin.ListPackagesAsync(token),
            nameof(RestAdminClient.GetPackageStatusAsync) => admin.GetPackageStatusAsync("pkg", token),
            nameof(RestAdminClient.ListUsersAsync) => admin.ListUsersAsync(token),
            nameof(RestAdminClient.ListKnownPartiesAsync) => admin.ListKnownPartiesAsync(token),
            nameof(RestAdminClient.ListIdentityProviderConfigsAsync) => admin.ListIdentityProviderConfigsAsync(token),
            nameof(RestAdminClient.ListVettedPackagesAsync) => admin.ListVettedPackagesAsync(cancellationToken: token),
            nameof(RestAdminClient.ValidateDarAsync) => admin.ValidateDarAsync(darFile, token),
            nameof(RestAdminClient.AllocatePartyAsync) => admin.AllocatePartyAsync("alice", cancellationToken: token),
            nameof(RestAdminClient.CreateUserAsync) => admin.CreateUserAsync("user-1", Alice, cancellationToken: token),
            nameof(RestAdminClient.GrantUserRightsAsync) => admin.GrantUserRightsAsync("user-1", [], token),
            nameof(RestAdminClient.UpdateVettedPackagesAsync) => admin.UpdateVettedPackagesAsync([], cancellationToken: token),
            nameof(RestAdminClient.DeleteUserAsync) => admin.DeleteUserAsync("user-1", cancellationToken: token),
            nameof(RestAdminClient.UploadDarAsync) => admin.UploadDarAsync(darFile, cancellationToken: token),
            nameof(RestAdminClient.DeleteIdentityProviderConfigAsync) =>
                admin.DeleteIdentityProviderConfigAsync("idp-1", token),
            _ => throw new InvalidOperationException($"The theory names an operation it cannot invoke: {operation}"),
        };
    }

    private static TheoryData<string> Rows(IEnumerable<string> operations)
    {
        TheoryData<string> rows = [];
        foreach (var operation in operations)
        {
            rows.Add(operation);
        }

        return rows;
    }

    private static string StructuredError(string errorId, string category) =>
        $$"""
        {
          "code": 5,
          "message": "{{errorId}}(11,abcd1234): the participant refused",
          "details": [
            {
              "@type": "type.googleapis.com/google.rpc.ErrorInfo",
              "reason": "{{errorId}}",
              "domain": "com.daml.error",
              "metadata": {"category": "{{category}}"}
            }
          ]
        }
        """;

    private static RuntimeCommands.CommandsSubmission Submission() =>
        RuntimeCommands.CommandsSubmission
            .Single(RuntimeCommands.CreateCommand.For(new FailureContractTemplate()))
            .WithActAs(Alice);

    private static RuntimeCommands.SubmitterInfo AliceSubmitter() =>
        new(new HashSet<Party> { Alice }, new HashSet<Party>());

    private static ReassignmentSubmission Reassignment() =>
        ReassignmentSubmission.Of(
            new UnassignCommand("00cid", new SynchronizerId("sync-a"), new SynchronizerId("sync-b")), Alice);

    private static SignedSubmission Signed() =>
        new(
            new PreparedSubmission(
                new byte[] { 0x0A, 0x03 }, new byte[] { 0xDE, 0xAD }, HashingSchemeVersion.V2, HashingDetails: null, CostEstimate: null),
            [
                new PartySignatures(
                    Alice,
                    [new LedgerSignature(SignatureFormat.Der, new byte[] { 0x01 }, "fingerprint-1", SigningAlgorithm.EcDsaSha256)]),
            ],
            "sub-1",
            DeduplicationPeriod: null,
            MinLedgerTime: null);

    private RestLedgerClient LedgerClientWith(HttpMessageHandler transport)
    {
        var factory = new StubHttpClientFactory(transport);
        _factories.Add(factory);
        return new RestLedgerClient(
            factory, Options.Create(new RestLedgerClientOptions { HttpAddress = "http://localhost:7575" }));
    }

    private RestAdminClient AdminClientWith(HttpMessageHandler transport)
    {
        var factory = new StubHttpClientFactory(transport);
        _factories.Add(factory);
        return new RestAdminClient(factory);
    }

    private sealed class FailingBodyContent(Exception failure) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(failure);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class HangingBodyContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.CompletedTask;

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new StreamThatNeverDeliversAByte());

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class StreamThatNeverDeliversAByte : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class SuccessWithABodyThatFailsHandler(Exception failure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new FailingBodyContent(failure) });
    }

    private sealed class SuccessWithABodyThatNeverArrivesHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new HangingBodyContent() });
    }

    private sealed class RejectionWithAnUnreadableErrorBodyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = new ByteArrayContent("<html>bad gateway</html>"u8.ToArray());
            body.Headers.TryAddWithoutValidation("Content-Type", "text/html; charset=bogus-charset");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = body });
        }
    }

    private sealed class CallerCancelsWhileTheBodyIsPendingHandler(CancellationTokenSource callerCancellation)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            await body.LoadIntoBufferAsync(CancellationToken.None).ConfigureAwait(false);
            await callerCancellation.CancelAsync().ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = body };
        }
    }

    private sealed record FailureContractTemplate : ITemplate, IDamlRecord<FailureContractTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "FailureContractTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "pkg-name";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, [new DamlField("owner", Alice.ToDamlValue())]);

        public static DamlRecord __ReadDamlLfJson(
            JsonElement json, Daml.Runtime.Serialization.DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context, ("owner", Daml.Runtime.Serialization.DamlLfJsonDecoders.ReadParty));

        public static FailureContractTemplate FromRecord(DamlRecord record) => new();
    }
}
