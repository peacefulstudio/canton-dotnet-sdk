// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Streams;
using Canton.Ledger.Kernel.Wire;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using WireCreatedEvent = Canton.Ledger.Rest.Client.Raw.CreatedEvent;

namespace Canton.Ledger.Rest.Client;

internal static partial class RestContractStreamProjector
{
    public static CreatedContract<T> ProjectCreatedContract<T>(WireCreatedEvent? created)
        where T : ITemplate, IDamlRecord<T>
    {
        if (created is null)
        {
            throw MalformedResponse.MissingRequiredField("the contract-by-id response has no createdEvent");
        }

        RequireTemplateId(created.TemplateId, nameof(Raw.CreatedEvent), created.ContractId);
        if (!RestMarkerMatcher<T>.MatchesCreated(created) || !TryResolveCreatedPayload<T>(created, out var payload))
        {
            throw new InvalidOperationException(
                $"Contract '{created.ContractId}' is a {created.TemplateId.ModuleName}.{created.TemplateId.EntityName}, "
                + $"which cannot be read as {typeof(T).Name}.");
        }

        return new CreatedContract<T>(
            new ContractId<T>(created.ContractId),
            payload,
            ContractKeyOf(created),
            RestWireConversions.ToPartyList(created.WitnessParties));
    }

    public static ContractLifecycle<T> ProjectContractLifecycle<T>(Raw.GetEventsByContractIdResponse response)
        where T : ITemplate, IDamlRecord<T>
    {
        ContractStreamEvent<T>.Created? created = null;
        if (response.Created?.CreatedEvent is { } createdEvent)
        {
            created = CreatedFromWire<T>(
                    createdEvent,
                    RequireSynchronizer(response.Created.SynchronizerId, createdEvent.ContractId),
                    RestWireConversions.ParseOffset(createdEvent.Offset))
                as ContractStreamEvent<T>.Created
                ?? throw new InvalidOperationException(
                    $"Contract '{createdEvent.ContractId}' cannot be read as {typeof(T).Name}: its interface view is unavailable.");
        }

        ContractStreamEvent<T>.Archived? archived = null;
        if (response.Archived?.ArchivedEvent is { } archivedEvent)
        {
            RequireTemplateId(archivedEvent.TemplateId, nameof(Raw.ArchivedEvent), archivedEvent.ContractId);
            archived = new ContractStreamEvent<T>.Archived(
                new ContractId<T>(archivedEvent.ContractId),
                LedgerOffset.At(RestWireConversions.ParseOffset(archivedEvent.Offset)),
                RequireSynchronizer(response.Archived.SynchronizerId, archivedEvent.ContractId),
                RestWireConversions.ToPartyList(archivedEvent.WitnessParties));
        }

        return new ContractLifecycle<T>(created, archived);
    }

    private static SynchronizerId RequireSynchronizer(string? wireSynchronizerId, string? contractId) =>
        StreamEventClassifier.Synchronizer(wireSynchronizerId)
        ?? throw MalformedResponse.MissingRequiredField($"the event for contract '{contractId}' has no synchronizerId");
}
