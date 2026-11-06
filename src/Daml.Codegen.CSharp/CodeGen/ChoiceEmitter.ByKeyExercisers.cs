// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

internal sealed partial class ChoiceEmitter
{
    private sealed record ByKeyExerciserSite(
        ByKeyExercisePlan Plan,
        string TemplateClassName,
        string KeyTypeName,
        string ResultTypeName,
        string? ArgumentParameterType)
    {
        public DamlChoice Choice => Plan.Choice;

        public bool HasArgument => ArgumentParameterType is not null;
    }

    private static readonly string[] ByKeyExerciserRemarkLines =
    [
        "/// <remarks>",
        "/// Contract keys are not unique: several active contracts may carry the same key, and the",
        "/// ledger resolves this command against a first match by an order it only partly",
        "/// guarantees. The outcome's ResolvedContractId names the contract the ledger chose; to",
        "/// force the choice, disclose the intended contract through configure. Keeping a key",
        "/// unique is the application's responsibility. No active contract with the key is a",
        "/// DamlError with error id CONTRACT_KEY_NOT_FOUND.",
        "/// </remarks>",
    ];

    /// <summary>
    /// Emits one <c>Try&lt;Choice&gt;ByKeyAsync</c> exerciser per choice on
    /// <paramref name="template"/>, beside its <c>&lt;Choice&gt;ByKeyCommand</c> builders in the
    /// template record body. Each submits the by-key command and returns the choice's result
    /// together with the contract the ledger resolved the key to. A key-less template emits
    /// nothing.
    /// </summary>
    internal void WriteChoiceByKeyExercisers(
        IndentWriter indent,
        DamlTemplate template,
        string emittedTemplateName,
        IReadOnlyList<DamlFieldDefinition> fields)
    {
        if (template.Key is null || template.Choices.Count == 0)
        {
            return;
        }

        EmittedUsings.RequireAsyncExerciserNamespaces(indent);
        StdlibPackages.RequireForFieldType(resolver, context.Package, indent, template.Key);

        var templateClassName = context.QualifyInModule(emittedTemplateName);

        foreach (var plan in planner.PlanByKey(template, fields))
        {
            var argument = GetChoiceArgumentInfo(plan.Choice);
            var site = new ByKeyExerciserSite(
                plan,
                templateClassName,
                mapper.MapType(template.Key),
                RequireAndMapReturnType(indent, plan.Choice),
                argument.HasArgument ? argument.ParameterType(templateClassName) : null);

            if (plan.OffersNamedControllers)
            {
                WriteByKeyExerciser(indent, site, namedControllers: true);
                indent.AppendLine();
            }

            WriteByKeyExerciser(indent, site, namedControllers: false);
            indent.AppendLine();
        }
    }

    private void WriteByKeyExerciser(IndentWriter indent, ByKeyExerciserSite site, bool namedControllers)
    {
        if (options.GenerateXmlDocs)
        {
            WriteByKeyExerciserDocs(indent, site, namedControllers);
        }

        WriteByKeyExerciserSignature(indent, site, namedControllers);
        indent.AppendLine("{");
        indent.Indent();
        if (namedControllers)
        {
            WriteByKeyNamedControllersForwardingBody(indent, site);
        }
        else
        {
            WriteByKeyDirectSubmissionBody(indent, site);
        }
        indent.Dedent();
        indent.AppendLine("}");
    }

    private static void WriteByKeyExerciserDocs(IndentWriter indent, ByKeyExerciserSite site, bool namedControllers)
    {
        indent.AppendLine("/// <summary>");
        indent.AppendLine($"/// Exercises the {site.Choice.Name} choice on the contract carrying <paramref name=\"key\"/> and returns the");
        indent.AppendLine("/// choice's exercise result together with the contract the ledger resolved the key to, as a");
        indent.AppendLine("/// <see cref=\"global::Daml.Runtime.Outcomes.ExerciseOutcome{T}\"/> over a");
        indent.AppendLine("/// <see cref=\"global::Daml.Runtime.Outcomes.ByKeyExercise{TTemplate,TResult}\"/>.");
        if (namedControllers)
        {
            indent.AppendLine("/// One <c>Party</c> parameter is emitted per Daml controller (declaration order).");
        }
        indent.AppendLine("/// </summary>");
        foreach (var remarkLine in ByKeyExerciserRemarkLines)
        {
            indent.AppendLine(remarkLine);
        }
        indent.AppendLine("/// <param name=\"client\">The ledger client.</param>");
        indent.AppendLine("/// <param name=\"key\">The contract key to exercise the choice against.</param>");
        if (site.HasArgument)
        {
            indent.AppendLine("/// <param name=\"argument\">The choice argument.</param>");
        }

        if (namedControllers)
        {
            foreach (var parameterName in site.Plan.ControllerParameters)
            {
                indent.AppendLine($"/// <param name=\"{Identifiers.DocCommentName(parameterName)}\">Controller party from the Daml <c>controller</c> clause, routed into the submission's <c>actAs</c> set.</param>");
            }
        }
        else
        {
            indent.AppendLine("/// <param name=\"submitter\">The submitter party set (<c>actAs</c> + optional <c>readAs</c>).</param>");
        }

        SubmissionParameters.WriteExerciseDocs(indent);
    }

