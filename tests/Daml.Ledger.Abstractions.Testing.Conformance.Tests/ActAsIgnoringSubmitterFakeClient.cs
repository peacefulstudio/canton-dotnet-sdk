// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

/// <summary>
/// Dispatches the submission's pre-set <see cref="CommandsSubmission.ActAs"/> directly
/// instead of applying the <c>submitter</c> parameter via
/// <see cref="CommandsSubmission.WithSubmitter"/>. Used to prove the conformance kit's
/// submitter-authority checks actually fail against a client that gets the contract
/// wrong, not just pass vacuously. Every other member throws via
/// <see cref="NotSupportedLedgerClient"/>.
/// </summary>
internal sealed class ActAsIgnoringSubmitterFakeClient(Party authorized) : NotSupportedLedgerClient
{
    public override Task<SubmitAndWaitResult> SubmitAndWaitAsync(
        CommandsSubmission submission, SubmitterInfo submitter, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        RequireAuthorized(submission);
        return Task.FromResult(new SubmitAndWaitResult(new CommandId("cmd-1"), "update-1", LedgerOffset.At(1)));
    }

    public override Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
        CommandsSubmission submission, SubmitterInfo submitter, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthorized(submission))
        {
            return Task.FromResult<ExerciseOutcome<TransactionResult>>(new ExerciseOutcome<TransactionResult>.DamlError(
                DamlErrorCategory.AuthorizationChecksFailed, "UNAUTHORIZED",
                $"party {authorized.Value} did not authorize this submission", new Dictionary<string, string>()));
        }

        return Task.FromResult<ExerciseOutcome<TransactionResult>>(new ExerciseOutcome<TransactionResult>.One(
            new TransactionResult("update-1", LedgerOffset.At(1), [], [], new CommandId("cmd-1"))));
    }

    private bool IsAuthorized(CommandsSubmission submission) =>
        (submission.ActAs ?? []).Contains(authorized);

    private void RequireAuthorized(CommandsSubmission submission)
    {
        if (!IsAuthorized(submission))
        {
            throw new LedgerOperationException(
                $"party {authorized.Value} did not authorize this submission",
                DamlErrorCategory.AuthorizationChecksFailed, "UNAUTHORIZED", new Dictionary<string, string>());
        }
    }
}
