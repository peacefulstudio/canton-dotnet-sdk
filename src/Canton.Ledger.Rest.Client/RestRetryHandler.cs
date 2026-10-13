// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Resilience;
using Canton.Ledger.Kernel.Telemetry;
using Microsoft.Extensions.Options;
using Polly;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Applies the kernel's opt-in retry pipeline to the JSON Ledger API requests that state they can be
/// replayed, mirroring the gRPC client's <c>LedgerCallInvoker</c>. A request that states nothing is a
/// raw-tier call: it is sent once unless it starts inside a <see cref="RawCallReplay.Allow"/> scope.
/// Registered outermost in the handler chain, so each attempt
/// resolves a fresh bearer token and emits its own client span. Register the source as
/// <c>tracing.AddSource(LedgerActivitySourceNames.RestLedgerClient)</c> to see the retry spans.
/// </summary>
internal sealed class RestRetryHandler : DelegatingHandler
{
    private const string RetryAttemptActivityName = "RestLedgerClient.RetryAttempt";

    private static readonly HttpRequestOptionsKey<bool> Replayable = new("Canton.Ledger.Rest.Replayable");

    private static readonly HttpRequestOptionsKey<int> AttemptsSent = new("Canton.Ledger.Rest.AttemptsSent");

    private static readonly ActivitySource Source = LedgerActivitySource.Create<RestLedgerClient>();

    private readonly ResiliencePipeline _retryPipeline;
    private readonly bool _enabled;

    public RestRetryHandler(IOptions<RestLedgerClientOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var retry = options.Value.Retry;
        _enabled = retry.Enabled;
        _retryPipeline = RetryPipelineFactory.Create(retry, IsTransientHttpFailure, RecordRetryAttempt);
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!_enabled || !IsReplayable(request))
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        await BufferBodySoEveryAttemptCanReplayItAsync(request, cancellationToken).ConfigureAwait(false);

        Exception? sentAttemptFailure = null;
        try
        {
            return await _retryPipeline
                .ExecuteAsync(
                    async token =>
                    {
                        RecordAttemptSent(request);
                        try
                        {
                            return await base.SendAsync(request, token).ConfigureAwait(false);
                        }
                        catch (TokenAcquisitionException) when (sentAttemptFailure is not null)
                        {
                            throw new RetryAbandonedException(sentAttemptFailure);
                        }
                        catch (Exception failure) when (IsTransientHttpFailure(failure))
                        {
                            sentAttemptFailure = failure;
                            throw;
                        }
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RetryAbandonedException abandoned)
        {
            throw abandoned.SentAttemptFailure;
        }
    }

    internal static bool WasRetried(HttpRequestMessage request) =>
        request.Options.TryGetValue(AttemptsSent, out var attemptsSent) && attemptsSent > 1;

    private static void RecordAttemptSent(HttpRequestMessage request) =>
        request.Options.Set(AttemptsSent, request.Options.TryGetValue(AttemptsSent, out var attemptsSent) ? attemptsSent + 1 : 1);

    internal static void MarkReplayable(HttpRequestMessage request) =>
        request.Options.Set(Replayable, true);

    internal static void MarkNotReplayable(HttpRequestMessage request) =>
        request.Options.Set(Replayable, false);

    private static bool IsReplayable(HttpRequestMessage request) =>
        request.Options.TryGetValue(Replayable, out var replayable) ? replayable : RawCallReplay.IsAllowed;

    private static async Task BufferBodySoEveryAttemptCanReplayItAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Content is not null)
            await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsTransientHttpFailure(Exception exception) => exception switch
    {
        HttpRequestException => true,
        TimeoutException => true,
        TaskCanceledException { InnerException: TimeoutException } => true,
        _ => false
    };

    private static void RecordRetryAttempt(RetryAttempt attempt)
    {
        using var activity = Source.StartActivity(RetryAttemptActivityName, ActivityKind.Internal);
        if (activity is null) return;

        activity.SetTag(LedgerActivityTagNames.RetryAttempt, attempt.AttemptNumber);
        activity.SetTag(LedgerActivityTagNames.RetryDelayMs, attempt.RetryDelay.TotalMilliseconds);
        if (attempt.Exception is { } exception)
        {
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity.SetTag(SemanticConventions.ErrorType, exception.GetType().FullName);
        }
    }
}

internal sealed class RetryAbandonedException(Exception sentAttemptFailure) : Exception
{
    internal Exception SentAttemptFailure { get; } = sentAttemptFailure;
}
