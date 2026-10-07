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
            yield return TArms.Checkpoint(LedgerOffset.At(RestWireConversions.ParseOffset(checkpoint.Offset)));
        }
        else
        {
            var variant = response.Update?.TopologyTransaction is not null
                ? nameof(response.Update.TopologyTransaction)
                : "Unknown";
            LogStreamVariantSkipped(logger ?? NullLogger.Instance, typeof(TMarker).Name, variant);
        }
    }

    public static IEnumerable<TEvent> ProjectTransactionEvents<TArms, TEvent, TMarker, TPayload>(
        WireTransaction transaction,
        ILogger? logger)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        if (!RestWireConversions.TryParseOffset(transaction.Offset, out var transactionOffset))
        {
            yield return Refused<TArms, TEvent, TMarker, TPayload>(
                UnparseableOffsetRefusal(typeof(TMarker).Name, transaction.Offset, logger));
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
        if (!RestWireConversions.TryParseOffset(reassignment.Offset, out var reassignmentOffset))
        {
            yield return Refused<TArms, TEvent, TMarker, TPayload>(
                UnparseableOffsetRefusal(typeof(TMarker).Name, reassignment.Offset, logger));
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
        var unassignmentWireOffset = entry.Unassigned?.Offset;

        if (!TryParseOptionalOffset(unassignmentWireOffset, snapshotOffset, out var entryResumeOffset))
        {
            yield return Refused<TArms, TEvent, TMarker, TPayload>(UnparseableOffsetInSnapshotRefusal(
                typeof(TMarker).Name, unassignmentWireOffset, snapshotOffset, logger));
            yield break;
        }

        var (createdEvent, admittedAsCreated) = ClassifyActiveCreated<TArms, TEvent, TMarker, TPayload>(
            entry, entryResumeOffset, snapshotOffset, logger);
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

    public static (TEvent Event, bool AdmittedAsCreated) CreatedFromWire<TArms, TEvent, TMarker, TPayload>(
        WireCreatedEvent created,
        SynchronizerId synchronizerId,
        long offset)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        var contractId = created.ContractId
            ?? throw MalformedResponse.MissingRequiredField(
                $"{nameof(Raw.CreatedEvent)} at offset {offset} has no contractId");
        if (!TryResolvePayload<TMarker, TPayload>(created, out var payload))
        {
            return (
                TArms.Unclassified(LedgerOffset.At(offset), UnclassifiedKind.InterfaceViewUnavailable, rawKind: null),
                false);
        }
        return (
            TArms.Created(
                RestWireConversions.ToContractId<TMarker>(contractId),
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
            var archivedContractId = archived.ContractId
                ?? throw new InvalidOperationException("Archived event has no contract id.");
            return TArms.Archived(
                RestWireConversions.ToContractId<TMarker>(archivedContractId),
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
            var exercisedContractId = exercised.ContractId
                ?? throw new InvalidOperationException("Exercised event has no contract id.");
            var payloads = RestPayloadDecoder.ExercisePayloadsOf(
                exercised, RestWireConversions.ToRuntimeIdentifier(exercised.TemplateId));
            return TArms.Exercised(
                RestWireConversions.ToContractId<TMarker>(exercisedContractId),
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
            var assignedContractId = created.ContractId
                ?? throw new InvalidOperationException("Assigned event's created event has no contract id.");
            if (!TryResolvePayload<TMarker, TPayload>(created, out var payload))
            {
                return TArms.Unclassified(
                    LedgerOffset.At(createdOffset), UnclassifiedKind.InterfaceViewUnavailable, rawKind: null);
            }
            return TArms.Assigned(
                RestWireConversions.ToContractId<TMarker>(assignedContractId),
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
        LedgerOffset? snapshotOffset,
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
        if (created.Offset is not null && !RestWireConversions.TryParseOffset(created.Offset, out _))
        {
            return (
                Refused<TArms, TEvent, TMarker, TPayload>(UnparseableOffsetInSnapshotRefusal(
                    typeof(TMarker).Name, created.Offset, snapshotOffset, logger)),
                false);
        }
        var offset = EventOffsetOrUpdateOffset(created.Offset, entryResumeOffset);
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
        var unassignedContractId = unassigned.ContractId
            ?? throw new InvalidOperationException("Unassigned event has no contract id.");
        return TArms.Unassigned(
            RestWireConversions.ToContractId<TMarker>(unassignedContractId),
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
        long offset,
        ILogger? logger,
        Exception cause)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload> =>
        Refused<TArms, TEvent, TMarker, TPayload>(
            StreamEventClassifier.DecodeFailure(typeof(TMarker).Name, offset, logger, cause));

    private static long EventOffsetOrUpdateOffset(long eventOffset, long updateOffset) =>
        eventOffset > 0 ? eventOffset : updateOffset;

    private static long EventOffsetOrUpdateOffset(string? wireEventOffset, long updateOffset) =>
        wireEventOffset is null
            ? updateOffset
            : EventOffsetOrUpdateOffset(RestWireConversions.ParseOffset(wireEventOffset), updateOffset);

    private static bool TryParseOptionalOffset(string? wireOffset, LedgerOffset? snapshotOffset, out long offset)
    {
        var snapshotResumeOffset = (snapshotOffset ?? LedgerOffset.Begin).Value;
        offset = snapshotResumeOffset;
        if (wireOffset is null)
        {
            return true;
        }
        if (!RestWireConversions.TryParseOffset(wireOffset, out var parsedOffset))
        {
            return false;
        }
        offset = EventOffsetOrUpdateOffset(parsedOffset, snapshotResumeOffset);
        return true;
    }

    private static StreamEntryRefusal UnparseableOffsetRefusal(string markerName, string? wireOffset, ILogger? logger)
    {
        LogOffsetParseFailed(logger ?? NullLogger.Instance, markerName, wireOffset);
        return new StreamEntryRefusal(null, UnclassifiedKind.DecodeFailure);
    }

    private static StreamEntryRefusal UnparseableOffsetInSnapshotRefusal(
        string markerName,
        string? wireOffset,
        LedgerOffset? snapshotOffset,
        ILogger? logger)
    {
        if (snapshotOffset is not { } resumeOffset)
        {
            return UnparseableOffsetRefusal(markerName, wireOffset, logger);
        }
        LogSnapshotOffsetParseFailed(logger ?? NullLogger.Instance, markerName, wireOffset, resumeOffset.Value);
        return new StreamEntryRefusal(resumeOffset, UnclassifiedKind.DecodeFailure);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not parse the wire offset '{WireOffset}' on the {TemplateType} stream — surfaced as Unclassified (decode-failure) carrying no offset, so a consumer must not persist this event as a resume point")]
    private static partial void LogOffsetParseFailed(ILogger logger, string templateType, string? wireOffset);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not parse the wire offset '{WireOffset}' on the {TemplateType} active-contract snapshot — surfaced as Unclassified (decode-failure) carrying the snapshot offset {SnapshotOffset}, so a consumer resuming from this event resumes where the snapshot ended instead of re-reading the stream from the start")]
    private static partial void LogSnapshotOffsetParseFailed(ILogger logger, string templateType, string? wireOffset, long snapshotOffset);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Subscribe stream for {TemplateType} skipped variant {Variant}")]
    private static partial void LogStreamVariantSkipped(ILogger logger, string templateType, string variant);
}
