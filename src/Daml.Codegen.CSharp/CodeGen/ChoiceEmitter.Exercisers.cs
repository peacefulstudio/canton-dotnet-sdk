// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

internal sealed partial class ChoiceEmitter
{
    private readonly ChoiceExercisePlanner planner = new(party);

    private sealed record ExerciserSite(
        ChoiceExercisePlan Plan,
        string OwnerTypeName,
        string ReturnTypeName,
        string? ArgumentParameterType,
        Func<string, string> PayloadMember)
    {
        public DamlChoice Choice => Plan.Choice;

        public bool HasArgument => ArgumentParameterType is not null;
    }

    private ExerciserSite TemplateExerciserSite(
        IndentWriter indent,
        ChoiceExercisePlan plan,
        string templateClassName,
        Func<string, string>? payloadMember = null)
    {
        var argument = GetChoiceArgumentInfo(plan.Choice);
        return new ExerciserSite(
            plan,
            templateClassName,
            RequireAndMapReturnType(indent, plan.Choice),
            argument.HasArgument ? argument.ParameterType(templateClassName) : null,
            payloadMember ?? KeepFieldName);
    }

    private static string KeepFieldName(string fieldName) => fieldName;

    private ExerciserSite InterfaceExerciserSite(ChoiceExercisePlan plan, string interfaceName)
    {
        var (argTypeName, hasArg) = ResolveInterfaceChoiceArgType(plan.Choice);
        return new ExerciserSite(
            plan,
            interfaceName,
            mapper.MapType(plan.Choice.ReturnType),
            hasArg ? argTypeName : null,
            KeepFieldName);
    }

    private void WriteExerciserOverloads(IndentWriter indent, ExerciserSite site)
    {
        for (var i = 0; i < site.Plan.Overloads.Count; i++)
        {
            if (i > 0)
            {
                indent.AppendLine();
            }
            WriteExerciser(indent, site, site.Plan.Overloads[i]);
        }
    }

    private void WriteExerciser(IndentWriter indent, ExerciserSite site, ExerciseOverload overload)
    {
        if (options.GenerateXmlDocs)
        {
            WriteExerciserDocs(indent, site, overload);
        }

        WriteExerciserSignature(indent, site, overload);
        indent.AppendLine("{");
        indent.Indent();
        WriteExerciserBody(indent, site, overload);
        indent.Dedent();
        indent.AppendLine("}");
    }

    private static void WriteExerciserSignature(IndentWriter indent, ExerciserSite site, ExerciseOverload overload)
    {
        var asyncModifier = overload.SubmitsDirectly ? "async " : string.Empty;
        var outcome = TypeReferenceQualifier.Qualify(RuntimeTypeNames.ExerciseOutcome);
        indent.AppendLine($"public static {asyncModifier}global::System.Threading.Tasks.Task<{outcome}<{site.ReturnTypeName}>> {ExerciseMemberNames.Exerciser(site.Choice)}(");
        indent.Indent();
        indent.AppendLine(ReceiverParameter(site, overload.Receiver));
        indent.AppendLine($"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.ILedgerWriter)} client,");
        if (site.HasArgument)
        {
            indent.AppendLine($"{site.ArgumentParameterType} argument,");
        }
        WriteSubmitterParameters(indent, site, overload.Submitter);
        WriteSubmissionParametersAndCloseSignature(indent);
        indent.Dedent();
    }

    private static string ReceiverParameter(ExerciserSite site, ExerciseReceiver receiver)
    {
        var contractId = $"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.ContractId)}<{site.OwnerTypeName}>";
        return receiver == ExerciseReceiver.ContractId
            ? $"this {contractId} contractId,"
            : $"this {TypeReferenceQualifier.Qualify(RuntimeTypeNames.IContract)}<{contractId}, {site.OwnerTypeName}> contract,";
    }

    private static void WriteSubmitterParameters(IndentWriter indent, ExerciserSite site, ExerciseSubmitter submitter)
    {
        switch (submitter)
        {
            case NamedControllersSubmitter named:
                foreach (var parameterName in named.ControllerParameters.Concat(named.ObserverParameters))
                {
                    indent.AppendLine($"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.Party)} {parameterName},");
                }
                break;
            case ExplicitSubmitter:
                var explicitSubmitter = ExplicitSubmitterParameter(site.Plan.Class);
                indent.AppendLine($"{explicitSubmitter.TypeName} {explicitSubmitter.Name},");
                break;
        }
    }

