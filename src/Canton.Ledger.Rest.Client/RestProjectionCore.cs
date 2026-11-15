// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Streams;
using Canton.Ledger.Kernel.Wire;
using Canton.Ledger.Rest.Client.Raw;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WireCreatedEvent = Canton.Ledger.Rest.Client.Raw.CreatedEvent;
using WireGetUpdatesResponse = Canton.Ledger.Rest.Client.Raw.GetUpdatesResponse;
using WireEvent = Canton.Ledger.Rest.Client.Raw.Event;
using WireIdentifier = Canton.Ledger.Rest.Client.Raw.Identifier;
using WireReassignment = Canton.Ledger.Rest.Client.Raw.Reassignment;
using WireReassignmentEvent = Canton.Ledger.Rest.Client.Raw.ReassignmentEvent;
using WireTransaction = Canton.Ledger.Rest.Client.Raw.Transaction;
using WireUnassignedEvent = Canton.Ledger.Rest.Client.Raw.UnassignedEvent;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// The active-contract entry of the JSON Ledger API, dissected once: the created event it carries,
/// the wire synchronizer its contract is active on, the unassigned event of an incomplete
/// unassignment, and the name the entry kind is reported under when it carries no created event.
/// </summary>
internal readonly record struct RestActiveEntry(
    WireCreatedEvent? Created,
    string? WireSynchronizerId,
    WireUnassignedEvent? Unassigned,
    string RawKind);

internal static partial class RestProjectionCore
{
    private const string EmptyTransactionEventRawKind = "empty-event";
    private const string EmptyReassignmentEventRawKind = "empty-reassignment-event";

