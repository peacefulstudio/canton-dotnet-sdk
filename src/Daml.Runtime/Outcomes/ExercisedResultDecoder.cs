// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Outcomes;

internal static class ExercisedResultDecoder
{
    internal static TResult Decode<TResult>(ExercisedEvent exercised, GeneratedTypeRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(exercised);

        if (typeof(DamlValue).IsAssignableFrom(typeof(TResult)))
        {
            return exercised.ExerciseResult.FromDamlValue<TResult>()!;
        }

        var unresolved = new List<string>();
        foreach (var owner in OwnersOf(exercised))
        {
            switch ((registry ?? GeneratedTypeReaders.Shared).FindChoice(owner, exercised.ChoiceName))
            {
                case RegistryLookup<IChoice>.Resolved resolved when typeof(TResult).IsAssignableFrom(resolved.Value.ResultType):
                    return (TResult)DecodeResult(resolved.Value, exercised)!;
                case RegistryLookup<IChoice>.Resolved resolved:
                    unresolved.Add(
                        $"its generated result type {resolved.Value.ResultType} is not assignable to {typeof(TResult)}");
                    break;
                case RegistryLookup<IChoice>.Ambiguous ambiguous:
                    unresolved.Add(
                        $"its generated binding is ambiguous between {string.Join(", ", ambiguous.Candidates)}");
                    break;
            }
        }

        return DecodeThroughFromDamlValue<TResult>(exercised, unresolved.FirstOrDefault()
            ?? "no generated binding for it is registered");
    }

    private static object? DecodeResult(IChoice choice, ExercisedEvent exercised) =>
        exercised.ExerciseResult is DamlUndecodedJson undecoded
            ? UndecodedJsonReader.ReadWith(undecoded, choice.ResultType.Name, choice.DecodeResultJson)
            : choice.DecodeResult(exercised.ExerciseResult);

    private static TResult DecodeThroughFromDamlValue<TResult>(ExercisedEvent exercised, string whyUnresolved)
    {
        try
        {
            return exercised.ExerciseResult.FromDamlValue<TResult>()!;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var owner = exercised.InterfaceId ?? exercised.TemplateId;
            throw new NotSupportedException(
                $"Choice '{exercised.ChoiceName.Value}' of '{owner.PackageId}:{owner.ModuleName}:{owner.EntityName}' "
                + $"did not resolve to a generated result decoder: {whyUnresolved}. "
                + $"Decoding its result as {typeof(TResult)} through FromDamlValue failed: {ex.Message}",
                ex);
        }
    }

    private static IEnumerable<Identifier> OwnersOf(ExercisedEvent exercised)
    {
        if (exercised.InterfaceId is { } interfaceId)
        {
            yield return interfaceId;
        }

        yield return exercised.TemplateId;
    }
}
