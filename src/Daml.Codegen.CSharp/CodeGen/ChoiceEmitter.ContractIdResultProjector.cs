// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

internal sealed partial class ChoiceEmitter
{
    /// <summary>
    /// Emits the private <c>Project&lt;Choice&gt;Result</c> helper the contract-id-returning
    /// exercisers project a committed transaction through, plus the
    /// <c>Decode&lt;Choice&gt;Result</c> helper it reads the choice's own exercise result with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The exercise result is the choice's return value, so it names the returned contracts even
    /// when the submitter is no stakeholder of them, where the transaction's ACS-delta
    /// <c>CreatedContracts</c> leaves them out. The exercise is located the way the non-contract
    /// projector locates it: by contract id, the template's module and entity names, and the
    /// choice name, so package-id drift from an upgrade still matches.
    /// </para>
    /// <para>
    /// <c>&lt;Choice&gt;Result.FromCreatedContracts</c> still runs first and wins when it reports
    /// <c>Many</c>, so a choice that creates more contracts of a slot's template than its return
    /// type names keeps surfacing as <c>Many</c>. A transaction carrying no matching exercise,
    /// such as one from a custom writer that does not project exercised events, falls back to the
    /// created-contracts projection.
    /// </para>
    /// </remarks>
    private void WriteContractIdResultProjector(
        IndentWriter indent,
        DamlChoice choice,
        string templateClassName,
        IReadOnlyList<ChoiceCreatedSlot> slots)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var resultName = $"{choiceName}Result";
        var outcomeType = $"{context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome)}<{resultName}>";

        indent.AppendLine($"private static {outcomeType} Project{choiceName}Result({context.Qualifier.Qualify(RuntimeTypeNames.TransactionResult)} tx, string contractId)");
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine($"var fromCreatedContracts = {resultName}.FromCreatedContracts(tx.CreatedContracts);");
        indent.AppendLine($"if (fromCreatedContracts is {outcomeType}.Many)");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine("return fromCreatedContracts;");
        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();

        indent.AppendLine("foreach (var exercised in tx.ExercisedEvents)");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine("if (string.Equals(exercised.ContractId, contractId, StringComparison.Ordinal)");
        indent.AppendLine($"    && string.Equals(exercised.TemplateId.ModuleName, {templateClassName}.TemplateId.ModuleName, StringComparison.Ordinal)");
        indent.AppendLine($"    && string.Equals(exercised.TemplateId.EntityName, {templateClassName}.TemplateId.EntityName, StringComparison.Ordinal)");
        indent.AppendLine($"    && string.Equals(exercised.ChoiceName.Value, \"{choice.Name}\", StringComparison.Ordinal))");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine("try");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"return Decode{choiceName}Result(exercised.ExerciseResult);");
        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine("catch (global::System.Exception ex) when (ex is not global::System.OperationCanceledException)");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"return new {outcomeType}.CommittedUndecodable(tx.UpdateId, ex.Message, ex);");
        indent.Dedent();
        indent.AppendLine("}");
        indent.Dedent();
        indent.AppendLine("}");
        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();
        indent.AppendLine("return fromCreatedContracts;");

        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();

        WriteExerciseResultDecoder(indent, choiceName, resultName, outcomeType, slots);
    }

    private void WriteExerciseResultDecoder(
        IndentWriter indent,
        string choiceName,
        string resultName,
        string outcomeType,
        IReadOnlyList<ChoiceCreatedSlot> slots)
    {
        indent.Require("System.Collections.Generic");

        indent.AppendLine($"private static {outcomeType} Decode{choiceName}Result({context.Qualifier.Qualify(RuntimeTypeNames.DamlValue)} exerciseResult)");
        indent.AppendLine("{");
        indent.Indent();

        for (var i = 0; i < slots.Count; i++)
        {
            indent.AppendLine($"var matches{i} = new List<string>();");
        }
        for (var i = 0; i < slots.Count; i++)
        {
            WriteResultPathWalk(indent, slots[i].ResultPath, stepIndex: 0, "exerciseResult", slotIndex: i);
        }

        WriteSlotCardinalityProjection(indent, resultName, slots);

        indent.Dedent();
        indent.AppendLine("}");
    }

    private void WriteResultPathWalk(
        IndentWriter indent,
        IReadOnlyList<ExerciseResultStep> path,
        int stepIndex,
        string value,
        int slotIndex)
    {
        if (stepIndex == path.Count)
        {
            indent.AppendLine($"matches{slotIndex}.Add({value}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlContractId)}>().Value);");
            return;
        }

        switch (path[stepIndex])
        {
            case ExerciseResultStep.TupleComponent { IsOptional: true } component:
            {
                var fields = $"fields{slotIndex}_{stepIndex}";
                indent.AppendLine($"var {fields} = {value}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord)}>().Fields;");
                indent.AppendLine($"if ({fields}.Count > {component.Index})");
                indent.AppendLine("{");
                indent.Indent();
                WriteResultPathWalk(indent, path, stepIndex + 1, $"{fields}[{component.Index}].Value", slotIndex);
                indent.Dedent();
                indent.AppendLine("}");
                return;
            }

            case ExerciseResultStep.TupleComponent component:
                WriteResultPathWalk(
                    indent,
                    path,
                    stepIndex + 1,
                    $"{value}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord)}>().Fields[{component.Index}].Value",
                    slotIndex);
                return;

            case ExerciseResultStep.OptionalValue:
            {
                var present = $"present{slotIndex}_{stepIndex}";
                indent.AppendLine($"if ({value} switch");
                indent.AppendLine("{");
                indent.Indent();
                indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.DamlOptional)} optional => optional.Value,");
                indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.DamlOptionalChain)} chain => chain.Value,");
                indent.AppendLine("var bare => bare,");
                indent.Dedent();
                indent.AppendLine($"}} is {{ }} {present})");
                indent.AppendLine("{");
                indent.Indent();
                WriteResultPathWalk(indent, path, stepIndex + 1, present, slotIndex);
                indent.Dedent();
                indent.AppendLine("}");
                return;
            }

            case ExerciseResultStep.ListElements:
            {
                var element = $"element{slotIndex}_{stepIndex}";
                indent.AppendLine($"foreach (var {element} in {value}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlList)}>().Values)");
                indent.AppendLine("{");
                indent.Indent();
                WriteResultPathWalk(indent, path, stepIndex + 1, element, slotIndex);
                indent.Dedent();
                indent.AppendLine("}");
                return;
            }

            default:
                throw new InvalidOperationException($"Unhandled exercise-result step {path[stepIndex]}.");
        }
    }
}
