// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.CSharp.CodeGen;

internal sealed partial class ChoiceEmitter
{
    private static void WriteSubmissionParameterDocs(IndentWriter indent)
    {
        indent.AppendLine("/// <param name=\"workflowId\">Optional workflow id; passed through to the ledger when supplied. No default — workflow IDs are correlation keys, and a per-choice default would bucket every submission of the same choice under one ID.</param>");
        indent.AppendLine("/// <param name=\"commandId\">Optional command id for deduplication; a fresh id is minted only when omitted, and a minted id is not reported back on a failed submission. Supply and retain your own id to make a retry of a lost-but-accepted submission deduplicable, so the ledger deduplicates the resubmission instead of re-executing the choice.</param>");
        indent.AppendLine("/// <param name=\"timeout\">Optional per-call deadline, applied best-effort by the transport; transports without a server-side deadline apply a client-side bound only. The default <c>null</c> applies no deadline. An overrun surfaces as an <c>InfraError</c> outcome.</param>");
        indent.AppendLine("/// <param name=\"configure\">Optional hook that receives the submission built for this call and returns the one to submit, so a caller can add what this helper does not expose: <c>s => s.WithDisclosedContracts(holding.Disclosure!)</c>, <c>WithDeduplicationPeriod</c>, <c>WithSynchronizerId</c> or <c>WithMinLedgerTime</c>. It runs after <c>workflowId</c> and <c>commandId</c> are applied. It must keep the helper's command, and the submitter's act-as and read-as parties replace any set on the submission. The default <c>null</c> submits the submission unchanged.</param>");
        indent.AppendLine("/// <param name=\"cancellationToken\">Cancellation token.</param>");
    }

    private void WriteSubmissionParametersAndCloseSignature(IndentWriter indent)
    {
        indent.AppendLine("string? workflowId = null,");
        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.CommandId)}? commandId = null,");
        indent.AppendLine("TimeSpan? timeout = null,");
        indent.AppendLine($"{ConfigureParameterType()}? configure = null,");
        indent.AppendLine("CancellationToken cancellationToken = default)");
    }

    private string ConfigureParameterType()
    {
        var submission = context.Qualifier.Qualify(RuntimeTypeNames.CommandsSubmission);
        return $"{context.Qualifier.Qualify("Func")}<{submission}, {submission}>";
    }
}
