// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Resilience;
using Canton.Ledger.Kernel.Wire;
using Com.Daml.Ledger.Api.V2;
using AwesomeAssertions;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;
using Grpc.Core;
using NSubstitute;
using Xunit;
using Status = Grpc.Core.Status;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerCallInvokerTests
{
    private readonly ITokenProvider _tokenProvider = new StaticTokenProvider("test-token");

    private static LedgerClientOptions Options(TimeSpan? timeout = null, RetryOptions? retry = null) =>
        new()
        {
            GrpcAddress = "https://participant.example:6001",
            Timeout = timeout,
            Retry = retry ?? new RetryOptions(),
        };

    [Fact]
    public async Task GetHeadersAsync_attaches_a_bearer_token_from_the_provider()
    {
        var invoker = new LedgerCallInvoker(Options(), _tokenProvider);

        var headers = await invoker.GetHeadersAsync(TestContext.Current.CancellationToken);

        headers.Should().NotBeNull();
        headers!.GetValue("authorization").Should().Be("Bearer test-token");
    }

    [Fact]
    public async Task GetHeadersAsync_returns_null_when_unauthenticated()
    {
        var invoker = new LedgerCallInvoker(Options(), ITokenProvider.None);

        var headers = await invoker.GetHeadersAsync(TestContext.Current.CancellationToken);

        headers.Should().BeNull();
    }

    [Fact]
    public async Task GetHeadersAsync_throws_when_the_provider_returns_an_empty_token()
    {
        var emptyProvider = Substitute.For<ITokenProvider>();
        emptyProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("   ");
        var invoker = new LedgerCallInvoker(Options(), emptyProvider);

        var act = () => invoker.GetHeadersAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*returned an empty token*");
    }

    [Fact]
    public void GetDeadline_returns_null_when_no_timeout_is_configured()
    {
        var invoker = new LedgerCallInvoker(Options(timeout: null), _tokenProvider);

        invoker.GetDeadline().Should().BeNull();
    }

    [Fact]
    public void GetDeadline_returns_a_future_deadline_when_a_timeout_is_configured()
    {
        var invoker = new LedgerCallInvoker(Options(timeout: TimeSpan.FromSeconds(30)), _tokenProvider);

        invoker.GetDeadline().Should().NotBeNull().And.Subject.As<DateTime?>()!.Value.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public void TagServerCall_tags_the_activity_with_grpc_semconv_from_the_parsed_endpoint()
    {
        var invoker = new LedgerCallInvoker(Options(), _tokenProvider);

        using var source = new ActivitySource("LedgerCallInvokerTests.TagServerCall");
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("call");
        activity.Should().NotBeNull();

        invoker.TagServerCall(activity, CommandService.Descriptor, "Submit");

        activity!.GetTagItem(ActivityHelper.RpcSystem).Should().Be("grpc");
        activity.GetTagItem(ActivityHelper.RpcService).Should().Be("com.daml.ledger.api.v2.CommandService");
        activity.GetTagItem(ActivityHelper.RpcMethod).Should().Be("Submit");
        activity.GetTagItem(ActivityHelper.ServerAddress).Should().Be("participant.example");
        activity.GetTagItem(ActivityHelper.ServerPort).Should().Be(6001);
    }

    [Fact]
    public async Task InvokeAsync_runs_the_call_once_and_returns_its_response_when_retry_is_disabled()
    {
        var invoker = new LedgerCallInvoker(Options(), _tokenProvider);
        var calls = 0;
        Metadata? seenHeaders = null;

        var response = await invoker.InvokeAsync(
            (headers, _, _) =>
            {
                calls++;
                seenHeaders = headers;
                return Ok(new GetLedgerEndResponse { Offset = 7L });
            },
            TestContext.Current.CancellationToken);

        calls.Should().Be(1);
        response.Offset.Should().Be(7L);
        seenHeaders.Should().NotBeNull("the invoker recomputes auth headers per attempt");
    }

    [Fact]
    public async Task InvokeAsync_does_not_retry_a_transient_failure_when_retry_is_disabled()
    {
        var invoker = new LedgerCallInvoker(Options(), _tokenProvider);
        var attempts = 0;

        var act = () => invoker.InvokeAsync(
            (_, _, _) =>
            {
                attempts++;
                return Faulted<GetLedgerEndResponse>(new RpcException(new Status(StatusCode.Unavailable, "down")));
            },
            TestContext.Current.CancellationToken).AsTask();

        await act.Should().ThrowAsync<RpcException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task InvokeTracedAsync_reclassifies_caller_cancellation_as_OperationCanceledException()
    {
        var invoker = new LedgerCallInvoker(Options(), _tokenProvider);
        using var source = new ActivitySource("LedgerCallInvokerTests.CancelReclassify");
        using var cts = new CancellationTokenSource();

        var act = () => invoker.InvokeTracedAsync<LedgerClient, GetLedgerEndResponse, long>(
            LedgerCallKind.Read,
            source,
            StateService.Descriptor,
            "GetLedgerEnd",
            (_, _, _) =>
            {
                cts.Cancel();
                return Faulted<GetLedgerEndResponse>(new RpcException(new Status(StatusCode.Cancelled, "cancelled")));
            },
            response => response.Offset,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task InvokeTracedAsync_rethrows_an_expected_failure_without_recording_it_on_the_span()
    {
        var invoker = new LedgerCallInvoker(Options(), _tokenProvider);
        using var source = new ActivitySource("LedgerCallInvokerTests.ExpectedFailure");
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var act = () => invoker.InvokeTracedAsync<LedgerClient, GetLedgerEndResponse, long>(
            LedgerCallKind.Read,
            source,
            StateService.Descriptor,
            "GetLedgerEnd",
            (_, _, _) => Faulted<GetLedgerEndResponse>(new RpcException(new Status(StatusCode.NotFound, "absent"))),
            response => response.Offset,
            TestContext.Current.CancellationToken,
            isExpectedFailure: ex => ex.StatusCode == StatusCode.NotFound);

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.NotFound);
        stopped.Should().ContainSingle().Which.Status.Should().Be(
            ActivityStatusCode.Unset,
            "an expected failure is left for the caller to translate and is not recorded as a span error");
    }

    [Fact]
    public async Task InvokeTracedAsync_records_the_failure_on_the_span_and_raises_it_as_a_ledger_operation_exception()
    {
        var invoker = new LedgerCallInvoker(Options(), _tokenProvider);
        using var source = new ActivitySource("LedgerCallInvokerTests.TranslatedFailure");
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var unavailable = new RpcException(new Status(StatusCode.Unavailable, "down"));

        var act = () => invoker.InvokeTracedAsync<LedgerClient, GetLedgerEndResponse, long>(
            LedgerCallKind.AcceptedOnlyWrite,
            source,
            StateService.Descriptor,
            "GetLedgerEnd",
            (_, _, _) => Faulted<GetLedgerEndResponse>(unavailable),
            response => response.Offset,
            TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.InnerException.Should().BeSameAs(unavailable);
        thrown.CommitState.Should().Be(CommitState.Unknown);
        stopped.Should().ContainSingle().Which.Status.Should().Be(ActivityStatusCode.Error);
    }

    [Theory]
    [InlineData("Read", CommitState.NotCommitted)]
    [InlineData("AcceptedOnlyWrite", CommitState.Unknown)]
    [InlineData("EffectAppliedWrite", CommitState.Committed)]
    public async Task InvokeTracedAsync_raises_an_undecodable_response_as_a_ledger_operation_exception_with_the_commit_state_of_its_kind(
        string kindName, CommitState expected)
    {
        var invoker = new LedgerCallInvoker(Options(), _tokenProvider);
        using var source = new ActivitySource("LedgerCallInvokerTests.UndecodableByKind");
        var malformed = new MalformedResponseException("the response has no offset");

        var act = () => invoker.InvokeTracedAsync<LedgerClient, GetLedgerEndResponse, long>(
            System.Enum.Parse<LedgerCallKind>(kindName),
            source,
            StateService.Descriptor,
            "GetLedgerEnd",
            (_, _, _) => Ok(new GetLedgerEndResponse()),
            _ => throw malformed,
            TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.InnerException.Should().BeSameAs(malformed);
        thrown.Message.Should().Be("Malformed response from ledger: the response has no offset");
        thrown.CommitState.Should().Be(expected);
        thrown.Category.Should().BeNull();
        thrown.ErrorId.Should().BeNull();
        thrown.Metadata.Should().BeNull();
    }

    [Fact]
    public async Task InvokeTracedAsync_wraps_a_wire_format_failure_in_a_malformed_response_inner_exception()
    {
        var invoker = new LedgerCallInvoker(Options(), _tokenProvider);
        using var source = new ActivitySource("LedgerCallInvokerTests.UndecodableFormat");
        var format = new FormatException("Cannot parse wire Int64 value 'x' as a 64-bit integer.");

        var act = () => invoker.InvokeTracedAsync<LedgerClient, GetLedgerEndResponse, long>(
            LedgerCallKind.Read,
            source,
            StateService.Descriptor,
            "GetLedgerEnd",
            (_, _, _) => Ok(new GetLedgerEndResponse()),
            _ => throw format,
            TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        var inner = thrown.InnerException.Should().BeOfType<MalformedResponseException>().Which;
        inner.Detail.Should().Be("Cannot parse wire Int64 value 'x' as a 64-bit integer.");
        inner.InnerException.Should().BeSameAs(format);
    }

    [Fact]
    public async Task InvokeTracedAsync_leaves_a_failure_that_is_not_a_wire_decode_failure_untouched()
    {
        var invoker = new LedgerCallInvoker(Options(), _tokenProvider);
        using var source = new ActivitySource("LedgerCallInvokerTests.UnrelatedProjectionFailure");
        var ours = new InvalidOperationException("a bug of ours");

        var act = () => invoker.InvokeTracedAsync<LedgerClient, GetLedgerEndResponse, long>(
            LedgerCallKind.EffectAppliedWrite,
            source,
            StateService.Descriptor,
            "GetLedgerEnd",
            (_, _, _) => Ok(new GetLedgerEndResponse()),
            _ => throw ours,
            TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(ours);
    }

    [Fact]
    public async Task InvokeTracedAsync_records_an_undecodable_response_on_the_span_and_does_not_retry_it()
    {
        var invoker = new LedgerCallInvoker(
            Options(retry: new RetryOptions { Enabled = true, MaxRetryAttempts = 3, Delay = TimeSpan.FromMilliseconds(1) }),
            _tokenProvider);
        using var source = new ActivitySource("LedgerCallInvokerTests.UndecodableRecorded");
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var attempts = 0;

        var act = () => invoker.InvokeTracedAsync<LedgerClient, GetLedgerEndResponse, long>(
            LedgerCallKind.Read,
            source,
            StateService.Descriptor,
            "GetLedgerEnd",
            (_, _, _) =>
            {
                attempts++;
                return Ok(new GetLedgerEndResponse());
            },
            _ => throw new MalformedResponseException("the response has no offset"),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
        attempts.Should().Be(1);
        var span = stopped.Should().ContainSingle().Which;
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.GetTagItem(ActivityHelper.ErrorType).Should().Be("UndecodableBody");
    }

    [Fact]
    public async Task A_call_made_after_the_outer_response_arrived_does_not_inherit_the_outer_response_received_state()
    {
        var format = new FormatException("the token is not valid base64");
        var outer = new LedgerCallInvoker(Options(), _tokenProvider);
        var child = new LedgerCallInvoker(Options(), FailingTokenProvider(format));
        using var source = new ActivitySource("LedgerCallInvokerTests.NestedPreSend");

        var escaped = await outer.ExecuteTracedAsync<LedgerClient, Exception?>(
            LedgerCallKind.Read,
            source,
            StateService.Descriptor,
            "GetLedgerEnd",
            async (_, token) =>
            {
                await outer.InvokeAsync((_, _, _) => Ok(new GetLedgerEndResponse()), token);
                return await CaptureFailure(() => ChildCall(child, source, token));
            },
            TestContext.Current.CancellationToken);

        escaped.Should().BeSameAs(format);
    }

    [Fact]
    public async Task Concurrent_calls_made_after_the_outer_response_arrived_do_not_inherit_the_outer_response_received_state()
    {
        var format = new FormatException("the token is not valid base64");
        var outer = new LedgerCallInvoker(Options(), _tokenProvider);
        var child = new LedgerCallInvoker(Options(), FailingTokenProvider(format));
        using var source = new ActivitySource("LedgerCallInvokerTests.ConcurrentPreSend");

        var escaped = await outer.ExecuteTracedAsync<LedgerClient, Exception?[]>(
            LedgerCallKind.Read,
            source,
            StateService.Descriptor,
            "GetLedgerEnd",
            async (_, token) =>
            {
                await outer.InvokeAsync((_, _, _) => Ok(new GetLedgerEndResponse()), token);
                return await Task.WhenAll(
                    CaptureFailure(() => ChildCall(child, source, token)),
                    CaptureFailure(() => ChildCall(child, source, token)));
            },
            TestContext.Current.CancellationToken);

        escaped.Should().HaveCount(2).And.AllSatisfy(failure => failure.Should().BeSameAs(format));
    }

    private static ITokenProvider FailingTokenProvider(Exception failure)
    {
        var tokenProvider = Substitute.For<ITokenProvider>();
        tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns<string>(_ => throw failure);
        return tokenProvider;
    }

    private static Task<long> ChildCall(LedgerCallInvoker child, ActivitySource source, CancellationToken token) =>
        child.InvokeTracedAsync<LedgerClient, GetLedgerEndResponse, long>(
            LedgerCallKind.Read,
            source,
            StateService.Descriptor,
            "GetLedgerEnd",
            (_, _, _) => Ok(new GetLedgerEndResponse()),
            _ => 0L,
            token);

    private static async Task<Exception?> CaptureFailure(Func<Task> call)
    {
        try
        {
            await call();
            return null;
        }
        catch (Exception failure)
        {
            return failure;
        }
    }

    private static AsyncUnaryCall<T> Ok<T>(T value) =>
        new(
            Task.FromResult(value),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    private static AsyncUnaryCall<T> Faulted<T>(RpcException exception) =>
        new(
            Task.FromException<T>(exception),
            Task.FromResult(new Metadata()),
            () => exception.Status,
            () => exception.Trailers ?? new Metadata(),
            () => { });
}
