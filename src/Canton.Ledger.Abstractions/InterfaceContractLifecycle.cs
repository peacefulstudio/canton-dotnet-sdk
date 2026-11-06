// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// The creation and, once it happened, the archival of one contract read through a Daml interface, as
/// served by <c>ICantonLedgerClient.GetEventsByContractIdAsync&lt;TInterface, TView&gt;</c>. The interface
/// analogue of <see cref="ContractLifecycle{T}"/>.
/// </summary>
/// <typeparam name="TInterface">The Daml interface marker the contract was read through.</typeparam>
/// <typeparam name="TView">The interface's view record type.</typeparam>
/// <param name="Created">
/// The creation, carrying the participant-computed interface view, or <see langword="null"/> when the
/// requesting parties do not see it.
/// </param>
/// <param name="Archived">The archival, or <see langword="null"/> while the contract is active.</param>
public sealed record InterfaceContractLifecycle<TInterface, TView>(
    InterfaceStreamEvent<TInterface, TView>.Created? Created,
    InterfaceStreamEvent<TInterface, TView>.Archived? Archived)
    where TInterface : IDamlInterface, IHasView<TView>
    where TView : IDamlRecord<TView>;
