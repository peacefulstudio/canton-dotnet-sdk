// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Mvc;

namespace Canton.Ledger.AspNetCore;

/// <summary>
/// The shape of the <c>202</c> problem a ledger command answers when it committed but its result could
/// not be read, as published in the OpenAPI document by
/// <see cref="LedgerProblemEndpointConventionBuilderExtensions.ProducesLedgerProblems{TBuilder}"/>. The response
/// carries <see cref="CommandId"/> and <see cref="UpdateId"/> as <c>ProblemDetails</c> extensions, each
/// omitted when the ledger did not report it. A client that receives it must not resubmit the command:
/// it committed, so it re-reads the resource instead.
/// </summary>
public sealed class CommittedCommandProblem : ProblemDetails
{
    /// <summary>The command id the ledger assigned to the committed submission, when known.</summary>
    public string? CommandId { get; init; }

    /// <summary>The id of the ledger update the command committed in, when known.</summary>
    public string? UpdateId { get; init; }
}
