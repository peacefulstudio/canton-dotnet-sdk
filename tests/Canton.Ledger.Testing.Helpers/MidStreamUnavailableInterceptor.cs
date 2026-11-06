// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// Fails a server-streaming call of one gRPC method with <see cref="StatusCode.Unavailable"/> once
/// <c>responsesKept</c> responses have passed through, or when the stream ends sooner, so the
/// caller meets a transport fault part-way through the stream instead of a clean end. Every other
/// method passes through whole.
/// </summary>
internal sealed class MidStreamUnavailableInterceptor(string methodName, int responsesKept) : Interceptor
{
    /// <inheritdoc />
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        var call = continuation(request, context);
        if (context.Method.Name != methodName)
        {
            return call;
        }

        return new AsyncServerStreamingCall<TResponse>(
            new UnavailableAfterReader<TResponse>(call.ResponseStream, responsesKept),
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            call.Dispose);
    }

    private sealed class UnavailableAfterReader<TResponse>(IAsyncStreamReader<TResponse> inner, int responsesKept)
        : IAsyncStreamReader<TResponse>
    {
        private int _passed;

        public TResponse Current => inner.Current;

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (_passed < responsesKept && await inner.MoveNext(cancellationToken).ConfigureAwait(false))
            {
                _passed++;
                return true;
            }

            throw new RpcException(new Status(StatusCode.Unavailable, "snapshot aborted mid-stream"));
        }
    }
}
