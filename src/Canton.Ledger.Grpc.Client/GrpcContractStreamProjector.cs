// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Streams;
using Canton.Ledger.Kernel.Wire;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using RuntimeDisclosedContract = Daml.Runtime.Commands.DisclosedContract;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client;

internal static class GrpcContractStreamProjector
{
    public static IEnumerable<ContractStreamEvent<T>> ProjectUpdate<T>(
        GetUpdatesResponse response,
        ILogger? logger = null)
        where T : ITemplate, IDamlRecord<T> =>
        GrpcProjectionCore.ProjectUpdate<ContractArms<T>, ContractStreamEvent<T>, T, T>(response, logger);

    public static IEnumerable<ContractStreamEvent<T>> ProjectTransactionEvents<T>(
        Transaction transaction,
        ILogger? logger = null)
        where T : ITemplate, IDamlRecord<T> =>
        GrpcProjectionCore.ProjectTransactionEvents<ContractArms<T>, ContractStreamEvent<T>, T, T>(transaction, logger);

    public static IEnumerable<ContractStreamEvent<T>> ProjectReassignmentEvents<T>(
        Reassignment reassignment,
        ILogger? logger = null)
        where T : ITemplate, IDamlRecord<T> =>
        GrpcProjectionCore.ProjectReassignmentEvents<ContractArms<T>, ContractStreamEvent<T>, T, T>(reassignment, logger);

    public static CreatedContract<T> ProjectCreatedContract<T>(ProtoCreatedEvent? created)
        where T : ITemplate, IDamlRecord<T>
    {
        if (created is null)
        {
            throw MalformedResponse.MissingRequiredField("the GetContract response has no created_event");
        }

        GrpcProjectionCore.RequireTemplateId(created.TemplateId, nameof(Com.Daml.Ledger.Api.V2.CreatedEvent), created.ContractId);
        if (!GrpcMarkerMatcher<T>.MatchesProtoCreated(created) || !GrpcProjectionCore.TryResolvePayload<T, T>(created, out var payload))
        {
            throw new InvalidOperationException(
                $"Contract '{created.ContractId}' is a {created.TemplateId.ModuleName}.{created.TemplateId.EntityName}, "
                + $"which cannot be read as {typeof(T).Name}.");
        }

        return new CreatedContract<T>(
            LedgerWireConversions.ToContractId<T>(created.ContractId),
            payload,
            GrpcProjectionCore.ContractKeyOf(created),
            LedgerWireConversions.ToPartyList(created.WitnessParties));
    }

    public static ContractLifecycle<T> ProjectContractLifecycle<T>(GetEventsByContractIdResponse response)
        where T : ITemplate, IDamlRecord<T>
    {
        ContractStreamEvent<T>.Created? created = null;
        if (response.Created is { CreatedEvent: { } createdEvent } createdEnvelope)
        {
            GrpcProjectionCore.RequireTemplateId(createdEvent.TemplateId, nameof(Com.Daml.Ledger.Api.V2.CreatedEvent), createdEvent.ContractId);
            var synchronizerId = RequireSynchronizer(createdEnvelope.SynchronizerId, createdEvent.ContractId);
            created = GrpcProjectionCore.CreatedFromProto<ContractArms<T>, ContractStreamEvent<T>, T, T>(
                    createdEvent, synchronizerId, createdEvent.Offset).Event
                as ContractStreamEvent<T>.Created
                ?? throw new InvalidOperationException(
                    $"Contract '{createdEvent.ContractId}' cannot be read as {typeof(T).Name}: its interface view is unavailable.");
        }

        ContractStreamEvent<T>.Archived? archived = null;
        if (response.Archived is { ArchivedEvent: { } archivedEvent } archivedEnvelope)
        {
            GrpcProjectionCore.RequireTemplateId(archivedEvent.TemplateId, nameof(Com.Daml.Ledger.Api.V2.ArchivedEvent), archivedEvent.ContractId);
            archived = (ContractStreamEvent<T>.Archived)ContractArms<T>.Archived(
                LedgerWireConversions.ToContractId<T>(archivedEvent.ContractId),
                LedgerWireConversions.ToLedgerOffset(archivedEvent.Offset),
                RequireSynchronizer(archivedEnvelope.SynchronizerId, archivedEvent.ContractId),
                LedgerWireConversions.ToPartyList(archivedEvent.WitnessParties));
        }

        return new ContractLifecycle<T>(created, archived);
    }

    private static SynchronizerId RequireSynchronizer(string? wireSynchronizerId, string contractId) =>
        StreamEventClassifier.Synchronizer(wireSynchronizerId)
        ?? throw MalformedResponse.MissingRequiredField($"the event for contract '{contractId}' has no synchronizer_id");

    public static bool IsTemplateMatch(ProtoIdentifier? proto, RuntimeIdentifier expected) =>
        proto is not null && MatchesModuleEntity(proto.ModuleName, proto.EntityName, expected);

    internal static bool MatchesModuleEntity(string moduleName, string entityName, RuntimeIdentifier expected) =>
        string.Equals(moduleName, expected.ModuleName, StringComparison.Ordinal)
        && string.Equals(entityName, expected.EntityName, StringComparison.Ordinal);

    public static RuntimeDisclosedContract? DisclosureOf(GetActiveContractsResponse response)
    {
        var (created, wireSynchronizerId) = GrpcProjectionCore.ActiveEntryOf(response);
        return DisclosureOf(created, wireSynchronizerId);
    }

    public static RuntimeDisclosedContract? DisclosureOf(GetEventsByContractIdResponse response) =>
        response.Archived is not null
            ? null
            : DisclosureOf(response.Created?.CreatedEvent, wireSynchronizerId: null);

    private static RuntimeDisclosedContract? DisclosureOf(ProtoCreatedEvent? created, string? wireSynchronizerId) =>
        created is { CreatedEventBlob.IsEmpty: false, TemplateId: { } templateId }
            ? new RuntimeDisclosedContract(
                created.ContractId,
                LedgerWireConversions.ToRuntimeIdentifier(templateId),
                created.CreatedEventBlob.Memory)
            {
                SynchronizerId = StreamEventClassifier.Synchronizer(wireSynchronizerId),
            }
            : null;

    public static IEnumerable<ContractStreamEvent<T>> ProjectActiveContractEntry<T>(
        GetActiveContractsResponse response,
        ILogger? logger = null,
        LedgerOffset? snapshotOffset = null)
        where T : ITemplate, IDamlRecord<T> =>
        GrpcProjectionCore.ProjectActiveContractEntry<ContractArms<T>, ContractStreamEvent<T>, T, T>(
            response, logger, snapshotOffset);
}
