// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.Logging;

namespace Canton.Ledger.Rest.Client;

internal sealed partial class RestCallEnvelope(
    IHttpClientFactory httpClientFactory,
    ILogger logger,
    long maxResponseBodyBytes = RestLedgerClientOptions.DefaultMaxResponseBodyBytes,
    TimeSpan? defaultTimeout = null)
{
    public async Task<TResult> SendAsync<TResponse, TResult>(
        RestCall call,
        Func<TResponse, TResult> project,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        var attempt = await AttemptJsonAsync(call, project, updateIdOf: null, timeout, cancellationToken).ConfigureAwait(false);
        return Unwrap(attempt, call);
    }

    public async Task<TResult> SendAsync<TResult>(
        RestCall call,
        Func<HttpResponseMessage, CancellationToken, Task<TResult>> read,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var attempt = await AttemptAsync<TResult>(
            call,
            async (response, token) => new Attempt<TResult>.Ok(await read(response, token).ConfigureAwait(false)),
            updateIdAfterFailure: static () => null,
            timeout,
            cancellationToken).ConfigureAwait(false);
        return Unwrap(attempt, call);
    }

    private static TResult Unwrap<TResult>(Attempt<TResult> attempt, RestCall call) => attempt switch
    {
        Attempt<TResult>.Ok ok => ok.Result,
        Attempt<TResult>.Failed failed => throw failed.Ending.ToException(call.Kind, Daml.Runtime.Commands.CommandId.FromWire(call.CommandId)),
        _ => throw new InvalidOperationException($"Unhandled REST call attempt: {attempt.GetType().Name}"),
    };

    public async Task<ExerciseOutcome<TProjection>> TrySendAsync<TResponse, TProjection>(
        RestCall call,
        Func<TResponse, ExerciseOutcome<TProjection>> project,
        Func<TResponse, string?> updateIdOf,
        TimeSpan? timeout,
        CancellationToken cancellationToken,
        Func<RetriedDuplicateVerdict, ExerciseOutcome<TProjection>, CancellationToken, Task<ExerciseOutcome<TProjection>>>? resolveRetriedDuplicate = null)
        where TResponse : class
    {
        var attempt = await AttemptJsonAsync(call, project, updateIdOf, timeout, cancellationToken).ConfigureAwait(false);
        var outcome = attempt switch
        {
            Attempt<ExerciseOutcome<TProjection>>.Ok ok => ok.Result,
            Attempt<ExerciseOutcome<TProjection>>.Failed failed => await ReportAsync(failed.Ending, resolveRetriedDuplicate, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unhandled REST call attempt: {attempt.GetType().Name}"),
        };
        return outcome.WithCommandId(call.CommandId);
    }

    private static async Task<ExerciseOutcome<TProjection>> ReportAsync<TProjection>(
        LedgerCallEnding ending,
        Func<RetriedDuplicateVerdict, ExerciseOutcome<TProjection>, CancellationToken, Task<ExerciseOutcome<TProjection>>>? resolveRetriedDuplicate,
        CancellationToken cancellationToken)
    {
        var outcome = ending.ToOutcome<TProjection>();
        var verdict = ending.ToRetriedDuplicateVerdict();
        return verdict is not RetriedDuplicateVerdict.NotARetriedDuplicate && resolveRetriedDuplicate is not null
            ? await resolveRetriedDuplicate(verdict, outcome, cancellationToken).ConfigureAwait(false)
            : outcome;
    }

    internal long MaxResponseBodyBytes { get; } = maxResponseBodyBytes;

    internal Task<byte[]> ReadBodyBytesAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        ResponseBodyCap.ReadBytesAsync(response.Content, MaxResponseBodyBytes, cancellationToken);

    internal Task<string> ReadBodyTextAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        ResponseBodyCap.ReadTextAsync(response.Content, MaxResponseBodyBytes, cancellationToken);

    internal HttpClient CreateClient() =>
        httpClientFactory.CreateClient(ServiceCollectionExtensions.HttpClientName);

    private Task<Attempt<TResult>> AttemptJsonAsync<TResponse, TResult>(
        RestCall call,
        Func<TResponse, TResult> project,
        Func<TResponse, string?>? updateIdOf,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        TResponse? decoded = null;
        return AttemptAsync(
            call,
            async (response, token) =>
            {
                using var capped = await ResponseBodyCap.CapAsync(response.Content, MaxResponseBodyBytes, token)
                    .ConfigureAwait(false);
                decoded = await capped
                    .ReadFromJsonAsync<TResponse>(RestRefitSettings.SerializerOptions, token)
                    .ConfigureAwait(false);

                return decoded is null
                    ? Failed<TResult>(new LedgerCallEnding.Unreadable(call.MissingBodyMessage, Cause: null, UpdateId: null))
                    : new Attempt<TResult>.Ok(project(decoded));
            },
            () => decoded is null || updateIdOf is null ? null : updateIdOf(decoded),
            timeout,
            cancellationToken);
    }

    private async Task<Attempt<TResult>> AttemptAsync<TResult>(
        RestCall call,
        Func<HttpResponseMessage, CancellationToken, Task<Attempt<TResult>>> readSuccess,
        Func<string?> updateIdAfterFailure,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var client = CreateClient();
        var attemptTimeout = timeout ?? defaultTimeout;

        HttpResponseMessage response;
        var afterRetry = false;
        try
        {
            using var request = CreateRequest(call, attemptTimeout);
            BearerTokenHandler.RecordCallerCancellation(request, cancellationToken);
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            afterRetry = RestRetryHandler.WasRetried(request);
        }
        catch (Exception failure) when (IsNoAnswerFailure(failure, cancellationToken))
        {
            return Failed<TResult>(ClassifyTransport(failure, HttpClientTimeoutExceeded));
        }

        using (response)
        {
            using var bodyTimeoutSource = CreateBodyTimeoutSource(client, attemptTimeout, cancellationToken);
            var bodyToken = bodyTimeoutSource?.Token ?? cancellationToken;

            if (!response.IsSuccessStatusCode)
            {
                return await RejectionAsync<TResult>(response, afterRetry, attemptTimeout, bodyToken, cancellationToken)
                    .ConfigureAwait(false);
            }

            try
            {
                return await readSuccess(response, bodyToken).ConfigureAwait(false);
            }
            catch (Exception failure) when (IsResponseFailure(call.Kind, failure, cancellationToken))
            {
                return Failed<TResult>(ClassifyResponse(
                    call.Kind,
                    failure,
                    DeadlineExceededWhileReading(attemptTimeout),
                    call.MalformedBodyMessagePrefix,
                    updateIdAfterFailure(),
                    cancellationToken));
            }
        }
    }

    private async Task<Attempt<TResult>> RejectionAsync<TResult>(
        HttpResponseMessage response,
        bool afterRetry,
        TimeSpan? timeout,
        CancellationToken requestToken,
        CancellationToken callerToken)
    {
        try
        {
            return Failed<TResult>(
                await RestErrorParser.ReadRejectionAsync(response, requestToken, afterRetry, MaxResponseBodyBytes).ConfigureAwait(false));
        }
        catch (Exception failure) when (IsTransportFailure(failure, callerToken))
        {
            return Failed<TResult>(ClassifyTransport(failure, DeadlineExceededWhileReading(timeout)));
        }
    }

    private static HttpRequestMessage CreateRequest(RestCall call, TimeSpan? attemptTimeout)
    {
        var request = new HttpRequestMessage(call.Method, call.Path);
        if (attemptTimeout is { } timeout)
        {
            RestAttemptTimeoutHandler.SetAttemptTimeout(request, timeout);
        }
        if (call.PathTemplate is { } pathTemplate)
        {
            request.Options.Set(RestActivityHandler.PathTemplateOption, pathTemplate);
        }
        if (call.Replayable)
        {
            RestRetryHandler.MarkReplayable(request);
        }
        else
        {
            RestRetryHandler.MarkNotReplayable(request);
        }
        if (call.Accept is { } accept)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        }
        request.Content = call.Body switch
        {
            null => null,
            byte[] octets => new ByteArrayContent(octets) { Headers = { ContentType = new MediaTypeHeaderValue(OctetStream) } },
            _ => JsonContent.Create(call.Body, options: RestRefitSettings.SerializerOptions),
        };
        return request;
    }

    internal const string OctetStream = "application/octet-stream";

    private static bool IsTransportFailure(Exception failure, CancellationToken callerToken) =>
        failure is HttpRequestException
        || (failure is OperationCanceledException && !callerToken.IsCancellationRequested);

    internal static bool IsNoAnswerFailure(Exception failure, CancellationToken callerToken) =>
        IsTransportFailure(failure, callerToken) || failure is TimeoutException or TokenAcquisitionException;

    private static bool IsBodyTransportFailure(Exception failure, CancellationToken callerToken) =>
        IsTransportFailure(failure, callerToken) || failure is IOException;

    private static bool IsResponseFailure(LedgerCallKind kind, Exception failure, CancellationToken callerToken) =>
        IsBodyTransportFailure(failure, callerToken)
        || (kind is LedgerCallKind.Read ? IsUndecodableBody(failure) : !IsCallerCancellation(failure, callerToken));

    private static bool IsCallerCancellation(Exception failure, CancellationToken callerToken) =>
        failure is OperationCanceledException && callerToken.IsCancellationRequested;

    private static bool IsUndecodableBody(Exception failure) =>
        MalformedResponse.IsWireDecodeFailure(failure)
        || (MalformedResponse.IsUndecodableWireValue(failure) && failure is not ProjectionRefusalException)
        || IsPayloadRefusal(failure)
        || failure is ResponseBodyTooLargeException;

    private const string CommittedButUndecodablePrefix =
        "The command committed, but its transaction could not be decoded: ";

    private static bool IsPayloadRefusal(Exception failure) =>
        failure is TemplateTypeRequiredException;

    private static LedgerCallEnding ClassifyTransport(Exception failure, string deadlineExceeded) =>
        failure is TokenAcquisitionException tokenFailure
            ? new LedgerCallEnding.NeverSent(tokenFailure)
            : new LedgerCallEnding.Unanswered(TransportMessage(failure, deadlineExceeded), failure);

    private static string TransportMessage(Exception failure, string deadlineExceeded) =>
        failure is HttpRequestException or IOException or TimeoutException ? failure.Message : deadlineExceeded;

    private LedgerCallEnding ClassifyResponse(
        LedgerCallKind kind,
        Exception failure,
        string deadlineExceeded,
        string malformedBodyMessagePrefix,
        string? updateId,
        CancellationToken callerToken)
    {
        if (IsBodyTransportFailure(failure, callerToken))
        {
            return kind is LedgerCallKind.Read
                ? ClassifyTransport(failure, deadlineExceeded)
                : new LedgerCallEnding.Unreadable(TransportMessage(failure, deadlineExceeded), failure, updateId);
        }

        LogUndecodableResponseBody(logger, failure);
        var messagePrefix = IsPayloadRefusal(failure) ? CommittedButUndecodablePrefix : malformedBodyMessagePrefix;
        return new LedgerCallEnding.Unreadable($"{messagePrefix}{failure.Message}", failure, updateId);
    }

    private static Attempt<TResult> Failed<TResult>(LedgerCallEnding ending) =>
        new Attempt<TResult>.Failed(ending);

    private const string HttpClientTimeoutExceeded = "Request exceeded the HttpClient default deadline.";

    private static string DeadlineExceededWhileReading(TimeSpan? timeout) =>
        $"Request exceeded the {DescribeDeadline(timeout)} deadline while reading the response body.";

    private static string DescribeDeadline(TimeSpan? timeout) =>
        timeout is { } window ? window.ToString() : "HttpClient default";

    internal static CancellationTokenSource? CreateBodyTimeoutSource(
        HttpClient client, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var window = timeout ?? (client.Timeout != Timeout.InfiniteTimeSpan ? client.Timeout : null);
        if (window is not { } bodyWindow)
            return null;

        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(bodyWindow);
        return source;
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The participant answered successfully, but the response body could not be decoded — surfaced as a failed call")]
    private static partial void LogUndecodableResponseBody(ILogger logger, Exception exception);

    private abstract record Attempt<TResult>
    {
        private Attempt()
        {
        }

        internal sealed record Ok(TResult Result) : Attempt<TResult>;

        internal sealed record Failed(LedgerCallEnding Ending) : Attempt<TResult>;
    }
}
