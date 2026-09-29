// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

internal sealed partial class ChoiceEmitter
{
    /// <summary>
    /// Emits the <c>&lt;Choice&gt;Result</c> record (typed result struct) and a static
    /// <c>FromCreatedContracts(...)</c> projector for every choice on
    /// <paramref name="template"/> whose return type carries one or more
    /// <c>ContractId T</c>s. See <see cref="ChoiceCreatedSlots.Extract"/>. The record is a
    /// top-level type written beside the template in the template's module namespace, after
    /// the template record has been closed — not nested inside it — so it is reached as
    /// <c>RetagResult</c>, not <c>Offer.RetagResult</c>. Two modules declaring a same-named
    /// choice therefore emit two result records in two namespaces, never a collision.
    /// </summary>
    /// <param name="indent">Writer positioned at the emission point in the template's file.</param>
    /// <param name="template">The template whose choices are scanned for created-contract slots.</param>
    internal void WriteChoiceResultStructs(IndentWriter indent, DamlTemplate template)
    {
        foreach (var choice in template.Choices)
        {
            var slots = ChoiceCreatedSlots.Extract(context, resolver, mapper, choice.ReturnType);
            if (slots.Count == 0)
            {
                continue;
            }

            WriteSingleChoiceResultStruct(indent, choice, slots);
        }
    }

    private void WriteSingleChoiceResultStruct(
        IndentWriter indent,
        DamlChoice choice,
        IReadOnlyList<ChoiceCreatedSlot> slots)
    {
        EmittedUsings.RequireAsyncExerciserNamespaces(indent);
        indent.Require("System.Collections.Generic");

        var choiceName = SanitizeIdentifier(choice.Name);
        var resultName = $"{choiceName}Result";

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Typed projection of the contracts created by the {choice.Name} choice.");
            indent.AppendLine("/// One field per template the choice creates; cardinality follows the choice's");
            indent.AppendLine("/// return type (single, optional, list).");
            indent.AppendLine("/// </summary>");
        }

        indent.AppendLine($"public sealed record {resultName}(");
        indent.Indent();
        for (var i = 0; i < slots.Count; i++)
        {
            var separator = i == slots.Count - 1 ? "" : ",";
            indent.AppendLine($"{SlotPropertyType(slots[i])} {slots[i].FieldName}{separator}");
        }
        indent.Dedent();
        indent.AppendLine(")");
        indent.AppendLine("{");
        indent.Indent();

