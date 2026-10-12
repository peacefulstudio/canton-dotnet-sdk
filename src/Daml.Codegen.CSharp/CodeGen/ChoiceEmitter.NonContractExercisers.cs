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

        var emittable = ChoiceExercisePlanner.PlanNonContractChoices(template);

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
            WriteChoiceCommandBuilder(indent, emittable[i].Choice, className);
            indent.AppendLine();
            WriteExerciserOverloads(indent, TemplateExerciserSite(indent, emittable[i], className));
        }

        foreach (var plan in emittable)
        {
            indent.AppendLine();
            WriteExerciseProjector(indent, plan.Choice, className);
        }

        indent.Dedent();
        indent.AppendLine("}");

        return true;
    }
}
