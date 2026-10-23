// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Streams;
using Canton.Ledger.Kernel.Wire;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Canton.Ledger.Rest.Client.Raw;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RuntimeDisclosedContract = Daml.Runtime.Commands.DisclosedContract;
using WireCreatedEvent = Canton.Ledger.Rest.Client.Raw.CreatedEvent;
using WireGetUpdatesResponse = Canton.Ledger.Rest.Client.Raw.GetUpdatesResponse;
using WireReassignment = Canton.Ledger.Rest.Client.Raw.Reassignment;
using WireTransaction = Canton.Ledger.Rest.Client.Raw.Transaction;

namespace Canton.Ledger.Rest.Client;

internal static partial class RestContractStreamProjector
{
    public static IEnumerable<ContractStreamEvent<T>> ProjectUpdate<T>(
        WireGetUpdatesResponse response,
        ILogger? logger = null)
        where T : ITemplate, IDamlRecord<T> =>
        RestProjectionCore.ProjectUpdate<ContractArms<T>, ContractStreamEvent<T>, T, T>(response, logger);

    public static IEnumerable<ContractStreamEvent<T>> ProjectTransactionEvents<T>(
        WireTransaction transaction,
        ILogger? logger = null)
        where T : ITemplate, IDamlRecord<T> =>
        RestProjectionCore.ProjectTransactionEvents<ContractArms<T>, ContractStreamEvent<T>, T, T>(transaction, logger);

    public static IEnumerable<ContractStreamEvent<T>> ProjectReassignmentEvents<T>(
        WireReassignment reassignment,
        ILogger? logger = null)
        where T : ITemplate, IDamlRecord<T> =>
        RestProjectionCore.ProjectReassignmentEvents<ContractArms<T>, ContractStreamEvent<T>, T, T>(reassignment, logger);

    public static IEnumerable<ContractStreamEvent<T>> ProjectActiveContractEntry<T>(
        GetActiveContractsResponse response,
        ILogger? logger = null,
        LedgerOffset? snapshotOffset = null)
        where T : ITemplate, IDamlRecord<T> =>
        RestProjectionCore.ProjectActiveContractEntry<ContractArms<T>, ContractStreamEvent<T>, T, T>(
            response, logger, snapshotOffset);

    public static RuntimeDisclosedContract? DisclosureOf(
        GetActiveContractsResponse response, ILogger? logger = null, bool logContractId = false)
    {
        var entry = RestProjectionCore.ActiveEntryOf(response);
        return string.IsNullOrWhiteSpace(entry.Created?.ContractId)
            ? null
            : DisclosureOf(entry.Created, entry.WireSynchronizerId, logger, logContractId);
    }

    public static RuntimeDisclosedContract? DisclosureOf(
        GetEventsByContractIdResponse response, ILogger? logger = null, bool logContractId = false) =>
        response.Archived is not null
            ? null
            : DisclosureOf(response.Created?.CreatedEvent, wireSynchronizerId: null, logger, logContractId);

    private static RuntimeDisclosedContract? DisclosureOf(
        WireCreatedEvent? createdEvent, string? wireSynchronizerId, ILogger? logger, bool logContractId)
    {
        if (createdEvent is not { CreatedEventBlob: { Length: > 0 } base64Blob, TemplateId: { } templateId } created)
        {
            return null;
        }

        var blob = new byte[base64Blob.Length];
        if (!Convert.TryFromBase64String(base64Blob, blob, out var blobLength))
        {
            if (logContractId)
            {
                LogCreatedEventBlobNotBase64(logger ?? NullLogger.Instance, created.ContractId);
            }
            else
            {
                LogCreatedEventBlobNotBase64(logger ?? NullLogger.Instance);
            }

            return null;
        }

        return new RuntimeDisclosedContract(
            MalformedResponse.RequireContractId(created.ContractId, "a created event"),
            RestWireConversions.ToRuntimeIdentifier(templateId),
            blob.AsMemory(0, blobLength))
        {
            SynchronizerId = StreamEventClassifier.Synchronizer(wireSynchronizerId),
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The created_event_blob of a contract in the active-contract snapshot is not base64 — its row carries no Disclosure")]
    private static partial void LogCreatedEventBlobNotBase64(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The created_event_blob of contract {ContractId} in the active-contract snapshot is not base64 — its row carries no Disclosure")]
    private static partial void LogCreatedEventBlobNotBase64(ILogger logger, string contractId);
}
