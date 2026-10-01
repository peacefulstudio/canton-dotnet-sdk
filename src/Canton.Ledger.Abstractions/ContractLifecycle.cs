// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// The creation and, once it happened, the archival of one contract, as served by
/// <c>ICantonLedgerClient.GetEventsByContractIdAsync</c>.
/// </summary>
/// <param name="Created">The creation, or <see langword="null"/> when the requesting parties do not see it.</param>
/// <param name="Archived">The archival, or <see langword="null"/> while the contract is active.</param>
public sealed record ContractLifecycle<T>(
    ContractStreamEvent<T>.Created? Created,
    ContractStreamEvent<T>.Archived? Archived)
    where T : ITemplate, IDamlRecord<T>;
