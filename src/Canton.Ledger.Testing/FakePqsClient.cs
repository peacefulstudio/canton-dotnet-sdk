// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;

namespace Canton.Ledger.Testing;

/// <summary>
/// An in-memory <see cref="IPqsClient"/> test double that replays canned query results staged,
/// per Daml type, through the fluent <see cref="FakePqsClientBuilder"/>. It lets business logic
/// that queries PQS be unit-tested without a live PostgreSQL-backed PQS instance and without a
/// mocking framework.
/// </summary>
/// <remarks>
/// The filtered overloads (<see cref="QueryAsync{T}(PqsFilter, CancellationToken)"/>,
/// <see cref="QueryAsync{T}(PqsFilter, PqsPage, CancellationToken)"/> and <see cref="QueryOneAsync{T}"/>)
/// evaluate the filter in memory against each staged contract's payload, with the same semantics as
/// the SQL a live PQS runs: a contract is returned only when the filter is definitely true for it, and
/// <see cref="QueryOneAsync{T}"/> returns <c>null</c> when no staged contract matches. A staged
/// contract whose field holds a value PQS would fail to cast (for example a non-numeric text compared as
/// <c>Int64</c>) makes the query throw <see cref="InvalidOperationException"/> naming the contract and
/// field. Any Daml type or interface that was not staged throws a descriptive
/// <see cref="NotSupportedException"/> naming the missing setup, so a test never silently exercises
/// unconfigured behaviour. Construct instances through <see cref="Create"/>.
/// <para>
/// The paged overloads order contracts by contract id before slicing, as <see cref="IPqsClient"/>
/// promises, using an ordinal comparison. A live PQS orders <c>contract_id</c> by its database
/// collation (<c>en_US.utf8</c> on the LocalNet PQS), so the two agree for real contract ids, which
/// are lowercase hex, but may disagree for made-up staged ids that mix letters, digits and
/// punctuation, such as <c>cid-2</c>, <c>cid10</c> and <c>cid2</c>. Stage hex-like ids when a test
/// depends on which contracts land on which page. The unpaged overloads return contracts in staging
/// order.
/// </para>
/// </remarks>
public sealed class FakePqsClient : IPqsClient
{
    private readonly IReadOnlyDictionary<Type, object> _templateResults;
    private readonly IReadOnlyDictionary<Type, object> _interfaceResults;

    internal FakePqsClient(
        IReadOnlyDictionary<Type, object> templateResults,
        IReadOnlyDictionary<Type, object> interfaceResults)
    {
        _templateResults = templateResults;
        _interfaceResults = interfaceResults;
    }

    /// <summary>Starts a new fluent builder for a <see cref="FakePqsClient"/>.</summary>
    /// <returns>An empty builder; stage query results on it, then call <see cref="FakePqsClientBuilder.Build"/>.</returns>
    public static FakePqsClientBuilder Create() => new();

