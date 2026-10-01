// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Testing;

public sealed partial class FakeLedgerClientBuilder
{
    private readonly Dictionary<(Type Template, string ContractId), object> _contracts = [];
    private readonly Dictionary<(Type Template, string ContractId), object> _contractLifecycles = [];
    private readonly Dictionary<Type, object> _activeContractsPages = [];
    private UpdatesPage[]? _updatesPages;
    private PrunedOffsets? _prunedOffsets;

    /// <summary>
    /// Stages the contract <see cref="FakeLedgerClient.GetContractAsync{T}"/> returns for
    /// <paramref name="contract"/>'s id.
    /// </summary>
    /// <param name="contract">The contract to reply with.</param>
    /// <returns>The same builder, for chaining.</returns>
    public FakeLedgerClientBuilder WithContract<T>(CreatedContract<T> contract)
        where T : ITemplate, IDamlRecord<T>
    {
        ArgumentNullException.ThrowIfNull(contract);
        _contracts[(typeof(T), contract.ContractId.Value)] = contract;
        return this;
    }

    /// <summary>
    /// Stages the creation and archival <see cref="FakeLedgerClient.GetEventsByContractIdAsync{T}"/>
    /// returns for <paramref name="contractId"/>.
    /// </summary>
    /// <param name="contractId">The contract the events belong to.</param>
    /// <param name="lifecycle">The events to reply with.</param>
    /// <returns>The same builder, for chaining.</returns>
    public FakeLedgerClientBuilder WithContractLifecycle<T>(ContractId<T> contractId, ContractLifecycle<T> lifecycle)
        where T : ITemplate, IDamlRecord<T>
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        _contractLifecycles[(typeof(T), contractId.Value)] = lifecycle;
        return this;
    }

    /// <summary>
    /// Stages the pages <see cref="FakeLedgerClient.GetActiveContractsPageAsync{T}"/> serves for
    /// <typeparamref name="T"/>, in reading order: a request with no page token gets the first page, and a
    /// request carrying a page's <see cref="AcsPage{T}.NextPageToken"/> gets the page after it. A request
    /// at <see cref="LedgerOffset.Begin"/> gets the empty snapshot of ledger begin, staged or not.
    /// </summary>
    /// <param name="pages">The pages, first to last.</param>
    /// <returns>The same builder, for chaining.</returns>
    public FakeLedgerClientBuilder WithActiveContractsPages<T>(params AcsPage<T>[] pages)
        where T : ITemplate, IDamlRecord<T>
    {
        ArgumentNullException.ThrowIfNull(pages);
        _activeContractsPages[typeof(T)] = pages.ToArray();
        return this;
    }

    /// <summary>
    /// Stages the pages <see cref="FakeLedgerClient.GetUpdatesPageAsync"/> serves, in reading order: a
    /// request with no page token gets the first page, and a request carrying a page's
    /// <see cref="UpdatesPage.NextPageToken"/> gets the page after it.
    /// </summary>
    /// <param name="pages">The pages, first to last.</param>
    /// <returns>The same builder, for chaining.</returns>
    public FakeLedgerClientBuilder WithUpdatesPages(params UpdatesPage[] pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        _updatesPages = pages.ToArray();
        return this;
    }

    /// <summary>
    /// Stages the offsets <see cref="FakeLedgerClient.GetLatestPrunedOffsetsAsync"/> returns.
    /// </summary>
    /// <param name="offsets">The pruned offsets to reply with.</param>
    /// <returns>The same builder, for chaining.</returns>
    public FakeLedgerClientBuilder WithPrunedOffsets(PrunedOffsets offsets)
    {
        ArgumentNullException.ThrowIfNull(offsets);
        _prunedOffsets = offsets;
        return this;
    }

    private FakeTypedReads SnapshotTypedReads() => new(
        new Dictionary<(Type Template, string ContractId), object>(_contracts),
        new Dictionary<(Type Template, string ContractId), object>(_contractLifecycles),
        new Dictionary<Type, object>(_activeContractsPages),
        _updatesPages?.ToArray(),
        _prunedOffsets);
}
