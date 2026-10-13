// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client;

internal sealed class ResponseReceipt
{
    private static readonly AsyncLocal<ResponseReceipt?> Current = new();

    private volatile bool _responseReceived;
    private int _attempts;
    private CommandId? _commandId;

    internal bool ResponseReceived => _responseReceived;

    internal CommandId? CommandId => _commandId;

    internal bool Retried => Volatile.Read(ref _attempts) > 1;

    internal static bool CurrentCallRetried => Current.Value?.Retried ?? false;

    internal static ResponseReceipt OpenForCurrentCall()
    {
        var receipt = new ResponseReceipt();
        Current.Value = receipt;
        return receipt;
    }

    internal static void AwaitResponse()
    {
        if (Current.Value is { } receipt)
        {
            receipt._responseReceived = false;
            Volatile.Write(ref receipt._attempts, 0);
        }
    }

    internal static void RecordAttempt()
    {
        if (Current.Value is { } receipt)
        {
            Interlocked.Increment(ref receipt._attempts);
        }
    }

    internal static void RecordCommandId(string commandId)
    {
        if (Current.Value is { } receipt)
        {
            receipt._commandId = new CommandId(commandId);
        }
    }

    internal static void MarkResponseReceived()
    {
        if (Current.Value is { } receipt)
        {
            receipt._responseReceived = true;
        }
    }
}
