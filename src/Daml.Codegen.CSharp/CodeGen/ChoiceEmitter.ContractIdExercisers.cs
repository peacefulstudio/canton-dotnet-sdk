// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

internal sealed partial class ChoiceEmitter
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="template"/> has at least one choice whose return
    /// type names contract ids (see <see cref="ReturnsContractIds(DamlType)"/>).
    /// </summary>
    private static bool TemplateHasEmittableAsyncExercisers(DamlTemplate template) =>
        template.Choices.Any(choice => ReturnsContractIds(choice.ReturnType));

    /// <summary>
    /// Whether a choice's return type names contract ids: a bare <c>ContractId</c>, or one reached
    /// through <c>Optional</c>, a list or a <c>DA.Types</c> tuple. A record, variant or primitive
    /// that merely contains a contract id does not count.
    /// </summary>
    internal static bool ReturnsContractIds(DamlType returnType) =>
        returnType.Accept(ContractIdBearingTypeVisitor.Instance);

    /// <summary>
    /// Emits the static <c>&lt;TemplateName&gt;Extensions</c> class containing one
    /// <c>Try&lt;Choice&gt;Async(...)</c> exerciser per create-bearing choice. Lives at the
    /// namespace level so the methods extend <c>ContractId&lt;TemplateName&gt;</c> in
    /// every consumer that imports the module's namespace. Skips emission entirely
    /// when no choice qualifies (avoids stranded empty classes).
    ///
    /// <para>
    /// The exerciser's parameter shape is driven by the static analyzer:
    /// <list type="bullet">
    ///   <item>When every controller is a payload-field reference, one
    ///   <c>Party</c> parameter per controller (declaration order) appears on
    ///   the method, and the wrapper unions them into <c>SubmitterInfo.actAs</c>.</item>
    ///   <item>When the template-level <c>observer</c> clause and/or the
    ///   choice's <c>observer</c> clause is statically resolvable, those
    ///   parties are added to <c>SubmitterInfo.readAs</c>, deduplicated.</item>
    ///   <item>When controllers are not statically resolvable, the wrapper
    ///   falls back to a single <c>SubmitterInfo submitter</c> parameter and
    ///   passes it through unchanged — caller takes responsibility for both
    ///   <c>actAs</c> and <c>readAs</c>.</item>
    /// </list>
    /// </para>
    /// </summary>
    internal void WriteChoiceAsyncExercisersClass(
        IndentWriter indent,
        DamlTemplate template,
        string emittedTemplateName,
        IReadOnlyList<DamlFieldDefinition> fields)
    {
        if (!TemplateHasEmittableAsyncExercisers(template))
        {
            return;
        }

        EmittedUsings.RequireAsyncExerciserNamespaces(indent);

        var templateClassName = context.QualifyInModule(emittedTemplateName);
        var partyFields = fields
            .Where(f => f.Type is DamlPrimitiveType { Primitive: DamlPrimitive.Party })
            .ToDictionary(f => f.Name, f => f, StringComparer.Ordinal);

        var templateObservers = party.ValidatePayloadParties(template.Observers, partyFields);

        indent.AppendLine();
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Static <c>Try&lt;Choice&gt;Async</c> extension methods for <see cref=\"{templateClassName}\"/>.");
            indent.AppendLine("/// One method per create-bearing choice; each delegates to");
            indent.AppendLine("/// <see cref=\"global::Daml.Ledger.Abstractions.Extensions.SingleCommandExtensions.TrySubmitSingleAsync\"/>");
            indent.AppendLine("/// and projects success from the choice's own exercise result.");
            indent.AppendLine("/// </summary>");
        }
        indent.AppendLine($"public static class {emittedTemplateName}Extensions");
        indent.AppendLine("{");
        indent.Indent();

        var first = true;
        foreach (var choice in template.Choices)
        {
            if (!ReturnsContractIds(choice.ReturnType))
            {
                continue;
            }

            if (!first)
            {
                indent.AppendLine();
            }
            var controllers = party.ValidatePayloadParties(choice.Controllers, partyFields);
            var choiceObservers = party.ValidatePayloadParties(choice.Observers, partyFields);
            var effectiveReadAs = party.UnionStaticParties(templateObservers, choiceObservers);
            WriteChoiceCommandBuilder(indent, choice, templateClassName);
            indent.AppendLine();
            WriteSingleChoiceAsyncExerciser(
                indent, choice, templateClassName, controllers, effectiveReadAs);

            if (controllers.Source == DamlPartySource.Static && controllers.Parties.Count > 0)
            {
                indent.AppendLine();
                WriteSubmitterInfoChoiceAsyncExerciser(
                    indent, choice, templateClassName);

                indent.AppendLine();
                WriteSingleContractChoiceAsyncExerciser(
                    indent, choice, templateClassName, controllers, effectiveReadAs, emittedTemplateName, context.TemplateFieldReservedNames(template));

                indent.AppendLine();
                WriteSubmitterInfoContractChoiceAsyncExerciser(
                    indent, choice, templateClassName);
            }

            indent.AppendLine();
            WriteExerciseProjector(indent, choice, templateClassName);
            first = false;
        }

        indent.Dedent();
        indent.AppendLine("}");
    }

    /// <remarks>
    /// The choice receiver here is a contract id, so no payload is in scope and observer parties
    /// can only be surfaced as named <c>Party</c> parameters. Observer-only fields — named in an
    /// observer clause but not as controllers — follow the controller parameters in declaration
    /// order, and the emitted body routes controllers into the submitter's <c>actAs</c> set and
    /// observers into its <c>readAs</c> set.
    /// </remarks>
    private void WriteSingleChoiceAsyncExerciser(
        IndentWriter indent,
        DamlChoice choice,
        string templateClassName,
        DamlPartyAnalysis controllers,
        DamlPartyAnalysis observers)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var returnTypeName = RequireAndMapReturnType(indent, choice);
        var argument = GetChoiceArgumentInfo(choice);
        var hasArg = argument.HasArgument;

        var staticControllers = controllers.Source == DamlPartySource.Static
                                 && controllers.Parties.Count > 0;

        var (controllerParams, readAsParams) = staticControllers
            ? party.PartitionControllersAndObservers(controllers, observers)
            : (new List<string>(), new List<string>());

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Exercises the {choice.Name} choice and lifts the choice's exercise result to <see cref=\"ExerciseOutcome{{T}}\"/> over <c>{EmitterHelpers.EscapeXmlText(returnTypeName)}</c>.");
            if (staticControllers && readAsParams.Count > 0)
            {
                indent.AppendLine("/// One <c>Party</c> parameter is emitted per Daml controller (declaration order),");
                indent.AppendLine("/// followed by one parameter per Daml observer that is not also a controller.");
                indent.AppendLine("/// The wrapper builds a <see cref=\"SubmitterInfo\"/> with controllers in <c>actAs</c>");
                indent.AppendLine("/// and observers in <c>readAs</c>, so the wire format reflects Daml's stakeholder");
                indent.AppendLine("/// model exactly.");
            }
            else if (staticControllers)
            {
                indent.AppendLine("/// One <c>Party</c> parameter is emitted per Daml controller (declaration order).");
                indent.AppendLine("/// The wrapper builds a <see cref=\"SubmitterInfo\"/> from those parties before");
                indent.AppendLine("/// dispatching to <c>ILedgerWriter</c>.");
            }
            else
            {
                indent.AppendLine("/// The submitter is passed explicitly via <paramref name=\"submitter\"/> — the static");
                indent.AppendLine("/// analyzer could not resolve the Daml <c>controller</c> clause to payload-field");
                indent.AppendLine("/// references. <see cref=\"SubmitterInfo\"/> implicitly converts from a");
                indent.AppendLine("/// single <c>Party</c>, so the single-party call site stays a one-liner.");
            }
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <param name=\"contractId\">The contract on which to exercise the choice.</param>");
            indent.AppendLine("/// <param name=\"client\">The ledger client.</param>");
            if (hasArg)
            {
                indent.AppendLine("/// <param name=\"argument\">The choice argument.</param>");
            }
            if (staticControllers)
            {
                foreach (var paramName in controllerParams)
                {
                    indent.AppendLine($"/// <param name=\"{Identifiers.DocCommentName(paramName)}\">Controller party from the Daml <c>controller</c> clause, routed into the submission's <c>actAs</c> set.</param>");
                }
                foreach (var paramName in readAsParams)
                {
                    indent.AppendLine($"/// <param name=\"{Identifiers.DocCommentName(paramName)}\">Observer party from the Daml <c>observer</c> clause, routed into the submission's <c>readAs</c> set.</param>");
                }
            }
            else
            {
                indent.AppendLine("/// <param name=\"submitter\">The submitter party set (<c>actAs</c> + optional <c>readAs</c>).</param>");
            }
            WriteSubmissionParameterDocs(indent);
        }

        var asyncModifier = staticControllers ? string.Empty : "async ";
        indent.AppendLine($"public static {asyncModifier}global::System.Threading.Tasks.Task<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.ExerciseOutcome)}<{returnTypeName}>> Try{choiceName}Async(");
        indent.Indent();
        indent.AppendLine($"this {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ContractId)}<{templateClassName}> contractId,");
        indent.AppendLine($"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.ILedgerWriter)} client,");
        if (hasArg)
        {
            indent.AppendLine($"{argument.ParameterType(templateClassName)} argument,");
        }

        if (staticControllers)
        {
            foreach (var paramName in controllerParams)
            {
                indent.AppendLine($"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.Party)} {paramName},");
            }
            foreach (var paramName in readAsParams)
            {
                indent.AppendLine($"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.Party)} {paramName},");
            }
        }
        else
        {
            indent.AppendLine($"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.SubmitterInfo)} submitter,");
        }
        WriteSubmissionParametersAndCloseSignature(indent);
        indent.Dedent();
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("global::System.ArgumentNullException.ThrowIfNull(client);");

        if (staticControllers)
        {
            indent.AppendLine();
            if (controllerParams.Count == 1 && readAsParams.Count == 0)
            {
                indent.AppendLine($"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.SubmitterInfo)} submitter = {controllerParams[0]};");
            }
            else if (readAsParams.Count == 0)
            {
                indent.Require("System.Collections.Generic");
                indent.AppendLine("// SubmitterInfo's actAs unions every named controller.");
                indent.AppendLine($"var submitter = new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.SubmitterInfo)}(new {TypeReferenceQualifier.Qualify("HashSet")}<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.Party)}> {{ {string.Join(", ", controllerParams)} }});");
            }
            else
            {
                indent.Require("System.Collections.Generic");
                indent.AppendLine("// actAs unions every named controller; readAs unions every observer that is");
                indent.AppendLine("// not also a controller, so the wire format reflects Daml's stakeholder model.");
                indent.AppendLine($"var submitter = new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.SubmitterInfo)}(");
                indent.Indent();
                indent.AppendLine($"actAs: new {TypeReferenceQualifier.Qualify("HashSet")}<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.Party)}> {{ {string.Join(", ", controllerParams)} }},");
                indent.AppendLine($"readAs: new {TypeReferenceQualifier.Qualify("HashSet")}<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.Party)}> {{ {string.Join(", ", readAsParams)} }});");
                indent.Dedent();
            }

            indent.AppendLine();
            indent.AppendLine($"return contractId.Try{choiceName}Async(");
            indent.Indent();
            indent.AppendLine("client,");
            if (hasArg)
            {
                indent.AppendLine("argument,");
            }
            indent.AppendLine("submitter,");
            indent.AppendLine("workflowId,");
            indent.AppendLine("commandId,");
            indent.AppendLine("timeout,");
            indent.AppendLine("configure,");
            indent.AppendLine("cancellationToken);");
            indent.Dedent();
        }
        else
        {
            WriteExerciserCommandDispatchAndProject(indent, choice, templateClassName);
        }

        indent.Dedent();
        indent.AppendLine("}");
    }
}
