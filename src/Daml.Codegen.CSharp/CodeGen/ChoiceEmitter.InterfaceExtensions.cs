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

        var plans = iface.Choices.Select(ChoiceExercisePlanner.PlanInterfaceChoice).ToList();

        if (plans.Count == 0)
        {
            return;
        }

        EmittedUsings.RequireAsyncExerciserNamespaces(indent);

        indent.AppendLine($"public static class {extensionsClassName}");
        indent.AppendLine("{");
        indent.Indent();

        for (var i = 0; i < plans.Count; i++)
        {
            if (i > 0)
            {
                indent.AppendLine();
            }
            WriteInterfaceChoiceExtensionMethod(indent, plans[i], interfaceName);
        }

        foreach (var plan in plans)
        {
            indent.AppendLine();
            WriteExerciseProjector(indent, plan.Choice, interfaceName);
        }

        indent.Dedent();
        indent.AppendLine("}");
    }

    private void WriteInterfaceChoiceExtensionMethod(
        IndentWriter indent,
        ChoiceExercisePlan plan,
        string interfaceName)
    {
        ChoiceCommandBuilderWriter.Write(indent, InterfaceCommandBuilder(plan.Choice, interfaceName));
        indent.AppendLine();
        WriteExerciserOverloads(indent, InterfaceExerciserSite(plan, interfaceName));
    }

    private ChoiceCommandBuilder InterfaceCommandBuilder(DamlChoice choice, string interfaceName)
    {
        var (argTypeName, hasArg) = ResolveInterfaceChoiceArgType(choice);
        var exerciseCommand = TypeReferenceQualifier.Qualify(RuntimeTypeNames.ExerciseCommand);
        var argExpr = hasArg
            ? mapper.ToValue(choice.ArgumentType, "argument")
            : EmptyArgumentExpression(choice);

        return new ChoiceCommandBuilder(
            CommandType: exerciseCommand,
            Name: ExerciseMemberNames.Command(choice),
            Docs: options.GenerateXmlDocs
                ? new CommandBuilderDocs(
                    Summary:
                    [
                        $"Builds the interface-typed <see cref=\"global::Daml.Runtime.Commands.ExerciseCommand\"/> for the <c>{choice.Name}</c> choice on this contract id.",
                        "The wire-level <c>template_id</c> slot carries the interface id — Canton's",
                        "ledger API resolves the concrete implementing template at the participant.",
                    ],
                    ReceiverParameter: "<param name=\"contractId\">The interface-typed contract id to exercise on.</param>",
                    Remarks: [])
                : null,
            ReceiverParameter: $"this {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ContractId)}<{interfaceName}> contractId",
            ArgumentParameterType: hasArg ? argTypeName : null,
            NullGuardedParameters: GuardedParameters("contractId", hasArg && choice.ArgumentType is DamlTypeRef),
            ReturnStatement:
            [
                $"return {exerciseCommand}.For<{interfaceName}>(contractId, new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ChoiceName)}(\"{choice.Name}\"), {argExpr});",
            ]);
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