    private static void WriteByKeyExerciserSignature(IndentWriter indent, ByKeyExerciserSite site, bool namedControllers)
    {
        var asyncModifier = namedControllers ? string.Empty : "async ";
        var outcome = TypeReferenceQualifier.Qualify(RuntimeTypeNames.ExerciseOutcome);
        var byKeyExercise = TypeReferenceQualifier.Qualify(RuntimeTypeNames.ByKeyExercise);
        indent.AppendLine($"public static {asyncModifier}global::System.Threading.Tasks.Task<{outcome}<{byKeyExercise}<{site.TemplateClassName}, {site.ResultTypeName}>>> {ExerciseMemberNames.ByKeyExerciser(site.Choice)}(");
        indent.Indent();
        indent.AppendLine($"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.ILedgerWriter)} client,");
        indent.AppendLine($"{site.KeyTypeName} key,");
        if (site.HasArgument)
        {
            indent.AppendLine($"{site.ArgumentParameterType} argument,");
        }

        if (namedControllers)
        {
            foreach (var parameterName in site.Plan.ControllerParameters)
            {
                indent.AppendLine($"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.Party)} {parameterName},");
            }
        }
        else
        {
            indent.AppendLine($"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.SubmitterInfo)} submitter,");
        }

        SubmissionParameters.WriteAndCloseSignature(indent);
        indent.Dedent();
    }

    private static void WriteByKeyDirectSubmissionBody(IndentWriter indent, ByKeyExerciserSite site)
    {
        indent.AppendLine("global::System.ArgumentNullException.ThrowIfNull(client);");
        indent.AppendLine();
        indent.AppendLine(site.HasArgument
            ? $"var command = {ExerciseMemberNames.ByKeyCommand(site.Choice)}(key, argument);"
            : $"var command = {ExerciseMemberNames.ByKeyCommand(site.Choice)}(key);");
        indent.AppendLine();
        indent.AppendLine("var outcome = await client.TrySubmitSingleAsync(command, submitter, workflowId, commandId, timeout, configure, cancellationToken).ConfigureAwait(false);");
        indent.AppendLine();
        indent.AppendLine($"return outcome.ProjectCommitted(tx => tx.ProjectByKeyExercise({site.TemplateClassName}.Choice{SanitizeIdentifier(site.Choice.Name)}));");
    }

    private static void WriteByKeyNamedControllersForwardingBody(IndentWriter indent, ByKeyExerciserSite site)
    {
        var submitterInfo = TypeReferenceQualifier.Qualify(RuntimeTypeNames.SubmitterInfo);
        var partyType = TypeReferenceQualifier.Qualify(RuntimeTypeNames.Party);
        var controllers = site.Plan.ControllerParameters;

        indent.AppendLine("global::System.ArgumentNullException.ThrowIfNull(client);");
        indent.AppendLine();
        if (controllers.Count == 1)
        {
            indent.AppendLine($"{submitterInfo} submitter = {controllers[0]};");
        }
        else
        {
            indent.Require("System.Collections.Generic");
            indent.AppendLine($"var submitter = new {submitterInfo}(new {TypeReferenceQualifier.Qualify("HashSet")}<{partyType}> {{ {string.Join(", ", controllers)} }});");
        }

        indent.AppendLine();
        var forwardedArguments = site.HasArgument
            ? "client, key, argument, submitter, workflowId, commandId, timeout, configure, cancellationToken"
            : "client, key, submitter, workflowId, commandId, timeout, configure, cancellationToken";
        indent.AppendLine($"return {ExerciseMemberNames.ByKeyExerciser(site.Choice)}({forwardedArguments});");
    }
}
