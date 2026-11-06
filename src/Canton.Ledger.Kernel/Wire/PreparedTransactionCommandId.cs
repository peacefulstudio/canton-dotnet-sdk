// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Google.Protobuf;
using PreparedTransaction = Com.Daml.Ledger.Api.V2.Interactive.PreparedTransaction;

namespace Canton.Ledger.Kernel.Wire;

internal static class PreparedTransactionCommandId
{
    internal static CommandId? Of(PreparedTransaction preparedTransaction) =>
        CommandId.FromWire(preparedTransaction.Metadata?.SubmitterInfo?.CommandId);

    internal static CommandId? Of(ReadOnlyMemory<byte> serializedPreparedTransaction)
    {
        try
        {
            return Of(PreparedTransaction.Parser.ParseFrom(serializedPreparedTransaction.Span));
        }
        catch (InvalidProtocolBufferException)
        {
            return null;
        }
    }
}
