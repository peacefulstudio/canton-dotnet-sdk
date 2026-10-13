// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Grpc.Core;

namespace Canton.Ledger.Grpc.Client;

internal static class RpcCallEnding
{
    internal static LedgerCallEnding.Rejected Rejected(RpcException rejection, bool afterRetry = false) =>
        new(DamlErrorParser.Parse(rejection), rejection, afterRetry);

    internal static LedgerCallEnding.Rejected SubmissionRejected(RpcException rejection, bool afterRetry) =>
        new(DamlErrorParser.Parse(rejection) switch
            {
                ParsedLedgerError.Unstructured unstructured =>
                    unstructured.WithMessage(rejection.Status.Detail ?? rejection.Message),
                var parsed => parsed,
            },
            rejection,
            afterRetry);

    internal static LedgerCallEnding.Rejected StreamFaulted(RpcException fault) =>
        new(
            DamlErrorParser.Parse(fault)
                .WithMessage(string.IsNullOrEmpty(fault.Status.Detail) ? fault.Message : fault.Status.Detail),
            fault);

    internal static LedgerCallEnding.Unreadable Undecodable(Exception decodeFailure)
    {
        var inner = decodeFailure is MalformedResponseException or PreparedHashMismatchException
            ? decodeFailure
            : MalformedResponse.WithDetail(decodeFailure.Message, decodeFailure);
        return new LedgerCallEnding.Unreadable(inner.Message, inner, UpdateId: null);
    }

    internal static LedgerCallEnding.Unreadable CommittedUndecodable(
        string message, Exception decodeFailure, string? updateId) =>
        new(message, decodeFailure, string.IsNullOrEmpty(updateId) ? null : updateId);

    internal static bool ReportsAbsence(RpcException rejection)
    {
        var parsed = DamlErrorParser.Parse(rejection);
        return LedgerCallOutcome.ReportsAbsence(parsed.Status, parsed.ClassifiedCategory);
    }
}
