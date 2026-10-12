// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// The generated static class a choice's exercisers land in. The return flavour of a template
/// choice survives only as this placement: a choice whose result names contract ids lands in
/// <c>{Template}Extensions</c>, every other template choice in
/// <c>{Template}NonContractExtensions</c>.
/// </summary>
internal enum ExerciserClass
{
    TemplateExtensions,
    TemplateNonContractExtensions,
    InterfaceExtensions,
}

/// <summary>What an exerciser extends: a bare contract id, or a fetched contract carrying its payload.</summary>
internal enum ExerciseReceiver
{
    ContractId,
    FetchedContract,
}

/// <summary>How an exerciser obtains the submitter of the command it submits.</summary>
internal abstract record ExerciseSubmitter;

/// <summary>The caller passes a <c>SubmitterInfo</c> carrying <c>actAs</c> and <c>readAs</c>.</summary>
internal sealed record ExplicitSubmitter : ExerciseSubmitter;

/// <summary>
/// The caller passes one <c>Party</c> parameter per controller, then one per observer that is
/// not a controller; the controllers feed <c>actAs</c> and the observers <c>readAs</c>.
/// </summary>
/// <param name="ControllerParameters">Controller parameter names in declaration order.</param>
/// <param name="ObserverParameters">Observer-only parameter names in declaration order.</param>
internal sealed record NamedControllersSubmitter(
    IReadOnlyList<string> ControllerParameters,
    IReadOnlyList<string> ObserverParameters) : ExerciseSubmitter;

/// <summary>
/// The exerciser reads every controller and observer party off the fetched contract's payload,
/// so the caller passes none.
/// </summary>
/// <param name="ControllerFields">Daml payload-field names of the controllers, in declaration order.</param>
/// <param name="ObserverFields">Daml payload-field names of the observer-only parties, in declaration order.</param>
internal sealed record PayloadPartiesSubmitter(
    IReadOnlyList<string> ControllerFields,
    IReadOnlyList<string> ObserverFields) : ExerciseSubmitter;

/// <summary>One <c>Try&lt;Choice&gt;Async</c> overload: its receiver and where its submitter comes from.</summary>
internal sealed record ExerciseOverload(ExerciseReceiver Receiver, ExerciseSubmitter Submitter)
{
    /// <summary>
    /// True when this overload builds the command and submits it itself; every other overload
    /// forwards to the one that does.
    /// </summary>
    public bool SubmitsDirectly =>
        Receiver == ExerciseReceiver.ContractId && Submitter is ExplicitSubmitter;
}

/// <summary>
/// The decision, taken once per choice, of which <c>Try&lt;Choice&gt;Async</c> overloads the
/// choice gets and in which generated class they live. Pure data: the writers render it, and
/// no writer decides overloads on its own.
/// </summary>
/// <param name="Choice">The planned choice.</param>
/// <param name="Class">The generated class the overloads are placed in.</param>
/// <param name="Overloads">The overloads in emission order.</param>
internal sealed record ChoiceExercisePlan(
    DamlChoice Choice,
    ExerciserClass Class,
    IReadOnlyList<ExerciseOverload> Overloads)
{
    /// <summary>True when one overload takes the controllers as named <c>Party</c> parameters.</summary>
    public bool OffersNamedControllers =>
        Overloads.Any(overload => overload.Submitter is NamedControllersSubmitter);
}

/// <summary>
/// Plans the exerciser overloads of every choice from <see cref="PartyAnalysis"/>. Needs no
/// emitter state, so a plan is asserted without rendering any text.
/// </summary>
internal sealed class ChoiceExercisePlanner(PartyAnalysis party)
{
    private static readonly ExerciseOverload ExplicitOnContractId =
        new(ExerciseReceiver.ContractId, new ExplicitSubmitter());

    private static readonly ExerciseOverload ExplicitOnFetchedContract =
        new(ExerciseReceiver.FetchedContract, new ExplicitSubmitter());

    /// <summary>Plans every choice of <paramref name="template"/>, in declaration order.</summary>
    /// <param name="template">The template whose choices are planned.</param>
    /// <param name="fields">The template payload fields the party clauses are validated against.</param>
    public IReadOnlyList<ChoiceExercisePlan> PlanTemplate(
        DamlTemplate template,
        IReadOnlyList<DamlFieldDefinition> fields)
    {
        var partyFields = fields
            .Where(f => f.Type is DamlPrimitiveType { Primitive: DamlPrimitive.Party })
            .ToDictionary(f => f.Name, f => f, StringComparer.Ordinal);
        var templateObservers = party.ValidatePayloadParties(template.Observers, partyFields);

        return [.. template.Choices.Select(choice => PlanTemplateChoice(choice, templateObservers, partyFields))];
    }