    public static IEnumerable<TEvent> ProjectUpdate<TArms, TEvent, TMarker, TPayload>(
        WireGetUpdatesResponse response,
        ILogger? logger)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        if (response.Update?.Transaction is { } transaction)
        {
            foreach (var projected in ProjectTransactionEvents<TArms, TEvent, TMarker, TPayload>(transaction, logger))
            {
                yield return projected;
            }
        }
        else if (response.Update?.Reassignment is { } reassignment)
        {
            foreach (var projected in ProjectReassignmentEvents<TArms, TEvent, TMarker, TPayload>(reassignment, logger))
            {
                yield return projected;
            }
        }
        else if (response.Update?.OffsetCheckpoint is { } checkpoint)
        {
            yield return ProjectCheckpoint<TArms, TEvent, TMarker, TPayload>(checkpoint.Offset, logger);
        }
        else
        {
            var variant = response.Update?.TopologyTransaction is not null
                ? nameof(response.Update.TopologyTransaction)
                : "Unknown";
            LogStreamVariantSkipped(logger ?? NullLogger.Instance, typeof(TMarker).Name, variant);
        }
    }

    private static TEvent ProjectCheckpoint<TArms, TEvent, TMarker, TPayload>(string? wireCheckpointOffset, ILogger? logger)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        var wireOffset = WireOffset.FromRequiredText(wireCheckpointOffset);
        return StreamOffsets.TryRequired(wireOffset, out var checkpointOffset)
            ? TArms.Checkpoint(LedgerOffset.At(checkpointOffset))
            : TArms.StreamError(StreamEventClassifier.UndecodableCheckpoint(
                typeof(TMarker).Name, logger, wireOffset.ToMalformedResponse("checkpoint offset")));
    }

    public static IEnumerable<TEvent> ProjectTransactionEvents<TArms, TEvent, TMarker, TPayload>(
        WireTransaction transaction,
        ILogger? logger)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        var wireOffset = WireOffset.FromRequiredText(transaction.Offset);
        if (!StreamOffsets.TryRequired(wireOffset, out var transactionOffset))
        {
            yield return Refused<TArms, TEvent, TMarker, TPayload>(
                StreamEventClassifier.UndecodableUpdateOffset(
                    typeof(TMarker).Name, logger, wireOffset.ToMalformedResponse("transaction offset")));
            yield break;
        }

        var synchronizerId = StreamEventClassifier.Synchronizer(transaction.SynchronizerId);

        foreach (var evt in transaction.Events ?? [])
        {
            TEvent projected;
            try
            {
                projected = ProjectTransactionEvent<TArms, TEvent, TMarker, TPayload>(
                    evt, synchronizerId, transactionOffset);
            }
            catch (Exception decodeFailure) when (StreamEventClassifier.IsNotCancellation(decodeFailure))
            {
                projected = DecodeFailed<TArms, TEvent, TMarker, TPayload>(transactionOffset, logger, decodeFailure);
            }
            yield return projected;
        }
    }

    public static IEnumerable<TEvent> ProjectReassignmentEvents<TArms, TEvent, TMarker, TPayload>(
        WireReassignment reassignment,
        ILogger? logger)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        var wireOffset = WireOffset.FromRequiredText(reassignment.Offset);
        if (!StreamOffsets.TryRequired(wireOffset, out var reassignmentOffset))
        {
            yield return Refused<TArms, TEvent, TMarker, TPayload>(
                StreamEventClassifier.UndecodableUpdateOffset(
                    typeof(TMarker).Name, logger, wireOffset.ToMalformedResponse("reassignment offset")));
            yield break;
        }

        foreach (var evt in reassignment.Events ?? [])
        {
            TEvent projected;
            try
            {
                projected = ProjectReassignmentEvent<TArms, TEvent, TMarker, TPayload>(evt, reassignmentOffset);
            }
            catch (Exception decodeFailure) when (StreamEventClassifier.IsNotCancellation(decodeFailure))
            {
                projected = DecodeFailed<TArms, TEvent, TMarker, TPayload>(reassignmentOffset, logger, decodeFailure);
            }
            yield return projected;
        }
    }

    public static IEnumerable<TEvent> ProjectActiveContractEntry<TArms, TEvent, TMarker, TPayload>(
        GetActiveContractsResponse response,
        ILogger? logger,
        LedgerOffset? snapshotOffset)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        var entry = ActiveEntryOf(response);
        var unassignmentOffset = WireOffset.FromText(entry.Unassigned?.Offset);
        var refusalOffset = StreamOffsets.AcsRefusalOffset(unassignmentOffset, snapshotOffset);

        if (!StreamOffsets.TryAcsEntryResume(
                unassignmentOffset, (snapshotOffset ?? LedgerOffset.Begin).Value, out var entryResumeOffset))
        {
            yield return DecodeFailed<TArms, TEvent, TMarker, TPayload>(
                refusalOffset,
                logger,
                unassignmentOffset.ToMalformedResponse("unassigned event offset"));
            yield break;
        }

        var (createdEvent, admittedAsCreated) = ClassifyActiveCreated<TArms, TEvent, TMarker, TPayload>(
            entry, entryResumeOffset, refusalOffset, logger);
        yield return createdEvent;

        if (!admittedAsCreated || entry.Unassigned is not { } unassigned)
        {
            yield break;
        }
        yield return ClassifyActiveUnassigned<TArms, TEvent, TMarker, TPayload>(unassigned, entryResumeOffset, logger);
    }

    public static RestActiveEntry ActiveEntryOf(GetActiveContractsResponse response) =>
        response.ContractEntry switch
        {
            { JsActiveContract: { } activeContract } =>
                new RestActiveEntry(
                    activeContract.CreatedEvent, activeContract.SynchronizerId, Unassigned: null, "active-contract"),
            { JsIncompleteUnassigned: { } incompleteUnassigned } =>
                new RestActiveEntry(
                    incompleteUnassigned.CreatedEvent,
                    incompleteUnassigned.UnassignedEvent?.Source,
                    incompleteUnassigned.UnassignedEvent,
                    "incomplete-unassigned"),
            { JsIncompleteAssigned: { } incompleteAssigned } =>
                new RestActiveEntry(
                    incompleteAssigned.AssignedEvent?.CreatedEvent,
                    incompleteAssigned.AssignedEvent?.Target,
                    Unassigned: null,
                    "incomplete-assigned"),
            _ => new RestActiveEntry(Created: null, WireSynchronizerId: null, Unassigned: null, "empty-contract-entry"),
        };

    public static (TEvent? Created, TEvent? Archived) ProjectContractLifecycle<TArms, TEvent, TMarker, TPayload>(
        GetEventsByContractIdResponse response)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TEvent : class
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        TEvent? created = null;
        if (response.Created?.CreatedEvent is { } createdEvent)
        {
            RequireTemplateId(createdEvent.TemplateId, nameof(Raw.CreatedEvent), createdEvent.ContractId);
            var (projected, admittedAsCreated) = CreatedFromWire<TArms, TEvent, TMarker, TPayload>(
                createdEvent,
                RequireSynchronizer(response.Created.SynchronizerId, createdEvent.ContractId),
                RestWireConversions.ParseOffset(createdEvent.Offset));
            created = admittedAsCreated
                ? projected
                : throw new ProjectionRefusalException(
                    $"Contract '{createdEvent.ContractId}' cannot be read as {typeof(TMarker).Name}: its interface view is unavailable.");
        }

        TEvent? archived = null;
        if (response.Archived?.ArchivedEvent is { } archivedEvent)
        {
            RequireTemplateId(archivedEvent.TemplateId, nameof(Raw.ArchivedEvent), archivedEvent.ContractId);
            archived = TArms.Archived(
                WireValues.ContractId<TMarker>(archivedEvent.ContractId),
                LedgerOffset.At(RestWireConversions.ParseOffset(archivedEvent.Offset)),
                RequireSynchronizer(response.Archived.SynchronizerId, archivedEvent.ContractId),
                RestWireConversions.ToPartyList(archivedEvent.WitnessParties));
        }

        return (created, archived);
    }

    private static SynchronizerId RequireSynchronizer(string? wireSynchronizerId, string? contractId) =>
        StreamEventClassifier.Synchronizer(wireSynchronizerId)
        ?? throw MalformedResponse.MissingRequiredField($"the event for contract '{contractId}' has no synchronizerId");

    public static (TEvent Event, bool AdmittedAsCreated) CreatedFromWire<TArms, TEvent, TMarker, TPayload>(
        WireCreatedEvent created,
        SynchronizerId synchronizerId,
        long offset)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        if (!TryResolvePayload<TMarker, TPayload>(created, out var payload))
        {
            return (
                TArms.Unclassified(LedgerOffset.At(offset), UnclassifiedKind.InterfaceViewUnavailable, rawKind: null),
                false);
        }
        return (
            TArms.Created(
                WireValues.ContractId<TMarker>(created.ContractId),
                payload,
                ContractKeyOf(created),
                LedgerOffset.At(offset),
                synchronizerId,
                RestWireConversions.ToPartyList(created.WitnessParties)),
            true);
    }

    public static bool TryResolvePayload<TMarker, TPayload>(WireCreatedEvent created, out TPayload payload)
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        if (RestMarkerMatcher<TMarker>.IsInterface)
        {
            if (!RestMarkerMatcher<TMarker>.TryGetInterfaceViewRecord<TPayload>(created, out var view))
            {
                payload = default!;
                return false;
            }
            RestPayloadDecoder.RequireCreateArgument(created);
            payload = MalformedResponse.Decoding(view, TPayload.FromRecord);
            return true;
        }

        payload = MalformedResponse.Decoding(
            RestValueDecoder.ToDamlRecord<TPayload>(RestPayloadDecoder.RequireCreateArgument(created)),
            TPayload.FromRecord);
        return true;
    }

    public static void RequireTemplateId(WireIdentifier? templateId, string wireEventKind, string? contractId)
    {
        if (templateId is null)
        {
            throw MalformedResponse.MissingRequiredField(
                $"{wireEventKind} for contract '{contractId}' has no templateId");
        }
    }

    public static ContractKey? ContractKeyOf(WireCreatedEvent created) =>
        RestPayloadDecoder.ContractKeyOf(created, RestWireConversions.ToRuntimeIdentifier(created.TemplateId));

    private static TEvent ProjectTransactionEvent<TArms, TEvent, TMarker, TPayload>(
        WireEvent evt,
        SynchronizerId? synchronizerId,
        long transactionOffset)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        if (evt?.CreatedEvent is { } created)
        {
            var offset = EventOffsetOrUpdateOffset(created.Offset, transactionOffset);
            RequireTemplateId(created.TemplateId, nameof(Raw.CreatedEvent), created.ContractId);
            var decoded = new DecodedStreamEvent<SynchronizerId>(
                offset,
                RestMarkerMatcher<TMarker>.MatchesCreated(created),
                synchronizerId,
                UnclassifiedKind.CreatedEvent);
            if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
            {
                return Refused<TArms, TEvent, TMarker, TPayload>(refusal);
            }
            return CreatedFromWire<TArms, TEvent, TMarker, TPayload>(created, scope, offset).Event;
        }

        if (evt?.ArchivedEvent is { } archived)
        {
            var offset = EventOffsetOrUpdateOffset(archived.Offset, transactionOffset);
            RequireTemplateId(archived.TemplateId, nameof(Raw.ArchivedEvent), archived.ContractId);
            var decoded = new DecodedStreamEvent<SynchronizerId>(
                offset,
                RestMarkerMatcher<TMarker>.MatchesArchived(archived),
                synchronizerId,
                UnclassifiedKind.ArchivedEvent);
            if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
            {
                return Refused<TArms, TEvent, TMarker, TPayload>(refusal);
            }
            return TArms.Archived(
                WireValues.ContractId<TMarker>(archived.ContractId),
                LedgerOffset.At(offset),
                scope,
                RestWireConversions.ToPartyList(archived.WitnessParties));
        }

        if (evt?.ExercisedEvent is { } exercised)
        {
            var offset = EventOffsetOrUpdateOffset(exercised.Offset, transactionOffset);
            RequireTemplateId(exercised.TemplateId, nameof(Raw.ExercisedEvent), exercised.ContractId);
            var decoded = new DecodedStreamEvent<SynchronizerId>(
                offset,
                RestMarkerMatcher<TMarker>.MatchesExercised(exercised),
                synchronizerId,
                UnclassifiedKind.ExercisedEvent);
            if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
            {
                return Refused<TArms, TEvent, TMarker, TPayload>(refusal);
            }
            var payloads = RestPayloadDecoder.ExercisePayloadsOf(
                exercised, RestWireConversions.ToRuntimeIdentifier(exercised.TemplateId));
            return TArms.Exercised(
                WireValues.ContractId<TMarker>(exercised.ContractId),
                new ChoiceName(exercised.Choice),
                payloads.Argument,
                payloads.Result,
                exercised.Consuming ?? false,
                LedgerOffset.At(offset),
                scope,
                RestWireConversions.ToPartyList(exercised.WitnessParties));
        }

        return TArms.Unclassified(
            LedgerOffset.At(transactionOffset), UnclassifiedKind.Unknown, EmptyTransactionEventRawKind);
    }

    private static TEvent ProjectReassignmentEvent<TArms, TEvent, TMarker, TPayload>(
        WireReassignmentEvent evt,
        long reassignmentOffset)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        if (evt?.JsAssignmentEvent is { } assigned)
        {
            var created = assigned.CreatedEvent;
            if (created is null)
            {
                return TArms.Unclassified(
                    LedgerOffset.At(reassignmentOffset), UnclassifiedKind.AssignedEvent, rawKind: null);
            }
            var createdOffset = EventOffsetOrUpdateOffset(created.Offset, reassignmentOffset);
            RequireTemplateId(created.TemplateId, nameof(Raw.CreatedEvent), created.ContractId);
            var decoded = new DecodedStreamEvent<ReassignmentScope>(
                createdOffset,
                RestMarkerMatcher<TMarker>.MatchesCreated(created),
                StreamEventClassifier.ReassignmentSynchronizers(assigned.Source, assigned.Target),
                UnclassifiedKind.AssignedEvent);
            if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
            {
                return Refused<TArms, TEvent, TMarker, TPayload>(refusal);
            }
            if (!TryResolvePayload<TMarker, TPayload>(created, out var payload))
            {
                return TArms.Unclassified(
                    LedgerOffset.At(createdOffset), UnclassifiedKind.InterfaceViewUnavailable, rawKind: null);
            }
            return TArms.Assigned(
                WireValues.ContractId<TMarker>(created.ContractId),
                payload,
                ContractKeyOf(created),
                LedgerOffset.At(createdOffset),
                scope.Source,
                scope.Target,
                assigned.ReassignmentId ?? string.Empty,
                RestWireConversions.ParseReassignmentCounter(assigned.ReassignmentCounter),
                RestWireConversions.ToPartyList(created.WitnessParties));
        }

        if (evt?.JsUnassignedEvent is { } unassigned)
        {
            var offset = EventOffsetOrUpdateOffset(unassigned.Offset, reassignmentOffset);
            RequireTemplateId(unassigned.TemplateId, nameof(Raw.UnassignedEvent), unassigned.ContractId);
            var decoded = new DecodedStreamEvent<ReassignmentScope>(
                offset,
                RestMarkerMatcher<TMarker>.MatchesUnassigned(unassigned),
                StreamEventClassifier.ReassignmentSynchronizers(unassigned.Source, unassigned.Target),
                UnclassifiedKind.UnassignedEvent);
            if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
            {
                return Refused<TArms, TEvent, TMarker, TPayload>(refusal);
            }
            return UnassignedFromWire<TArms, TEvent, TMarker, TPayload>(unassigned, offset, scope);
        }

        return TArms.Unclassified(
            LedgerOffset.At(reassignmentOffset), UnclassifiedKind.Unknown, EmptyReassignmentEventRawKind);
    }

    private static (TEvent Event, bool AdmittedAsCreated) ClassifyActiveCreated<TArms, TEvent, TMarker, TPayload>(
        RestActiveEntry entry,
        long entryResumeOffset,
        LedgerOffset? refusalOffset,
        ILogger? logger)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        if (entry.Created is not { } created)
        {
            return (
                TArms.Unclassified(LedgerOffset.At(entryResumeOffset), UnclassifiedKind.Unknown, entry.RawKind),
                false);
        }
        var createdWireOffset = WireOffset.FromText(created.Offset);
        if (!StreamOffsets.TryAcsCreatedResume(createdWireOffset, entryResumeOffset, out var offset))
        {
            return (
                DecodeFailed<TArms, TEvent, TMarker, TPayload>(
                    refusalOffset, logger, createdWireOffset.ToMalformedResponse("created event offset")),
                false);
        }
        var decoded = new DecodedStreamEvent<SynchronizerId>(
            offset,
            RestMarkerMatcher<TMarker>.MatchesCreated(created),
            StreamEventClassifier.Synchronizer(entry.WireSynchronizerId),
            UnclassifiedKind.CreatedEvent);
        if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
        {
            return (Refused<TArms, TEvent, TMarker, TPayload>(refusal), false);
        }
        try
        {
            return CreatedFromWire<TArms, TEvent, TMarker, TPayload>(created, scope, offset);
        }
        catch (Exception decodeFailure) when (StreamEventClassifier.IsNotCancellation(decodeFailure))
        {
            return (DecodeFailed<TArms, TEvent, TMarker, TPayload>(offset, logger, decodeFailure), false);
        }
    }

    private static TEvent ClassifyActiveUnassigned<TArms, TEvent, TMarker, TPayload>(
        WireUnassignedEvent unassigned,
        long offset,
        ILogger? logger)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        var decoded = new DecodedStreamEvent<ReassignmentScope>(
            offset,
            MatchesMarker: true,
            StreamEventClassifier.ReassignmentSynchronizers(unassigned.Source, unassigned.Target),
            UnclassifiedKind.UnassignedEvent);
        if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
        {
            return Refused<TArms, TEvent, TMarker, TPayload>(refusal);
        }
        try
        {
            return UnassignedFromWire<TArms, TEvent, TMarker, TPayload>(unassigned, offset, scope);
        }
        catch (Exception decodeFailure) when (StreamEventClassifier.IsNotCancellation(decodeFailure))
        {
            return DecodeFailed<TArms, TEvent, TMarker, TPayload>(offset, logger, decodeFailure);
        }
    }

    private static TEvent UnassignedFromWire<TArms, TEvent, TMarker, TPayload>(
        WireUnassignedEvent unassigned,
        long offset,
        ReassignmentScope scope)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        return TArms.Unassigned(
            WireValues.ContractId<TMarker>(unassigned.ContractId),
            LedgerOffset.At(offset),
            scope.Source,
            scope.Target,
            unassigned.ReassignmentId ?? string.Empty,
            RestWireConversions.ParseReassignmentCounter(unassigned.ReassignmentCounter),
            RestWireConversions.ToPartyList(unassigned.WitnessParties));
    }

    private static TEvent Refused<TArms, TEvent, TMarker, TPayload>(StreamEntryRefusal refusal)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload> =>
        TArms.Unclassified(refusal.Offset, refusal.Kind, rawKind: null);

    private static TEvent DecodeFailed<TArms, TEvent, TMarker, TPayload>(
        LedgerOffset? offset,
        ILogger? logger,
        Exception cause)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload> =>
        Refused<TArms, TEvent, TMarker, TPayload>(
            StreamEventClassifier.DecodeFailure(typeof(TMarker).Name, offset, logger, cause));

    private static TEvent DecodeFailed<TArms, TEvent, TMarker, TPayload>(
        long offset,
        ILogger? logger,
        Exception cause)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload> =>
        Refused<TArms, TEvent, TMarker, TPayload>(
            StreamEventClassifier.DecodeFailure(typeof(TMarker).Name, offset, logger, cause));

    private static long EventOffsetOrUpdateOffset(string? wireEventOffset, long updateOffset) =>
        StreamOffsets.EventOrUpdate(
            wireEventOffset is null ? null : RestWireConversions.ParseOffset(wireEventOffset), updateOffset);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Subscribe stream for {TemplateType} skipped variant {Variant}")]
    private static partial void LogStreamVariantSkipped(ILogger logger, string templateType, string variant);
}
