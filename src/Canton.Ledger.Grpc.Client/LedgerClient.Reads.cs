// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Streams;
using Canton.Ledger.Kernel.Telemetry;
using Canton.Ledger.Kernel.Wire;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Google.Protobuf;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client;

internal sealed partial class LedgerClient
{
    /// <inheritdoc />
    public async Task<CreatedContract<T>> GetContractAsync<T>(
        ContractId<T> contractId, RuntimeCommands.SubmitterInfo submitter, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>
    {
        var request = new GetContractRequest { ContractId = contractId.Value };
        request.QueryingParties.AddRange(SubscribeFilterPolicy.FilteredPartyIds(submitter));

        var created = await _invoker.InvokeTracedAsync<LedgerClient, GetContractResponse, Com.Daml.Ledger.Api.V2.CreatedEvent?>(
            LedgerCallInvoker.Source,
            ContractService.Descriptor,
            "GetContract",
            (headers, deadline, token) => _contractService.GetContractAsync(request, headers, deadline, token),
            response => response.CreatedEvent,
            cancellationToken,
            timeout: timeout,
            configureActivity: activity =>
            {
                activity?.SetTag(LedgerActivityTagNames.DamlContractId, contractId.Value);
                activity.SetSubmitterTags(submitter, _options);
            }).ConfigureAwait(false);

        // Workaround: Canton 3.5.18's ContractService.GetContract has no verbose option and answers
        // create_arguments without field labels, which the by-name payload decoding cannot read.
        if (created?.CreateArguments is { } arguments && arguments.Fields.Any(field => field.Label.Length == 0))
        {
            var lifecycle = await GetEventsByContractIdAsync(contractId, submitter, timeout, cancellationToken).ConfigureAwait(false);
            return lifecycle.Created is { } labelled
                ? new CreatedContract<T>(labelled.ContractId, labelled.Payload, labelled.Key, labelled.WitnessParties)
                : throw new InvalidOperationException(
                    $"Contract '{contractId.Value}' was served without field labels and is not visible to the event query.");
        }

        return GrpcContractStreamProjector.ProjectCreatedContract<T>(created);
    }

    /// <inheritdoc />
    public Task<ContractLifecycle<T>> GetEventsByContractIdAsync<T>(
        ContractId<T> contractId, RuntimeCommands.SubmitterInfo submitter, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>
    {
        var request = new GetEventsByContractIdRequest
        {
            ContractId = contractId.Value,
            EventFormat = GrpcSubscribeRequestBuilder.BuildEventFormat(
                submitter, GrpcMarkerMatcher<T>.StreamFilterIdentifier(), GrpcMarkerMatcher<T>.IsInterface),
        };

        return _invoker.InvokeTracedAsync<LedgerClient, GetEventsByContractIdResponse, ContractLifecycle<T>>(
            LedgerCallInvoker.Source,
            EventQueryService.Descriptor,
            "GetEventsByContractId",
            (headers, deadline, token) => _eventQueryService.GetEventsByContractIdAsync(request, headers, deadline, token),
            GrpcContractStreamProjector.ProjectContractLifecycle<T>,
            cancellationToken,
            timeout: timeout,
            configureActivity: activity =>
            {
                activity?.SetTag(LedgerActivityTagNames.DamlContractId, contractId.Value);
                activity?.SetTag(LedgerActivityTagNames.DamlTemplateId, typeof(T).Name);
                activity.SetSubmitterTags(submitter, _options);
            });
    }

    /// <inheritdoc />
    public Task<AcsPage<T>> GetActiveContractsPageAsync<T>(
        RuntimeCommands.SubmitterInfo submitter, LedgerOffset? activeAtOffset = null, int? maxPageSize = null, LedgerPageToken? pageToken = null,
        bool includeDisclosure = false, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>
    {
        // Workaround: Canton 3.5.18's StateService.GetActiveContractsPage reads an active_at_offset of
        // 0 as unset and answers at the ledger end, where its own documentation answers the empty
        // snapshot of ledger begin.
        if (activeAtOffset == LedgerOffset.Begin)
        {
            return Task.FromResult(new AcsPage<T>([], LedgerOffset.Begin, null));
        }

        var request = new GetActiveContractsPageRequest
        {
            EventFormat = GrpcSubscribeRequestBuilder.BuildEventFormat(
                submitter, GrpcMarkerMatcher<T>.StreamFilterIdentifier(), GrpcMarkerMatcher<T>.IsInterface, includeDisclosure),
        };
        if (activeAtOffset is { } offset) request.ActiveAtOffset = offset.Value;
        if (maxPageSize is { } pageSize) request.MaxPageSize = pageSize;
        if (pageToken is not null) request.PageToken = ToWireToken(pageToken);

        return _invoker.InvokeTracedAsync<LedgerClient, GetActiveContractsPageResponse, AcsPage<T>>(
            LedgerCallInvoker.Source,
            StateService.Descriptor,
            "GetActiveContractsPage",
            (headers, deadline, token) => _stateService.GetActiveContractsPageAsync(request, headers, deadline, token),
            ProjectAcsPage<T>,
            cancellationToken,
            timeout: timeout,
            configureActivity: activity =>
            {
                activity?.SetTag(LedgerActivityTagNames.DamlTemplateId, typeof(T).Name);
                activity.SetSubmitterTags(submitter, _options);
            });
    }

    private AcsPage<T> ProjectAcsPage<T>(GetActiveContractsPageResponse response)
        where T : ITemplate, IDamlRecord<T>
    {
        var activeAt = LedgerOffset.At(response.ActiveAtOffset);
        var entries = new List<AcsSnapshotEntry<T>>();
        foreach (var wireEntry in response.ActiveContracts)
        {
            var disclosure = GrpcContractStreamProjector.DisclosureOf(wireEntry);
            entries.AddRange(GrpcContractStreamProjector
                .ProjectActiveContractEntry<T>(wireEntry, _logger, activeAt)
                .Select(projected => ToAcsSnapshotEntry(projected, disclosure)));
        }

        return new AcsPage<T>(entries, activeAt, FromWireToken(response.HasNextPageToken ? response.NextPageToken : null));
    }

    /// <inheritdoc />
    public Task<PrunedOffsets> GetLatestPrunedOffsetsAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        _invoker.InvokeTracedAsync<LedgerClient, GetLatestPrunedOffsetsResponse, PrunedOffsets>(
            LedgerCallInvoker.Source,
            StateService.Descriptor,
            "GetLatestPrunedOffsets",
            (headers, deadline, token) => _stateService.GetLatestPrunedOffsetsAsync(new GetLatestPrunedOffsetsRequest(), headers, deadline, token),
            response => new PrunedOffsets(
                LedgerOffset.At(response.ParticipantPrunedUpToInclusive),
                LedgerOffset.At(response.AllDivulgedContractsPrunedUpToInclusive)),
            cancellationToken,
            timeout: timeout);

    /// <inheritdoc />
    public Task<UpdatesPage> GetUpdatesPageAsync(
        RuntimeCommands.SubmitterInfo submitter, LedgerOffset? beginExclusive = null, LedgerOffset? endInclusive = null, int? maxPageSize = null,
        bool descendingOrder = false, LedgerPageToken? pageToken = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var request = new GetUpdatesPageRequest
        {
            UpdateFormat = GrpcSubscribeRequestBuilder.BuildTransactionUpdateFormat(submitter),
            DescendingOrder = descendingOrder,
        };
        if (beginExclusive is { } begin) request.BeginOffsetExclusive = begin.Value;
        if (endInclusive is { } end) request.EndOffsetInclusive = end.Value;
        if (maxPageSize is { } pageSize) request.MaxPageSize = pageSize;
        if (pageToken is not null) request.PageToken = ToWireToken(pageToken);

        return _invoker.InvokeTracedAsync<LedgerClient, GetUpdatesPageResponse, UpdatesPage>(
            LedgerCallInvoker.Source,
            UpdateService.Descriptor,
            "GetUpdatesPage",
            (headers, deadline, token) => _updateService.GetUpdatesPageAsync(request, headers, deadline, token),
            ProjectUpdatesPage,
            cancellationToken,
            timeout: timeout,
            configureActivity: activity => activity.SetSubmitterTags(submitter, _options));
    }

    private static UpdatesPage ProjectUpdatesPage(GetUpdatesPageResponse response) =>
        new(
            response.Updates
                .Select((update, index) => ProjectPointRead(update, $"page position {index}", GrpcTransactionResultProjector.Project))
                .ToList(),
            LedgerOffset.At(response.LowestPageOffsetExclusive),
            LedgerOffset.At(response.HighestPageOffsetInclusive),
            FromWireToken(response.HasNextPageToken ? response.NextPageToken : null));

    /// <inheritdoc />
    public IAsyncEnumerable<CompletionStreamEvent> GetCompletionsAsync(
        IEnumerable<Party> parties, LedgerOffset? beginExclusiveOffset = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parties);

        var request = new GetCompletionsRequest { BeginExclusive = (beginExclusiveOffset ?? LedgerOffset.Begin).Value };
        request.Parties.AddRange(parties.Select(party => party.Value).Distinct());
        return GetCompletionsAsyncCore(request, cancellationToken);
    }

    private async IAsyncEnumerable<CompletionStreamEvent> GetCompletionsAsyncCore(
        GetCompletionsRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = LedgerActivitySource.StartActivity<LedgerClient>(LedgerCallInvoker.Source);
        _invoker.TagServerCall(activity, CommandCompletionService.Descriptor, "GetCompletions");
        activity?.SetTag(LedgerActivityTagNames.CantonFromOffset, request.BeginExclusive);

        LogCompletionStreamStarted(_logger, request.BeginExclusive);

        using var call = _commandCompletionService.GetCompletions(
            request,
            headers: await _invoker.GetHeadersAsync(cancellationToken).ConfigureAwait(false),
            deadline: null,
            cancellationToken: cancellationToken);

        await foreach (var completionEvent in DrainCompletionStreamAsync(call.ResponseStream, activity, cancellationToken).ConfigureAwait(false))
        {
            yield return completionEvent;
        }
    }

    private static ByteString ToWireToken(LedgerPageToken token) => ByteString.FromBase64(token.Value);

    private static LedgerPageToken? FromWireToken(ByteString? token) =>
        token is { IsEmpty: false } ? new LedgerPageToken(token.ToBase64()) : null;
}
