// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Outcomes;

internal static class ExerciseOutcomeCommandIdExtensions
{
    internal static ExerciseOutcome<T> WithCommandId<T>(this ExerciseOutcome<T> outcome, string? commandId) =>
        commandId is null || outcome.CommandId == commandId ? outcome : outcome with { CommandId = commandId };
}