    private static void WriteExerciserBody(IndentWriter indent, ExerciserSite site, ExerciseOverload overload)
    {
        if (overload.SubmitsDirectly)
        {
            WriteDirectSubmissionBody(indent, site);
            return;
        }

        switch (overload.Submitter)
        {
            case NamedControllersSubmitter named:
                WriteNamedControllersForwardingBody(indent, site, named);
                break;
            case PayloadPartiesSubmitter payloadParties:
                WriteFetchedContractForwardingBody(
                    indent,
                    site,
                    [.. payloadParties.ControllerFields
                        .Concat(payloadParties.ObserverFields)
                        .Select(site.PayloadMember)
                        .Select(member => $"contract.Data.{member}")]);
                break;
            default:
                WriteFetchedContractForwardingBody(indent, site, [ExplicitSubmitterParameter(site.Plan.Class).Name]);
                break;
        }
    }

    private static void WriteDirectSubmissionBody(IndentWriter indent, ExerciserSite site)
    {
        var submitter = ExplicitSubmitterParameter(site.Plan.Class).Name;
        indent.AppendLine("global::System.ArgumentNullException.ThrowIfNull(client);");
        indent.AppendLine();
        indent.AppendLine(site.HasArgument
            ? $"var command = contractId.{ExerciseMemberNames.Command(site.Choice)}(argument);"
            : $"var command = contractId.{ExerciseMemberNames.Command(site.Choice)}();");
        indent.AppendLine();
        indent.AppendLine($"var outcome = await client.TrySubmitSingleAsync(command, {submitter}, workflowId, commandId, timeout, configure, cancellationToken).ConfigureAwait(false);");
        indent.AppendLine();
        indent.AppendLine($"return outcome.ProjectCommitted(tx => {ExerciseMemberNames.Projector(site.Choice)}(tx, contractId.Value));");
    }

    private static void WriteNamedControllersForwardingBody(
        IndentWriter indent,
        ExerciserSite site,
        NamedControllersSubmitter named)
    {
        var submitterInfo = TypeReferenceQualifier.Qualify(RuntimeTypeNames.SubmitterInfo);
        var partyType = TypeReferenceQualifier.Qualify(RuntimeTypeNames.Party);
        var controllers = string.Join(", ", named.ControllerParameters);

        indent.AppendLine("global::System.ArgumentNullException.ThrowIfNull(client);");
        indent.AppendLine();
        if (named.ControllerParameters.Count == 1 && named.ObserverParameters.Count == 0)
        {
            indent.AppendLine($"{submitterInfo} submitter = {named.ControllerParameters[0]};");
        }
        else if (named.ObserverParameters.Count == 0)
        {
            indent.Require("System.Collections.Generic");
            indent.AppendLine("// SubmitterInfo's actAs unions every named controller.");
            indent.AppendLine($"var submitter = new {submitterInfo}(new {TypeReferenceQualifier.Qualify("HashSet")}<{partyType}> {{ {controllers} }});");
        }
        else
        {
            indent.Require("System.Collections.Generic");
            indent.AppendLine("// actAs unions every named controller; readAs unions every observer that is");
            indent.AppendLine("// not also a controller, so the wire format reflects Daml's stakeholder model.");
            indent.AppendLine($"var submitter = new {submitterInfo}(");
            indent.Indent();
            indent.AppendLine($"actAs: new {TypeReferenceQualifier.Qualify("HashSet")}<{partyType}> {{ {controllers} }},");
            indent.AppendLine($"readAs: new {TypeReferenceQualifier.Qualify("HashSet")}<{partyType}> {{ {string.Join(", ", named.ObserverParameters)} }});");
            indent.Dedent();
        }

        indent.AppendLine();
        WriteForwardedExerciserCall(indent, site, "contractId", ["submitter"]);
    }

    private static void WriteFetchedContractForwardingBody(
        IndentWriter indent,
        ExerciserSite site,
        IReadOnlyList<string> forwardedSubmitterArguments)
    {
        indent.AppendLine("global::System.ArgumentNullException.ThrowIfNull(contract);");
        indent.AppendLine("global::System.ArgumentNullException.ThrowIfNull(client);");
        if (site.HasArgument)
        {
            indent.AppendLine("global::System.ArgumentNullException.ThrowIfNull(argument);");
        }

        indent.AppendLine();
        WriteForwardedExerciserCall(indent, site, "contract.Id", forwardedSubmitterArguments);
    }

    private static void WriteForwardedExerciserCall(
        IndentWriter indent,
        ExerciserSite site,
        string receiver,
        IReadOnlyList<string> forwardedSubmitterArguments)
    {
        indent.AppendLine($"return {receiver}.{ExerciseMemberNames.Exerciser(site.Choice)}(");
        indent.Indent();
        indent.AppendLine("client,");
        if (site.HasArgument)
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
        indent.AppendLine("configure,");
        indent.AppendLine("cancellationToken);");
        indent.Dedent();
    }
}
