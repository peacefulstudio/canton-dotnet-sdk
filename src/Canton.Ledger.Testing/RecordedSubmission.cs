// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime.Commands;

namespace Canton.Ledger.Testing;

/// <summary>
/// A write a <see cref="FakeLedgerClient"/> was handed, as listed in
/// <see cref="FakeLedgerClient.Submissions"/>. The union is closed: every write is one of
/// <see cref="Commands"/>, <see cref="Reassignment"/> or <see cref="Execution"/>, so a <c>switch</c> over it is exhaustive
/// and <c>Submissions.OfType&lt;RecordedSubmission.Commands&gt;()</c> selects one kind.
/// </summary>
public abstract record RecordedSubmission
{
    private protected RecordedSubmission() { }

    /// <summary>
    /// A command submission: the one a caller built for <c>SubmitAsync</c>, <c>SubmitAndWaitAsync</c>,
    /// <c>TrySubmitAndWaitForTransactionAsync</c> or <c>TrySubmitAndWaitForTransactionTreeAsync</c>, or
    /// the single-command submission a <c>TryCreateAsync</c> or <c>TryExerciseAsync</c> stands for.
    /// </summary>
    /// <param name="Submission">
    /// The submission as the client put it on the wire: the submitter's act-as and read-as parties
    /// applied, the effective command id (the caller's, else the one the fake minted), and, for
    /// <c>TryCreateAsync</c> and <c>TryExerciseAsync</c>, the workflow id the caller passed or the
    /// <c>create-&lt;template&gt;</c> / <c>exercise-&lt;choice&gt;</c> default the real clients substitute.
    /// </param>
    public sealed record Commands(CommandsSubmission Submission) : RecordedSubmission;

    /// <summary>
    /// A reassignment submitted through <c>SubmitReassignmentAsync</c> or
    /// <c>TrySubmitAndWaitForReassignmentAsync</c>.
    /// </summary>
    /// <param name="Submission">The submission with its effective command id.</param>
    public sealed record Reassignment(ReassignmentSubmission Submission) : RecordedSubmission;

    /// <summary>
    /// An interactive submission executed through <c>ExecuteSubmissionAsync</c>,
    /// <c>ExecuteSubmissionAndWaitAsync</c> or <c>ExecuteSubmissionAndWaitForTransactionAsync</c>.
    /// </summary>
    /// <param name="Submission">The prepared transaction and signatures the call was handed.</param>
    public sealed record Execution(SignedSubmission Submission) : RecordedSubmission;
}
