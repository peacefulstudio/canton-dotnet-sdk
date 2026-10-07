// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

internal sealed partial class ChoiceEmitter
{
    /// <summary>
    /// Emits a static <c>&lt;TemplateName&gt;NonContractExtensions</c> class
    /// with one <c>Try&lt;Choice&gt;Async</c> extension per non-CID-returning
    /// choice on <paramref name="template"/>, plus a private projector helper
    /// per choice that walks <c>tx.ExercisedEvents</c> and runs the choice's
    /// <c>ResultDecoder</c>. Returns <c>true</c> when at least one extension
    /// was emitted (so the caller can decide whether the per-template
    /// extensions class is needed at all).
    /// </summary>
    internal bool TryWriteNonContractChoiceExtensions(
        IndentWriter indent,
        DamlTemplate template)
    {
        var emittedTemplateName = context.EmittedTypeName(context.Module.Name, template.Name);
        var className = context.QualifyInModule(emittedTemplateName);

        var emittable = template.Choices
            .Where(c => !ReturnsContractIds(c.ReturnType))
            .ToList();

        if (emittable.Count == 0)
        {
            return false;
        }

        EmittedUsings.RequireAsyncExerciserNamespaces(indent);

        indent.AppendLine();
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Async exerciser extensions for <see cref=\"{className}\"/> contract IDs whose choices");
            indent.AppendLine("/// return a non-contract-id payload (Decimal, records, lists, Unit, etc.).");
            indent.AppendLine("/// Each method submits the choice via");
            indent.AppendLine("/// <c>SingleCommandExtensions.TrySubmitSingleAsync</c> and lifts the typed result");
            indent.AppendLine("/// into <c>ExerciseOutcome&lt;TReturn&gt;</c>.");
            indent.AppendLine("/// </summary>");
        }
        indent.AppendLine($"public static class {emittedTemplateName}NonContractExtensions");
        indent.AppendLine("{");
        indent.Indent();

        for (var i = 0; i < emittable.Count; i++)
        {
            if (i > 0)
            {
                indent.AppendLine();
            }
            WriteChoiceCommandBuilder(indent, emittable[i], className);
            indent.AppendLine();
            WriteSingleNonContractChoiceAsyncExerciser(
                indent, emittable[i], className, SubmitterInfoParameter());
        }

        foreach (var choice in emittable)
        {
            indent.AppendLine();
            WriteExerciseProjector(indent, choice, className);
        }

        indent.Dedent();
        indent.AppendLine("}");

        return true;
    }

    private void WriteSingleNonContractChoiceAsyncExerciser(
        IndentWriter indent,
        DamlChoice choice,
        string templateClassName,
        ChoiceSubmitterParameter submitter)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var returnTypeName = RequireAndMapReturnType(indent, choice);
        var argument = GetChoiceArgumentInfo(choice);
        var hasArg = argument.HasArgument;

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Exercises the {choice.Name} choice and lifts the choice's exercise result to");
            indent.AppendLine($"/// <see cref=\"ExerciseOutcome{{T}}\"/> over <c>{EmitterHelpers.EscapeXmlText(returnTypeName)}</c>. Structured Canton/Daml errors");
            indent.AppendLine("/// and infrastructure/transport errors pass through unchanged.");
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <param name=\"contractId\">The contract on which to exercise the choice.</param>");
            indent.AppendLine("/// <param name=\"client\">The ledger client.</param>");
            if (hasArg)
            {
                indent.AppendLine("/// <param name=\"argument\">The choice argument.</param>");
            }
            indent.AppendLine($"/// <param name=\"{submitter.Name}\">{submitter.DocSummary}</param>");
            WriteSubmissionParameterDocs(indent);
        }

        indent.AppendLine($"public static async global::System.Threading.Tasks.Task<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.ExerciseOutcome)}<{returnTypeName}>> Try{choiceName}Async(");
        indent.Indent();
        indent.AppendLine($"this {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ContractId)}<{templateClassName}> contractId,");
        indent.AppendLine($"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.ILedgerWriter)} client,");
        if (hasArg)
        {
            indent.AppendLine($"{argument.ParameterType(templateClassName)} argument,");
        }
        indent.AppendLine($"{submitter.TypeName} {submitter.Name},");
        WriteSubmissionParametersAndCloseSignature(indent);
        indent.Dedent();
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("global::System.ArgumentNullException.ThrowIfNull(client);");
        indent.AppendLine();

        indent.AppendLine(hasArg
            ? $"var command = contractId.{choiceName}Command(argument);"
            : $"var command = contractId.{choiceName}Command();");

        indent.AppendLine();
        indent.AppendLine($"var outcome = await client.TrySubmitSingleAsync(command, {submitter.Name}, workflowId, commandId, timeout, configure, cancellationToken).ConfigureAwait(false);");
        indent.AppendLine();
        indent.AppendLine($"return outcome.ProjectCommitted(tx => Project{choiceName}Result(tx, contractId.Value));");

        indent.Dedent();
        indent.AppendLine("}");
    }
}
