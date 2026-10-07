// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using RuntimeNamespaces = Daml.Runtime.RuntimeNamespaces;

namespace Daml.Codegen.CSharp.CodeGen;

internal sealed partial class ChoiceEmitter
{
    /// <summary>
    /// Emits the choice descriptor surface nested inside the interface marker: the
    /// <c>Choice&lt;...&gt;</c> property (with its argument encoder and result decoder)
    /// for every choice on <paramref name="iface"/>, mirroring
    /// <see cref="WriteChoiceDescriptors"/> for templates so both kinds of choice owner
    /// carry the identical descriptor shape.
    /// </summary>
    internal void WriteInterfaceChoiceDescriptors(IndentWriter indent, DamlInterface iface, string interfaceName)
    {
        foreach (var choice in iface.Choices)
        {
            WriteInterfaceChoiceDescriptor(indent, choice, interfaceName);
        }
    }

    private void WriteInterfaceChoiceDescriptor(IndentWriter indent, DamlChoice choice, string interfaceName)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var returnType = mapper.MapType(choice.ReturnType);
        var (argTypeName, hasArg) = ResolveInterfaceChoiceArgType(choice);
        var argTypeRef = hasArg ? argTypeName : TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlUnit);

        indent.Require(RuntimeNamespaces.Commands);
        StdlibPackages.RequireForFieldType(resolver, context.Package, indent, choice.ReturnType);
        StdlibPackages.RequireForFieldType(resolver, context.Package, indent, choice.ArgumentType);

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Exercise the {choice.Name} choice.");
            if (choice.Consuming)
            {
                indent.AppendLine("/// This choice is consuming and will archive the contract.");
            }
            indent.AppendLine("/// </summary>");
        }

        indent.AppendLine($"public static {TypeReferenceQualifier.Qualify(RuntimeTypeNames.Choice)}<{interfaceName}, {argTypeRef}, {returnType}> Choice{choiceName} {{ get; }} = new()");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"Name = new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ChoiceName)}(\"{choice.Name}\"),");
        indent.AppendLine($"Consuming = {(choice.Consuming ? "true" : "false")},");

        if (hasArg)
        {
            indent.AppendLine($"ArgumentEncoder = arg => {mapper.ToValue(choice.ArgumentType, "arg")},");
            indent.AppendLine($"ArgumentDecoder = val => {mapper.FromValue(choice.ArgumentType, "val")},");
            WriteResultDecoder(indent, choice.ReturnType, returnType, mapper);
            WriteJsonReaderProperty(indent, "ArgumentJsonReader", choice.ArgumentType, mapper);
        }
        else
        {
            indent.AppendLine($"ArgumentEncoder = _ => {EmptyArgumentExpression(choice)},");
            WriteEmptyArgumentDecoder(indent, choice);
            WriteResultDecoder(indent, choice.ReturnType, returnType, mapper);
            WriteEmptyArgumentJsonReader(indent, choice);
        }

        WriteJsonReaderProperty(indent, "ResultJsonReader", choice.ReturnType, mapper);

        indent.Dedent();
        indent.AppendLine("};");
        indent.AppendLine();
    }

    internal void WriteInterfaceChoiceExtensions(
        IndentWriter indent,
        DamlInterface iface,
        string interfaceName)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Static <c>Try&lt;Choice&gt;Async</c> extension methods for the <c>{iface.Name}</c> Daml interface.");
            indent.AppendLine("/// One method per choice; each submits an interface-typed");
            indent.AppendLine($"/// <see cref=\"global::Daml.Runtime.Commands.ExerciseCommand\"/> built via");
            indent.AppendLine($"/// <see cref=\"global::Daml.Runtime.Commands.ExerciseCommand.For{{TOwner}}(global::Daml.Runtime.Contracts.ContractId{{TOwner}},global::Daml.Runtime.Commands.ChoiceName,global::Daml.Runtime.Data.DamlValue)\"/>");
            indent.AppendLine("/// through <see cref=\"global::Daml.Ledger.Abstractions.Extensions.SingleCommandExtensions.TrySubmitSingleAsync\"/>");
            indent.AppendLine("/// and projects the committed transaction's matching exercise event through the choice");
            indent.AppendLine("/// descriptor's <c>ResultDecoder</c>, surfacing a typed");
            indent.AppendLine("/// <see cref=\"global::Daml.Runtime.Outcomes.ExerciseOutcome{TResult}\"/> — the choice's own return");
            indent.AppendLine("/// type, not the implementing template's. A decode failure after a successful commit");
            indent.AppendLine("/// surfaces as <see cref=\"global::Daml.Runtime.Outcomes.ExerciseOutcome{TResult}.CommittedUndecodable\"/>,");
            indent.AppendLine("/// never as a resubmittable error.");
            indent.AppendLine("/// </summary>");
        }

        var extensionsClassName = $"{interfaceName}Extensions";

        var emittable = iface.Choices.ToList();

        if (emittable.Count == 0)
        {
            return;
        }

        EmittedUsings.RequireAsyncExerciserNamespaces(indent);

        indent.AppendLine($"public static class {extensionsClassName}");
        indent.AppendLine("{");
        indent.Indent();

        for (var i = 0; i < emittable.Count; i++)
        {
            if (i > 0)
            {
                indent.AppendLine();
            }
            WriteInterfaceChoiceExtensionMethod(indent, emittable[i], interfaceName);
        }

        foreach (var choice in emittable)
        {
            indent.AppendLine();
            WriteInterfaceChoiceExerciseProjector(indent, choice, interfaceName);
        }

        indent.Dedent();
        indent.AppendLine("}");
    }

    private void WriteInterfaceChoiceExtensionMethod(
        IndentWriter indent,
        DamlChoice choice,
        string interfaceName)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var commandMethodName = $"{choiceName}Command";
        var methodName = $"Try{choiceName}Async";
        var (argTypeName, hasArg) = ResolveInterfaceChoiceArgType(choice);
        var requiresArgumentNullCheck = hasArg && choice.ArgumentType is DamlTypeRef;
        var argExpr = hasArg
            ? mapper.ToValue(choice.ArgumentType, "argument")
            : EmptyArgumentExpression(choice);

        WriteInterfaceChoiceCommandBuilder(indent, choice, interfaceName, commandMethodName, argTypeName, hasArg, requiresArgumentNullCheck, argExpr);
        indent.AppendLine();
        WriteInterfaceChoiceAsyncMethod(indent, choice, commandMethodName, methodName, argTypeName, hasArg, interfaceName, SubmitterInfoParameter());
    }

    private void WriteInterfaceChoiceCommandBuilder(
        IndentWriter indent,
        DamlChoice choice,
        string interfaceName,
        string commandMethodName,
        string argTypeName,
        bool hasArg,
        bool requiresArgumentNullCheck,
        string argExpr)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Builds the interface-typed <see cref=\"global::Daml.Runtime.Commands.ExerciseCommand\"/> for the <c>{choice.Name}</c> choice on this contract id.");
            indent.AppendLine("/// The wire-level <c>template_id</c> slot carries the interface id — Canton's");
            indent.AppendLine("/// ledger API resolves the concrete implementing template at the participant.");
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <param name=\"contractId\">The interface-typed contract id to exercise on.</param>");
            if (hasArg)
            {
                indent.AppendLine("/// <param name=\"argument\">The choice argument.</param>");
            }
        }

        indent.AppendLine($"public static {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ExerciseCommand)} {commandMethodName}(");
        indent.Indent();
        if (hasArg)
        {
            indent.AppendLine($"this {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ContractId)}<{interfaceName}> contractId,");
            indent.AppendLine($"{argTypeName} argument)");
        }
        else
        {
            indent.AppendLine($"this {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ContractId)}<{interfaceName}> contractId)");
        }
        indent.Dedent();
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine("global::System.ArgumentNullException.ThrowIfNull(contractId);");
        if (requiresArgumentNullCheck)
        {
            indent.AppendLine("global::System.ArgumentNullException.ThrowIfNull(argument);");
        }
        indent.AppendLine($"return {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ExerciseCommand)}.For<{interfaceName}>(contractId, new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ChoiceName)}(\"{choice.Name}\"), {argExpr});");
        indent.Dedent();
        indent.AppendLine("}");
    }

    /// <remarks>
    /// The emitted signature mirrors the concrete-template non-contract async exerciser
    /// (<see cref="WriteSingleNonContractChoiceAsyncExerciser"/>): it submits the command, then
    /// runs <see cref="Daml.Runtime.Outcomes.ExerciseOutcomeProjection.ProjectCommitted{TProjected}"/>
    /// over the committed transaction using a per-choice projector
    /// (<see cref="WriteInterfaceChoiceExerciseProjector"/>) that hands the choice descriptor
    /// <see cref="WriteInterfaceChoiceDescriptor"/> emits to the runtime, so the returned
    /// <c>ExerciseOutcome&lt;TResult&gt;</c> is typed to the choice's actual return type rather
    /// than the implementing template's.
    /// </remarks>
    private void WriteInterfaceChoiceAsyncMethod(
        IndentWriter indent,
        DamlChoice choice,
        string commandMethodName,
        string methodName,
        string argTypeName,
        bool hasArg,
        string interfaceName,
        ChoiceSubmitterParameter submitter)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var returnType = mapper.MapType(choice.ReturnType);

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Exercises the <c>{choice.Name}</c> interface choice on this contract id, submitting the");
            indent.AppendLine("/// resulting <see cref=\"global::Daml.Runtime.Commands.ExerciseCommand\"/> through");
            indent.AppendLine("/// <see cref=\"global::Daml.Ledger.Abstractions.Extensions.SingleCommandExtensions.TrySubmitSingleAsync\"/>");
            indent.AppendLine($"/// and decoding the committed result through <c>Choice{choiceName}.ResultDecoder</c>.");
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <param name=\"contractId\">The interface-typed contract id to exercise on.</param>");
            indent.AppendLine("/// <param name=\"client\">The ledger client.</param>");
            if (hasArg)
            {
                indent.AppendLine("/// <param name=\"argument\">The choice argument.</param>");
            }
            indent.AppendLine($"/// <param name=\"{submitter.Name}\">{submitter.DocSummary}</param>");
            WriteSubmissionParameterDocs(indent);
        }

        indent.AppendLine($"public static async global::System.Threading.Tasks.Task<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.ExerciseOutcome)}<{returnType}>> {methodName}(");
        indent.Indent();
        indent.AppendLine($"this {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ContractId)}<{interfaceName}> contractId,");
        indent.AppendLine($"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.ILedgerWriter)} client,");
        if (hasArg)
        {
            indent.AppendLine($"{argTypeName} argument,");
        }
        indent.AppendLine($"{submitter.TypeName} {submitter.Name},");
        WriteSubmissionParametersAndCloseSignature(indent);
        indent.Dedent();
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("global::System.ArgumentNullException.ThrowIfNull(client);");
        indent.AppendLine();

        indent.AppendLine(hasArg
            ? $"var command = contractId.{commandMethodName}(argument);"
            : $"var command = contractId.{commandMethodName}();");
        indent.AppendLine();
        indent.AppendLine($"var outcome = await client.TrySubmitSingleAsync(command, {submitter.Name}, workflowId, commandId, timeout, configure, cancellationToken).ConfigureAwait(false);");
        indent.AppendLine();
        indent.AppendLine($"return outcome.ProjectCommitted(tx => Project{choiceName}Result(tx, contractId.Value));");

        indent.Dedent();
        indent.AppendLine("}");
    }

    /// <summary>
    /// Emits the private <c>Project&lt;Choice&gt;Result</c> hand-off: it passes the choice's
    /// generated descriptor to the runtime's Exercise-result projection
    /// (<c>ExerciseOutcomeProjection.ProjectChoiceResult</c>), which matches the exercised event on
    /// the interface id, decodes it through the descriptor's <c>ResultDecoder</c>, and owns the
    /// no-event diagnostic.
    /// </summary>
    private void WriteInterfaceChoiceExerciseProjector(
        IndentWriter indent,
        DamlChoice choice,
        string interfaceName)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var returnType = mapper.MapType(choice.ReturnType);

        indent.AppendLine($"private static {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ExerciseOutcome)}<{returnType}> Project{choiceName}Result({TypeReferenceQualifier.Qualify(RuntimeTypeNames.TransactionResult)} tx, string contractId) =>");
        indent.Indent();
        indent.AppendLine($"tx.ProjectChoiceResult({interfaceName}.Choice{choiceName}, contractId);");
        indent.Dedent();
    }

    private (string TypeName, bool HasArg) ResolveInterfaceChoiceArgType(DamlChoice choice)
    {
        if (choice.ArgumentType is DamlPrimitiveType { Primitive: DamlPrimitive.Unit })
        {
            return ("DamlUnit", false);
        }
        if (IsSyntheticArchive(choice))
        {
            return ("DamlUnit", false);
        }
        return (mapper.MapType(choice.ArgumentType), true);
    }
}
