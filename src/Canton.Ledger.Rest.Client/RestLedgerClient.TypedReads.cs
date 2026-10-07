// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Streams;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using RuntimeCommands = Daml.Runtime.Commands;
using Canton.Ledger.Kernel.Wire;

namespace Canton.Ledger.Rest.Client;

internal sealed partial class RestLedgerClient
{
    private const string ContractByIdPath = "/v2/contracts/contract-by-id";
    private const string EventsByContractIdPath = "/v2/events/events-by-contract-id";
    private const string LatestPrunedOffsetsPath = "/v2/state/latest-pruned-offsets";
    private const string UpdatesPagePath = "/v2/updates/get-updates-page";

    private const string GetCompletionsPath = "/v2/commands/command-completions";

    /// <inheritdoc />
    public Task<CreatedContract<T>> GetContractAsync<T>(
        ContractId<T> contractId, RuntimeCommands.SubmitterInfo submitter, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>
    {
        var request = new Raw.GetContractRequest
        {
            ContractId = contractId.Value,
            QueryingParties = SubscribeFilterPolicy.FilteredPartyIds(submitter).ToList(),
        };

        return _calls.SendAsync<Raw.GetContractResponse, CreatedContract<T>>(
            Post(ContractByIdPath, request, "contract"),
            response => RestContractStreamProjector.ProjectCreatedContract<T>(response.CreatedEvent),
            timeout,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<ContractLifecycle<T>> GetEventsByContractIdAsync<T>(
        ContractId<T> contractId, RuntimeCommands.SubmitterInfo submitter, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>
    {
        var request = new Raw.GetEventsByContractIdRequest
        {
            ContractId = contractId.Value,
            EventFormat = RestSubscribeRequestBuilder.BuildEventFormat<T>(submitter),
        };

        return _calls.SendAsync<Raw.GetEventsByContractIdResponse, ContractLifecycle<T>>(
            Post(EventsByContractIdPath, request, "contract events"),
            RestContractStreamProjector.ProjectContractLifecycle<T>,
            timeout,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RuntimeCommands.DisclosedContract?> GetDisclosureAsync<T>(
        ContractId<T> contractId, RuntimeCommands.SubmitterInfo submitter, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        where T : IDamlType
    {
        var request = new Raw.GetEventsByContractIdRequest
        {
            ContractId = contractId.Value,
            EventFormat = RestSubscribeRequestBuilder.BuildDisclosureEventFormat(submitter),
        };

        try
        {
            return await _calls.SendAsync<Raw.GetEventsByContractIdResponse, RuntimeCommands.DisclosedContract?>(
                Post(EventsByContractIdPath, request, "contract events"),
                response => RestContractStreamProjector.DisclosureOf(response, _logger),
                timeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (LedgerOperationException rejection) when (IsStructuredResourceMissing(rejection))
        {
            return null;
        }
    }

    private static bool IsStructuredResourceMissing(LedgerOperationException rejection) =>
        rejection is { Category: DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing, ErrorId: not null };

    /// <inheritdoc />
    public async Task<AcsPage<T>> GetActiveContractsPageAsync<T>(
        RuntimeCommands.SubmitterInfo submitter, LedgerOffset? activeAtOffset = null, int? maxPageSize = null, LedgerPageToken? pageToken = null,
        bool includeDisclosure = false, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>
    {
        // Workaround: Canton 3.5.18's POST /v2/state/active-contracts-page reads an activeAtOffset of
        // 0 as unset and answers at the ledger end, where its own documentation answers the empty
        // snapshot of ledger begin.
        if (activeAtOffset == LedgerOffset.Begin)
        {
            return new AcsPage<T>([], LedgerOffset.Begin, null);
        }

        var request = new Raw.GetActiveContractsPageRequest
        {
            ActiveAtOffset = activeAtOffset?.Value.ToString(CultureInfo.InvariantCulture)!,
            EventFormat = RestSubscribeRequestBuilder.BuildEventFormat<T>(submitter, includeDisclosure),
            MaxPageSize = maxPageSize,
            PageToken = pageToken?.Value!,
        };

        return await _calls.SendAsync<Raw.GetActiveContractsPageResponse, AcsPage<T>>(
            Post(ActiveContractsPagePath, request, "active-contracts page"),
            ProjectAcsPage<T>,
            timeout,
            cancellationToken).ConfigureAwait(false);
    }

    private AcsPage<T> ProjectAcsPage<T>(Raw.GetActiveContractsPageResponse response)
        where T : ITemplate, IDamlRecord<T>
    {
        var activeAt = LedgerOffset.At(RestWireConversions.ParseOffset(response.ActiveAtOffset));
        var entries = new List<AcsSnapshotEntry<T>>();
        foreach (var wireEntry in response.ActiveContracts ?? [])
        {
            var disclosure = RestContractStreamProjector.DisclosureOf(wireEntry, _logger);
            entries.AddRange(RestContractStreamProjector
                .ProjectActiveContractEntry<T>(wireEntry, _logger, activeAt)
                .Select(projected => ContractSnapshotEntryArms<T>.From(projected, disclosure)));
        }

        return new AcsPage<T>(entries, activeAt, ToPageToken(response.NextPageToken));
    }

    /// <inheritdoc />
    public Task<PrunedOffsets> GetLatestPrunedOffsetsAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        _calls.SendAsync<Raw.GetLatestPrunedOffsetsResponse, PrunedOffsets>(
            new RestCall(
                HttpMethod.Get,
                LatestPrunedOffsetsPath,
                Body: null,
                "Server returned a successful response but no body was present for the pruned offsets.",
                "Server returned a malformed pruned offsets response body: ",
                LedgerCallKind.Read),
            response => new PrunedOffsets(
                PrunedOffsetOf(response.ParticipantPrunedUpToInclusive),
                PrunedOffsetOf(response.AllDivulgedContractsPrunedUpToInclusive)),
            timeout,
            cancellationToken);

    /// <inheritdoc />
    public Task<UpdatesPage> GetUpdatesPageAsync(
        RuntimeCommands.SubmitterInfo submitter, LedgerOffset? beginExclusive = null, LedgerOffset? endInclusive = null, int? maxPageSize = null,
        bool descendingOrder = false, LedgerPageToken? pageToken = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var request = new Raw.GetUpdatesPageRequest
        {
            BeginOffsetExclusive = beginExclusive?.Value.ToString(CultureInfo.InvariantCulture)!,
            EndOffsetInclusive = endInclusive?.Value.ToString(CultureInfo.InvariantCulture)!,
            MaxPageSize = maxPageSize,
            UpdateFormat = RestSubscribeRequestBuilder.BuildTransactionUpdateFormat(submitter),
            DescendingOrder = descendingOrder,
            PageToken = pageToken?.Value!,
        };

        return _calls.SendAsync<Raw.GetUpdatesPageResponse, UpdatesPage>(
            Post(UpdatesPagePath, request, "updates page"),
            ProjectUpdatesPage,
            timeout,
            cancellationToken);
    }

    private static UpdatesPage ProjectUpdatesPage(Raw.GetUpdatesPageResponse response) =>
        new(
            (response.Updates ?? [])
                .Select((update, index) => ProjectPointRead(update, $"page position {index}", RestTransactionResultProjector.Project))
                .ToList(),
            LedgerOffset.At(RestWireConversions.ParseOffset(response.LowestPageOffsetExclusive)),
            LedgerOffset.At(RestWireConversions.ParseOffset(response.HighestPageOffsetInclusive)),
            ToPageToken(response.NextPageToken));

    /// <inheritdoc />
    public IAsyncEnumerable<CompletionStreamEvent> GetCompletionsAsync(
        IEnumerable<Party> parties, LedgerOffset? beginExclusiveOffset = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parties);
        cancellationToken.ThrowIfCancellationRequested();

        var partyIds = parties.Select(party => party.Value).Distinct().ToList();
        return StreamCompletionsAsync(
            GetCompletionsPath,
            (beginExclusiveOffset ?? LedgerOffset.Begin).Value,
            beginExclusive => new RestGetCompletionsRequest(partyIds, beginExclusive.ToString(CultureInfo.InvariantCulture)),
            cancellationToken);
    }

    private static LedgerOffset PrunedOffsetOf(string? wireOffset) =>
        LedgerOffset.At(wireOffset is null ? EmptyLedgerEndOffset : RestWireConversions.ParseOffset(wireOffset));

    private static LedgerPageToken? ToPageToken(string? wireToken) =>
        string.IsNullOrEmpty(wireToken) ? null : new LedgerPageToken(wireToken);

    private static RestCall Post(string path, object body, string subject) =>
        new(
            HttpMethod.Post,
            path,
            body,
            $"Server returned a successful response but no body was present for the {subject}.",
            $"Server returned a malformed {subject} response body: ",
            LedgerCallKind.Read);
}
