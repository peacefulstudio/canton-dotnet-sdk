// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;

namespace Canton.Ledger.AspNetCore;

internal static class LedgerProblemResponses
{
    internal const string ProblemJson = "application/problem+json";

    internal static IReadOnlyList<(int StatusCode, Type ResponseType)> All { get; } =
    [
        (StatusCodes.Status202Accepted, typeof(CommittedCommandProblem)),
        (StatusCodes.Status400BadRequest, typeof(Microsoft.AspNetCore.Mvc.ProblemDetails)),
        (StatusCodes.Status403Forbidden, typeof(Microsoft.AspNetCore.Mvc.ProblemDetails)),
        (StatusCodes.Status404NotFound, typeof(Microsoft.AspNetCore.Mvc.ProblemDetails)),
        (StatusCodes.Status409Conflict, typeof(Microsoft.AspNetCore.Mvc.ProblemDetails)),
        (StatusCodes.Status502BadGateway, typeof(Microsoft.AspNetCore.Mvc.ProblemDetails)),
    ];
}
