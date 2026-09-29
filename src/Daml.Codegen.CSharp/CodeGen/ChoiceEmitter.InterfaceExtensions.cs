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
        var argTypeRef = hasArg ? argTypeName : context.Qualifier.Qualify(RuntimeTypeNames.DamlUnit);

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

        indent.AppendLine($"public static {context.Qualifier.Qualify(RuntimeTypeNames.Choice)}<{interfaceName}, {argTypeRef}, {returnType}> Choice{choiceName} {{ get; }} = new()");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"Name = new {context.Qualifier.Qualify(RuntimeTypeNames.ChoiceName)}(\"{choice.Name}\"),");
        indent.AppendLine($"Consuming = {(choice.Consuming ? "true" : "false")},");

        if (hasArg)
        {
            indent.AppendLine($"ArgumentEncoder = arg => {PackageQualifiedMapper.ToValue(choice.ArgumentType, "arg")},");
            indent.AppendLine($"ArgumentDecoder = val => {PackageQualifiedMapper.FromValue(choice.ArgumentType, "val")},");
            WriteResultDecoder(indent, choice.ReturnType, returnType, PackageQualifiedMapper);
            WriteJsonReaderProperty(indent, "ArgumentJsonReader", choice.ArgumentType, PackageQualifiedMapper);
        }
        else
        {
            indent.AppendLine($"ArgumentEncoder = _ => {EmptyArgumentExpression(choice)},");
            WriteEmptyArgumentDecoder(indent, choice);
            WriteResultDecoder(indent, choice.ReturnType, returnType, PackageQualifiedMapper);
            WriteEmptyArgumentJsonReader(indent, choice);
        }

        WriteJsonReaderProperty(indent, "ResultJsonReader", choice.ReturnType, PackageQualifiedMapper);

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

        indent.AppendLine($"public static {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseCommand)} {commandMethodName}(");
        indent.Indent();
        if (hasArg)
        {
            indent.AppendLine($"this {context.Qualifier.Qualify(RuntimeTypeNames.ContractId)}<{interfaceName}> contractId,");
            indent.AppendLine($"{argTypeName} argument)");
        }
        else
        {
            indent.AppendLine($"this {context.Qualifier.Qualify(RuntimeTypeNames.ContractId)}<{interfaceName}> contractId)");
        }
        indent.Dedent();
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine("ArgumentNullException.ThrowIfNull(contractId);");
        if (requiresArgumentNullCheck)
        {
            indent.AppendLine("ArgumentNullException.ThrowIfNull(argument);");
        }
        indent.AppendLine($"return {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseCommand)}.For<{interfaceName}>(contractId, new {context.Qualifier.Qualify(RuntimeTypeNames.ChoiceName)}(\"{choice.Name}\"), {argExpr});");
        indent.Dedent();
        indent.AppendLine("}");
    }

    /// <remarks>
    /// The emitted signature mirrors the concrete-template non-contract async exerciser
    /// (<see cref="WriteSingleNonContractChoiceAsyncExerciser"/>): it submits the command, then
    /// runs <see cref="Daml.Runtime.Outcomes.ExerciseOutcomeProjection.ProjectCommitted{TProjected}"/>
    /// over the committed transaction using a per-choice projector
    /// (<see cref="WriteInterfaceChoiceExerciseProjector"/>) that locates the matching exercise
    /// event and decodes it through the choice descriptor's <c>ResultDecoder</c> — the same
    /// decoder <see cref="WriteInterfaceChoiceDescriptor"/> emits, so the returned
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

        indent.AppendLine($"public static async Task<{context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome)}<{returnType}>> {methodName}(");
        indent.Indent();
        indent.AppendLine($"this {context.Qualifier.Qualify(RuntimeTypeNames.ContractId)}<{interfaceName}> contractId,");
        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.ILedgerWriter)} client,");
        if (hasArg)
        {
            indent.AppendLine($"{argTypeName} argument,");
        }
        indent.AppendLine($"{submitter.TypeName} {submitter.Name},");
        WriteSubmissionParametersAndCloseSignature(indent);
        indent.Dedent();
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("ArgumentNullException.ThrowIfNull(client);");
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
    /// Emits a private static helper that locates the exercise event matching this choice in
    /// <c>tx.ExercisedEvents</c>, decodes it through <c>Choice{choiceName}.ResultDecoder</c>, and
    /// returns <c>ExerciseOutcome&lt;TResult&gt;.One(...)</c>. Mirrors
    /// <see cref="WriteExerciseProjector"/> for templates, with two deliberate differences:
    /// <list type="bullet">
    ///   <item>Matching keys on <see cref="Daml.Runtime.Contracts.ExercisedEvent.InterfaceId"/>
    ///   rather than <c>TemplateId</c> — the implementing template is not known at the interface
    ///   call site, but the interface id the choice was exercised through is.</item>
    ///   <item>The decode call is wrapped so any exception it raises maps to
    ///   <c>ExerciseOutcome&lt;TResult&gt;.CommittedUndecodable</c> instead of propagating: the
    ///   command already committed by this point, so the caller must not read a thrown exception
    ///   as grounds to resubmit.</item>
    /// </list>
    /// Throws <see cref="InvalidOperationException"/> when no matching exercise event is found,
    /// the same cardinality contract <see cref="WriteExerciseProjector"/> uses.
    /// </summary>
    private void WriteInterfaceChoiceExerciseProjector(
        IndentWriter indent,
        DamlChoice choice,
        string interfaceName)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var returnType = mapper.MapType(choice.ReturnType);

        indent.AppendLine($"private static {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome)}<{returnType}> Project{choiceName}Result({context.Qualifier.Qualify(RuntimeTypeNames.TransactionResult)} tx, string contractId)");
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("foreach (var exercised in tx.ExercisedEvents)");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine("if (exercised.InterfaceId is { } interfaceId");
        indent.AppendLine("    && string.Equals(exercised.ContractId, contractId, global::System.StringComparison.Ordinal)");
        indent.AppendLine($"    && string.Equals(interfaceId.ModuleName, {interfaceName}.InterfaceId.ModuleName, global::System.StringComparison.Ordinal)");
        indent.AppendLine($"    && string.Equals(interfaceId.EntityName, {interfaceName}.InterfaceId.EntityName, global::System.StringComparison.Ordinal)");
        indent.AppendLine($"    && string.Equals(exercised.ChoiceName.Value, \"{choice.Name}\", global::System.StringComparison.Ordinal))");
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("try");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"var decoded = {interfaceName}.Choice{choiceName}.ResultDecoder!(exercised.ExerciseResult);");
        indent.AppendLine($"return new {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome)}<{returnType}>.One(decoded);");
        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine("catch (global::System.Exception ex) when (ex is not global::System.OperationCanceledException)");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"return new {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome)}<{returnType}>.CommittedUndecodable(tx.UpdateId, ex.Message, ex);");
        indent.Dedent();
        indent.AppendLine("}");

        indent.Dedent();
        indent.AppendLine("}");
        indent.Dedent();
        indent.AppendLine("}");

        indent.AppendLine();
        indent.AppendLine("throw new global::System.InvalidOperationException(");
        indent.Indent();
        indent.AppendLine($"$\"Submission succeeded but no '{choice.Name}' exercise on contract '{{contractId}}' was recorded on transaction {{tx.UpdateId}}. \" +");
        indent.AppendLine("\"The transaction returned for this submission carries no exercised event for it. \" +");
        indent.AppendLine("\"Either a custom ILedgerWriter did not project the transaction's exercised events into TransactionResult.ExercisedEvents, \" +");
        indent.AppendLine("\"or the transaction was requested in a shape without exercised events (ACS_DELTA); \" +");
        indent.AppendLine("\"request the LEDGER_EFFECTS shape with verbose events.\");");
        indent.Dedent();

        indent.Dedent();
        indent.AppendLine("}");
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