        new CollectionValueSemanticsEmitter(context, options).Write(
            indent,
            resultName,
            [.. slots.Select(SlotValueMember)],
            derivesFromRecord: false);
        WriteFromCreatedContractsProjector(indent, resultName, slots);

        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();
    }

    private ValueMember SlotValueMember(ChoiceCreatedSlot slot) => new(
        slot.FieldName,
        SlotPropertyType(slot),
        slot.Cardinality == CreatedCardinality.List ? CollectionShape.List : CollectionShape.None,
        $"The <c>{EmitterHelpers.EscapeXmlText(slot.CSharpTemplateType)}</c> contracts this slot of the choice's return type carries.",
        DamlFieldName: null);

    private string SlotPropertyType(ChoiceCreatedSlot slot) => slot.Cardinality switch
    {
        CreatedCardinality.Single => $"{context.Qualifier.Qualify(RuntimeTypeNames.ContractId)}<{slot.CSharpTemplateType}>",
        CreatedCardinality.Optional => $"{context.Qualifier.Qualify(RuntimeTypeNames.ContractId)}<{slot.CSharpTemplateType}>?",
        CreatedCardinality.List => $"{context.Qualifier.Qualify("IReadOnlyList")}<{context.Qualifier.Qualify(RuntimeTypeNames.ContractId)}<{slot.CSharpTemplateType}>>",
        _ => $"{context.Qualifier.Qualify(RuntimeTypeNames.ContractId)}<{slot.CSharpTemplateType}>",
    };

    /// <remarks>
    /// Slots that name the same template share a single bucket of created contracts, which is
    /// then distributed across them in declaration order. A per-slot <c>if</c>/<c>else if</c>
    /// chain cannot express that: the second slot's branch is unreachable, so a return type
    /// such as <c>(ContractId Half, ContractId Half)</c> would leave its bucket empty and
    /// project to <c>None</c>.
    /// <para>
    /// Buckets are keyed on the qualified template reference <em>and</em> the interface matcher,
    /// never the reference alone: a template and an interface marker can generate the same C#
    /// name (template <c>IFactory</c> against the marker for interface <c>Factory</c>), and
    /// merging them would match one slot's contracts on the other slot's branch.
    /// </para>
    /// <para>
    /// Distribution gives each single- and optional-cardinality slot one match; list slots drain
    /// whatever remains. Contracts left over after distribution are appended to the group's last
    /// slot so the per-slot cardinality validator counts the full population — for a single or
    /// optional slot that trips its <c>Many</c> branch, which is the intended outcome when the
    /// ledger created more contracts than the consumer asked for.
    /// </para>
    /// </remarks>
    private void WriteFromCreatedContractsProjector(
        IndentWriter indent,
        string resultName,
        IReadOnlyList<ChoiceCreatedSlot> slots)
    {
        string Q(string templateName) => context.QualifyInModule(templateName);

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Projects an upstream transaction's created contracts to a typed <see cref=\"{resultName}\"/>.");
            indent.AppendLine("/// Returns:");
            indent.AppendLine("/// <list type=\"bullet\">");
            indent.AppendLine("///   <item><see cref=\"ExerciseOutcome{T}.One\"/> when every expected template's cardinality matches.</item>");
            indent.AppendLine("///   <item><see cref=\"ExerciseOutcome{T}.None\"/> when at least one required slot's template is missing from the transaction.</item>");
            indent.AppendLine("///   <item><see cref=\"ExerciseOutcome{T}.Many\"/> when a single-cardinality slot has more than one created contract of its template, or an optional-cardinality slot has more than one.</item>");
            indent.AppendLine("/// </list>");
            indent.AppendLine("/// Cardinality is matched by template ID's <c>(module, entity)</c> pair only — package upgrades that share the same logical template name match cleanly.");
            indent.AppendLine("/// </summary>");
        }

        indent.AppendLine($"public static {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome)}<{resultName}> FromCreatedContracts(IEnumerable<{context.Qualifier.Qualify(RuntimeTypeNames.CreatedContract)}> created)");
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("ArgumentNullException.ThrowIfNull(created);");

        var templateGroups = new List<(string TemplateRef, InterfaceMatcher? Interface, List<int> SlotIndexes)>();
        for (var i = 0; i < slots.Count; i++)
        {
            var templateRef = Q(slots[i].CSharpTemplateType);
            var slotInterface = slots[i].Interface;
            var groupIndex = -1;
            for (var g = 0; g < templateGroups.Count; g++)
            {
                if (string.Equals(templateGroups[g].TemplateRef, templateRef, StringComparison.Ordinal)
                    && templateGroups[g].Interface == slotInterface)
                {
                    groupIndex = g;
                    break;
                }
            }

            if (groupIndex < 0)
            {
                templateGroups.Add((templateRef, slots[i].Interface, new List<int> { i }));
            }
            else
            {
                templateGroups[groupIndex].SlotIndexes.Add(i);
            }
        }

        for (var g = 0; g < templateGroups.Count; g++)
        {
            indent.AppendLine($"var templateMatches{g} = new List<string>();");
        }

        indent.AppendLine("foreach (var item in created)");
        indent.AppendLine("{");
        indent.Indent();
        for (var g = 0; g < templateGroups.Count; g++)
        {
            var prefix = g == 0 ? "if" : "else if";
            var group = templateGroups[g];
            if (group.Interface is not null)
            {
                indent.Require("System.Linq");
                indent.AppendLine($"{prefix} (item.InterfaceIds.Any(interfaceId =>");
                indent.Indent();
                indent.AppendLine($"string.Equals(interfaceId.ModuleName, {group.TemplateRef}.InterfaceId.ModuleName, StringComparison.Ordinal)");
                indent.AppendLine($"&& string.Equals(interfaceId.EntityName, {group.TemplateRef}.InterfaceId.EntityName, StringComparison.Ordinal)))");
                indent.Dedent();
            }
            else
            {
                indent.AppendLine($"{prefix} (string.Equals(item.TemplateId.ModuleName, {group.TemplateRef}.TemplateId.ModuleName, StringComparison.Ordinal)");
                indent.Indent();
                indent.AppendLine($"&& string.Equals(item.TemplateId.EntityName, {group.TemplateRef}.TemplateId.EntityName, StringComparison.Ordinal))");
                indent.Dedent();
            }
            indent.AppendLine("{");
            indent.Indent();
            indent.AppendLine($"templateMatches{g}.Add(item.ContractId);");
            indent.Dedent();
            indent.AppendLine("}");
        }
        indent.Dedent();
        indent.AppendLine("}");

        for (var i = 0; i < slots.Count; i++)
        {
            indent.AppendLine($"var matches{i} = new List<string>();");
        }
        for (var g = 0; g < templateGroups.Count; g++)
        {
            var slotIndexes = templateGroups[g].SlotIndexes;
            indent.AppendLine($"var templateMatchIndex{g} = 0;");
            for (var k = 0; k < slotIndexes.Count; k++)
            {
                var slotIndex = slotIndexes[k];
                var slot = slots[slotIndex];
                switch (slot.Cardinality)
                {
                    case CreatedCardinality.Single:
                    case CreatedCardinality.Optional:
                        indent.AppendLine($"if (templateMatchIndex{g} < templateMatches{g}.Count)");
                        indent.AppendLine("{");
                        indent.Indent();
                        indent.AppendLine($"matches{slotIndex}.Add(templateMatches{g}[templateMatchIndex{g}]);");
                        indent.AppendLine($"templateMatchIndex{g}++;");
                        indent.Dedent();
                        indent.AppendLine("}");
                        break;

                    case CreatedCardinality.List:
                        indent.AppendLine($"while (templateMatchIndex{g} < templateMatches{g}.Count)");
                        indent.AppendLine("{");
                        indent.Indent();
                        indent.AppendLine($"matches{slotIndex}.Add(templateMatches{g}[templateMatchIndex{g}]);");
                        indent.AppendLine($"templateMatchIndex{g}++;");
                        indent.Dedent();
                        indent.AppendLine("}");
                        break;
                }
            }

            var lastSlotIndex = slotIndexes[^1];
            indent.AppendLine($"while (templateMatchIndex{g} < templateMatches{g}.Count)");
            indent.AppendLine("{");
            indent.Indent();
            indent.AppendLine($"matches{lastSlotIndex}.Add(templateMatches{g}[templateMatchIndex{g}]);");
            indent.AppendLine($"templateMatchIndex{g}++;");
            indent.Dedent();
            indent.AppendLine("}");
        }

        WriteSlotCardinalityProjection(indent, resultName, slots);

        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();
    }

    /// <summary>
    /// Emits the tail both result projectors share: given one <c>matches{i}</c> list of raw
    /// contract ids per slot, already declared and filled, validates each slot's cardinality
    /// and returns the typed <c>ExerciseOutcome</c>.
    /// </summary>
    private void WriteSlotCardinalityProjection(
        IndentWriter indent,
        string resultName,
        IReadOnlyList<ChoiceCreatedSlot> slots)
    {
        string Q(string templateName) => context.QualifyInModule(templateName);

        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var local = $"matches{i}";
            indent.AppendLine();
            switch (slot.Cardinality)
            {
                case CreatedCardinality.Single:
                    indent.AppendLine($"if ({local}.Count == 0)");
                    indent.AppendLine("{");
                    indent.Indent();
                    indent.AppendLine($"return new {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome)}<{resultName}>.None();");
                    indent.Dedent();
                    indent.AppendLine("}");
                    indent.AppendLine($"if ({local}.Count > 1)");
                    indent.AppendLine("{");
                    indent.Indent();
                    indent.AppendLine($"return new {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome)}<{resultName}>.Many({context.Qualifier.Qualify(RuntimeTypeNames.EquatableArray)}.Create({local}));");
                    indent.Dedent();
                    indent.AppendLine("}");
                    break;
                case CreatedCardinality.Optional:
                    indent.AppendLine($"if ({local}.Count > 1)");
                    indent.AppendLine("{");
                    indent.Indent();
                    indent.AppendLine($"return new {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome)}<{resultName}>.Many({context.Qualifier.Qualify(RuntimeTypeNames.EquatableArray)}.Create({local}));");
                    indent.Dedent();
                    indent.AppendLine("}");
                    break;
                case CreatedCardinality.List:
                    break;
            }
        }

        indent.AppendLine();
        indent.AppendLine($"return new {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome)}<{resultName}>.One(new {resultName}(");
        indent.Indent();
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var local = $"matches{i}";
            var separator = i == slots.Count - 1 ? "" : ",";
            var templateRef = Q(slot.CSharpTemplateType);
            switch (slot.Cardinality)
            {
                case CreatedCardinality.Single:
                    indent.AppendLine($"{slot.FieldName}: new {context.Qualifier.Qualify(RuntimeTypeNames.ContractId)}<{templateRef}>({local}[0]){separator}");
                    break;
                case CreatedCardinality.Optional:
                    indent.AppendLine($"{slot.FieldName}: {local}.Count == 1 ? new {context.Qualifier.Qualify(RuntimeTypeNames.ContractId)}<{templateRef}>({local}[0]) : null{separator}");
                    break;
                case CreatedCardinality.List:
                    indent.AppendLine($"{slot.FieldName}: {local}.ConvertAll(c => new {context.Qualifier.Qualify(RuntimeTypeNames.ContractId)}<{templateRef}>(c)){separator}");
                    break;
            }
        }
        indent.Dedent();
        indent.AppendLine("));");
    }
}
