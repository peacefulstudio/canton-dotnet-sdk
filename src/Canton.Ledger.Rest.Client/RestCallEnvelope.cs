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

internal sealed partial class RestCallEnvelope(IHttpClientFactory httpClientFactory, ILogger logger)
{
    public async Task<TResult> SendAsync<TResponse, TResult>(
        RestCall call,
        Func<TResponse, TResult> project,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        var attempt = await AttemptJsonAsync(call, project, updateIdOf: null, timeout, cancellationToken).ConfigureAwait(false);
        return Unwrap(attempt, call.Kind);
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
        return Unwrap(attempt, call.Kind);
    }

    private static TResult Unwrap<TResult>(Attempt<TResult> attempt, LedgerCallKind kind) => attempt switch
    {
        Attempt<TResult>.Ok ok => ok.Result,
        Attempt<TResult>.Failed failed => throw ToException(failed.Failure, kind),
        _ => throw new InvalidOperationException($"Unhandled REST call attempt: {attempt.GetType().Name}"),
    };

    public async Task<ExerciseOutcome<TProjection>> TrySendAsync<TResponse, TProjection>(
        RestCall call,
        Func<TResponse, ExerciseOutcome<TProjection>> project,
        Func<TResponse, string?> updateIdOf,
        TimeSpan? timeout,
        CancellationToken cancellationToken,
        Func<ExerciseOutcome<TProjection>.DamlError, CancellationToken, Task<ExerciseOutcome<TProjection>>>? resolveRetriedDuplicate = null)
        where TResponse : class
    {
        var attempt = await AttemptJsonAsync(call, project, updateIdOf, timeout, cancellationToken).ConfigureAwait(false);
        return attempt switch
        {
            Attempt<ExerciseOutcome<TProjection>>.Ok ok => ok.Result,
            Attempt<ExerciseOutcome<TProjection>>.Failed failed => await ResolveRejectionAsync(failed.Failure, resolveRetriedDuplicate, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unhandled REST call attempt: {attempt.GetType().Name}"),
        };
    }

    private static async Task<ExerciseOutcome<TProjection>> ResolveRejectionAsync<TProjection>(
        RestCallFailure failure,
        Func<ExerciseOutcome<TProjection>.DamlError, CancellationToken, Task<ExerciseOutcome<TProjection>>>? resolveRetriedDuplicate,
        CancellationToken cancellationToken)
    {
        var outcome = ToOutcome<TProjection>(failure);
        return failure is RestCallFailure.Rejected { AfterRetry: true }
            && outcome is ExerciseOutcome<TProjection>.DamlError { ErrorId: RetriedDuplicateCommand.ErrorId } duplicate
            && resolveRetriedDuplicate is not null
                ? await resolveRetriedDuplicate(duplicate, cancellationToken).ConfigureAwait(false)
                : outcome;
    }

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
                decoded = await response.Content
                    .ReadFromJsonAsync<TResponse>(RestRefitSettings.SerializerOptions, token)
                    .ConfigureAwait(false);

                return decoded is null
                    ? Failed<TResult>(new RestCallFailure.Undecodable(call.MissingBodyMessage, null, null))
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
        using var timeoutSource = CreateTimeoutSource(timeout, cancellationToken);
        var requestToken = timeoutSource?.Token ?? cancellationToken;

        HttpResponseMessage response;
        var afterRetry = false;
        try
        {
            using var request = CreateRequest(call);
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken)
                .ConfigureAwait(false);
            afterRetry = RestRetryHandler.WasRetried(request);
        }
        catch (Exception failure) when (IsNoAnswerFailure(failure, cancellationToken))
        {
            return Failed<TResult>(ClassifyTransport(failure, DeadlineExceeded(timeout)));
        }

