// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Streams;
using Canton.Ledger.Kernel.Wire;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Grpc;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;

namespace Canton.Ledger.Grpc.Client;

internal static partial class GrpcProjectionCore
{
    public static IEnumerable<TEvent> ProjectUpdate<TArms, TEvent, TMarker, TPayload>(
        GetUpdatesResponse response,
        ILogger? logger)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        switch (response.UpdateCase)
        {
            case GetUpdatesResponse.UpdateOneofCase.Transaction:
                foreach (var projected in ProjectTransactionEvents<TArms, TEvent, TMarker, TPayload>(
                    response.Transaction, logger))
                {
                    yield return projected;
                }
                break;
            case GetUpdatesResponse.UpdateOneofCase.OffsetCheckpoint:
                yield return TArms.Checkpoint(LedgerOffset.At(response.OffsetCheckpoint.Offset));
                break;
            case GetUpdatesResponse.UpdateOneofCase.Reassignment:
                foreach (var projected in ProjectReassignmentEvents<TArms, TEvent, TMarker, TPayload>(
                    response.Reassignment, logger))
                {
                    yield return projected;
                }
                break;
            default:
                LogStreamVariantSkipped(logger ?? NullLogger.Instance, typeof(TMarker).Name, response.UpdateCase);
                break;
        }
    }

    public static IEnumerable<TEvent> ProjectTransactionEvents<TArms, TEvent, TMarker, TPayload>(
        Transaction transaction,
        ILogger? logger)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        var synchronizerId = StreamEventClassifier.Synchronizer(transaction.SynchronizerId);
        foreach (var evt in transaction.Events)
        {
            TEvent projected;
            try
            {
                projected = ProjectTransactionEvent<TArms, TEvent, TMarker, TPayload>(
                    evt, synchronizerId, transaction.Offset);
            }
            catch (Exception decodeFailure) when (StreamEventClassifier.IsNotCancellation(decodeFailure))
            {
                projected = DecodeFailed<TArms, TEvent, TMarker, TPayload>(transaction.Offset, logger, decodeFailure);
            }
            yield return projected;
        }
    }

    public static IEnumerable<TEvent> ProjectReassignmentEvents<TArms, TEvent, TMarker, TPayload>(
        Reassignment reassignment,
        ILogger? logger)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        foreach (var evt in reassignment.Events)
        {
            TEvent projected;
            try
            {
                projected = ProjectReassignmentEvent<TArms, TEvent, TMarker, TPayload>(evt, reassignment.Offset);
            }
            catch (Exception decodeFailure) when (StreamEventClassifier.IsNotCancellation(decodeFailure))
            {
                projected = DecodeFailed<TArms, TEvent, TMarker, TPayload>(reassignment.Offset, logger, decodeFailure);
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
        var snapshotResumeOffset = (snapshotOffset ?? LedgerOffset.Begin).Value;
        var (created, wireSynchronizerId) = ActiveEntryOf(response);
        var entryResumeOffset = UnassignmentOffsetOr(response.IncompleteUnassigned, snapshotResumeOffset);

        var (createdEvent, admittedAsCreated) = ClassifyActiveCreated<TArms, TEvent, TMarker, TPayload>(
            response.ContractEntryCase, created, wireSynchronizerId, entryResumeOffset, logger);
        yield return createdEvent;

        if (!admittedAsCreated || response.IncompleteUnassigned?.UnassignedEvent is not { } unassigned)
        {
            yield break;
        }
        yield return ClassifyActiveUnassigned<TArms, TEvent, TMarker, TPayload>(
            unassigned, UnassignedOffsetOr(unassigned, snapshotResumeOffset), logger);
    }

    public static (ProtoCreatedEvent? Created, string? WireSynchronizerId) ActiveEntryOf(
        GetActiveContractsResponse response) =>
        response.ContractEntryCase switch
        {
            GetActiveContractsResponse.ContractEntryOneofCase.ActiveContract =>
                (response.ActiveContract?.CreatedEvent, response.ActiveContract?.SynchronizerId),
            GetActiveContractsResponse.ContractEntryOneofCase.IncompleteUnassigned =>
                (response.IncompleteUnassigned?.CreatedEvent, response.IncompleteUnassigned?.UnassignedEvent?.Source),
            GetActiveContractsResponse.ContractEntryOneofCase.IncompleteAssigned =>
                (response.IncompleteAssigned?.AssignedEvent?.CreatedEvent, response.IncompleteAssigned?.AssignedEvent?.Target),
            _ => (null, null),
        };

    public static (TEvent Event, bool AdmittedAsCreated) CreatedFromProto<TArms, TEvent, TMarker, TPayload>(
        ProtoCreatedEvent created,
        SynchronizerId synchronizerId,
        long offset)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        if (!TryResolvePayload<TMarker, TPayload>(created, out var payload))
        {
            return (
                TArms.Unclassified(
                    LedgerWireConversions.ToLedgerOffset(offset), UnclassifiedKind.InterfaceViewUnavailable, rawKind: null),
                false);
        }
        return (
            TArms.Created(
                LedgerWireConversions.ToContractId<TMarker>(created.ContractId),
                payload,
                ContractKeyOf(created),
                LedgerWireConversions.ToLedgerOffset(offset),
                synchronizerId,
                LedgerWireConversions.ToPartyList(created.WitnessParties)),
            true);
    }

    public static bool TryResolvePayload<TMarker, TPayload>(ProtoCreatedEvent created, out TPayload payload)
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        if (GrpcMarkerMatcher<TMarker>.IsInterface)
        {
            if (!GrpcMarkerMatcher<TMarker>.TryGetInterfaceViewRecord(created, out var view))
            {
                payload = default!;
                return false;
            }
            _ = RequireCreateArguments(created);
            payload = MalformedResponse.Decoding(view, TPayload.FromRecord);
            return true;
        }

        var createArguments = RequireCreateArguments(created);
        payload = MalformedResponse.Decoding(
            createArguments, arguments => TPayload.FromRecord(DamlValueConverter.FromProtoRecord(arguments)));
        return true;
    }

    public static void RequireTemplateId(ProtoIdentifier? templateId, string wireEventKind, string contractId)
    {
        if (templateId is null)
        {
            throw MalformedResponse.MissingRequiredField(
                $"{wireEventKind} for contract '{contractId}' has no template_id");
        }
    }

    public static ContractKey? ContractKeyOf(ProtoCreatedEvent created)
    {
        if (created.ContractKey is null)
        {
            return null;
        }

        return new ContractKey(
            GrpcValueDecoder.ToDamlValue(created.ContractKey),
            created.TemplateId is null ? null : LedgerWireConversions.ToRuntimeIdentifier(created.TemplateId))
        {
            KeyHash = LedgerWireConversions.ToKeyHash(created.ContractKeyHash),
        };
    }

    private static TEvent ProjectTransactionEvent<TArms, TEvent, TMarker, TPayload>(
        Event evt,
        SynchronizerId? synchronizerId,
        long transactionOffset)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        switch (evt.EventCase)
        {
            case Event.EventOneofCase.Created:
                {
                    var created = evt.Created;
                    RequireTemplateId(created.TemplateId, nameof(Com.Daml.Ledger.Api.V2.CreatedEvent), created.ContractId);
                    var eventOffset = EventOffsetOrUpdateOffset(created.Offset, transactionOffset);
                    var decoded = new DecodedStreamEvent<SynchronizerId>(
                        eventOffset,
                        GrpcMarkerMatcher<TMarker>.MatchesProtoCreated(created),
                        synchronizerId,
                        UnclassifiedKind.CreatedEvent);
                    if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
                    {
                        return Refused<TArms, TEvent, TMarker, TPayload>(refusal);
                    }
                    return CreatedFromProto<TArms, TEvent, TMarker, TPayload>(created, scope, eventOffset).Event;
                }
            case Event.EventOneofCase.Archived:
                {
                    var archived = evt.Archived;
                    RequireTemplateId(archived.TemplateId, nameof(Com.Daml.Ledger.Api.V2.ArchivedEvent), archived.ContractId);
                    var eventOffset = EventOffsetOrUpdateOffset(archived.Offset, transactionOffset);
                    var decoded = new DecodedStreamEvent<SynchronizerId>(
                        eventOffset,
                        GrpcMarkerMatcher<TMarker>.MatchesProtoArchived(archived),
                        synchronizerId,
                        UnclassifiedKind.ArchivedEvent);
                    if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
                    {
                        return Refused<TArms, TEvent, TMarker, TPayload>(refusal);
                    }
                    return TArms.Archived(
                        LedgerWireConversions.ToContractId<TMarker>(archived.ContractId),
                        LedgerWireConversions.ToLedgerOffset(eventOffset),
                        scope,
                        LedgerWireConversions.ToPartyList(archived.WitnessParties));
                }
            case Event.EventOneofCase.Exercised:
                {
                    var exercised = evt.Exercised;
                    RequireTemplateId(exercised.TemplateId, nameof(Com.Daml.Ledger.Api.V2.ExercisedEvent), exercised.ContractId);
                    var eventOffset = EventOffsetOrUpdateOffset(exercised.Offset, transactionOffset);
                    var decoded = new DecodedStreamEvent<SynchronizerId>(
                        eventOffset,
                        GrpcMarkerMatcher<TMarker>.MatchesProtoExercised(exercised),
                        synchronizerId,
                        UnclassifiedKind.ExercisedEvent);
                    if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
                    {
                        return Refused<TArms, TEvent, TMarker, TPayload>(refusal);
                    }
                    var argument = DamlValueConverter.FromProtoValue(
                        GrpcTransactionResultProjector.RequireChoiceArgument(exercised));
                    var result = DamlValueConverter.FromProtoValue(
                        GrpcTransactionResultProjector.RequireExerciseResult(exercised));
                    return TArms.Exercised(
                        LedgerWireConversions.ToContractId<TMarker>(exercised.ContractId),
                        new ChoiceName(exercised.Choice),
                        argument,
                        result,
                        exercised.Consuming,
                        LedgerWireConversions.ToLedgerOffset(eventOffset),
                        scope,
                        LedgerWireConversions.ToPartyList(exercised.WitnessParties));
                }
            default:
                return TArms.Unclassified(
                    LedgerWireConversions.ToLedgerOffset(transactionOffset), UnclassifiedKind.Unknown, evt.EventCase.ToString());
        }
    }

    private static TEvent ProjectReassignmentEvent<TArms, TEvent, TMarker, TPayload>(
        ReassignmentEvent evt,
        long reassignmentOffset)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        switch (evt.EventCase)
        {
            case ReassignmentEvent.EventOneofCase.Assigned:
                {
                    var assigned = evt.Assigned;
                    var created = assigned.CreatedEvent;
                    if (created is null)
                    {
                        return TArms.Unclassified(
                            LedgerWireConversions.ToLedgerOffset(reassignmentOffset), UnclassifiedKind.AssignedEvent, rawKind: null);
                    }
                    RequireTemplateId(created.TemplateId, nameof(Com.Daml.Ledger.Api.V2.CreatedEvent), created.ContractId);
                    var eventOffset = EventOffsetOrUpdateOffset(created.Offset, reassignmentOffset);
                    var decoded = new DecodedStreamEvent<ReassignmentScope>(
                        eventOffset,
                        GrpcMarkerMatcher<TMarker>.MatchesProtoCreated(created),
                        StreamEventClassifier.ReassignmentSynchronizers(assigned.Source, assigned.Target),
                        UnclassifiedKind.AssignedEvent);
                    if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
                    {
                        return Refused<TArms, TEvent, TMarker, TPayload>(refusal);
                    }
                    if (!TryResolvePayload<TMarker, TPayload>(created, out var payload))
                    {
                        return TArms.Unclassified(
                            LedgerWireConversions.ToLedgerOffset(eventOffset), UnclassifiedKind.InterfaceViewUnavailable, rawKind: null);
                    }
                    return TArms.Assigned(
                        LedgerWireConversions.ToContractId<TMarker>(created.ContractId),
                        payload,
                        ContractKeyOf(created),
                        LedgerWireConversions.ToLedgerOffset(eventOffset),
                        scope.Source,
                        scope.Target,
                        assigned.ReassignmentId,
                        (long)assigned.ReassignmentCounter,
                        LedgerWireConversions.ToPartyList(created.WitnessParties));
                }
            case ReassignmentEvent.EventOneofCase.Unassigned:
                {
                    var unassigned = evt.Unassigned;
                    RequireTemplateId(unassigned.TemplateId, nameof(Com.Daml.Ledger.Api.V2.UnassignedEvent), unassigned.ContractId);
                    var eventOffset = EventOffsetOrUpdateOffset(unassigned.Offset, reassignmentOffset);
                    var decoded = new DecodedStreamEvent<ReassignmentScope>(
                        eventOffset,
                        GrpcMarkerMatcher<TMarker>.MatchesProtoUnassigned(unassigned),
                        StreamEventClassifier.ReassignmentSynchronizers(unassigned.Source, unassigned.Target),
                        UnclassifiedKind.UnassignedEvent);
                    if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
                    {
                        return Refused<TArms, TEvent, TMarker, TPayload>(refusal);
                    }
                    return UnassignedFromProto<TArms, TEvent, TMarker, TPayload>(unassigned, eventOffset, scope);
                }
            default:
                return TArms.Unclassified(
                    LedgerWireConversions.ToLedgerOffset(reassignmentOffset), UnclassifiedKind.Unknown, evt.EventCase.ToString());
        }
    }

    private static (TEvent Event, bool AdmittedAsCreated) ClassifyActiveCreated<TArms, TEvent, TMarker, TPayload>(
        GetActiveContractsResponse.ContractEntryOneofCase entryCase,
        ProtoCreatedEvent? created,
        string? wireSynchronizerId,
        long entryResumeOffset,
        ILogger? logger)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload>
    {
        if (created is null)
        {
            return (
                TArms.Unclassified(
                    LedgerWireConversions.ToLedgerOffset(entryResumeOffset), UnclassifiedKind.Unknown, entryCase.ToString()),
                false);
        }
        var resumeOffset = created.Offset > 0 ? created.Offset : entryResumeOffset;
        var decoded = new DecodedStreamEvent<SynchronizerId>(
            resumeOffset,
            GrpcMarkerMatcher<TMarker>.MatchesProtoCreated(created),
            StreamEventClassifier.Synchronizer(wireSynchronizerId),
            UnclassifiedKind.CreatedEvent);
        if (!StreamEventClassifier.TryAdmit(decoded, out var scope, out var refusal))
        {
            return (Refused<TArms, TEvent, TMarker, TPayload>(refusal), false);
        }
        try
        {
            return CreatedFromProto<TArms, TEvent, TMarker, TPayload>(
                created, scope, EventOffsetOrUpdateOffset(created.Offset, entryResumeOffset));
        }
        catch (Exception decodeFailure) when (StreamEventClassifier.IsNotCancellation(decodeFailure))
        {
            return (DecodeFailed<TArms, TEvent, TMarker, TPayload>(resumeOffset, logger, decodeFailure), false);
        }
    }

    private static TEvent ClassifyActiveUnassigned<TArms, TEvent, TMarker, TPayload>(
        UnassignedEvent unassigned,
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
            return UnassignedFromProto<TArms, TEvent, TMarker, TPayload>(unassigned, offset, scope);
        }
        catch (Exception decodeFailure) when (StreamEventClassifier.IsNotCancellation(decodeFailure))
        {
            return DecodeFailed<TArms, TEvent, TMarker, TPayload>(offset, logger, decodeFailure);
        }
    }

    private static TEvent UnassignedFromProto<TArms, TEvent, TMarker, TPayload>(
        UnassignedEvent unassigned,
        long offset,
        ReassignmentScope scope)
        where TArms : IStreamArms<TEvent, TMarker, TPayload>
        where TMarker : IDamlType
        where TPayload : IDamlRecord<TPayload> =>
        TArms.Unassigned(
            LedgerWireConversions.ToContractId<TMarker>(unassigned.ContractId),
            LedgerWireConversions.ToLedgerOffset(offset),
            scope.Source,
            scope.Target,
            unassigned.ReassignmentId,
            (long)unassigned.ReassignmentCounter,
            LedgerWireConversions.ToPartyList(unassigned.WitnessParties));

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

    private static Record RequireCreateArguments(ProtoCreatedEvent created) =>
        created.CreateArguments
        ?? throw MalformedResponse.MissingRequiredField(
            $"CreatedEvent for contract '{created.ContractId}' has no create_arguments");

    private static long EventOffsetOrUpdateOffset(long eventOffset, long updateOffset) =>
        eventOffset == 0 ? updateOffset : eventOffset;

    private static long UnassignmentOffsetOr(IncompleteUnassigned? entry, long snapshotResumeOffset) =>
        entry?.UnassignedEvent is { } unassigned
            ? UnassignedOffsetOr(unassigned, snapshotResumeOffset)
            : snapshotResumeOffset;

    private static long UnassignedOffsetOr(UnassignedEvent unassigned, long snapshotResumeOffset) =>
        unassigned.Offset > 0 ? unassigned.Offset : snapshotResumeOffset;

    [LoggerMessage(Level = LogLevel.Debug, Message = "Subscribe stream for {TemplateType} skipped variant {Variant}")]
    private static partial void LogStreamVariantSkipped(ILogger logger, string templateType, GetUpdatesResponse.UpdateOneofCase variant);
}
