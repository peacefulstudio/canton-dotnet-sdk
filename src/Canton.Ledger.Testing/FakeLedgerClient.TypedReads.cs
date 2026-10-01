// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime;
using RuntimeCommands = Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;


namespace Canton.Ledger.Testing;

public sealed partial class FakeLedgerClient
{
    internal FakeTypedReads TypedReads { get; init; } = FakeTypedReads.Empty;

    /// <inheritdoc />
    public Task<CreatedContract<T>> GetContractAsync<T>(
        ContractId<T> contractId, RuntimeCommands.SubmitterInfo submitter, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T> =>
        Task.FromResult(TypedReads.Contracts.TryGetValue((typeof(T), contractId.Value), out var contract)
            ? (CreatedContract<T>)contract
            : throw StagingMissing($"contract '{contractId.Value}'", nameof(GetContractAsync), $"WithContract<{typeof(T).Name}>"));

    /// <inheritdoc />
    public Task<ContractLifecycle<T>> GetEventsByContractIdAsync<T>(
        ContractId<T> contractId, RuntimeCommands.SubmitterInfo submitter, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T> =>
        Task.FromResult(TypedReads.ContractLifecycles.TryGetValue((typeof(T), contractId.Value), out var lifecycle)
            ? (ContractLifecycle<T>)lifecycle
            : throw StagingMissing(
                $"events for contract '{contractId.Value}'", nameof(GetEventsByContractIdAsync), $"WithContractLifecycle<{typeof(T).Name}>"));

    /// <inheritdoc />
    public Task<AcsPage<T>> GetActiveContractsPageAsync<T>(
        RuntimeCommands.SubmitterInfo submitter, LedgerOffset? activeAtOffset = null, int? maxPageSize = null, LedgerPageToken? pageToken = null,
        bool includeDisclosure = false, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>
    {
        if (activeAtOffset == LedgerOffset.Begin)
        {
            return Task.FromResult(new AcsPage<T>([], LedgerOffset.Begin, null));
        }

        var pages = TypedReads.ActiveContractsPages.TryGetValue(typeof(T), out var staged)
            ? (AcsPage<T>[])staged
            : throw StagingMissing(
                "active-contracts pages", nameof(GetActiveContractsPageAsync), $"WithActiveContractsPages<{typeof(T).Name}>");
        return Task.FromResult(PageAfter(pages, page => page.NextPageToken, pageToken, nameof(GetActiveContractsPageAsync)));
    }

    /// <inheritdoc />
    public Task<PrunedOffsets> GetLatestPrunedOffsetsAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(TypedReads.PrunedOffsets ?? throw StagingMissing(
            "pruned offsets", nameof(GetLatestPrunedOffsetsAsync), "WithPrunedOffsets"));

    /// <inheritdoc />
    public Task<UpdatesPage> GetUpdatesPageAsync(
        RuntimeCommands.SubmitterInfo submitter, LedgerOffset? beginExclusive = null, LedgerOffset? endInclusive = null, int? maxPageSize = null,
        bool descendingOrder = false, LedgerPageToken? pageToken = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var pages = TypedReads.UpdatesPages ?? throw StagingMissing(
            "updates pages", nameof(GetUpdatesPageAsync), "WithUpdatesPages");
        return Task.FromResult(PageAfter(pages, page => page.NextPageToken, pageToken, nameof(GetUpdatesPageAsync)));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<CompletionStreamEvent> GetCompletionsAsync(
        IEnumerable<Party> parties, LedgerOffset? beginExclusiveOffset = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parties);
        return Replay(After(StagedCompletions(), beginExclusiveOffset ?? LedgerOffset.Begin), cancellationToken);
    }

    private static TPage PageAfter<TPage>(
        IReadOnlyList<TPage> pages, Func<TPage, LedgerPageToken?> nextTokenOf, LedgerPageToken? requestedToken, string member)
    {
        if (requestedToken is null)
        {
            return pages.Count > 0
                ? pages[0]
                : throw new NotSupportedException($"FakeLedgerClient has no page staged for '{member}'.");
        }

        for (var index = 0; index < pages.Count - 1; index++)
        {
            if (nextTokenOf(pages[index]) == requestedToken)
            {
                return pages[index + 1];
            }
        }

        throw new ArgumentException(
            $"FakeLedgerClient staged no page after page token '{requestedToken.Value}' for '{member}'.", nameof(requestedToken));
    }
}

internal sealed record FakeTypedReads(
    IReadOnlyDictionary<(Type Template, string ContractId), object> Contracts,
    IReadOnlyDictionary<(Type Template, string ContractId), object> ContractLifecycles,
    IReadOnlyDictionary<Type, object> ActiveContractsPages,
    IReadOnlyList<UpdatesPage>? UpdatesPages,
    PrunedOffsets? PrunedOffsets)
{
    internal static FakeTypedReads Empty { get; } = new(
        new Dictionary<(Type Template, string ContractId), object>(),
        new Dictionary<(Type Template, string ContractId), object>(),
        new Dictionary<Type, object>(),
        null,
        null);
}
