// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Canton.Ledger.Kernel.Results;

internal static class AcsDeltaView
{
    internal static TransactionResult Of(
        TransactionResult ledgerEffects,
        IReadOnlySet<string> createdInAcsDelta,
        IReadOnlySet<string> exercisedInAcsDelta)
    {
        ArgumentNullException.ThrowIfNull(ledgerEffects);
        ArgumentNullException.ThrowIfNull(createdInAcsDelta);
        ArgumentNullException.ThrowIfNull(exercisedInAcsDelta);

        var acsDeltaCreates = ledgerEffects.CreatedContracts
            .Where(created => createdInAcsDelta.Contains(created.ContractId));
        var acsDeltaArchives = ledgerEffects.ExercisedEvents
            .Where(exercised => exercised.Consuming && exercisedInAcsDelta.Contains(exercised.ContractId))
            .Select(exercised => exercised.ContractId)
            .Distinct(StringComparer.Ordinal);

        return ledgerEffects with
        {
            CreatedContracts = EquatableArray.Create(acsDeltaCreates.ToList()),
            ArchivedContractIds = EquatableArray.Create(acsDeltaArchives.ToList()),
        };
    }
}
