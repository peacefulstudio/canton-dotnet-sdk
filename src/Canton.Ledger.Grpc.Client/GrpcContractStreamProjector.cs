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
        var (created, archived) = GrpcProjectionCore.ProjectContractLifecycle<ContractArms<T>, ContractStreamEvent<T>, T, T>(response);
        return new ContractLifecycle<T>(created as ContractStreamEvent<T>.Created, archived as ContractStreamEvent<T>.Archived);
    }

    public static RuntimeDisclosedContract? DisclosureOf(GetActiveContractsResponse response)
    {
        var (created, wireSynchronizerId) = GrpcProjectionCore.ActiveEntryOf(response);
        return string.IsNullOrWhiteSpace(created?.ContractId)
            ? null
            : DisclosureOf(created, wireSynchronizerId);
    }

    public static RuntimeDisclosedContract? DisclosureOf(GetEventsByContractIdResponse response) =>
        response.Archived is not null
            ? null
            : DisclosureOf(response.Created?.CreatedEvent, wireSynchronizerId: null);

    private static RuntimeDisclosedContract? DisclosureOf(ProtoCreatedEvent? created, string? wireSynchronizerId) =>
        created is { CreatedEventBlob.IsEmpty: false, TemplateId: { } templateId }
            ? new RuntimeDisclosedContract(
                MalformedResponse.RequireContractId(created.ContractId, "a created event"),
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
