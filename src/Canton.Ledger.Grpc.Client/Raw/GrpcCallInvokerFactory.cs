// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Canton.Ledger.Grpc.Client.Raw;

internal sealed class GrpcCallInvokerFactory : IGrpcCallInvokerFactory
{
    private readonly GrpcChannel _channel;
    private readonly LedgerCallInvoker _invoker;
    private readonly ILogger<GrpcCallInvokerFactory> _logger;

    internal GrpcCallInvokerFactory(
        IOptions<LedgerClientOptions> options,
        LedgerChannelProvider channels,
        ITokenProvider tokenProvider,
        ILogger<GrpcCallInvokerFactory>? logger = null)
        : this(options.Value, channels.Channel, tokenProvider, logger)
    {
    }

    internal GrpcCallInvokerFactory(
        LedgerClientOptions options,
        GrpcChannel channel,
        ITokenProvider tokenProvider,
        ILogger<GrpcCallInvokerFactory>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(tokenProvider);

        _logger = logger ?? NullLogger<GrpcCallInvokerFactory>.Instance;
        _channel = channel;
        _invoker = new LedgerCallInvoker(options, tokenProvider);

        CallContextHelper.LogStartupDiagnostics(
            _logger, tokenProvider, options.GrpcAddress, nameof(GrpcCallInvokerFactory), "AddLedgerRawGrpc");
    }

    public CallInvoker CreateCallInvoker() =>
        new AuthenticatedCallInvoker(_channel.CreateCallInvoker(), _invoker, _logger);
}