    /// <summary>
    /// Plans the choices of <paramref name="template"/> that do not return contract ids. Their
    /// plan never reads the payload fields or <see cref="PartyAnalysis"/>, so no fields are needed.
    /// </summary>
    /// <param name="template">The template whose value-returning choices are planned.</param>
    public static IReadOnlyList<ChoiceExercisePlan> PlanNonContractChoices(DamlTemplate template) =>
        [.. template.Choices
            .Where(choice => !ChoiceEmitter.ReturnsContractIds(choice.ReturnType))
            .Select(PlanNonContractChoice)];

    /// <summary>Plans the single exerciser an interface choice gets.</summary>
    /// <param name="choice">The interface choice.</param>
    public static ChoiceExercisePlan PlanInterfaceChoice(DamlChoice choice) =>
        new(choice, ExerciserClass.InterfaceExtensions, [ExplicitOnContractId]);

    private ChoiceExercisePlan PlanTemplateChoice(
        DamlChoice choice,
        DamlPartyAnalysis templateObservers,
        IReadOnlyDictionary<string, DamlFieldDefinition> partyFields)
    {
        if (!ChoiceEmitter.ReturnsContractIds(choice.ReturnType))
        {
            return PlanNonContractChoice(choice);
        }

        var controllers = party.ValidatePayloadParties(choice.Controllers, partyFields);
        var choiceObservers = party.ValidatePayloadParties(choice.Observers, partyFields);
        var observers = party.UnionStaticParties(templateObservers, choiceObservers);

        return new ChoiceExercisePlan(
            choice,
            ExerciserClass.TemplateExtensions,
            NamesStaticControllers(controllers) ? StaticControllerOverloads(controllers, observers) : [ExplicitOnContractId]);
    }

    private static ChoiceExercisePlan PlanNonContractChoice(DamlChoice choice) =>
        new(choice, ExerciserClass.TemplateNonContractExtensions, [ExplicitOnContractId]);

    private static bool NamesStaticControllers(DamlPartyAnalysis controllers) =>
        controllers.Source == DamlPartySource.Static && controllers.Parties.Count > 0;

    private ExerciseOverload[] StaticControllerOverloads(DamlPartyAnalysis controllers, DamlPartyAnalysis observers)
    {
        var (controllerParameters, observerParameters) = party.PartitionControllersAndObservers(controllers, observers);
        var (controllerFields, observerFields) = party.PartitionControllerAndObserverFieldNames(controllers, observers);

        return
        [
            new ExerciseOverload(
                ExerciseReceiver.ContractId,
                new NamedControllersSubmitter(controllerParameters, observerParameters)),
            ExplicitOnContractId,
            new ExerciseOverload(
                ExerciseReceiver.FetchedContract,
                new PayloadPartiesSubmitter(controllerFields, observerFields)),
            ExplicitOnFetchedContract,
        ];
    }
}

/// <summary>
/// The one spelling of every member name an exercised choice contributes to the generated code,
/// shared by the writer that declares a member and every writer that refers to it.
/// </summary>
internal static class ExerciseMemberNames
{
    /// <summary>The <c>Try&lt;Choice&gt;Async</c> exerciser.</summary>
    public static string Exerciser(DamlChoice choice) => $"Try{Identifiers.Sanitize(choice.Name)}Async";

    /// <summary>The <c>&lt;Choice&gt;Command</c> builder.</summary>
    public static string Command(DamlChoice choice) => $"{Identifiers.Sanitize(choice.Name)}Command";

    /// <summary>The <c>&lt;Choice&gt;ByKeyCommand</c> builder.</summary>
    public static string ByKeyCommand(DamlChoice choice) => $"{Identifiers.Sanitize(choice.Name)}ByKeyCommand";

    /// <summary>The private <c>Project&lt;Choice&gt;Result</c> hand-off.</summary>
    public static string Projector(DamlChoice choice) => $"Project{Identifiers.Sanitize(choice.Name)}Result";
}
