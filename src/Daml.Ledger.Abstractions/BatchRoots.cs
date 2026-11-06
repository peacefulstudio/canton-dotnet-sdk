// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;

namespace Daml.Ledger.Abstractions;

internal sealed class BatchRoots
{
    private readonly List<ExercisedEvent> _exercises;
    private readonly List<CreatedContract> _creates;

    private readonly IReadOnlyList<BatchEntry> _submitted;
    private readonly bool _nestingUnknown;

    internal BatchRoots(TransactionResult transaction, IReadOnlyList<BatchEntry> submitted)
    {
        _submitted = submitted;
        _nestingUnknown = transaction.ExercisedEvents.Count > 0
            && transaction.CreatedContracts.Any(candidate => candidate.NodeId is null);
        var exercised = transaction.ExercisedEvents;
        _exercises = [.. exercised.Where(candidate => !IsNested(exercised, candidate.NodeId))];
        _creates = [.. transaction.CreatedContracts.Where(candidate => !IsNested(exercised, candidate.NodeId))];
    }

    internal ExercisedEvent? ClaimExercise(Func<ExercisedEvent, bool> matches) => Claim(_exercises, matches);

    internal CreatedContract? ClaimCreate(Func<CreatedContract, bool> matches) => Claim(_creates, matches);

    internal bool CreatesAreAmbiguous<TTemplate>(Func<CreatedContract, bool> matches)
        where TTemplate : ITemplate =>
        _nestingUnknown
        && _creates.Count(matches) > _submitted.OfType<CreateEntry<TTemplate>>().Count();

    private static bool IsNested(IEnumerable<ExercisedEvent> exercised, int? nodeId) =>
        exercised.Any(enclosing => ExerciseResultProjector.Encloses(enclosing, nodeId));

    private static T? Claim<T>(List<T> unclaimed, Func<T, bool> matches)
        where T : class
    {
        var position = unclaimed.FindIndex(candidate => matches(candidate));
        if (position < 0)
        {
            return null;
        }

        var claimed = unclaimed[position];
        unclaimed.RemoveAt(position);
        return claimed;
    }
}
