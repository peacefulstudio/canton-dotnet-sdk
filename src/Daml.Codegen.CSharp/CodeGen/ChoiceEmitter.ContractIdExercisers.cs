// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

internal sealed partial class ChoiceEmitter
{
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
    /// Which overloads each choice gets is decided by <see cref="ChoiceExercisePlanner"/> from
    /// <see cref="PartyAnalysis"/>:
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
        var plans = planner.PlanTemplate(template, fields)
            .Where(plan => plan.Class == ExerciserClass.TemplateExtensions)
            .ToList();
        if (plans.Count == 0)
        {
            return;
        }

        EmittedUsings.RequireAsyncExerciserNamespaces(indent);

        var templateClassName = context.QualifyInModule(emittedTemplateName);
        var reservedFieldNames = context.TemplateFieldReservedNames(template);
        string PayloadMember(string fieldName) => MemberName(fieldName, emittedTemplateName, reservedFieldNames);

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

        for (var i = 0; i < plans.Count; i++)
        {
            if (i > 0)
            {
                indent.AppendLine();
            }
            var choice = plans[i].Choice;
            WriteChoiceCommandBuilder(indent, choice, templateClassName);
            indent.AppendLine();
            WriteExerciserOverloads(indent, TemplateExerciserSite(indent, plans[i], templateClassName, PayloadMember));
            indent.AppendLine();
            WriteExerciseProjector(indent, choice, templateClassName);
        }

        indent.Dedent();
        indent.AppendLine("}");
    }
}
