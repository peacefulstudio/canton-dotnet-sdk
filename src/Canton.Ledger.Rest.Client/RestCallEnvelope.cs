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
        return Unwrap(attempt);
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
        return Unwrap(attempt);
    }

    private static TResult Unwrap<TResult>(Attempt<TResult> attempt) => attempt switch
    {
        Attempt<TResult>.Ok ok => ok.Result,
        Attempt<TResult>.Failed failed => throw ToException(failed.Failure),
        _ => throw new InvalidOperationException($"Unhandled REST call attempt: {attempt.GetType().Name}"),
    };

    public async Task<ExerciseOutcome<TProjection>> TrySendAsync<TResponse, TProjection>(
        RestCall call,
        Func<TResponse, ExerciseOutcome<TProjection>> project,
        Func<TResponse, string?> updateIdOf,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        var attempt = await AttemptJsonAsync(call, project, updateIdOf, timeout, cancellationToken).ConfigureAwait(false);
        return attempt switch
        {
            Attempt<ExerciseOutcome<TProjection>>.Ok ok => ok.Result,
            Attempt<ExerciseOutcome<TProjection>>.Failed failed => ToOutcome<TProjection>(failed.Failure),
            _ => throw new InvalidOperationException($"Unhandled REST call attempt: {attempt.GetType().Name}"),
        };
    }

    internal static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var rejection = await RestErrorParser.ParseAsync(response, cancellationToken).ConfigureAwait(false);
        throw rejection.ToException();
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
            () => decoded is null ? null : updateIdOf?.Invoke(decoded),
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
        try
        {
            using var request = CreateRequest(call);
            response = await client.SendAsync(request, requestToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (IsTransportFailure(failure, cancellationToken))
        {
            return Failed<TResult>(ClassifyTransport(failure, DeadlineExceeded(timeout)));
        }

        using (response)
        {
            try
            {
                if (!response.IsSuccessStatusCode)
                {
                    return Failed<TResult>(new RestCallFailure.Rejected(
                        await RestErrorParser.ParseAsync(response, requestToken).ConfigureAwait(false)));
                }

                return await readSuccess(response, requestToken).ConfigureAwait(false);
            }
            catch (Exception failure) when (IsResponseFailure(failure, cancellationToken))
            {
                return Failed<TResult>(ClassifyResponse(
                    failure, DeadlineExceededWhileReading(timeout), call.MalformedBodyMessagePrefix, updateIdAfterFailure()));
            }
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

    private static bool IsResponseFailure(Exception failure, CancellationToken callerToken) =>
        IsTransportFailure(failure, callerToken) || IsUndecodableBody(failure);

    private static bool IsUndecodableBody(Exception failure) =>
        failure is JsonException
        || MalformedResponse.IsWireDecodeFailure(failure)
        || IsPayloadRefusal(failure);

    private const string CommittedButUndecodablePrefix =
        "The command committed, but its transaction could not be decoded: ";

    private static bool IsPayloadRefusal(Exception failure) =>
        failure is TemplateTypeRequiredException;

    private static RestCallFailure ClassifyTransport(Exception failure, string deadlineExceeded) =>
        new RestCallFailure.NoResponse(failure is HttpRequestException ? failure.Message : deadlineExceeded, failure);

    private RestCallFailure ClassifyResponse(
        Exception failure, string deadlineExceeded, string malformedBodyMessagePrefix, string? updateId)
    {
        if (IsUndecodableBody(failure))
        {
            LogUndecodableResponseBody(logger, failure);
            var messagePrefix = IsPayloadRefusal(failure) ? CommittedButUndecodablePrefix : malformedBodyMessagePrefix;
            return new RestCallFailure.Undecodable($"{messagePrefix}{failure.Message}", failure, updateId);
        }

        return ClassifyTransport(failure, deadlineExceeded);
    }

    private static LedgerOperationException ToException(RestCallFailure failure) => failure switch
    {
        RestCallFailure.Rejected rejected => rejected.Parsed.ToException(),
        RestCallFailure.NoResponse noResponse =>
            new LedgerOperationException(
                noResponse.Message, new TransportStatus.NoResponse(), innerException: noResponse.Cause),
        RestCallFailure.Undecodable { Cause: { } cause } undecodable =>
            new LedgerOperationException(
                undecodable.Message, new TransportStatus.UndecodableBody(), innerException: cause),
        RestCallFailure.Undecodable undecodable =>
            new LedgerOperationException(undecodable.Message, new TransportStatus.UndecodableBody()),
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
