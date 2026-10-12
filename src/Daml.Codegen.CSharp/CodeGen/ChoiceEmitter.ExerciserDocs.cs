// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

internal sealed partial class ChoiceEmitter
{
    private static ChoiceSubmitterParameter ExplicitSubmitterParameter(ExerciserClass exerciserClass) =>
        exerciserClass == ExerciserClass.TemplateExtensions
            ? new ChoiceSubmitterParameter(
                TypeReferenceQualifier.Qualify(RuntimeTypeNames.SubmitterInfo),
                "submitter",
                "The submitter party set (<c>actAs</c> + optional <c>readAs</c>).")
            : SubmitterInfoParameter();

    private static void WriteExerciserDocs(IndentWriter indent, ExerciserSite site, ExerciseOverload overload)
    {
        indent.AppendLine("/// <summary>");
        foreach (var summaryLine in ExerciserSummaryLines(site, overload))
        {
            indent.AppendLine(summaryLine);
        }
        indent.AppendLine("/// </summary>");
        indent.AppendLine(ReceiverParameterDoc(site, overload.Receiver));
        indent.AppendLine("/// <param name=\"client\">The ledger client.</param>");
        if (site.HasArgument)
        {
            indent.AppendLine("/// <param name=\"argument\">The choice argument.</param>");
        }
        WriteSubmitterParameterDocs(indent, site, overload.Submitter);
        WriteSubmissionParameterDocs(indent);
    }

    private static string ReceiverParameterDoc(ExerciserSite site, ExerciseReceiver receiver) =>
        (receiver, site.Plan.Class) switch
        {
            (ExerciseReceiver.FetchedContract, _) =>
                "/// <param name=\"contract\">The fetched contract on which to exercise the choice.</param>",
            (_, ExerciserClass.InterfaceExtensions) =>
                "/// <param name=\"contractId\">The interface-typed contract id to exercise on.</param>",
            _ => "/// <param name=\"contractId\">The contract on which to exercise the choice.</param>",
        };

    private static void WriteSubmitterParameterDocs(IndentWriter indent, ExerciserSite site, ExerciseSubmitter submitter)
    {
        switch (submitter)
        {
            case NamedControllersSubmitter named:
                foreach (var parameterName in named.ControllerParameters)
                {
                    indent.AppendLine($"/// <param name=\"{Identifiers.DocCommentName(parameterName)}\">Controller party from the Daml <c>controller</c> clause, routed into the submission's <c>actAs</c> set.</param>");
                }
                foreach (var parameterName in named.ObserverParameters)
                {
                    indent.AppendLine($"/// <param name=\"{Identifiers.DocCommentName(parameterName)}\">Observer party from the Daml <c>observer</c> clause, routed into the submission's <c>readAs</c> set.</param>");
                }
                break;
            case ExplicitSubmitter:
                var explicitSubmitter = ExplicitSubmitterParameter(site.Plan.Class);
                indent.AppendLine($"/// <param name=\"{explicitSubmitter.Name}\">{explicitSubmitter.DocSummary}</param>");
                break;
        }
    }

    private static IReadOnlyList<string> ExerciserSummaryLines(ExerciserSite site, ExerciseOverload overload) =>
        (site.Plan.Class, overload.Receiver, overload.Submitter) switch
        {
            (ExerciserClass.InterfaceExtensions, _, _) => InterfaceSummaryLines(site.Choice),
            (ExerciserClass.TemplateNonContractExtensions, _, _) => NonContractSummaryLines(site),
            (_, ExerciseReceiver.FetchedContract, PayloadPartiesSubmitter) => PayloadPartiesContractSummaryLines(site),
            (_, ExerciseReceiver.FetchedContract, _) => ExplicitContractSummaryLines(site),
            (_, _, NamedControllersSubmitter named) => NamedControllersSummaryLines(site, named),
            _ when site.Plan.OffersNamedControllers => CompanionSubmitterInfoSummaryLines(site),
            _ => SoleSubmitterInfoSummaryLines(site),
        };

    private static string LiftedResultSummary(ExerciserSite site) =>
        $"/// Exercises the {site.Choice.Name} choice and lifts the choice's exercise result to <see cref=\"ExerciseOutcome{{T}}\"/> over <c>{EmitterHelpers.EscapeXmlText(site.ReturnTypeName)}</c>.";

