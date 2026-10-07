// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Grpc.Client;

internal sealed class ResponseReceipt
{
    private static readonly AsyncLocal<ResponseReceipt?> Current = new();

    private volatile bool _responseReceived;

    internal bool ResponseReceived => _responseReceived;

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
