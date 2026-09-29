// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.CSharp.Tests.TestHelpers;

/// <summary>
/// The emitted call into the shared single-command submission path, pinned once for the
/// whole suite. Every generated exerciser forwards the same seven arguments in the same
/// order, so an emitter-side transposition — <c>commandId</c> and <c>workflowId</c>
/// swapped, say — must fail loudly. Holding the expected text in one place keeps a
/// legitimate signature change from being applied at some call sites and missed at
/// others, which would leave the stragglers pinning a signature that no longer exists.
/// </summary>
internal static class EmittedSubmissionShape
{
    /// <summary>
    /// The <c>TrySubmitSingleAsync</c> call as emitted, with its arguments in
    /// declaration order.
    /// </summary>
    internal const string TrySubmitSingleArgumentOrder =
        "TrySubmitSingleAsync(command, submitter, workflowId, commandId, timeout, configure, cancellationToken)";

    /// <summary>
    /// The optional submission hook every generated submit helper declares immediately before
    /// its cancellation token.
    /// </summary>
    internal const string ConfigureParameter =
        "Func<CommandsSubmission, CommandsSubmission>? configure = null,";
}