    /// <inheritdoc />
    public Task<IReadOnlyList<Contract<T>>> QueryAsync<T>(CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T> =>
        Task.FromResult(StagedContracts<T>());

    /// <summary>
    /// Returns the slice of the staged contracts selected by <paramref name="page"/>. Like the real
    /// client's <c>ORDER BY contract_id</c>, the staged set is ordered by contract id before the slice
    /// is taken, whatever order the contracts were staged in. The fake compares ids with
    /// <see cref="StringComparer.Ordinal"/>; see the class remarks for how that differs from a live PQS.
    /// </summary>
    public Task<IReadOnlyList<Contract<T>>> QueryAsync<T>(PqsPage page, CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>
    {
        ArgumentNullException.ThrowIfNull(page);
        return Task.FromResult(Slice(StagedContracts<T>(), contract => contract.Id.Value, page));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InterfaceContract<TInterface, TView>>> QueryAsync<TInterface, TView>(
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        Task.FromResult(StagedInterfaceContracts<TInterface, TView>());

    /// <summary>
    /// Returns the slice of the staged interface contracts selected by <paramref name="page"/>.
    /// Like the real client's <c>ORDER BY contract_id</c>, the staged set is ordered by contract id
    /// before the slice is taken, whatever order the contracts were staged in. The fake compares ids
    /// with <see cref="StringComparer.Ordinal"/>; see the class remarks for how that differs from a
    /// live PQS.
    /// </summary>
    public Task<IReadOnlyList<InterfaceContract<TInterface, TView>>> QueryAsync<TInterface, TView>(
        PqsPage page,
        CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        ArgumentNullException.ThrowIfNull(page);
        return Task.FromResult(Slice(StagedInterfaceContracts<TInterface, TView>(), contract => contract.Id.Value, page));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Contract<T>>> QueryAsync<T>(PqsFilter filter, CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Task.FromResult(MatchingContracts<T>(filter));
    }

    /// <summary>
    /// Returns the slice selected by <paramref name="page"/> of the staged contracts that match
    /// <paramref name="filter"/>. The filter is applied first, then, like the real client's
    /// <c>ORDER BY contract_id</c>, the matches are ordered by contract id before the slice is taken,
    /// whatever order the contracts were staged in. The fake compares ids with
    /// <see cref="StringComparer.Ordinal"/>; see the class remarks for how that differs from a live PQS.
    /// </summary>
    public Task<IReadOnlyList<Contract<T>>> QueryAsync<T>(
        PqsFilter filter,
        PqsPage page,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);
        return Task.FromResult(Slice(MatchingContracts<T>(filter), contract => contract.Id.Value, page));
    }

    /// <inheritdoc />
    public Task<Contract<T>?> QueryOneAsync<T>(PqsFilter filter, CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>
    {
        ArgumentNullException.ThrowIfNull(filter);
        var matches = MatchingContracts<T>(filter);
        return Task.FromResult(matches.Count > 0 ? matches[0] : null);
    }

    /// <inheritdoc />
    public Task<Contract<T>?> FetchByIdAsync<T>(ContractId<T> contractId, CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>
    {
        ArgumentNullException.ThrowIfNull(contractId);
        return Task.FromResult(StagedContracts<T>().FirstOrDefault(c => c.Id.Equals(contractId)));
    }

    /// <inheritdoc />
    public Task<InterfaceContract<TInterface, TView>?> FetchByIdAsync<TInterface, TView>(
        ContractId<TInterface> contractId, CancellationToken cancellationToken = default)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        ArgumentNullException.ThrowIfNull(contractId);
        return Task.FromResult(StagedInterfaceContracts<TInterface, TView>()
            .FirstOrDefault(c => c.Id.Equals(contractId)));
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync<T>(ContractId<T> contractId, CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        ArgumentNullException.ThrowIfNull(contractId);
        return Task.FromResult(StagedContracts<T>().Any(c => c.Id.Equals(contractId)));
    }

    private IReadOnlyList<Contract<T>> StagedContracts<T>()
        where T : ITemplate
    {
        if (_templateResults.TryGetValue(typeof(T), out var staged))
        {
            return (IReadOnlyList<Contract<T>>)staged;
        }

        throw new NotSupportedException(
            $"FakePqsClient has no query results staged for Daml type '{typeof(T).Name}'. Stage some with " +
            $"FakePqsClient.Create().WithQueryResults<{typeof(T).Name}>(...).Build() before exercising this path.");
    }

    private IReadOnlyList<Contract<T>> MatchingContracts<T>(PqsFilter filter)
        where T : ITemplate, IDamlRecord<T> =>
        [.. StagedContracts<T>().Where(contract => Matches(filter, contract))];

    private static bool Matches<T>(PqsFilter filter, Contract<T> contract)
        where T : ITemplate, IDamlRecord<T>
    {
        using var payload = JsonDocument.Parse(DamlJsonSerializer.Serialize(contract.Data.ToRecord()));
        return PqsFilterEvaluator.Matches(filter, contract.Id.Value, payload.RootElement);
    }

    private IReadOnlyList<InterfaceContract<TInterface, TView>> StagedInterfaceContracts<TInterface, TView>()
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        if (_interfaceResults.TryGetValue(typeof(TInterface), out var staged))
        {
            return (IReadOnlyList<InterfaceContract<TInterface, TView>>)staged;
        }

        throw new NotSupportedException(
            $"FakePqsClient has no interface query results staged for Daml interface '{typeof(TInterface).Name}'. " +
            $"Stage some with FakePqsClient.Create().WithInterfaceQueryResults<{typeof(TInterface).Name}, " +
            $"{typeof(TView).Name}>(...).Build() before exercising this path.");
    }

    private static IReadOnlyList<TItem> Slice<TItem>(
        IReadOnlyList<TItem> items, Func<TItem, string> contractIdOf, PqsPage page) =>
        [.. items.OrderBy(contractIdOf, StringComparer.Ordinal).Skip(page.Offset).Take(page.Limit)];
}
