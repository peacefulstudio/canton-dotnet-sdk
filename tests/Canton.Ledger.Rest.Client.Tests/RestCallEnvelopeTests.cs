// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Serialization;
using System.Text.Json;
using System.Net;
using System.Text;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestCallEnvelopeTests : IDisposable
{
    private static readonly Party Alice = new("party::alice");
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReadDeadline = TimeSpan.FromMilliseconds(50);

    private readonly List<StubHttpClientFactory> _factories = [];

    private static readonly HashSet<string> ReportedAsCommittedUndecodable =
    [
        nameof(RestLedgerClient.TrySubmitAndWaitForTransactionAsync),
        nameof(RestLedgerClient.TrySubmitAndWaitForReassignmentAsync),
    ];

    private static readonly EnvelopedCall[] EnvelopedCalls =
    [
        new(
            nameof(RestLedgerClient.SubmitAndWaitAsync),
            "Server returned a successful response but no body was present for submit-and-wait.",
            "Server returned a malformed submit-and-wait response body: "),
        new(
            nameof(RestLedgerClient.TrySubmitAndWaitForTransactionAsync),
            "Server returned a successful response but no transaction was present.",
            "Server returned a malformed transaction: "),
        new(
            nameof(RestLedgerClient.TrySubmitAndWaitForReassignmentAsync),
            "Server returned a successful response but no reassignment was present.",
            "Server returned a malformed reassignment response body: "),
        new(
            nameof(RestLedgerClient.EstimateTrafficCostAsync),
            "Server returned a successful response but no prepared submission was present for the "
            + "traffic-cost estimate.",
            "Server returned a malformed traffic-cost estimate response body: "),
    ];

    public static TheoryData<string, HttpStatusCode, int, DamlErrorCategory> RedactedAuthFailures()
    {
        TheoryData<string, HttpStatusCode, int, DamlErrorCategory> rows = [];
        foreach (var call in EnvelopedCalls)
        {
            rows.Add(
                call.Operation,
                HttpStatusCode.Unauthorized,
                16,
                DamlErrorCategory.AuthInterceptorInvalidAuthenticationCredentials);
            rows.Add(
                call.Operation,
                HttpStatusCode.Forbidden,
                7,
                DamlErrorCategory.AuthorizationChecksFailed);
        }

        return rows;
    }

    public static TheoryData<string> EnvelopedOperations()
    {
        TheoryData<string> operations = [];
        foreach (var call in EnvelopedCalls)
        {
            operations.Add(call.Operation);
        }

        return operations;
    }

    public static TheoryData<string, string> MissingBodyMessages()
    {
        TheoryData<string, string> rows = [];
        foreach (var call in EnvelopedCalls)
        {
            rows.Add(call.Operation, call.MissingBodyMessage);
        }

        return rows;
    }

    public static TheoryData<string, string> MalformedBodyPrefixes()
    {
        TheoryData<string, string> rows = [];
        foreach (var call in EnvelopedCalls)
        {
            rows.Add(call.Operation, call.MalformedBodyPrefix);
        }

        return rows;
    }

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(EnvelopedOperations))]
    public async Task Enveloped_operation_reports_a_deadline_overrun_as_no_response(string operation)
    {
        var failure = await InvokeAsync(ClientWith(TimedOutTransport()), operation);

        failure.Status.Should().Be(new TransportStatus.NoResponse());
        failure.Message.Should().Be($"Request exceeded the {Deadline} deadline.");
    }

    [Theory]
    [MemberData(nameof(EnvelopedOperations))]
    public async Task Enveloped_operation_reports_a_transport_failure_as_no_response(string operation)
    {
        var transport = new RecordingHttpHandler()
            .WithTransportException(new HttpRequestException("connection refused"));

        var failure = await InvokeAsync(ClientWith(transport), operation);

        failure.Status.Should().Be(new TransportStatus.NoResponse());
        failure.Message.Should().Be("connection refused");
    }

    [Theory]
    [MemberData(nameof(EnvelopedOperations))]
    public async Task Enveloped_operation_reports_a_non_success_response_with_the_participants_status(string operation)
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.ServiceUnavailable,
            """{"code": 14, "message": "participant unavailable"}""");

        var failure = await InvokeAsync(ClientWith(transport), operation);

        failure.Status.Should().Be(new TransportStatus.Http(HttpStatusCode.ServiceUnavailable));
        failure.Message.Should().Be("participant unavailable");
    }

    [Theory]
    [MemberData(nameof(RedactedAuthFailures))]
    public async Task Enveloped_operation_carries_the_recovered_Category_when_the_participant_redacted_the_error_info(
        string operation, HttpStatusCode statusCode, int grpcCodeValue, DamlErrorCategory expected)
    {
        var transport = new RecordingHttpHandler().WithResponse(
            statusCode,
            $$"""{"code": {{grpcCodeValue}}, "message": "a security-sensitive error has been received"}""");

        var failure = await InvokeAsync(ClientWith(transport), operation);

        failure.Category.Should().Be(
            expected,
            "a thrown failure and a reported one must carry the recovered classification identically");
    }

    [Theory]
    [MemberData(nameof(EnvelopedOperations))]
    public async Task Enveloped_operation_leaves_Category_null_on_an_unclassified_failure(string operation)
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.ServiceUnavailable,
            """{"code": 14, "message": "participant unavailable"}""");

        var failure = await InvokeAsync(ClientWith(transport), operation);

        failure.Category.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(MalformedBodyPrefixes))]
    public async Task Enveloped_operation_reports_an_undecodable_success_body_as_malformed(
        string operation, string expectedPrefix)
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{ not json");

        var failure = await InvokeAsync(ClientWith(transport), operation);

        AssertClassifiedAsUndecodable(operation, failure, "an undecodable body");
        failure.Message.Should().StartWith(expectedPrefix);
    }

    [Theory]
    [MemberData(nameof(MissingBodyMessages))]
    public async Task Enveloped_operation_reports_an_absent_success_body_as_a_missing_payload(
        string operation, string expectedMessage)
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "null");

        var failure = await InvokeAsync(ClientWith(transport), operation);

        AssertClassifiedAsUndecodable(operation, failure, "an absent body");
        failure.Message.Should().Be(expectedMessage);
    }

    [Fact]
    public async Task Enveloped_write_reports_a_deadline_that_overran_while_reading_the_body_as_committed_and_unreadable()
    {
        var client = ClientWith(new DeadlineElapsesBeforeTheBodyIsReadHandler());

        var act = () => client.SubmitAndWaitAsync(
            Submission(), ReadDeadline, TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.Which.CommitState.Should().Be(CommitState.Committed);
        thrown.Which.Message.Should().Be(
            $"Request exceeded the {ReadDeadline} deadline while reading the response body.");
    }

    [Fact]
    public async Task Enveloped_write_reports_the_try_variant_of_a_deadline_that_overran_while_reading_the_body_as_CommittedUndecodable()
    {
        var client = ClientWith(new DeadlineElapsesBeforeTheBodyIsReadHandler());

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            Submission(), ReadDeadline, TestContext.Current.CancellationToken);

        var undecodable = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.CommittedUndecodable>().Subject;
        undecodable.Message.Should().Be(
            $"Request exceeded the {ReadDeadline} deadline while reading the response body.");
    }

    [Theory]
    [InlineData("Read", CommitState.NotCommitted, "NoResponse")]
    [InlineData("AcceptedOnlyWrite", CommitState.Unknown, "UndecodableBody")]
    [InlineData("EffectAppliedWrite", CommitState.Committed, "UndecodableBody")]
    public async Task SendAsync_reports_a_success_body_that_fails_in_transit_with_the_commit_state_of_the_calls_kind(
        string kindName, CommitState expected, string expectedStatusName)
    {
        var raised = new HttpRequestException("connection reset while reading the body");
        var factory = new StubHttpClientFactory(new SuccessWithABodyThatFailsHandler(raised));
        _factories.Add(factory);
        var envelope = new RestCallEnvelope(factory, NullLogger.Instance);

        var act = () => envelope.SendAsync<Raw.SubmitAndWaitForTransactionResponse, string>(
            CallOfKind(Enum.Parse<LedgerCallKind>(kindName)), _ => "unused", timeout: null, TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.CommitState.Should().Be(expected);
        thrown.Status.Should().Be(expectedStatusName == "NoResponse"
            ? new TransportStatus.NoResponse()
            : new TransportStatus.UndecodableBody());
        thrown.Message.Should().Be("connection reset while reading the body");
        thrown.InnerException.Should().BeSameAs(raised);
    }

    [Fact]
    public async Task Enveloped_read_reports_a_deadline_that_overran_while_reading_the_body_as_no_response()
    {
        var factory = new StubHttpClientFactory(new DeadlineElapsesBeforeTheBodyIsReadHandler());
        _factories.Add(factory);
        var envelope = new RestCallEnvelope(factory, NullLogger.Instance);

        var act = () => envelope.SendAsync<Raw.SubmitAndWaitForTransactionResponse, string>(
            CallOfKind(LedgerCallKind.Read), _ => "unused", ReadDeadline, TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.Status.Should().Be(new TransportStatus.NoResponse());
        thrown.Which.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Which.Message.Should().Be(
            $"Request exceeded the {ReadDeadline} deadline while reading the response body.");
    }

    public static TheoryData<Exception, string> ProjectionExceptionsAfterASuccessResponse() =>
        new()
        {
            { new System.Text.Json.JsonException("not a json shape"), "malformed prefix: " },
            { new InvalidCastException("boom"), "malformed prefix: " },
            { new KeyNotFoundException("boom"), "malformed prefix: " },
            { NullDereference(), "malformed prefix: " },
            { new ArgumentException("boom"), "malformed prefix: " },
            { new TimeoutException("boom"), "malformed prefix: " },
            { new TypeInitializationException("Some.Type", new InvalidOperationException("boom")), "malformed prefix: " },
        };

    [Theory]
    [InlineData("Read", CommitState.NotCommitted)]
    [InlineData("AcceptedOnlyWrite", CommitState.Unknown)]
    [InlineData("EffectAppliedWrite", CommitState.Committed)]
    public async Task SendAsync_reports_a_success_body_the_projection_cannot_read_with_the_commit_state_of_the_calls_kind(
        string kindName, CommitState expected)
    {
        var raised = new System.Text.Json.JsonException("not a json shape");

        var thrown = await ThrownBySendAsync(kindName, _ => throw raised);

        thrown.CommitState.Should().Be(expected);
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.Message.Should().Be("malformed prefix: not a json shape");
        thrown.InnerException.Should().BeSameAs(raised);
    }

    [Theory]
    [InlineData("AcceptedOnlyWrite", CommitState.Unknown)]
    [InlineData("EffectAppliedWrite", CommitState.Committed)]
    public async Task SendAsync_reports_whatever_a_write_projection_raised_after_a_success_response_as_unreadable(
        string kindName, CommitState expected)
    {
        var raised = new InvalidCastException("boom");

        var thrown = await ThrownBySendAsync(kindName, _ => throw raised);

        thrown.CommitState.Should().Be(expected);
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.Message.Should().Be("malformed prefix: boom");
        thrown.InnerException.Should().BeSameAs(raised);
    }

    [Theory]
    [InlineData("AcceptedOnlyWrite", CommitState.Unknown)]
    [InlineData("EffectAppliedWrite", CommitState.Committed)]
    public async Task SendAsync_reports_a_TimeoutException_raised_after_a_success_response_as_unreadable_not_as_no_response(
        string kindName, CommitState expected)
    {
        var raised = new TimeoutException("projection timed out");

        var thrown = await ThrownBySendAsync(kindName, _ => throw raised);

        thrown.CommitState.Should().Be(expected);
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.Message.Should().Be("malformed prefix: projection timed out");
        thrown.InnerException.Should().BeSameAs(raised);
    }

    [Fact]
    public async Task SendAsync_lets_a_TimeoutException_raised_by_a_read_projection_after_a_success_response_propagate()
    {
        var raised = new TimeoutException("projection timed out");
        var envelope = EnvelopeServing(HttpStatusCode.OK, "{}");

        var act = () => envelope.SendAsync<Raw.SubmitAndWaitForTransactionResponse, string>(
            CallOfKind(LedgerCallKind.Read),
            _ => throw raised,
            timeout: null,
            TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<TimeoutException>()).Which.Should().BeSameAs(raised);
    }

    [Fact]
    public async Task SendAsync_lets_a_read_projection_exception_that_is_not_a_decode_failure_propagate()
    {
        var raised = new InvalidCastException("boom");
        var envelope = EnvelopeServing(HttpStatusCode.OK, "{}");

        var act = () => envelope.SendAsync<Raw.SubmitAndWaitForTransactionResponse, string>(
            CallOfKind(LedgerCallKind.Read),
            _ => throw raised,
            timeout: null,
            TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidCastException>()).Which.Should().BeSameAs(raised);
    }

    [Theory]
    [InlineData("Read")]
    [InlineData("AcceptedOnlyWrite")]
    [InlineData("EffectAppliedWrite")]
    public async Task SendAsync_raises_the_caller_cancellation_that_interrupts_the_projection_of_a_success_body(
        string kindName)
    {
        using var callerCancellation = new CancellationTokenSource();
        var envelope = EnvelopeServing(HttpStatusCode.OK, "{}");

        var act = () => envelope.SendAsync<Raw.SubmitAndWaitForTransactionResponse, string>(
            CallOfKind(Enum.Parse<LedgerCallKind>(kindName)),
            _ =>
            {
                callerCancellation.Cancel();
                throw new OperationCanceledException(callerCancellation.Token);
            },
            timeout: null,
            callerCancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("Read")]
    [InlineData("AcceptedOnlyWrite")]
    [InlineData("EffectAppliedWrite")]
    public async Task SendAsync_propagates_an_exception_raised_before_any_response_when_it_is_not_a_transport_failure(
        string kindName)
    {
        var raised = new InvalidOperationException("handler bug");
        var factory = new StubHttpClientFactory(new RecordingHttpHandler().WithTransportException(raised));
        _factories.Add(factory);
        var envelope = new RestCallEnvelope(factory, NullLogger.Instance);

        var act = () => envelope.SendAsync<Raw.SubmitAndWaitForTransactionResponse, string>(
            CallOfKind(Enum.Parse<LedgerCallKind>(kindName)),
            _ => "unreachable",
            timeout: null,
            TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(raised);
    }

    private async Task<LedgerOperationException> ThrownBySendAsync(
        string kindName, Func<Raw.SubmitAndWaitForTransactionResponse, string> project)
    {
        var envelope = EnvelopeServing(HttpStatusCode.OK, "{}");

        var act = () => envelope.SendAsync(
            CallOfKind(Enum.Parse<LedgerCallKind>(kindName)), project, timeout: null, TestContext.Current.CancellationToken);

        return (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
    }

    private RestCallEnvelope EnvelopeServing(HttpStatusCode statusCode, string body)
    {
        var factory = new StubHttpClientFactory(new RecordingHttpHandler().WithResponse(statusCode, body));
        _factories.Add(factory);
        return new RestCallEnvelope(factory, NullLogger.Instance);
    }

    private static RestCall CallOfKind(LedgerCallKind kind) =>
        new(HttpMethod.Post, "/v2/commands/submit-and-wait-for-transaction", Body: null, "missing body", "malformed prefix: ", kind);

    private static NullReferenceException NullDereference()
    {
        try
        {
            _ = ((string?)null)!.Length;
        }
        catch (NullReferenceException raised)
        {
            return raised;
        }

        throw new InvalidOperationException("Dereferencing null did not raise");
    }

    [Theory]
    [MemberData(nameof(ProjectionExceptionsAfterASuccessResponse))]
    public async Task TrySendAsync_reports_whatever_the_projection_raised_after_a_success_response_as_CommittedUndecodable(
        Exception raised, string expectedPrefix)
    {
        var factory = new StubHttpClientFactory(
            new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, """{"transaction": {"updateId": "update-1"}}"""));
        _factories.Add(factory);
        var envelope = new RestCallEnvelope(factory, NullLogger.Instance);

        var outcome = await envelope.TrySendAsync<Raw.SubmitAndWaitForTransactionResponse, string>(
            new RestCall(
                HttpMethod.Post, "/v2/commands/submit-and-wait-for-transaction", Body: null,
                "missing body", "malformed prefix: ", LedgerCallKind.EffectAppliedWrite),
            _ => throw raised,
            body => body.Transaction?.UpdateId,
            timeout: null,
            TestContext.Current.CancellationToken);

        var undecodable = outcome.Should().BeOfType<ExerciseOutcome<string>.CommittedUndecodable>().Subject;
        undecodable.Message.Should().StartWith(expectedPrefix);
        undecodable.UpdateId.Should().Be("update-1");
        undecodable.SourceException.Should().BeSameAs(raised);
    }

    private static RecordingHttpHandler TimedOutTransport() =>
        new RecordingHttpHandler().WithTransportException(
            new TaskCanceledException("timed out", new TimeoutException()));

    private static void AssertClassifiedAsUndecodable(string operation, CallFailure failure, string what)
    {
        if (ReportedAsCommittedUndecodable.Contains(operation))
        {
            failure.CommittedUndecodable.Should().BeTrue(
                $"{what} after a 2xx means the command committed, so a reported outcome is CommittedUndecodable");
            failure.Status.Should().BeNull();
            failure.UpdateId.Should().BeNull("the body was not decoded far enough to read an update id");
            failure.SourceException.Should().NotBeNull();
        }
        else
        {
            failure.CommittedUndecodable.Should().BeFalse();
            failure.Status.Should().Be(
                new TransportStatus.UndecodableBody(),
                $"a thrown failure reports {what} as the undecodable body it is, not an invented HTTP status");
        }
    }

    private static async Task<CallFailure> InvokeAsync(RestLedgerClient client, string operation)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        return operation switch
        {
            nameof(RestLedgerClient.SubmitAndWaitAsync) => await ThrownAsync(
                () => client.SubmitAndWaitAsync(Submission(), Deadline, cancellationToken)),
            nameof(RestLedgerClient.TrySubmitAndWaitForTransactionAsync) => Reported(
                await client.TrySubmitAndWaitForTransactionAsync(Submission(), Deadline, cancellationToken)),
            nameof(RestLedgerClient.TrySubmitAndWaitForReassignmentAsync) => Reported(
                await client.TrySubmitAndWaitForReassignmentAsync<TestTemplate>(
                    Reassignment(), Deadline, cancellationToken)),
            nameof(RestLedgerClient.EstimateTrafficCostAsync) => await ThrownAsync(
                () => client.EstimateTrafficCostAsync(Submission(), Deadline, cancellationToken)),
            _ => throw new InvalidOperationException($"The theory names an operation it cannot invoke: {operation}"),
        };
    }

    private static async Task<CallFailure> ThrownAsync<TResult>(Func<Task<TResult>> operation)
    {
        var thrown = await operation.Should().ThrowAsync<LedgerOperationException>();
        return new CallFailure(thrown.Which.Status, thrown.Which.Message, thrown.Which.Category);
    }

    private static CallFailure Reported<TResult>(ExerciseOutcome<TResult> outcome)
    {
        if (outcome is ExerciseOutcome<TResult>.CommittedUndecodable undecodable)
        {
            return new CallFailure(
                null, undecodable.Message, null, true, undecodable.UpdateId, undecodable.SourceException);
        }

        var infraError = outcome.Should().BeOfType<ExerciseOutcome<TResult>.InfraError>().Subject;
        return new CallFailure(infraError.Status, infraError.Message, infraError.Category);
    }

    private static RuntimeCommands.CommandsSubmission Submission() =>
        RuntimeCommands.CommandsSubmission
            .Single(RuntimeCommands.CreateCommand.For(new TestTemplate()))
            .WithActAs(Alice);

    private static ReassignmentSubmission Reassignment() =>
        ReassignmentSubmission.Of(
            new UnassignCommand("00cid", new SynchronizerId("sync-a"), new SynchronizerId("sync-b")), Alice);

    private RestLedgerClient ClientWith(HttpMessageHandler transport)
    {
        var factory = new StubHttpClientFactory(transport);
        _factories.Add(factory);
        return new RestLedgerClient(
            factory, Options.Create(new RestLedgerClientOptions { HttpAddress = "http://localhost:7575" }));
    }

    private readonly record struct CallFailure(
        TransportStatus? Status,
        string Message,
        DamlErrorCategory? Category,
        bool CommittedUndecodable = false,
        string? UpdateId = null,
        Exception? SourceException = null);

    private sealed record EnvelopedCall(
        string Operation, string MissingBodyMessage, string MalformedBodyPrefix);

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

    private sealed class SuccessWithABodyThatFailsHandler(Exception failure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new FailingBodyContent(failure) });
    }

    private sealed class DeadlineElapsesBeforeTheBodyIsReadHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = new StringContent("{}", Encoding.UTF8, "application/json");
            await body.LoadIntoBufferAsync(CancellationToken.None).ConfigureAwait(false);

            var deadlineElapsed = new TaskCompletionSource();
            await using var _ = cancellationToken
                .Register(() => deadlineElapsed.TrySetResult())
                .ConfigureAwait(false);
            await deadlineElapsed.Task.ConfigureAwait(false);

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = body };
        }
    }

    private sealed record TestTemplate : ITemplate, IDamlRecord<TestTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "EnvelopeTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "pkg-name";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, [new DamlField("owner", Alice.ToDamlValue())]);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(
                json,
                context,
                ("owner", DamlLfJsonDecoders.ReadParty));
        public static TestTemplate FromRecord(DamlRecord record) =>
            new();
    }
}
