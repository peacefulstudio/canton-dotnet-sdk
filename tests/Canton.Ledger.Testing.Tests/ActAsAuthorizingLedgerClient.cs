// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Daml.Ledger.Abstractions.Testing.Conformance;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;

namespace Canton.Ledger.Testing.Tests;

/// <summary>
/// Plays a participant that accepts a submission only when it acts as exactly the authorized
/// party, judging it by what <see cref="FakeLedgerClient.Submissions"/> recorded the fake was
/// handed. Every member other than the two submit-and-wait writes throws via
/// <see cref="NotSupportedLedgerClient"/>.
/// </summary>
internal sealed class ActAsAuthorizingLedgerClient(FakeLedgerClient fake, Party authorized) : NotSupportedLedgerClient
{
    public override async Task<SubmitAndWaitResult> SubmitAndWaitAsync(
        CommandsSubmission submission, SubmitterInfo submitter, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var result = await fake.SubmitAndWaitAsync(submission, submitter, timeout, cancellationToken);
        return ActedAsAuthorized()
            ? result
            : throw new LedgerOperationException(
                $"party {authorized.Value} did not authorize this submission",
                DamlErrorCategory.AuthorizationChecksFailed, "UNAUTHORIZED", new Dictionary<string, string>());
    }

    public override async Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
        CommandsSubmission submission, SubmitterInfo submitter, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var outcome = await fake.TrySubmitAndWaitForTransactionAsync(submission, submitter, timeout, cancellationToken);
        return ActedAsAuthorized()
            ? outcome
            : new ExerciseOutcome<TransactionResult>.DamlError(
                DamlErrorCategory.AuthorizationChecksFailed, "UNAUTHORIZED",
                $"party {authorized.Value} did not authorize this submission", new Dictionary<string, string>());
    }

    private bool ActedAsAuthorized() => ((RecordedSubmission.Commands)fake.Submissions[^1]).Submission.ActAs!.SequenceEqual([authorized]);
}