    private static IReadOnlyList<string> NamedControllersSummaryLines(ExerciserSite site, NamedControllersSubmitter named) =>
        named.ObserverParameters.Count > 0
            ?
            [
                LiftedResultSummary(site),
                "/// One <c>Party</c> parameter is emitted per Daml controller (declaration order),",
                "/// followed by one parameter per Daml observer that is not also a controller.",
                "/// The wrapper builds a <see cref=\"SubmitterInfo\"/> with controllers in <c>actAs</c>",
                "/// and observers in <c>readAs</c>, so the wire format reflects Daml's stakeholder",
                "/// model exactly.",
            ]
            :
            [
                LiftedResultSummary(site),
                "/// One <c>Party</c> parameter is emitted per Daml controller (declaration order).",
                "/// The wrapper builds a <see cref=\"SubmitterInfo\"/> from those parties before",
                "/// dispatching to <c>ILedgerWriter</c>.",
            ];

    private static IReadOnlyList<string> SoleSubmitterInfoSummaryLines(ExerciserSite site) =>
    [
        LiftedResultSummary(site),
        "/// The submitter is passed explicitly via <paramref name=\"submitter\"/> — the static",
        "/// analyzer could not resolve the Daml <c>controller</c> clause to payload-field",
        "/// references. <see cref=\"SubmitterInfo\"/> implicitly converts from a",
        "/// single <c>Party</c>, so the single-party call site stays a one-liner.",
    ];

    private static IReadOnlyList<string> CompanionSubmitterInfoSummaryLines(ExerciserSite site) =>
    [
        $"/// Exercises the {site.Choice.Name} choice with an explicit <see cref=\"SubmitterInfo\"/> and lifts the choice's exercise result to <see cref=\"ExerciseOutcome{{T}}\"/> over <c>{EmitterHelpers.EscapeXmlText(site.ReturnTypeName)}</c>.",
        "/// Companion to the named-<c>Party</c> overload for the case where the submitter must",
        "/// read contracts it does not act as — the choice's created contracts are visible to an",
        "/// observer but not to the submitter, so the caller supplies the <c>readAs</c> parties.",
    ];

    private static IReadOnlyList<string> PayloadPartiesContractSummaryLines(ExerciserSite site) =>
    [
        $"/// Exercises the {site.Choice.Name} choice on a fetched <see cref=\"{site.OwnerTypeName}\"/> contract,",
        "/// reading every controller and observer party off the contract payload so the",
        "/// caller passes no parties. Delegates to the",
        $"/// <c>ContractId&lt;{site.OwnerTypeName}&gt;</c> overload.",
    ];

    private static IReadOnlyList<string> ExplicitContractSummaryLines(ExerciserSite site) =>
    [
        $"/// Exercises the {site.Choice.Name} choice on a fetched <see cref=\"{site.OwnerTypeName}\"/> contract with an",
        "/// explicit <see cref=\"SubmitterInfo\"/>. Companion to the payload-derived overload for",
        "/// multi-party submissions and for callers who must supply <c>readAs</c> parties the",
        "/// payload cannot name. Delegates to the",
        $"/// <c>ContractId&lt;{site.OwnerTypeName}&gt;</c> overload.",
    ];

    private static IReadOnlyList<string> NonContractSummaryLines(ExerciserSite site) =>
    [
        $"/// Exercises the {site.Choice.Name} choice and lifts the choice's exercise result to",
        $"/// <see cref=\"ExerciseOutcome{{T}}\"/> over <c>{EmitterHelpers.EscapeXmlText(site.ReturnTypeName)}</c>. Structured Canton/Daml errors",
        "/// and infrastructure/transport errors pass through unchanged.",
    ];

    private static IReadOnlyList<string> InterfaceSummaryLines(DamlChoice choice) =>
    [
        $"/// Exercises the <c>{choice.Name}</c> interface choice on this contract id, submitting the",
        "/// resulting <see cref=\"global::Daml.Runtime.Commands.ExerciseCommand\"/> through",
        "/// <see cref=\"global::Daml.Ledger.Abstractions.Extensions.SingleCommandExtensions.TrySubmitSingleAsync\"/>",
        $"/// and decoding the committed result through <c>Choice{Identifiers.Sanitize(choice.Name)}.ResultDecoder</c>.",
    ];
}
