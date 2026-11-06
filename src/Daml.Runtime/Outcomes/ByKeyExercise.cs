// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Outcomes;

/// <summary>
/// The success payload of a by-key exercise: the choice's result together with the contract the
/// ledger resolved the key to. Contract keys are not unique, so the caller cannot predict which
/// active contract is exercised; this record is how it learns.
/// </summary>
/// <typeparam name="TTemplate">The keyed template that declares the choice.</typeparam>
/// <typeparam name="TResult">The choice's result type.</typeparam>
/// <param name="ResolvedContractId">The active contract the ledger resolved the key to and exercised.
/// Distinct from <paramref name="Result"/>, which may itself be a contract id of the same template.</param>
/// <param name="Result">The decoded result of the choice.</param>
public sealed record ByKeyExercise<TTemplate, TResult>(ContractId<TTemplate> ResolvedContractId, TResult Result)
    where TTemplate : IDamlType;
