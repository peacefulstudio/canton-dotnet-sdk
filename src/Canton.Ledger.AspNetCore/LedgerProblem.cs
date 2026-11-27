// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;
using Microsoft.AspNetCore.Http;

namespace Canton.Ledger.AspNetCore;

internal sealed record LedgerProblem(
    string Title,
    string Detail,
    int Status,
    string? CommandId = null,
    string? UpdateId = null)
{
    private const string ContractNotFoundErrorId = "CONTRACT_NOT_FOUND";
    private const string InactiveContractsErrorId = "LOCAL_VERDICT_INACTIVE_CONTRACTS";

    internal static LedgerProblem For(LedgerOperationException exception, bool includeLedgerErrorMessages) =>
        exception.CommitState switch
        {
            CommitState.Committed => CommittedUnreadable(exception),
            CommitState.Unknown when exception.RejectedAfterRetry => OutcomeUnknown(exception),
            CommitState.Unknown => DefinitelyRefusedWithoutDetail(exception, includeLedgerErrorMessages) ?? OutcomeUnknown(exception),
            _ => Refused(exception, includeLedgerErrorMessages),
        };

    private static LedgerProblem? DefinitelyRefusedWithoutDetail(
        LedgerOperationException exception, bool includeLedgerErrorMessages) =>
        (exception.Category, exception.Status) switch
        {
            (DamlErrorCategory.InsufficientPermission, _)
                or (_, TransportStatus.Grpc { StatusCode: GrpcStatusCode.PermissionDenied })
                or (_, TransportStatus.Http { StatusCode: HttpStatusCode.Forbidden }) => Forbidden(),
            (_, TransportStatus.Grpc { StatusCode: GrpcStatusCode.InvalidArgument })
                or (_, TransportStatus.Http { StatusCode: HttpStatusCode.BadRequest }) => BadRequest(
                exception, includeLedgerErrorMessages, "The ledger rejected the command as invalid"),
            _ => null,
        };

    private static LedgerProblem BadRequest(
        LedgerOperationException exception, bool includeLedgerErrorMessages, string fixedDetail) => new(
        "Bad Request",
        includeLedgerErrorMessages ? exception.Message : WithErrorId(fixedDetail, exception.ErrorId),
        StatusCodes.Status400BadRequest);

    private static string WithErrorId(string fixedDetail, string? errorId) =>
        ErrorCodeId.OrNull(errorId) is { } errorCode ? $"{fixedDetail} ({errorCode})." : $"{fixedDetail}.";

    private static LedgerProblem Forbidden() => new(
        "Forbidden",
        "The ledger refused this submission's authority.",
        StatusCodes.Status403Forbidden);

    private static LedgerProblem CommittedUnreadable(LedgerOperationException exception) => new(
        "Accepted",
        "The command was committed but its result could not be read. Do not retry; re-read the resource.",
        StatusCodes.Status202Accepted,
        exception.CommandId?.Value,
        exception.UpdateId);

    private static LedgerProblem OutcomeUnknown(LedgerOperationException exception) => new(
        "Outcome Unknown",
        "The ledger did not confirm whether the command committed. Do not retry blindly; re-read the resource.",
        StatusCodes.Status502BadGateway,
        exception.CommandId?.Value);

    private static LedgerProblem Refused(LedgerOperationException exception, bool includeLedgerErrorMessages) =>
        exception.Category switch
        {
            DamlErrorCategory.ContentionOnSharedResources => new(
                "Conflict",
                "Contention on the contract; please retry.",
                StatusCodes.Status409Conflict),

            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing
                when exception.ErrorId is ContractNotFoundErrorId or InactiveContractsErrorId => new(
                "Conflict",
                "The contract was already consumed or never existed.",
                StatusCodes.Status409Conflict),

            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing => new(
                "Not Found",
                "The ledger has no such resource.",
                StatusCodes.Status404NotFound),

            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists => new(
                "Conflict",
                "The contract this submission would create already exists.",
                StatusCodes.Status409Conflict),

            DamlErrorCategory.InvalidIndependentOfSystemState => BadRequest(
                exception, includeLedgerErrorMessages, "The ledger rejected the command as invalid"),

            DamlErrorCategory.InvalidGivenCurrentSystemStateOther => BadRequest(
                exception, includeLedgerErrorMessages, "The ledger rejected the command in its current state"),

            DamlErrorCategory.SecurityAlert => BadRequest(
                exception, includeLedgerErrorMessages, "The ledger refused the request as malformed"),

            DamlErrorCategory.InsufficientPermission => Forbidden(),

            DamlErrorCategory.TransientServerFailure => new(
                "Ledger Unavailable",
                "Ledger submission failed; please retry.",
                StatusCodes.Status502BadGateway),

            _ => new(
                "Ledger Error",
                "The ledger submission failed unexpectedly.",
                StatusCodes.Status502BadGateway),
        };
}