        using (response)
        {
            using var bodyTimeoutSource = CreateBodyTimeoutSource(client, timeout, cancellationToken);
            var bodyToken = bodyTimeoutSource?.Token ?? requestToken;

            if (!response.IsSuccessStatusCode)
            {
                return await RejectionAsync<TResult>(response, afterRetry, timeout, bodyToken, cancellationToken)
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
                    DeadlineExceededWhileReading(timeout),
                    call.MalformedBodyMessagePrefix,
                    updateIdAfterFailure(),
                    cancellationToken));
            }
        }
    }

    private static async Task<Attempt<TResult>> RejectionAsync<TResult>(
        HttpResponseMessage response,
        bool afterRetry,
        TimeSpan? timeout,
        CancellationToken requestToken,
        CancellationToken callerToken)
    {
        try
        {
            return Failed<TResult>(new RestCallFailure.Rejected(
                await RestErrorParser.ParseAsync(response, requestToken).ConfigureAwait(false), afterRetry));
        }
        catch (Exception failure) when (IsTransportFailure(failure, callerToken))
        {
            return Failed<TResult>(ClassifyTransport(failure, DeadlineExceededWhileReading(timeout)));
        }
    }

    private static HttpRequestMessage CreateRequest(RestCall call)
    {
        var request = new HttpRequestMessage(call.Method, call.Path);
        if (!call.Replayable)
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
        IsTransportFailure(failure, callerToken) || failure is TimeoutException;

    private static bool IsBodyTransportFailure(Exception failure, CancellationToken callerToken) =>
        IsTransportFailure(failure, callerToken) || failure is IOException;

    private static bool IsResponseFailure(LedgerCallKind kind, Exception failure, CancellationToken callerToken) =>
        IsBodyTransportFailure(failure, callerToken)
        || (kind is LedgerCallKind.Read ? IsUndecodableBody(failure) : !IsCallerCancellation(failure, callerToken));

    private static bool IsCallerCancellation(Exception failure, CancellationToken callerToken) =>
        failure is OperationCanceledException && callerToken.IsCancellationRequested;

    private static bool IsUndecodableBody(Exception failure) =>
        failure is JsonException
        || MalformedResponse.IsWireDecodeFailure(failure)
        || IsPayloadRefusal(failure);

    private const string CommittedButUndecodablePrefix =
        "The command committed, but its transaction could not be decoded: ";

    private static bool IsPayloadRefusal(Exception failure) =>
        failure is TemplateTypeRequiredException;

    private static RestCallFailure ClassifyTransport(Exception failure, string deadlineExceeded) =>
        new RestCallFailure.NoResponse(TransportMessage(failure, deadlineExceeded), failure);

    private static string TransportMessage(Exception failure, string deadlineExceeded) =>
        failure is HttpRequestException or IOException or TimeoutException ? failure.Message : deadlineExceeded;

    private RestCallFailure ClassifyResponse(
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
                : new RestCallFailure.Undecodable(TransportMessage(failure, deadlineExceeded), failure, updateId);
        }

        LogUndecodableResponseBody(logger, failure);
        var messagePrefix = IsPayloadRefusal(failure) ? CommittedButUndecodablePrefix : malformedBodyMessagePrefix;
        return new RestCallFailure.Undecodable($"{messagePrefix}{failure.Message}", failure, updateId);
    }

    private static LedgerOperationException ToException(RestCallFailure failure, LedgerCallKind kind) => failure switch
    {
        RestCallFailure.Rejected rejected => rejected.Parsed.ToException(kind),
        RestCallFailure.NoResponse noResponse =>
            kind.NoAnswer(noResponse.Message, new TransportStatus.NoResponse(), noResponse.Cause),
        RestCallFailure.Undecodable undecodable => kind.UnreadableResponse(undecodable.Message, undecodable.Cause),
        _ => throw new InvalidOperationException($"Unhandled REST call failure: {failure.GetType().Name}"),
    };

    private static ExerciseOutcome<T> ToOutcome<T>(RestCallFailure failure) => failure switch
    {
        RestCallFailure.Rejected { Parsed: ParsedLedgerError.Structured structured } =>
            new ExerciseOutcome<T>.DamlError(
                structured.Category, structured.ErrorId, structured.Message, structured.Metadata),
        RestCallFailure.Rejected { Parsed: ParsedLedgerError.Unstructured unstructured } =>
            new ExerciseOutcome<T>.InfraError(
                unstructured.Status, unstructured.Message, unstructured.Category),
        RestCallFailure.NoResponse noResponse =>
            new ExerciseOutcome<T>.InfraError(
                new TransportStatus.NoResponse(), noResponse.Message, SourceException: noResponse.Cause),
        RestCallFailure.Undecodable undecodable =>
            new ExerciseOutcome<T>.CommittedUndecodable(
                undecodable.UpdateId,
                undecodable.Message,
                undecodable.Cause ?? new InvalidOperationException(undecodable.Message)),
        _ => throw new InvalidOperationException($"Unhandled REST call failure: {failure.GetType().Name}"),
    };

    private static Attempt<TResult> Failed<TResult>(RestCallFailure failure) =>
        new Attempt<TResult>.Failed(failure);

    private static string DeadlineExceeded(TimeSpan? timeout) =>
        $"Request exceeded the {DescribeDeadline(timeout)} deadline.";

    private static string DeadlineExceededWhileReading(TimeSpan? timeout) =>
        $"Request exceeded the {DescribeDeadline(timeout)} deadline while reading the response body.";

    private static string DescribeDeadline(TimeSpan? timeout) =>
        timeout is { } window ? window.ToString() : "HttpClient default";

    private static CancellationTokenSource? CreateBodyTimeoutSource(
        HttpClient client, TimeSpan? timeout, CancellationToken cancellationToken) =>
        timeout is null && client.Timeout != Timeout.InfiniteTimeSpan
            ? CreateTimeoutSource(client.Timeout, cancellationToken)
            : null;

    internal static CancellationTokenSource? CreateTimeoutSource(TimeSpan? timeout, CancellationToken cancellationToken)
    {
        if (timeout is not { } window)
            return null;

        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(window);
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

        internal sealed record Failed(RestCallFailure Failure) : Attempt<TResult>;
    }
}
