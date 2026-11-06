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

    internal static LedgerProblem For(LedgerOperationException exception) =>
        exception.CommitState switch
        {
            CommitState.Committed => CommittedUnreadable(exception),
            CommitState.Unknown when exception.RejectedAfterRetry => OutcomeUnknown(exception),
            CommitState.Unknown => DefinitelyRefusedWithoutDetail(exception) ?? OutcomeUnknown(exception),
            _ => Refused(exception),
        };

    private static LedgerProblem? DefinitelyRefusedWithoutDetail(LedgerOperationException exception) =>
        (exception.Category, exception.Status) switch
        {
            (DamlErrorCategory.InsufficientPermission, _)
                or (_, TransportStatus.Grpc { StatusCode: GrpcStatusCode.PermissionDenied })
                or (_, TransportStatus.Http { StatusCode: HttpStatusCode.Forbidden }) => Forbidden(),
            (_, TransportStatus.Grpc { StatusCode: GrpcStatusCode.InvalidArgument })
                or (_, TransportStatus.Http { StatusCode: HttpStatusCode.BadRequest }) => new(
                "Bad Request",
                exception.Message,
                StatusCodes.Status400BadRequest),
            _ => null,
        };

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

    private static LedgerProblem Refused(LedgerOperationException exception) =>
        exception.Category switch
        {
            DamlErrorCategory.ContentionOnSharedResources => new(
                "Conflict",
                "Contention on the contract; please retry.",
                StatusCodes.Status409Conflict),

            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing
                when exception.ErrorId == ContractNotFoundErrorId => new(
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

            DamlErrorCategory.InvalidIndependentOfSystemState
                or DamlErrorCategory.SecurityAlert
                or DamlErrorCategory.InvalidGivenCurrentSystemStateOther => new(
                "Bad Request",
                exception.Message,
                StatusCodes.Status400BadRequest),

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
