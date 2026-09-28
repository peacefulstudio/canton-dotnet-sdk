// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

internal sealed partial class ChoiceEmitter
{
    /// <summary>
    /// Emits the readAs-capable <c>Try&lt;Choice&gt;Async</c> overload on
    /// <c>ContractId&lt;TemplateName&gt;</c> that takes an explicit
    /// <c>SubmitterInfo</c> instead of named <c>Party</c> parameters.
    /// Companion to the ergonomic named-<c>Party</c> overload for choices whose
    /// created contracts are visible to an observer but not to the submitter —
    /// the caller supplies <c>readAs</c> parties the payload cannot derive.
    /// Emitted alongside the named-<c>Party</c> overload whenever controllers are
    /// statically resolvable; the dynamic-controller case already surfaces a
    /// <c>SubmitterInfo</c> parameter on its sole overload.
    /// </summary>
    private void WriteSubmitterInfoChoiceAsyncExerciser(
        IndentWriter indent,
        DamlChoice choice,
        string templateClassName,
        IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var resultName = $"{choiceName}Result";
        var argument = GetChoiceArgumentInfo(choice, dataTypes);
        var hasArg = argument.HasArgument;

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Exercises the {choice.Name} choice with an explicit <see cref=\"SubmitterInfo\"/> and projects the resulting transaction's created contracts to a typed <see cref=\"{resultName}\"/>.");
            indent.AppendLine("/// Companion to the named-<c>Party</c> overload for the case where the submitter must");
            indent.AppendLine("/// read contracts it does not act as — the choice's created contracts are visible to an");
            indent.AppendLine("/// observer but not to the submitter, so the caller supplies the <c>readAs</c> parties.");
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <param name=\"contractId\">The contract on which to exercise the choice.</param>");
            indent.AppendLine("/// <param name=\"client\">The ledger client.</param>");
            if (hasArg)
            {
                indent.AppendLine("/// <param name=\"argument\">The choice argument.</param>");
            }
            indent.AppendLine("/// <param name=\"submitter\">The submitter party set (<c>actAs</c> + optional <c>readAs</c>).</param>");
            WriteSubmissionParameterDocs(indent);
        }

        indent.AppendLine($"public static async Task<{context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome)}<{resultName}>> Try{choiceName}Async(");
        indent.Indent();
        indent.AppendLine($"this {context.Qualifier.Qualify(RuntimeTypeNames.ContractId)}<{templateClassName}> contractId,");
        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.ILedgerWriter)} client,");
        if (hasArg)
        {
            indent.AppendLine($"{argument.ParameterType(templateClassName)} argument,");
        }
        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.SubmitterInfo)} submitter,");
        WriteSubmissionParametersAndCloseSignature(indent);
        indent.Dedent();
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("ArgumentNullException.ThrowIfNull(client);");

        WriteExerciserCommandDispatchAndProject(indent, choice, templateClassName, dataTypes);

        indent.Dedent();
        indent.AppendLine("}");
    }

    /// <summary>
    /// Emits the readAs-capable sibling of <see cref="WriteSingleContractChoiceAsyncExerciser"/>:
    /// the same <c>IContract&lt;ContractId&lt;TemplateName&gt;, TemplateName&gt;</c> receiver, but with an explicit
    /// <c>SubmitterInfo</c> replacing the payload-derived parties. Without it a caller
    /// holding a fetched contract has to unwrap <c>contract.Id</c> by hand the moment the
    /// submission needs <c>readAs</c> or a multi-party <c>actAs</c>, because the
    /// payload-derived overload can only ever submit the parties the payload names.
    /// </summary>
    private void WriteSubmitterInfoContractChoiceAsyncExerciser(
        IndentWriter indent,
        DamlChoice choice,
        string templateClassName,
        IReadOnlyDictionary<string, DamlDataType> dataTypes) =>
        WriteContractChoiceAsyncExerciser(
            indent,
            choice,
            templateClassName,
            dataTypes,
            [
                $"/// Exercises the {choice.Name} choice on a fetched <see cref=\"{templateClassName}\"/> contract with an",
                "/// explicit <see cref=\"SubmitterInfo\"/>. Companion to the payload-derived overload for",
                "/// multi-party submissions and for callers who must supply <c>readAs</c> parties the",
                "/// payload cannot name. Delegates to the",
                $"/// <c>ContractId&lt;{templateClassName}&gt;</c> overload.",
            ],
            declaresSubmitterInfoParameter: true,
            ["submitter"]);

    private void WriteExerciserCommandDispatchAndProject(
        IndentWriter indent,
        DamlChoice choice,
        string templateClassName,
        IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var resultName = $"{choiceName}Result";
        var hasArg = GetChoiceArgumentInfo(choice, dataTypes).HasArgument;

        indent.AppendLine();
        indent.AppendLine(hasArg
            ? $"var command = contractId.{choiceName}Command(argument);"
            : $"var command = contractId.{choiceName}Command();");

        indent.AppendLine();
        indent.AppendLine("var outcome = await client.TrySubmitSingleAsync(command, submitter, workflowId, commandId, timeout, cancellationToken).ConfigureAwait(false);");
        indent.AppendLine();
        indent.AppendLine($"return outcome.ProjectCommitted(tx => {resultName}.FromCreatedContracts(tx.CreatedContracts));");
    }

    /// <summary>
    /// Emits the sibling <c>Try&lt;Choice&gt;Async</c> overload that receives a fetched
    /// contract instead of a bare <c>ContractId&lt;TemplateName&gt;</c>. The receiver is
    /// <c>IContract&lt;ContractId&lt;TemplateName&gt;, TemplateName&gt;</c>, which both the
    /// key-less <c>Contract&lt;T&gt;</c> and the keyed <c>Contract&lt;T, TKey&gt;</c> implement,
    /// so one overload serves every shape a stream, snapshot or <c>FromCreatedEvent</c> returns.
    /// Because the receiver carries the payload, the wrapper reads every
    /// controller / observer party off <c>contract.Data</c> and delegates to the
    /// <c>ContractId&lt;T&gt;</c> overload — the caller passes zero parties. Emitted
    /// only when controllers are statically resolvable to payload fields; the
    /// dynamic case has no payload-derivable submitter, so no <c>Contract</c>
    /// overload is generated and callers stay on the named-parameter
    /// <c>ContractId&lt;T&gt;</c> path.
    /// </summary>
    private void WriteSingleContractChoiceAsyncExerciser(
        IndentWriter indent,
        DamlChoice choice,
        string templateClassName,
        IReadOnlyDictionary<string, DamlDataType> dataTypes,
        DamlPartyAnalysis controllers,
        DamlPartyAnalysis observers)
    {
        var (controllerFieldNames, readAsFieldNames) =
            party.PartitionControllerAndObserverFieldNames(controllers, observers);

        WriteContractChoiceAsyncExerciser(
            indent,
            choice,
            templateClassName,
            dataTypes,
            [
                $"/// Exercises the {choice.Name} choice on a fetched <see cref=\"{templateClassName}\"/> contract,",
                "/// reading every controller and observer party off the contract payload so the",
                "/// caller passes no parties. Delegates to the",
                $"/// <c>ContractId&lt;{templateClassName}&gt;</c> overload.",
            ],
            declaresSubmitterInfoParameter: false,
            [.. controllerFieldNames
                .Concat(readAsFieldNames)
                .Select(fieldName => $"contract.Data.{MemberName(fieldName, templateClassName)}")]);
    }

    private void WriteContractChoiceAsyncExerciser(
        IndentWriter indent,
        DamlChoice choice,
        string templateClassName,
        IReadOnlyDictionary<string, DamlDataType> dataTypes,
        IReadOnlyList<string> summaryLines,
        bool declaresSubmitterInfoParameter,
        IReadOnlyList<string> forwardedSubmitterArguments)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var resultName = $"{choiceName}Result";
        var argument = GetChoiceArgumentInfo(choice, dataTypes);
        var hasArg = argument.HasArgument;

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            foreach (var summaryLine in summaryLines)
            {
                indent.AppendLine(summaryLine);
            }
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <param name=\"contract\">The fetched contract on which to exercise the choice.</param>");
            indent.AppendLine("/// <param name=\"client\">The ledger client.</param>");
            if (hasArg)
            {
                indent.AppendLine("/// <param name=\"argument\">The choice argument.</param>");
            }
            if (declaresSubmitterInfoParameter)
            {
                indent.AppendLine("/// <param name=\"submitter\">The submitter party set (<c>actAs</c> + optional <c>readAs</c>).</param>");
            }
            WriteSubmissionParameterDocs(indent);
        }

        indent.AppendLine($"public static Task<{context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome)}<{resultName}>> Try{choiceName}Async(");
        indent.Indent();
        indent.AppendLine($"this {context.Qualifier.Qualify(RuntimeTypeNames.IContract)}<{context.Qualifier.Qualify(RuntimeTypeNames.ContractId)}<{templateClassName}>, {templateClassName}> contract,");
        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.ILedgerWriter)} client,");
        if (hasArg)
        {
            indent.AppendLine($"{argument.ParameterType(templateClassName)} argument,");
        }
        if (declaresSubmitterInfoParameter)
        {
            indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.SubmitterInfo)} submitter,");
        }
        WriteSubmissionParametersAndCloseSignature(indent);
        indent.Dedent();
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("ArgumentNullException.ThrowIfNull(contract);");
        indent.AppendLine("ArgumentNullException.ThrowIfNull(client);");
        if (hasArg)
        {
            indent.AppendLine("ArgumentNullException.ThrowIfNull(argument);");
        }

        indent.AppendLine();
        indent.AppendLine($"return contract.Id.Try{choiceName}Async(");
        indent.Indent();
        indent.AppendLine("client,");
        if (hasArg)
        {
            indent.AppendLine("argument,");
        }
        foreach (var submitterArgument in forwardedSubmitterArguments)
        {
            indent.AppendLine($"{submitterArgument},");
        }
        indent.AppendLine("workflowId,");
        indent.AppendLine("commandId,");
        indent.AppendLine("timeout,");
        indent.AppendLine("cancellationToken);");
        indent.Dedent();

        indent.Dedent();
        indent.AppendLine("}");
    }
}
