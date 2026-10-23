// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Bounds each attempt of a request that carries a timeout with its own deadline, mirroring the gRPC
/// client's per-attempt <c>CallOptions.Deadline</c>. Registered directly below
/// <see cref="RestRetryHandler"/>, so a retried request is granted a fresh budget per attempt, and an
/// attempt that outlasts it surfaces as a <see cref="TimeoutException"/> the retry pipeline treats as
/// transient. A request that carries no timeout, such as a raw-API call, is passed through untouched.
/// </summary>
internal sealed class RestAttemptTimeoutHandler : DelegatingHandler
{
    private static readonly HttpRequestOptionsKey<TimeSpan> AttemptTimeout = new("Canton.Ledger.Rest.AttemptTimeout");

    internal static void SetAttemptTimeout(HttpRequestMessage request, TimeSpan timeout) =>
        request.Options.Set(AttemptTimeout, timeout);

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!request.Options.TryGetValue(AttemptTimeout, out var timeout))
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(timeout);
        try
        {
            return await base.SendAsync(request, attempt.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException cancelled)
            when (attempt.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Request exceeded the {timeout} deadline.", cancelled);
        }
    }
}
