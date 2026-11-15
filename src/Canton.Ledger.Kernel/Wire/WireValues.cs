// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;

namespace Canton.Ledger.Kernel.Wire;

internal static class WireValues
{
    internal static ContractId<T> ContractId<T>(string? wireContractId)
        where T : IDamlType =>
        new(MalformedResponse.RequireContractId(wireContractId, "the event"));

    internal static long ReassignmentCounter(ulong wireCounter) =>
        wireCounter <= long.MaxValue
            ? (long)wireCounter
            : throw MalformedResponse.WithDetail(
                $"reassignment counter {wireCounter} exceeds the signed 64-bit range.");
}
