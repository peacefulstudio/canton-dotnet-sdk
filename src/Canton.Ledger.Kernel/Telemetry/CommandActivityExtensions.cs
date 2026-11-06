// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Canton.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Outcomes;

namespace Canton.Ledger.Kernel.Telemetry;

internal static class CommandActivityExtensions
{
    private const char ValueSeparator = ',';

    internal static void SetCommandTags(this Activity? activity, CommandsSubmission submission, bool emitIdentifiers)
    {
        if (activity is null) return;

        var described = submission.Commands.Select(Describe).OfType<CommandDescription>().ToList();
        SetJoinedTag(activity, LedgerActivityTagNames.DamlTemplateId, described.Select(command => command.TemplateId));
        SetJoinedTag(activity, LedgerActivityTagNames.DamlChoice, described.Select(command => command.Choice));
        if (emitIdentifiers)
        {
            SetJoinedTag(activity, LedgerActivityTagNames.DamlContractId, described.Select(command => command.ContractId));
        }
    }

    internal static void SetDamlType<T>(this Activity? activity) where T : IDamlType =>
        activity?.SetTag(LedgerActivityTagNames.DamlTemplateId, T.DamlTypeId.Identifier.ToString());

    internal static void SetDamlErrorTags(this Activity? activity, ParsedLedgerError error, bool emitMetadata)
    {
        if (error is ParsedLedgerError.Structured structured)
        {
            activity.SetDamlErrorTags(structured.ErrorId, structured.Category, structured.Metadata, emitMetadata);
        }
    }

    internal static void SetDamlErrorTags(
        this Activity? activity,
        string errorId,
        DamlErrorCategory category,
        IReadOnlyDictionary<string, string> metadata,
        bool emitMetadata)
    {
        if (activity is null) return;

        if (errorId.Length > 0)
        {
            activity.SetTag(LedgerActivityTagNames.DamlErrorId, errorId);
        }

        activity.SetTag(LedgerActivityTagNames.DamlErrorCategory, category.ToString());
        if (!emitMetadata) return;

        foreach (var (key, value) in metadata)
        {
            activity.SetTag(LedgerActivityTagNames.DamlErrorMetadataPrefix + key, value);
        }
    }

    private static CommandDescription? Describe(ICommand command) => command switch
    {
        CreateCommand create => new(create.TemplateId.ToString(), null, null),
        ExerciseCommand exercise => new(exercise.TemplateId.ToString(), exercise.Choice.Value, exercise.ContractId.Value),
        ExerciseByKeyCommand byKey => new(byKey.TemplateId.ToString(), byKey.Choice.Value, null),
        CreateAndExerciseCommand createAndExercise =>
            new(createAndExercise.TemplateId.ToString(), createAndExercise.Choice.Value, null),
        _ => null,
    };

    private static void SetJoinedTag(Activity activity, string tagName, IEnumerable<string?> values)
    {
        var distinct = values.OfType<string>().Distinct().ToList();
        if (distinct.Count > 0)
        {
            activity.SetTag(tagName, string.Join(ValueSeparator, distinct));
        }
    }

    private sealed record CommandDescription(string TemplateId, string? Choice, string? ContractId);
}
