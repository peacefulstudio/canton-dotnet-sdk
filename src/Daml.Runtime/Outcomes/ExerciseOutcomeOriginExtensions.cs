// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Outcomes;

internal static class ExerciseOutcomeOriginExtensions
{
    internal static ExerciseOutcome<T> WithCommandId<T>(this ExerciseOutcome<T> outcome, string? commandId) =>
        commandId is null || outcome.CommandId == commandId ? outcome : outcome with { CommandId = commandId };

    internal static ExerciseOutcome<T> WithOperation<T>(this ExerciseOutcome<T> outcome, LedgerOperation? operation) =>
        operation is null || outcome.Operation == operation ? outcome : outcome with { Operation = operation };

    internal static ExerciseOutcome<T> WithOriginOf<T, TSource>(this ExerciseOutcome<T> outcome, ExerciseOutcome<TSource> source) =>
        outcome.WithCommandId(source.CommandId).WithOperation(source.Operation);
}
