// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;

namespace Canton.Ledger.Kernel.Wire;

internal static class MalformedResponse
{
    internal static MalformedResponseException MissingRequiredField(string detail) =>
        new($"{detail}, though the Ledger API marks the field as required.");

    internal static MalformedResponseException WithDetail(string detail) =>
        new(detail);

    internal static MalformedResponseException WithDetail(string detail, Exception innerException) =>
        new(detail, innerException);

    internal static bool IsWireDecodeFailure(Exception exception) =>
        exception is FormatException or MalformedTransactionTreeException or MalformedResponseException;

    internal static LedgerOperationException ToUndecodableBodyException(
        this Exception decodeFailure, LedgerCallKind kind)
    {
        var malformed = decodeFailure as MalformedResponseException ?? WithDetail(decodeFailure.Message, decodeFailure);
        return LedgerOperationException.FromFailedCall(
            malformed.Message,
            new TransportStatus.UndecodableBody(),
            category: null,
            errorId: null,
            metadata: null,
            malformed,
            CommitStateOfUndecodableResponse(kind));
    }

    internal static TDecoded Decoding<TWire, TDecoded>(TWire wire, Func<TWire, TDecoded> decode)
    {
        try
        {
            return decode(wire);
        }
        catch (Exception undecodable) when (IsUndecodableWireValue(undecodable))
        {
            throw WithDetail(undecodable.Message, undecodable);
        }
    }

    internal static MalformedResponseException CouldNotDecodeTransaction(
        string lookupDescription, Exception decodeFailure) =>
        new($"the transaction at {lookupDescription} could not be decoded: {DetailOf(decodeFailure)}", decodeFailure);

    private static CommitState CommitStateOfUndecodableResponse(LedgerCallKind kind) => kind switch
    {
        LedgerCallKind.Read => CommitState.NotCommitted,
        LedgerCallKind.AcceptedOnlyWrite => CommitState.Unknown,
        LedgerCallKind.EffectAppliedWrite => CommitState.Committed,
        _ => throw new InvalidOperationException($"Unhandled ledger call kind: {kind}"),
    };

    private static bool IsUndecodableWireValue(Exception exception) =>
        exception is ArgumentException or InvalidOperationException or InvalidCastException or NotSupportedException or JsonException
        && !IsWireDecodeFailure(exception);

    private static string DetailOf(Exception decodeFailure) =>
        decodeFailure is MalformedResponseException malformed ? malformed.Detail : decodeFailure.Message;
}
