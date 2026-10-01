// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// A contract's creation as served by <c>ICantonLedgerClient.GetContractAsync</c>, decoded into its
/// template payload. It carries no offset: <c>ContractService.GetContract</c> never populates the
/// created event's offset, so a value read from it could only replay a resumed stream from ledger begin.
/// </summary>
/// <param name="ContractId">The contract id.</param>
/// <param name="Payload">The decoded create arguments.</param>
/// <param name="Key">The contract key, when the template declares one.</param>
/// <param name="WitnessParties">The requesting parties that witnessed the creation.</param>
public sealed record CreatedContract<T>(
    ContractId<T> ContractId,
    T Payload,
    ContractKey? Key,
    EquatableArray<Party> WitnessParties)
    where T : ITemplate, IDamlRecord<T>;
