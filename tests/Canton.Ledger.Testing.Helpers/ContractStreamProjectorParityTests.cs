// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using Xunit;
using InterfaceEvent = Daml.Runtime.Streams.InterfaceStreamEvent<
    Canton.Ledger.Testing.Helpers.InterfaceMarker, Canton.Ledger.Testing.Helpers.InterfaceMarkerView>;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// Behavioural parity suite over the active-contract classification policy, run against every
/// transport's <c>GrpcContractStreamProjector</c> or <c>RestContractStreamProjector</c> through one shared set of test bodies. It pins the
/// no-silent-drop decision tree that both transports delegate to a single
/// implementation: which shape becomes which <see cref="UnclassifiedKind"/>, the missing-
/// synchronizer rule, the order the two are reported in, how many events each entry shape
/// projects to, and the offset each projected event is reported at — including the fallback an
/// entry that carries no offset of its own lands on when no snapshot offset is supplied.
///
/// A second lane runs the same entry shapes subscribed as an <em>interface</em> marker, pinning
/// that both transports project the participant-computed interface view — never the implementing
/// template's create argument — onto an interface row, and agree on which view shapes leave the
/// row unclassified. Wire decoding stays per-transport and is covered by each transport's own
/// suite.
///
/// Two further lanes run the same policy over the transaction and reassignment streams, where each
/// update carries a list of events rather than a single entry: which event shape becomes which
/// <see cref="UnclassifiedKind"/>, the missing-synchronizer rule over a single synchronizer and
/// over a source/target pair, the offset each classified event is reported at, and that an event
/// the transport cannot decode is surfaced in band, at the containing update's offset, while the
/// remaining events keep projecting.
///
/// The same two update lanes also run subscribed as an <em>interface</em> marker, pinning the
/// Interface read family where the Contract family is already pinned: the null arm, the missing-
/// synchronizer rule, a marker mismatch, an unavailable or argument-less view, a payload the
/// transport cannot decode, an assigned event without a created event, and the unassigned half.
/// Every arm of a matching interface event is pinned field by field: witness parties, contract key,
/// the exercised argument against its result, and a source distinct from its target.
/// </summary>
public abstract class ContractStreamProjectorParityTests
{
    /// <summary>
    /// Renders <paramref name="scenario"/> into this transport's wire shape, runs it through that
    /// transport's active-contract projection, and returns the projected events in order.
    /// </summary>
    protected abstract Task<IReadOnlyList<ContractStreamEvent<TemplateMarker>>> ProjectActiveContractEntryAsync(
        ActiveContractScenario scenario);

    /// <summary>
    /// Renders <paramref name="scenario"/> into this transport's wire shape, runs it through that
    /// transport's active-contract projection subscribed as an interface marker, and returns the
    /// projected events in order.
    /// </summary>
    protected abstract Task<IReadOnlyList<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>>> ProjectActiveContractEntryAsInterfaceAsync(
        ActiveContractScenario scenario);

    /// <summary>
    /// Renders <paramref name="scenario"/> into this transport's wire shape, runs it through that
    /// transport's transaction projection, and returns the projected events in order.
    /// </summary>
    protected abstract Task<IReadOnlyList<ContractStreamEvent<TemplateMarker>>> ProjectTransactionEventsAsync(
        TransactionEventScenario scenario);

    /// <summary>
    /// Renders <paramref name="scenario"/> into this transport's wire shape, runs it through that
    /// transport's reassignment projection, and returns the projected events in order.
    /// </summary>
    protected abstract Task<IReadOnlyList<ContractStreamEvent<TemplateMarker>>> ProjectReassignmentEventsAsync(
        ReassignmentEventScenario scenario);

    /// <summary>
    /// Renders <paramref name="scenario"/> into this transport's wire shape, runs it through that
    /// transport's transaction projection subscribed as an interface marker, logging to
    /// <paramref name="logger"/>, and returns the projected events in order.
    /// </summary>
    protected abstract Task<IReadOnlyList<InterfaceEvent>> ProjectTransactionEventsAsInterfaceAsync(
        TransactionEventScenario scenario, ILogger logger);

    /// <summary>
    /// Renders <paramref name="scenario"/> into this transport's wire shape, runs it through that
    /// transport's reassignment projection subscribed as an interface marker, logging to
    /// <paramref name="logger"/>, and returns the projected events in order.
    /// </summary>
    protected abstract Task<IReadOnlyList<InterfaceEvent>> ProjectReassignmentEventsAsInterfaceAsync(
        ReassignmentEventScenario scenario, ILogger logger);

    private async Task<ContractStreamEvent<TemplateMarker>> ProjectOnlyEventAsync(ActiveContractScenario scenario)
    {
        var projected = await ProjectActiveContractEntryAsync(scenario);
        return projected.Should().ContainSingle().Subject;
    }

    private static void ShouldBeTheCreatedScopedToTheEntrysSynchronizer(ContractStreamEvent<TemplateMarker> projected)
    {
        var created = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject;
        created.ContractId.Value.Should().Be(ActiveContractScenario.ContractId);
        created.Offset.Should().Be(LedgerOffset.At(ActiveContractScenario.CreatedOffset));
        created.SynchronizerId.Should().Be(new SynchronizerId(ActiveContractScenario.SynchronizerId));
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active)]
    [InlineData(ActiveContractEntry.IncompleteAssigned)]
    public async Task ProjectActiveContractEntry_classifies_a_matching_entry_as_Created_scoped_to_the_entrys_synchronizer(
        ActiveContractEntry entry)
    {
        var projected = await ProjectOnlyEventAsync(new ActiveContractScenario { Entry = entry });

        ShouldBeTheCreatedScopedToTheEntrysSynchronizer(projected);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_follows_a_matching_IncompleteUnassigneds_Created_with_its_Unassigned()
    {
        var projected = await ProjectActiveContractEntryAsync(
            new ActiveContractScenario { Entry = ActiveContractEntry.IncompleteUnassigned });

        projected.Should().HaveCount(2);
        ShouldBeTheCreatedScopedToTheEntrysSynchronizer(projected[0]);
        var unassigned = projected[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unassigned>().Subject;
        unassigned.ContractId.Value.Should().Be(ActiveContractScenario.ContractId);
        unassigned.Offset.Should().Be(LedgerOffset.At(ActiveContractScenario.UnassignedOffset));
        unassigned.Source.Should().Be(new SynchronizerId(ActiveContractScenario.SynchronizerId));
        unassigned.Target.Should().Be(new SynchronizerId(ActiveContractScenario.CounterpartSynchronizerId));
        unassigned.ReassignmentId.Should().Be(ActiveContractScenario.ReassignmentId);
        unassigned.ReassignmentCounter.Should().Be(ActiveContractScenario.ReassignmentCounter);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_surfaces_an_undecodable_Unassigned_as_Unclassified_DecodeFailure_after_its_Created()
    {
        var projected = await ProjectActiveContractEntryAsync(new ActiveContractScenario
        {
            Entry = ActiveContractEntry.IncompleteUnassigned,
            OmitUnassignedContractId = true,
        });

        projected.Should().HaveCount(2);
        ShouldBeTheCreatedScopedToTheEntrysSynchronizer(projected[0]);
        var unclassified = projected[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.Offset.Should().Be(LedgerOffset.At(ActiveContractScenario.UnassignedOffset));
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned)]
    [InlineData(ActiveContractEntry.IncompleteAssigned)]
    public async Task ProjectActiveContractEntry_surfaces_an_entry_that_does_not_match_the_marker_as_Unclassified_CreatedEvent(
        ActiveContractEntry entry)
    {
        var projected = await ProjectOnlyEventAsync(new ActiveContractScenario
        {
            Entry = entry,
            EntityName = ActiveContractScenario.OtherEntityName,
        });

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.CreatedEvent);
        unclassified.Offset.Should().Be(LedgerOffset.At(ActiveContractScenario.CreatedOffset));
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned)]
    [InlineData(ActiveContractEntry.IncompleteAssigned)]
    public async Task ProjectActiveContractEntry_surfaces_an_entry_carrying_no_synchronizer_as_Unclassified_MissingSynchronizerId(
        ActiveContractEntry entry)
    {
        var projected = await ProjectOnlyEventAsync(new ActiveContractScenario { Entry = entry, Synchronizer = null });

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
        unclassified.Offset.Should().Be(LedgerOffset.At(ActiveContractScenario.CreatedOffset));
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned)]
    [InlineData(ActiveContractEntry.IncompleteAssigned)]
    public async Task ProjectActiveContractEntry_reports_a_marker_mismatch_ahead_of_a_missing_synchronizer(
        ActiveContractEntry entry)
    {
        var projected = await ProjectOnlyEventAsync(new ActiveContractScenario
        {
            Entry = entry,
            EntityName = ActiveContractScenario.OtherEntityName,
            Synchronizer = null,
        });

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.CreatedEvent);
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned)]
    [InlineData(ActiveContractEntry.IncompleteAssigned)]
    public async Task ProjectActiveContractEntry_surfaces_an_entry_without_a_created_event_as_Unclassified_Unknown(
        ActiveContractEntry entry)
    {
        var projected = await ProjectOnlyEventAsync(new ActiveContractScenario { Entry = entry, OmitCreatedEvent = true });

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.Unknown);
        unclassified.RawKind.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned)]
    [InlineData(ActiveContractEntry.IncompleteAssigned)]
    public async Task ProjectActiveContractEntry_reports_an_entry_without_a_created_event_at_the_offset_the_entry_carries(
        ActiveContractEntry entry)
    {
        var expectedOffset = entry == ActiveContractEntry.IncompleteUnassigned
            ? LedgerOffset.At(ActiveContractScenario.UnassignedOffset)
            : LedgerOffset.Begin;

        var projected = await ProjectOnlyEventAsync(new ActiveContractScenario { Entry = entry, OmitCreatedEvent = true });

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().Be(expectedOffset);
    }

    private Task<IReadOnlyList<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>>> ProjectImplementingEntryAsync(
        ActiveContractEntry entry, InterfaceViewRendering interfaceView) =>
        ProjectActiveContractEntryAsInterfaceAsync(new ActiveContractScenario
        {
            Entry = entry,
            EntityName = ActiveContractScenario.OtherEntityName,
            InterfaceView = interfaceView,
        });

    private static void ShouldCarryTheComputedInterfaceView(
        InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView> projected)
    {
        var created = projected.Should()
            .BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Created>().Subject;
        created.ContractId.Value.Should().Be(ActiveContractScenario.ContractId);
        created.Offset.Should().Be(LedgerOffset.At(ActiveContractScenario.CreatedOffset));
        created.SynchronizerId.Should().Be(new SynchronizerId(ActiveContractScenario.SynchronizerId));
        created.Payload.Amount.Should().Be(ActiveContractScenario.InterfaceViewValue);
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active)]
    [InlineData(ActiveContractEntry.IncompleteAssigned)]
    public async Task ProjectActiveContractEntry_decodes_the_participant_computed_view_for_an_interface_marker(
        ActiveContractEntry entry)
    {
        var projected = await ProjectImplementingEntryAsync(entry, InterfaceViewRendering.Computed);

        ShouldCarryTheComputedInterfaceView(projected.Should().ContainSingle().Subject);
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active)]
    [InlineData(ActiveContractEntry.IncompleteAssigned)]
    public async Task ProjectActiveContractEntry_never_projects_the_implementing_templates_create_argument_onto_an_interface_row(
        ActiveContractEntry entry)
    {
        var projected = await ProjectImplementingEntryAsync(entry, InterfaceViewRendering.Computed);

        var created = projected.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Created>().Subject;
        created.Payload.Amount.Should().NotBe(ActiveContractScenario.CreateArgumentValue);
    }

    [Fact]
    public async Task ProjectActiveContractEntry_follows_an_interface_matching_IncompleteUnassigneds_Created_with_its_Unassigned()
    {
        var projected = await ProjectImplementingEntryAsync(
            ActiveContractEntry.IncompleteUnassigned, InterfaceViewRendering.Computed);

        projected.Should().HaveCount(2);
        ShouldCarryTheComputedInterfaceView(projected[0]);
        var unassigned = projected[1].Should()
            .BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unassigned>().Subject;
        unassigned.ContractId.Value.Should().Be(ActiveContractScenario.ContractId);
        unassigned.Offset.Should().Be(LedgerOffset.At(ActiveContractScenario.UnassignedOffset));
        unassigned.Source.Should().Be(new SynchronizerId(ActiveContractScenario.SynchronizerId));
        unassigned.Target.Should().Be(new SynchronizerId(ActiveContractScenario.CounterpartSynchronizerId));
        unassigned.ReassignmentId.Should().Be(ActiveContractScenario.ReassignmentId);
        unassigned.ReassignmentCounter.Should().Be(ActiveContractScenario.ReassignmentCounter);
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active, InterfaceViewRendering.ComputationFailed)]
    [InlineData(ActiveContractEntry.Active, InterfaceViewRendering.ValueOmitted)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned, InterfaceViewRendering.ComputationFailed)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned, InterfaceViewRendering.ValueOmitted)]
    [InlineData(ActiveContractEntry.IncompleteAssigned, InterfaceViewRendering.ComputationFailed)]
    [InlineData(ActiveContractEntry.IncompleteAssigned, InterfaceViewRendering.ValueOmitted)]
    public async Task ProjectActiveContractEntry_surfaces_an_undecodable_interface_view_as_Unclassified_InterfaceViewUnavailable(
        ActiveContractEntry entry, InterfaceViewRendering interfaceView)
    {
        var projected = await ProjectImplementingEntryAsync(entry, interfaceView);

        var unclassified = projected.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned)]
    [InlineData(ActiveContractEntry.IncompleteAssigned)]
    public async Task ProjectActiveContractEntry_surfaces_an_entry_carrying_no_interface_view_as_Unclassified_CreatedEvent(
        ActiveContractEntry entry)
    {
        var projected = await ProjectImplementingEntryAsync(entry, InterfaceViewRendering.None);

        var unclassified = projected.Should().ContainSingle().Subject
            .Should().BeOfType<InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.CreatedEvent);
        unclassified.Offset.Should().Be(LedgerOffset.At(ActiveContractScenario.CreatedOffset));
    }

    private async Task<ContractStreamEvent<TemplateMarker>> ProjectOnlyTransactionEventAsync(
        TransactionEventScenario scenario)
    {
        var projected = await ProjectTransactionEventsAsync(scenario);
        return projected.Should().ContainSingle().Subject;
    }

    private async Task<ContractStreamEvent<TemplateMarker>> ProjectOnlyReassignmentEventAsync(
        ReassignmentEventScenario scenario)
    {
        var projected = await ProjectReassignmentEventsAsync(scenario);
        return projected.Should().ContainSingle().Subject;
    }

    private static ContractStreamEvent<TemplateMarker>.Unclassified ShouldBeUnclassified(
        ContractStreamEvent<TemplateMarker> projected) =>
        projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;

    private static UnclassifiedKind UnmatchedKindOf(TransactionEventShape shape) => shape switch
    {
        TransactionEventShape.Created => UnclassifiedKind.CreatedEvent,
        TransactionEventShape.Archived => UnclassifiedKind.ArchivedEvent,
        TransactionEventShape.Exercised => UnclassifiedKind.ExercisedEvent,
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    private static UnclassifiedKind UnmatchedKindOf(ReassignmentEventShape shape) => shape switch
    {
        ReassignmentEventShape.Assigned => UnclassifiedKind.AssignedEvent,
        ReassignmentEventShape.Unassigned => UnclassifiedKind.UnassignedEvent,
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    private static void ShouldBeTheMatchedEventScopedToTheTransactionsSynchronizer(
        ContractStreamEvent<TemplateMarker> projected, TransactionEventShape shape)
    {
        var expectedOffset = LedgerOffset.At(TransactionEventScenario.EventOffset);
        var expectedSynchronizer = new SynchronizerId(ActiveContractScenario.SynchronizerId);
        switch (shape)
        {
            case TransactionEventShape.Created:
                {
                    var created = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject;
                    created.ContractId.Value.Should().Be(ActiveContractScenario.ContractId);
                    created.Offset.Should().Be(expectedOffset);
                    created.SynchronizerId.Should().Be(expectedSynchronizer);
                    break;
                }
            case TransactionEventShape.Archived:
                {
                    var archived = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Archived>().Subject;
                    archived.ContractId.Value.Should().Be(ActiveContractScenario.ContractId);
                    archived.Offset.Should().Be(expectedOffset);
                    archived.SynchronizerId.Should().Be(expectedSynchronizer);
                    break;
                }
            case TransactionEventShape.Exercised:
                {
                    var exercised = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Exercised>().Subject;
                    exercised.ContractId.Value.Should().Be(ActiveContractScenario.ContractId);
                    exercised.ChoiceName.Should().Be(new ChoiceName(TransactionEventScenario.ChoiceName));
                    exercised.Consuming.Should().BeTrue();
                    exercised.Offset.Should().Be(expectedOffset);
                    exercised.SynchronizerId.Should().Be(expectedSynchronizer);
                    break;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
    }

    [Theory]
    [InlineData(TransactionEventShape.Created)]
    [InlineData(TransactionEventShape.Archived)]
    [InlineData(TransactionEventShape.Exercised)]
    public async Task ProjectTransactionEvents_classifies_a_matching_event_scoped_to_the_transactions_synchronizer(
        TransactionEventShape shape)
    {
        var projected = await ProjectOnlyTransactionEventAsync(new TransactionEventScenario { Event = shape });

        ShouldBeTheMatchedEventScopedToTheTransactionsSynchronizer(projected, shape);
    }

    [Theory]
    [InlineData(TransactionEventShape.Created)]
    [InlineData(TransactionEventShape.Archived)]
    [InlineData(TransactionEventShape.Exercised)]
    public async Task ProjectTransactionEvents_surfaces_an_event_that_does_not_match_the_marker_as_Unclassified_for_its_shape(
        TransactionEventShape shape)
    {
        var projected = await ProjectOnlyTransactionEventAsync(new TransactionEventScenario
        {
            Event = shape,
            EntityName = ActiveContractScenario.OtherEntityName,
        });

        var unclassified = ShouldBeUnclassified(projected);
        unclassified.Kind.Should().Be(UnmatchedKindOf(shape));
        unclassified.Offset.Should().Be(LedgerOffset.At(TransactionEventScenario.EventOffset));
    }

    [Theory]
    [InlineData(TransactionEventShape.Created)]
    [InlineData(TransactionEventShape.Archived)]
    [InlineData(TransactionEventShape.Exercised)]
    public async Task ProjectTransactionEvents_surfaces_a_transaction_carrying_no_synchronizer_as_Unclassified_MissingSynchronizerId(
        TransactionEventShape shape)
    {
        var projected = await ProjectOnlyTransactionEventAsync(new TransactionEventScenario
        {
            Event = shape,
            Synchronizer = null,
        });

        var unclassified = ShouldBeUnclassified(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
        unclassified.Offset.Should().Be(LedgerOffset.At(TransactionEventScenario.EventOffset));
    }

    [Theory]
    [InlineData(TransactionEventShape.Created)]
    [InlineData(TransactionEventShape.Archived)]
    [InlineData(TransactionEventShape.Exercised)]
    public async Task ProjectTransactionEvents_reports_a_marker_mismatch_ahead_of_a_missing_synchronizer(
        TransactionEventShape shape)
    {
        var projected = await ProjectOnlyTransactionEventAsync(new TransactionEventScenario
        {
            Event = shape,
            EntityName = ActiveContractScenario.OtherEntityName,
            Synchronizer = null,
        });

        ShouldBeUnclassified(projected).Kind.Should().Be(UnmatchedKindOf(shape));
    }

    [Fact]
    public async Task ProjectTransactionEvents_surfaces_an_event_with_no_case_set_as_Unclassified_Unknown_at_the_transactions_offset()
    {
        var projected = await ProjectOnlyTransactionEventAsync(
            new TransactionEventScenario { Event = TransactionEventShape.Empty });

        var unclassified = ShouldBeUnclassified(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.Unknown);
        unclassified.Offset.Should().Be(LedgerOffset.At(TransactionEventScenario.TransactionOffset));
        unclassified.RawKind.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(TransactionEventShape.Created, false)]
    [InlineData(TransactionEventShape.Created, true)]
    [InlineData(TransactionEventShape.Archived, false)]
    [InlineData(TransactionEventShape.Archived, true)]
    [InlineData(TransactionEventShape.Exercised, false)]
    [InlineData(TransactionEventShape.Exercised, true)]
    public async Task ProjectTransactionEvents_surfaces_an_event_carrying_no_template_id_as_Unclassified_DecodeFailure_at_the_transactions_offset(
        TransactionEventShape shape, bool omitEventOffset)
    {
        var projected = await ProjectOnlyTransactionEventAsync(new TransactionEventScenario
        {
            Event = shape,
            OmitTemplateId = true,
            OmitEventOffset = omitEventOffset,
        });

        var unclassified = ShouldBeUnclassified(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.Offset.Should().Be(LedgerOffset.At(TransactionEventScenario.TransactionOffset));
    }

    [Theory]
    [InlineData(TransactionEventShape.Created)]
    [InlineData(TransactionEventShape.Archived)]
    [InlineData(TransactionEventShape.Exercised)]
    public async Task ProjectTransactionEvents_keeps_projecting_the_events_that_follow_an_undecodable_one(
        TransactionEventShape shape)
    {
        var projected = await ProjectTransactionEventsAsync(new TransactionEventScenario
        {
            Event = shape,
            OmitTemplateId = true,
            FollowedByMatchingCreated = true,
        });

        projected.Should().HaveCount(2);
        ShouldBeUnclassified(projected[0]).Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        projected[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>()
            .Which.Offset.Should().Be(LedgerOffset.At(TransactionEventScenario.TrailingEventOffset));
    }

    private static void ShouldBeTheMatchedEventScopedToItsSourceAndTarget(
        ContractStreamEvent<TemplateMarker> projected, ReassignmentEventShape shape)
    {
        var expectedOffset = LedgerOffset.At(ReassignmentEventScenario.EventOffset);
        var expectedSource = new SynchronizerId(ActiveContractScenario.SynchronizerId);
        var expectedTarget = new SynchronizerId(ActiveContractScenario.CounterpartSynchronizerId);
        switch (shape)
        {
            case ReassignmentEventShape.Assigned:
                {
                    var assigned = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Assigned>().Subject;
                    assigned.ContractId.Value.Should().Be(ActiveContractScenario.ContractId);
                    assigned.Offset.Should().Be(expectedOffset);
                    assigned.Source.Should().Be(expectedSource);
                    assigned.Target.Should().Be(expectedTarget);
                    assigned.ReassignmentId.Should().Be(ReassignmentEventScenario.ReassignmentId);
                    assigned.ReassignmentCounter.Should().Be(ReassignmentEventScenario.ReassignmentCounter);
                    break;
                }
            case ReassignmentEventShape.Unassigned:
                {
                    var unassigned = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unassigned>().Subject;
                    unassigned.ContractId.Value.Should().Be(ActiveContractScenario.ContractId);
                    unassigned.Offset.Should().Be(expectedOffset);
                    unassigned.Source.Should().Be(expectedSource);
                    unassigned.Target.Should().Be(expectedTarget);
                    unassigned.ReassignmentId.Should().Be(ReassignmentEventScenario.ReassignmentId);
                    unassigned.ReassignmentCounter.Should().Be(ReassignmentEventScenario.ReassignmentCounter);
                    break;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
    }

    [Theory]
    [InlineData(ReassignmentEventShape.Assigned)]
    [InlineData(ReassignmentEventShape.Unassigned)]
    public async Task ProjectReassignmentEvents_classifies_a_matching_event_scoped_to_its_source_and_target(
        ReassignmentEventShape shape)
    {
        var projected = await ProjectOnlyReassignmentEventAsync(new ReassignmentEventScenario { Event = shape });

        ShouldBeTheMatchedEventScopedToItsSourceAndTarget(projected, shape);
    }

    [Theory]
    [InlineData(TransactionEventShape.Created)]
    [InlineData(TransactionEventShape.Archived)]
    [InlineData(TransactionEventShape.Exercised)]
    public async Task ProjectTransactionEvents_carries_the_witness_parties_and_the_exercised_argument_apart_from_its_result(
        TransactionEventShape shape)
    {
        var projected = await ProjectOnlyTransactionEventAsync(new TransactionEventScenario { Event = shape });

        switch (shape)
        {
            case TransactionEventShape.Created:
                {
                    var created = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>().Subject;
                    ShouldCarryTheTemplatesContractKey(created.Key);
                    ShouldCarryTheWitnessParties(created.WitnessParties);
                    break;
                }
            case TransactionEventShape.Archived:
                {
                    var archived = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Archived>().Subject;
                    ShouldCarryTheWitnessParties(archived.WitnessParties);
                    break;
                }
            case TransactionEventShape.Exercised:
                {
                    var exercised = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Exercised>().Subject;
                    exercised.ChoiceArgument.Should().Be(new DamlText("choice-argument-value"));
                    exercised.ExerciseResult.Should().Be(new DamlText("exercise-result-value"));
                    ShouldCarryTheWitnessParties(exercised.WitnessParties);
                    break;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
    }

    [Theory]
    [InlineData(ReassignmentEventShape.Assigned)]
    [InlineData(ReassignmentEventShape.Unassigned)]
    public async Task ProjectReassignmentEvents_carries_the_witness_parties_of_a_matching_event(
        ReassignmentEventShape shape)
    {
        var projected = await ProjectOnlyReassignmentEventAsync(new ReassignmentEventScenario { Event = shape });

        switch (shape)
        {
            case ReassignmentEventShape.Assigned:
                {
                    var assigned = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Assigned>().Subject;
                    ShouldCarryTheTemplatesContractKey(assigned.Key);
                    ShouldCarryTheWitnessParties(assigned.WitnessParties);
                    break;
                }
            case ReassignmentEventShape.Unassigned:
                {
                    var unassigned = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unassigned>().Subject;
                    ShouldCarryTheWitnessParties(unassigned.WitnessParties);
                    break;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
    }

    [Theory]
    [InlineData(ReassignmentEventShape.Assigned)]
    [InlineData(ReassignmentEventShape.Unassigned)]
    public async Task ProjectReassignmentEvents_surfaces_an_event_that_does_not_match_the_marker_as_Unclassified_for_its_shape(
        ReassignmentEventShape shape)
    {
        var projected = await ProjectOnlyReassignmentEventAsync(new ReassignmentEventScenario
        {
            Event = shape,
            EntityName = ActiveContractScenario.OtherEntityName,
        });

        var unclassified = ShouldBeUnclassified(projected);
        unclassified.Kind.Should().Be(UnmatchedKindOf(shape));
        unclassified.Offset.Should().Be(LedgerOffset.At(ReassignmentEventScenario.EventOffset));
    }

    [Theory]
    [InlineData(ReassignmentEventShape.Assigned, true)]
    [InlineData(ReassignmentEventShape.Assigned, false)]
    [InlineData(ReassignmentEventShape.Unassigned, true)]
    [InlineData(ReassignmentEventShape.Unassigned, false)]
    public async Task ProjectReassignmentEvents_surfaces_an_event_missing_either_reassignment_synchronizer_as_Unclassified_MissingSynchronizerId(
        ReassignmentEventShape shape, bool omitSource)
    {
        var projected = await ProjectOnlyReassignmentEventAsync(new ReassignmentEventScenario
        {
            Event = shape,
            Source = omitSource ? null : ActiveContractScenario.SynchronizerId,
            Target = omitSource ? ActiveContractScenario.CounterpartSynchronizerId : null,
        });

        var unclassified = ShouldBeUnclassified(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
        unclassified.Offset.Should().Be(LedgerOffset.At(ReassignmentEventScenario.EventOffset));
    }

    [Theory]
    [InlineData(ReassignmentEventShape.Assigned)]
    [InlineData(ReassignmentEventShape.Unassigned)]
    public async Task ProjectReassignmentEvents_reports_a_marker_mismatch_ahead_of_a_missing_synchronizer(
        ReassignmentEventShape shape)
    {
        var projected = await ProjectOnlyReassignmentEventAsync(new ReassignmentEventScenario
        {
            Event = shape,
            EntityName = ActiveContractScenario.OtherEntityName,
            Source = null,
            Target = null,
        });

        ShouldBeUnclassified(projected).Kind.Should().Be(UnmatchedKindOf(shape));
    }

    [Fact]
    public async Task ProjectReassignmentEvents_surfaces_an_assigned_event_without_a_created_event_as_Unclassified_AssignedEvent()
    {
        var projected = await ProjectOnlyReassignmentEventAsync(new ReassignmentEventScenario
        {
            Event = ReassignmentEventShape.Assigned,
            OmitCreatedEvent = true,
        });

        var unclassified = ShouldBeUnclassified(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.AssignedEvent);
        unclassified.Offset.Should().Be(LedgerOffset.At(ReassignmentEventScenario.ReassignmentOffset));
    }

    [Fact]
    public async Task ProjectReassignmentEvents_surfaces_an_event_with_no_case_set_as_Unclassified_Unknown_at_the_reassignments_offset()
    {
        var projected = await ProjectOnlyReassignmentEventAsync(
            new ReassignmentEventScenario { Event = ReassignmentEventShape.Empty });

        var unclassified = ShouldBeUnclassified(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.Unknown);
        unclassified.Offset.Should().Be(LedgerOffset.At(ReassignmentEventScenario.ReassignmentOffset));
        unclassified.RawKind.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(ReassignmentEventShape.Assigned, false)]
    [InlineData(ReassignmentEventShape.Assigned, true)]
    [InlineData(ReassignmentEventShape.Unassigned, false)]
    [InlineData(ReassignmentEventShape.Unassigned, true)]
    public async Task ProjectReassignmentEvents_surfaces_an_event_carrying_no_template_id_as_Unclassified_DecodeFailure_at_the_reassignments_offset(
        ReassignmentEventShape shape, bool omitEventOffset)
    {
        var projected = await ProjectOnlyReassignmentEventAsync(new ReassignmentEventScenario
        {
            Event = shape,
            OmitTemplateId = true,
            OmitEventOffset = omitEventOffset,
        });

        var unclassified = ShouldBeUnclassified(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.Offset.Should().Be(LedgerOffset.At(ReassignmentEventScenario.ReassignmentOffset));
    }

    [Theory]
    [InlineData(ReassignmentEventShape.Assigned)]
    [InlineData(ReassignmentEventShape.Unassigned)]
    public async Task ProjectReassignmentEvents_keeps_projecting_the_events_that_follow_an_undecodable_one(
        ReassignmentEventShape shape)
    {
        var projected = await ProjectReassignmentEventsAsync(new ReassignmentEventScenario
        {
            Event = shape,
            OmitTemplateId = true,
            FollowedByMatchingUnassigned = true,
        });

        projected.Should().HaveCount(2);
        ShouldBeUnclassified(projected[0]).Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        projected[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unassigned>()
            .Which.Offset.Should().Be(LedgerOffset.At(ReassignmentEventScenario.TrailingEventOffset));
    }

    private async Task<(IReadOnlyList<InterfaceEvent> Projected, CapturingLoggerFactory Logs)> ProjectInterfaceTransactionAsync(
        TransactionEventScenario scenario)
    {
        var logs = new CapturingLoggerFactory();
        var projected = await ProjectTransactionEventsAsInterfaceAsync(scenario, logs.CreateLogger("parity"));
        return (projected, logs);
    }

    private async Task<(IReadOnlyList<InterfaceEvent> Projected, CapturingLoggerFactory Logs)> ProjectInterfaceReassignmentAsync(
        ReassignmentEventScenario scenario)
    {
        var logs = new CapturingLoggerFactory();
        var projected = await ProjectReassignmentEventsAsInterfaceAsync(scenario, logs.CreateLogger("parity"));
        return (projected, logs);
    }

    private async Task<InterfaceEvent> ProjectOnlyInterfaceTransactionEventAsync(TransactionEventScenario scenario)
    {
        var (projected, _) = await ProjectInterfaceTransactionAsync(scenario);
        return projected.Should().ContainSingle().Subject;
    }

    private async Task<InterfaceEvent> ProjectOnlyInterfaceReassignmentEventAsync(ReassignmentEventScenario scenario)
    {
        var (projected, _) = await ProjectInterfaceReassignmentAsync(scenario);
        return projected.Should().ContainSingle().Subject;
    }

    private static InterfaceEvent.Unclassified ShouldBeUnclassifiedInterfaceEvent(InterfaceEvent projected) =>
        projected.Should().BeOfType<InterfaceEvent.Unclassified>().Subject;

    private static TransactionEventScenario InterfaceTransactionEvent(TransactionEventShape shape) => new()
    {
        Event = shape,
        InterfaceView = shape == TransactionEventShape.Created ? InterfaceViewRendering.Computed : InterfaceViewRendering.None,
        ImplementsSubscribedInterface = shape != TransactionEventShape.Created,
    };

    private static ReassignmentEventScenario InterfaceReassignmentEvent(ReassignmentEventShape shape) => new()
    {
        Event = shape,
        InterfaceView = shape == ReassignmentEventShape.Assigned ? InterfaceViewRendering.Computed : InterfaceViewRendering.None,
    };

    private static void ShouldHaveLoggedOneDecodeFailureOnTheInterfaceStream(CapturingLoggerFactory logs, long offset)
    {
        var record = logs.Records.Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Warning);
        record.Message.Should().Be(
            $"Could not decode event at offset {offset} on the InterfaceMarker stream — surfaced as Unclassified (decode-failure)");
    }

    private static void ShouldCarryTheWitnessParties(EquatableArray<Party> witnessParties) =>
        witnessParties.ToArray().Should().Equal((Party)"witness-one::ns1", (Party)"witness-two::ns1");

    private static void ShouldCarryTheTemplatesContractKey(ContractKey? key)
    {
        key.Should().NotBeNull();
        key!.Value.Should().Be(new DamlParty("key-holder::ns1"));
        key.TemplateId.Should().Be(new Identifier("tmpl-pkg", "Sample.Token", "Holding"));
        key.KeyHash.Should().Be("AQID");
    }

    private static void ShouldBeTheMatchedInterfaceEventScopedToTheTransactionsSynchronizer(
        InterfaceEvent projected, TransactionEventShape shape)
    {
        var expectedOffset = LedgerOffset.At(71L);
        var expectedSynchronizer = new SynchronizerId("sync-1");
        switch (shape)
        {
            case TransactionEventShape.Created:
                {
                    var created = projected.Should().BeOfType<InterfaceEvent.Created>().Subject;
                    created.ContractId.Value.Should().Be("00holding");
                    created.Payload.Amount.Should().Be("interface-view-value");
                    ShouldCarryTheTemplatesContractKey(created.Key);
                    created.Offset.Should().Be(expectedOffset);
                    created.SynchronizerId.Should().Be(expectedSynchronizer);
                    ShouldCarryTheWitnessParties(created.WitnessParties);
                    break;
                }
            case TransactionEventShape.Archived:
                {
                    var archived = projected.Should().BeOfType<InterfaceEvent.Archived>().Subject;
                    archived.ContractId.Value.Should().Be("00holding");
                    archived.Offset.Should().Be(expectedOffset);
                    archived.SynchronizerId.Should().Be(expectedSynchronizer);
                    ShouldCarryTheWitnessParties(archived.WitnessParties);
                    break;
                }
            case TransactionEventShape.Exercised:
                {
                    var exercised = projected.Should().BeOfType<InterfaceEvent.Exercised>().Subject;
                    exercised.ContractId.Value.Should().Be("00holding");
                    exercised.ChoiceName.Should().Be(new ChoiceName("Transfer"));
                    exercised.ChoiceArgument.Should().Be(new DamlText("choice-argument-value"));
                    exercised.ExerciseResult.Should().Be(new DamlText("exercise-result-value"));
                    exercised.Consuming.Should().BeTrue();
                    exercised.Offset.Should().Be(expectedOffset);
                    exercised.SynchronizerId.Should().Be(expectedSynchronizer);
                    ShouldCarryTheWitnessParties(exercised.WitnessParties);
                    break;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
    }

    private static void ShouldBeTheMatchedInterfaceEventScopedToItsSourceAndTarget(
        InterfaceEvent projected, ReassignmentEventShape shape)
    {
        var expectedOffset = LedgerOffset.At(81L);
        var expectedSource = new SynchronizerId("sync-1");
        var expectedTarget = new SynchronizerId("sync-counterpart");
        switch (shape)
        {
            case ReassignmentEventShape.Assigned:
                {
                    var assigned = projected.Should().BeOfType<InterfaceEvent.Assigned>().Subject;
                    assigned.ContractId.Value.Should().Be("00holding");
                    assigned.Payload.Amount.Should().Be("interface-view-value");
                    ShouldCarryTheTemplatesContractKey(assigned.Key);
                    assigned.Offset.Should().Be(expectedOffset);
                    assigned.Source.Should().Be(expectedSource);
                    assigned.Target.Should().Be(expectedTarget);
                    assigned.ReassignmentId.Should().Be("reassignment-2");
                    assigned.ReassignmentCounter.Should().Be(3L);
                    ShouldCarryTheWitnessParties(assigned.WitnessParties);
                    break;
                }
            case ReassignmentEventShape.Unassigned:
                {
                    var unassigned = projected.Should().BeOfType<InterfaceEvent.Unassigned>().Subject;
                    unassigned.ContractId.Value.Should().Be("00holding");
                    unassigned.Offset.Should().Be(expectedOffset);
                    unassigned.Source.Should().Be(expectedSource);
                    unassigned.Target.Should().Be(expectedTarget);
                    unassigned.ReassignmentId.Should().Be("reassignment-2");
                    unassigned.ReassignmentCounter.Should().Be(3L);
                    ShouldCarryTheWitnessParties(unassigned.WitnessParties);
                    break;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
    }

    [Theory]
    [InlineData(TransactionEventShape.Created)]
    [InlineData(TransactionEventShape.Archived)]
    [InlineData(TransactionEventShape.Exercised)]
    public async Task ProjectTransactionEventsAsInterface_classifies_an_event_implementing_the_interface_scoped_to_the_transactions_synchronizer(
        TransactionEventShape shape)
    {
        var projected = await ProjectOnlyInterfaceTransactionEventAsync(InterfaceTransactionEvent(shape));

        ShouldBeTheMatchedInterfaceEventScopedToTheTransactionsSynchronizer(projected, shape);
    }

    [Theory]
    [InlineData(TransactionEventShape.Created)]
    [InlineData(TransactionEventShape.Archived)]
    [InlineData(TransactionEventShape.Exercised)]
    public async Task ProjectTransactionEventsAsInterface_surfaces_an_event_not_implementing_the_interface_as_Unclassified_for_its_shape(
        TransactionEventShape shape)
    {
        var projected = await ProjectOnlyInterfaceTransactionEventAsync(new TransactionEventScenario { Event = shape });

        var unclassified = ShouldBeUnclassifiedInterfaceEvent(projected);
        unclassified.Kind.Should().Be(UnmatchedKindOf(shape));
        unclassified.Offset.Should().Be(LedgerOffset.At(TransactionEventScenario.EventOffset));
    }

    [Theory]
    [InlineData(TransactionEventShape.Created)]
    [InlineData(TransactionEventShape.Archived)]
    [InlineData(TransactionEventShape.Exercised)]
    public async Task ProjectTransactionEventsAsInterface_surfaces_a_transaction_carrying_no_synchronizer_as_Unclassified_MissingSynchronizerId(
        TransactionEventShape shape)
    {
        var projected = await ProjectOnlyInterfaceTransactionEventAsync(
            InterfaceTransactionEvent(shape) with { Synchronizer = null });

        var unclassified = ShouldBeUnclassifiedInterfaceEvent(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
        unclassified.Offset.Should().Be(LedgerOffset.At(TransactionEventScenario.EventOffset));
    }

    [Theory]
    [InlineData(TransactionEventShape.Created)]
    [InlineData(TransactionEventShape.Archived)]
    [InlineData(TransactionEventShape.Exercised)]
    public async Task ProjectTransactionEventsAsInterface_reports_a_marker_mismatch_ahead_of_a_missing_synchronizer(
        TransactionEventShape shape)
    {
        var projected = await ProjectOnlyInterfaceTransactionEventAsync(
            new TransactionEventScenario { Event = shape, Synchronizer = null });

        ShouldBeUnclassifiedInterfaceEvent(projected).Kind.Should().Be(UnmatchedKindOf(shape));
    }

    [Fact]
    public async Task ProjectTransactionEventsAsInterface_surfaces_an_event_with_no_case_set_as_Unclassified_Unknown_at_the_transactions_offset()
    {
        var projected = await ProjectOnlyInterfaceTransactionEventAsync(
            new TransactionEventScenario { Event = TransactionEventShape.Empty });

        var unclassified = ShouldBeUnclassifiedInterfaceEvent(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.Unknown);
        unclassified.Offset.Should().Be(LedgerOffset.At(TransactionEventScenario.TransactionOffset));
        unclassified.RawKind.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(InterfaceViewRendering.ComputationFailed)]
    [InlineData(InterfaceViewRendering.ValueOmitted)]
    public async Task ProjectTransactionEventsAsInterface_surfaces_an_unavailable_view_as_Unclassified_InterfaceViewUnavailable_at_the_created_offset(
        InterfaceViewRendering interfaceView)
    {
        var projected = await ProjectOnlyInterfaceTransactionEventAsync(
            new TransactionEventScenario { Event = TransactionEventShape.Created, InterfaceView = interfaceView });

        var unclassified = ShouldBeUnclassifiedInterfaceEvent(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
        unclassified.Offset.Should().Be(LedgerOffset.At(TransactionEventScenario.EventOffset));
    }

    [Fact]
    public async Task ProjectTransactionEventsAsInterface_surfaces_a_view_without_create_arguments_as_Unclassified_DecodeFailure_at_the_transactions_offset()
    {
        var (projected, logs) = await ProjectInterfaceTransactionAsync(new TransactionEventScenario
        {
            Event = TransactionEventShape.Created,
            InterfaceView = InterfaceViewRendering.Computed,
            OmitCreateArguments = true,
        });

        var unclassified = ShouldBeUnclassifiedInterfaceEvent(projected.Should().ContainSingle().Subject);
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.Offset.Should().Be(LedgerOffset.At(TransactionEventScenario.TransactionOffset));
        ShouldHaveLoggedOneDecodeFailureOnTheInterfaceStream(logs, TransactionEventScenario.TransactionOffset);
    }

    [Theory]
    [InlineData(TransactionEventShape.Created, false)]
    [InlineData(TransactionEventShape.Created, true)]
    [InlineData(TransactionEventShape.Archived, false)]
    [InlineData(TransactionEventShape.Archived, true)]
    [InlineData(TransactionEventShape.Exercised, false)]
    [InlineData(TransactionEventShape.Exercised, true)]
    public async Task ProjectTransactionEventsAsInterface_surfaces_an_event_carrying_no_template_id_as_Unclassified_DecodeFailure_at_the_transactions_offset_and_logs_the_marker_name(
        TransactionEventShape shape, bool omitEventOffset)
    {
        var (projected, logs) = await ProjectInterfaceTransactionAsync(InterfaceTransactionEvent(shape) with
        {
            OmitTemplateId = true,
            OmitEventOffset = omitEventOffset,
        });

        var unclassified = ShouldBeUnclassifiedInterfaceEvent(projected.Should().ContainSingle().Subject);
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.Offset.Should().Be(LedgerOffset.At(TransactionEventScenario.TransactionOffset));
        ShouldHaveLoggedOneDecodeFailureOnTheInterfaceStream(logs, TransactionEventScenario.TransactionOffset);
    }

    [Theory]
    [InlineData(TransactionEventShape.Created)]
    [InlineData(TransactionEventShape.Archived)]
    [InlineData(TransactionEventShape.Exercised)]
    public async Task ProjectTransactionEventsAsInterface_keeps_projecting_the_events_that_follow_an_undecodable_one(
        TransactionEventShape shape)
    {
        var (projected, _) = await ProjectInterfaceTransactionAsync(InterfaceTransactionEvent(shape) with
        {
            OmitTemplateId = true,
            InterfaceView = InterfaceViewRendering.Computed,
            FollowedByMatchingCreated = true,
        });

        projected.Should().HaveCount(2);
        ShouldBeUnclassifiedInterfaceEvent(projected[0]).Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        projected[1].Should().BeOfType<InterfaceEvent.Created>()
            .Which.Offset.Should().Be(LedgerOffset.At(TransactionEventScenario.TrailingEventOffset));
    }

    [Theory]
    [InlineData(ReassignmentEventShape.Assigned)]
    [InlineData(ReassignmentEventShape.Unassigned)]
    public async Task ProjectReassignmentEventsAsInterface_classifies_a_matching_event_scoped_to_its_source_and_target(
        ReassignmentEventShape shape)
    {
        var projected = await ProjectOnlyInterfaceReassignmentEventAsync(InterfaceReassignmentEvent(shape));

        ShouldBeTheMatchedInterfaceEventScopedToItsSourceAndTarget(projected, shape);
    }

    [Fact]
    public async Task ProjectReassignmentEventsAsInterface_projects_an_unassigned_event_whatever_template_it_carries()
    {
        var projected = await ProjectOnlyInterfaceReassignmentEventAsync(new ReassignmentEventScenario
        {
            Event = ReassignmentEventShape.Unassigned,
            EntityName = ActiveContractScenario.OtherEntityName,
        });

        ShouldBeTheMatchedInterfaceEventScopedToItsSourceAndTarget(projected, ReassignmentEventShape.Unassigned);
    }

    [Fact]
    public async Task ProjectReassignmentEventsAsInterface_surfaces_an_assigned_event_not_implementing_the_interface_as_Unclassified_AssignedEvent()
    {
        var projected = await ProjectOnlyInterfaceReassignmentEventAsync(
            new ReassignmentEventScenario { Event = ReassignmentEventShape.Assigned });

        var unclassified = ShouldBeUnclassifiedInterfaceEvent(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.AssignedEvent);
        unclassified.Offset.Should().Be(LedgerOffset.At(ReassignmentEventScenario.EventOffset));
    }

    [Theory]
    [InlineData(ReassignmentEventShape.Assigned, true)]
    [InlineData(ReassignmentEventShape.Assigned, false)]
    [InlineData(ReassignmentEventShape.Unassigned, true)]
    [InlineData(ReassignmentEventShape.Unassigned, false)]
    public async Task ProjectReassignmentEventsAsInterface_surfaces_an_event_missing_either_reassignment_synchronizer_as_Unclassified_MissingSynchronizerId(
        ReassignmentEventShape shape, bool omitSource)
    {
        var projected = await ProjectOnlyInterfaceReassignmentEventAsync(InterfaceReassignmentEvent(shape) with
        {
            Source = omitSource ? null : ActiveContractScenario.SynchronizerId,
            Target = omitSource ? ActiveContractScenario.CounterpartSynchronizerId : null,
        });

        var unclassified = ShouldBeUnclassifiedInterfaceEvent(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
        unclassified.Offset.Should().Be(LedgerOffset.At(ReassignmentEventScenario.EventOffset));
    }

    [Fact]
    public async Task ProjectReassignmentEventsAsInterface_reports_a_marker_mismatch_ahead_of_a_missing_synchronizer()
    {
        var projected = await ProjectOnlyInterfaceReassignmentEventAsync(new ReassignmentEventScenario
        {
            Event = ReassignmentEventShape.Assigned,
            Source = null,
            Target = null,
        });

        ShouldBeUnclassifiedInterfaceEvent(projected).Kind.Should().Be(UnclassifiedKind.AssignedEvent);
    }

    [Fact]
    public async Task ProjectReassignmentEventsAsInterface_surfaces_an_assigned_event_without_a_created_event_as_Unclassified_AssignedEvent()
    {
        var projected = await ProjectOnlyInterfaceReassignmentEventAsync(new ReassignmentEventScenario
        {
            Event = ReassignmentEventShape.Assigned,
            OmitCreatedEvent = true,
        });

        var unclassified = ShouldBeUnclassifiedInterfaceEvent(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.AssignedEvent);
        unclassified.Offset.Should().Be(LedgerOffset.At(ReassignmentEventScenario.ReassignmentOffset));
    }

    [Fact]
    public async Task ProjectReassignmentEventsAsInterface_surfaces_an_event_with_no_case_set_as_Unclassified_Unknown_at_the_reassignments_offset()
    {
        var projected = await ProjectOnlyInterfaceReassignmentEventAsync(
            new ReassignmentEventScenario { Event = ReassignmentEventShape.Empty });

        var unclassified = ShouldBeUnclassifiedInterfaceEvent(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.Unknown);
        unclassified.Offset.Should().Be(LedgerOffset.At(ReassignmentEventScenario.ReassignmentOffset));
        unclassified.RawKind.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(InterfaceViewRendering.ComputationFailed, false, ReassignmentEventScenario.EventOffset)]
    [InlineData(InterfaceViewRendering.ValueOmitted, false, ReassignmentEventScenario.EventOffset)]
    [InlineData(InterfaceViewRendering.ComputationFailed, true, ReassignmentEventScenario.ReassignmentOffset)]
    [InlineData(InterfaceViewRendering.ValueOmitted, true, ReassignmentEventScenario.ReassignmentOffset)]
    public async Task ProjectReassignmentEventsAsInterface_surfaces_an_unavailable_view_as_Unclassified_InterfaceViewUnavailable_at_the_created_offset_falling_back_to_the_reassignments(
        InterfaceViewRendering interfaceView, bool omitEventOffset, long expectedOffset)
    {
        var projected = await ProjectOnlyInterfaceReassignmentEventAsync(new ReassignmentEventScenario
        {
            Event = ReassignmentEventShape.Assigned,
            InterfaceView = interfaceView,
            OmitEventOffset = omitEventOffset,
        });

        var unclassified = ShouldBeUnclassifiedInterfaceEvent(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
        unclassified.Offset.Should().Be(LedgerOffset.At(expectedOffset));
    }

    [Fact]
    public async Task ProjectReassignmentEventsAsInterface_surfaces_a_view_without_create_arguments_as_Unclassified_DecodeFailure_at_the_reassignments_offset()
    {
        var (projected, logs) = await ProjectInterfaceReassignmentAsync(new ReassignmentEventScenario
        {
            Event = ReassignmentEventShape.Assigned,
            InterfaceView = InterfaceViewRendering.Computed,
            OmitCreateArguments = true,
        });

        var unclassified = ShouldBeUnclassifiedInterfaceEvent(projected.Should().ContainSingle().Subject);
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.Offset.Should().Be(LedgerOffset.At(ReassignmentEventScenario.ReassignmentOffset));
        ShouldHaveLoggedOneDecodeFailureOnTheInterfaceStream(logs, ReassignmentEventScenario.ReassignmentOffset);
    }

    [Theory]
    [InlineData(ReassignmentEventShape.Assigned, false)]
    [InlineData(ReassignmentEventShape.Assigned, true)]
    [InlineData(ReassignmentEventShape.Unassigned, false)]
    [InlineData(ReassignmentEventShape.Unassigned, true)]
    public async Task ProjectReassignmentEventsAsInterface_surfaces_an_event_carrying_no_template_id_as_Unclassified_DecodeFailure_at_the_reassignments_offset_and_logs_the_marker_name(
        ReassignmentEventShape shape, bool omitEventOffset)
    {
        var (projected, logs) = await ProjectInterfaceReassignmentAsync(InterfaceReassignmentEvent(shape) with
        {
            OmitTemplateId = true,
            OmitEventOffset = omitEventOffset,
        });

        var unclassified = ShouldBeUnclassifiedInterfaceEvent(projected.Should().ContainSingle().Subject);
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.Offset.Should().Be(LedgerOffset.At(ReassignmentEventScenario.ReassignmentOffset));
        ShouldHaveLoggedOneDecodeFailureOnTheInterfaceStream(logs, ReassignmentEventScenario.ReassignmentOffset);
    }

    [Theory]
    [InlineData(ReassignmentEventShape.Assigned)]
    [InlineData(ReassignmentEventShape.Unassigned)]
    public async Task ProjectReassignmentEventsAsInterface_keeps_projecting_the_events_that_follow_an_undecodable_one(
        ReassignmentEventShape shape)
    {
        var (projected, _) = await ProjectInterfaceReassignmentAsync(InterfaceReassignmentEvent(shape) with
        {
            OmitTemplateId = true,
            FollowedByMatchingUnassigned = true,
        });

        projected.Should().HaveCount(2);
        ShouldBeUnclassifiedInterfaceEvent(projected[0]).Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        projected[1].Should().BeOfType<InterfaceEvent.Unassigned>()
            .Which.Offset.Should().Be(LedgerOffset.At(ReassignmentEventScenario.TrailingEventOffset));
    }

    /// <summary>
    /// Renders <paramref name="scenario"/> into this transport's wire shape and runs it through that
    /// transport's template-typed transaction projection subscribed as a template marker that
    /// reports the interface kind, returning the projected events in order.
    /// </summary>
    protected abstract Task<IReadOnlyList<ContractStreamEvent<InterfaceKindTemplateMarker>>> ProjectTransactionEventsAsInterfaceKindTemplateAsync(
        TransactionEventScenario scenario);

    [Theory]
    [InlineData(TransactionEventShape.Created)]
    [InlineData(TransactionEventShape.Archived)]
    [InlineData(TransactionEventShape.Exercised)]
    public async Task ProjectTransactionEvents_frames_a_marker_mismatch_of_an_event_without_an_event_offset_at_the_transactions_offset(
        TransactionEventShape shape)
    {
        var projected = await ProjectOnlyTransactionEventAsync(new TransactionEventScenario
        {
            Event = shape,
            EntityName = ActiveContractScenario.OtherEntityName,
            OmitEventOffset = true,
        });

        var unclassified = ShouldBeUnclassified(projected);
        unclassified.Kind.Should().Be(UnmatchedKindOf(shape));
        unclassified.Offset.Should().Be(LedgerOffset.At(70L));
    }

    [Theory]
    [InlineData(TransactionEventShape.Created)]
    [InlineData(TransactionEventShape.Archived)]
    [InlineData(TransactionEventShape.Exercised)]
    public async Task ProjectTransactionEvents_frames_a_missing_synchronizer_of_an_event_without_an_event_offset_at_the_transactions_offset(
        TransactionEventShape shape)
    {
        var projected = await ProjectOnlyTransactionEventAsync(new TransactionEventScenario
        {
            Event = shape,
            Synchronizer = null,
            OmitEventOffset = true,
        });

        var unclassified = ShouldBeUnclassified(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
        unclassified.Offset.Should().Be(LedgerOffset.At(70L));
    }

    [Theory]
    [InlineData(InterfaceViewRendering.ComputationFailed)]
    [InlineData(InterfaceViewRendering.ValueOmitted)]
    public async Task ProjectTransactionEvents_frames_an_unavailable_view_of_a_created_event_without_an_event_offset_at_the_transactions_offset_for_an_interface_kind_template_marker(
        InterfaceViewRendering interfaceView)
    {
        var projected = await ProjectTransactionEventsAsInterfaceKindTemplateAsync(new TransactionEventScenario
        {
            Event = TransactionEventShape.Created,
            InterfaceView = interfaceView,
            OmitEventOffset = true,
        });

        var unclassified = projected.Should().ContainSingle().Subject
            .Should().BeOfType<ContractStreamEvent<InterfaceKindTemplateMarker>.Unclassified>().Subject;
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
        unclassified.Offset.Should().Be(LedgerOffset.At(70L));
    }

    [Theory]
    [InlineData(TransactionEventShape.Created)]
    [InlineData(TransactionEventShape.Archived)]
    [InlineData(TransactionEventShape.Exercised)]
    public async Task ProjectTransactionEventsAsInterface_frames_a_marker_mismatch_of_an_event_without_an_event_offset_at_the_transactions_offset(
        TransactionEventShape shape)
    {
        var projected = await ProjectOnlyInterfaceTransactionEventAsync(new TransactionEventScenario
        {
            Event = shape,
            OmitEventOffset = true,
        });

        var unclassified = ShouldBeUnclassifiedInterfaceEvent(projected);
        unclassified.Kind.Should().Be(UnmatchedKindOf(shape));
        unclassified.Offset.Should().Be(LedgerOffset.At(70L));
    }

    [Theory]
    [InlineData(TransactionEventShape.Created)]
    [InlineData(TransactionEventShape.Archived)]
    [InlineData(TransactionEventShape.Exercised)]
    public async Task ProjectTransactionEventsAsInterface_frames_a_missing_synchronizer_of_an_event_without_an_event_offset_at_the_transactions_offset(
        TransactionEventShape shape)
    {
        var projected = await ProjectOnlyInterfaceTransactionEventAsync(InterfaceTransactionEvent(shape) with
        {
            Synchronizer = null,
            OmitEventOffset = true,
        });

        var unclassified = ShouldBeUnclassifiedInterfaceEvent(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
        unclassified.Offset.Should().Be(LedgerOffset.At(70L));
    }

    [Theory]
    [InlineData(InterfaceViewRendering.ComputationFailed)]
    [InlineData(InterfaceViewRendering.ValueOmitted)]
    public async Task ProjectTransactionEventsAsInterface_frames_an_unavailable_view_of_a_created_event_without_an_event_offset_at_the_transactions_offset(
        InterfaceViewRendering interfaceView)
    {
        var projected = await ProjectOnlyInterfaceTransactionEventAsync(new TransactionEventScenario
        {
            Event = TransactionEventShape.Created,
            InterfaceView = interfaceView,
            OmitEventOffset = true,
        });

        var unclassified = ShouldBeUnclassifiedInterfaceEvent(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
        unclassified.Offset.Should().Be(LedgerOffset.At(70L));
    }

    [Theory]
    [InlineData(TransactionEventShape.Created)]
    [InlineData(TransactionEventShape.Archived)]
    [InlineData(TransactionEventShape.Exercised)]
    public async Task ProjectTransactionEvents_reports_a_matching_event_without_an_event_offset_at_the_transactions_offset(
        TransactionEventShape shape)
    {
        var projected = await ProjectOnlyTransactionEventAsync(
            new TransactionEventScenario { Event = shape, OmitEventOffset = true });

        var expectedOffset = LedgerOffset.At(70L);
        switch (shape)
        {
            case TransactionEventShape.Created:
                projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>()
                    .Which.Offset.Should().Be(expectedOffset);
                break;
            case TransactionEventShape.Archived:
                projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Archived>()
                    .Which.Offset.Should().Be(expectedOffset);
                break;
            case TransactionEventShape.Exercised:
                projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Exercised>()
                    .Which.Offset.Should().Be(expectedOffset);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
    }

    [Theory]
    [InlineData(TransactionEventShape.Created)]
    [InlineData(TransactionEventShape.Archived)]
    [InlineData(TransactionEventShape.Exercised)]
    public async Task ProjectTransactionEventsAsInterface_reports_a_matching_event_without_an_event_offset_at_the_transactions_offset(
        TransactionEventShape shape)
    {
        var projected = await ProjectOnlyInterfaceTransactionEventAsync(
            InterfaceTransactionEvent(shape) with { OmitEventOffset = true });

        var expectedOffset = LedgerOffset.At(70L);
        switch (shape)
        {
            case TransactionEventShape.Created:
                projected.Should().BeOfType<InterfaceEvent.Created>().Which.Offset.Should().Be(expectedOffset);
                break;
            case TransactionEventShape.Archived:
                projected.Should().BeOfType<InterfaceEvent.Archived>().Which.Offset.Should().Be(expectedOffset);
                break;
            case TransactionEventShape.Exercised:
                projected.Should().BeOfType<InterfaceEvent.Exercised>().Which.Offset.Should().Be(expectedOffset);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
    }

    [Theory]
    [InlineData(ReassignmentEventShape.Assigned)]
    [InlineData(ReassignmentEventShape.Unassigned)]
    public async Task ProjectReassignmentEvents_frames_a_marker_mismatch_of_an_event_without_an_event_offset_at_the_reassignments_offset(
        ReassignmentEventShape shape)
    {
        var projected = await ProjectOnlyReassignmentEventAsync(new ReassignmentEventScenario
        {
            Event = shape,
            EntityName = ActiveContractScenario.OtherEntityName,
            OmitEventOffset = true,
        });

        var unclassified = ShouldBeUnclassified(projected);
        unclassified.Kind.Should().Be(UnmatchedKindOf(shape));
        unclassified.Offset.Should().Be(LedgerOffset.At(80L));
    }

    [Theory]
    [InlineData(ReassignmentEventShape.Assigned, true)]
    [InlineData(ReassignmentEventShape.Assigned, false)]
    [InlineData(ReassignmentEventShape.Unassigned, true)]
    [InlineData(ReassignmentEventShape.Unassigned, false)]
    public async Task ProjectReassignmentEvents_frames_a_missing_synchronizer_of_an_event_without_an_event_offset_at_the_reassignments_offset(
        ReassignmentEventShape shape, bool omitSource)
    {
        var projected = await ProjectOnlyReassignmentEventAsync(new ReassignmentEventScenario
        {
            Event = shape,
            Source = omitSource ? null : ActiveContractScenario.SynchronizerId,
            Target = omitSource ? ActiveContractScenario.CounterpartSynchronizerId : null,
            OmitEventOffset = true,
        });

        var unclassified = ShouldBeUnclassified(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
        unclassified.Offset.Should().Be(LedgerOffset.At(80L));
    }

    [Theory]
    [InlineData(ReassignmentEventShape.Assigned)]
    [InlineData(ReassignmentEventShape.Unassigned)]
    public async Task ProjectReassignmentEvents_reports_a_matching_event_without_an_event_offset_at_the_reassignments_offset(
        ReassignmentEventShape shape)
    {
        var projected = await ProjectOnlyReassignmentEventAsync(
            new ReassignmentEventScenario { Event = shape, OmitEventOffset = true });

        var expectedOffset = LedgerOffset.At(80L);
        switch (shape)
        {
            case ReassignmentEventShape.Assigned:
                projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Assigned>()
                    .Which.Offset.Should().Be(expectedOffset);
                break;
            case ReassignmentEventShape.Unassigned:
                projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unassigned>()
                    .Which.Offset.Should().Be(expectedOffset);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
    }

    [Fact]
    public async Task ProjectReassignmentEventsAsInterface_frames_an_assigned_event_not_implementing_the_interface_without_an_event_offset_at_the_reassignments_offset()
    {
        var projected = await ProjectOnlyInterfaceReassignmentEventAsync(new ReassignmentEventScenario
        {
            Event = ReassignmentEventShape.Assigned,
            OmitEventOffset = true,
        });

        var unclassified = ShouldBeUnclassifiedInterfaceEvent(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.AssignedEvent);
        unclassified.Offset.Should().Be(LedgerOffset.At(80L));
    }

    [Theory]
    [InlineData(ReassignmentEventShape.Assigned, true)]
    [InlineData(ReassignmentEventShape.Assigned, false)]
    [InlineData(ReassignmentEventShape.Unassigned, true)]
    [InlineData(ReassignmentEventShape.Unassigned, false)]
    public async Task ProjectReassignmentEventsAsInterface_frames_a_missing_synchronizer_of_an_event_without_an_event_offset_at_the_reassignments_offset(
        ReassignmentEventShape shape, bool omitSource)
    {
        var projected = await ProjectOnlyInterfaceReassignmentEventAsync(InterfaceReassignmentEvent(shape) with
        {
            Source = omitSource ? null : ActiveContractScenario.SynchronizerId,
            Target = omitSource ? ActiveContractScenario.CounterpartSynchronizerId : null,
            OmitEventOffset = true,
        });

        var unclassified = ShouldBeUnclassifiedInterfaceEvent(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
        unclassified.Offset.Should().Be(LedgerOffset.At(80L));
    }

    [Theory]
    [InlineData(ReassignmentEventShape.Assigned)]
    [InlineData(ReassignmentEventShape.Unassigned)]
    public async Task ProjectReassignmentEventsAsInterface_reports_a_matching_event_without_an_event_offset_at_the_reassignments_offset(
        ReassignmentEventShape shape)
    {
        var projected = await ProjectOnlyInterfaceReassignmentEventAsync(
            InterfaceReassignmentEvent(shape) with { OmitEventOffset = true });

        var expectedOffset = LedgerOffset.At(80L);
        switch (shape)
        {
            case ReassignmentEventShape.Assigned:
                projected.Should().BeOfType<InterfaceEvent.Assigned>().Which.Offset.Should().Be(expectedOffset);
                break;
            case ReassignmentEventShape.Unassigned:
                projected.Should().BeOfType<InterfaceEvent.Unassigned>().Which.Offset.Should().Be(expectedOffset);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
    }

    private static ActiveContractScenario ActiveEntryWithoutCreatedEventOffset(ActiveContractEntry entry) => new()
    {
        Entry = entry,
        OmitCreatedEventOffset = true,
        SnapshotOffset = 77L,
    };

    [Theory]
    [InlineData(ActiveContractEntry.Active, 77L)]
    [InlineData(ActiveContractEntry.IncompleteAssigned, 77L)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned, 43L)]
    public async Task ProjectActiveContractEntry_reports_a_matching_Created_without_an_event_offset_at_the_entrys_resume_offset(
        ActiveContractEntry entry, long expectedOffset)
    {
        var projected = await ProjectActiveContractEntryAsync(ActiveEntryWithoutCreatedEventOffset(entry));

        projected[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>()
            .Which.Offset.Should().Be(LedgerOffset.At(expectedOffset));
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active, 77L)]
    [InlineData(ActiveContractEntry.IncompleteAssigned, 77L)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned, 43L)]
    public async Task ProjectActiveContractEntry_frames_a_marker_mismatch_of_a_Created_without_an_event_offset_at_the_entrys_resume_offset(
        ActiveContractEntry entry, long expectedOffset)
    {
        var projected = await ProjectOnlyEventAsync(ActiveEntryWithoutCreatedEventOffset(entry) with
        {
            EntityName = ActiveContractScenario.OtherEntityName,
        });

        var unclassified = ShouldBeUnclassified(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.CreatedEvent);
        unclassified.Offset.Should().Be(LedgerOffset.At(expectedOffset));
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active, 77L)]
    [InlineData(ActiveContractEntry.IncompleteAssigned, 77L)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned, 43L)]
    public async Task ProjectActiveContractEntry_frames_a_missing_synchronizer_of_a_Created_without_an_event_offset_at_the_entrys_resume_offset(
        ActiveContractEntry entry, long expectedOffset)
    {
        var projected = await ProjectOnlyEventAsync(ActiveEntryWithoutCreatedEventOffset(entry) with { Synchronizer = null });

        var unclassified = ShouldBeUnclassified(projected);
        unclassified.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
        unclassified.Offset.Should().Be(LedgerOffset.At(expectedOffset));
    }

    [Fact]
    public async Task ProjectActiveContractEntry_reports_the_Unassigned_of_an_IncompleteUnassigned_without_an_event_offset_at_the_snapshot_offset()
    {
        var projected = await ProjectActiveContractEntryAsync(new ActiveContractScenario
        {
            Entry = ActiveContractEntry.IncompleteUnassigned,
            OmitUnassignedEventOffset = true,
            SnapshotOffset = 77L,
        });

        projected.Should().HaveCount(2);
        projected[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>()
            .Which.Offset.Should().Be(LedgerOffset.At(42L));
        projected[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unassigned>()
            .Which.Offset.Should().Be(LedgerOffset.At(77L));
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active, 77L)]
    [InlineData(ActiveContractEntry.IncompleteAssigned, 77L)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned, 43L)]
    public async Task ProjectActiveContractEntryAsInterface_reports_a_matching_Created_without_an_event_offset_at_the_entrys_resume_offset(
        ActiveContractEntry entry, long expectedOffset)
    {
        var projected = await ProjectActiveContractEntryAsInterfaceAsync(
            ActiveEntryWithoutCreatedEventOffset(entry) with
            {
                EntityName = ActiveContractScenario.OtherEntityName,
                InterfaceView = InterfaceViewRendering.Computed,
            });

        projected[0].Should().BeOfType<InterfaceEvent.Created>()
            .Which.Offset.Should().Be(LedgerOffset.At(expectedOffset));
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active, 77L)]
    [InlineData(ActiveContractEntry.IncompleteAssigned, 77L)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned, 43L)]
    public async Task ProjectActiveContractEntryAsInterface_frames_a_Created_without_an_event_offset_that_carries_no_interface_view_at_the_entrys_resume_offset(
        ActiveContractEntry entry, long expectedOffset)
    {
        var projected = await ProjectActiveContractEntryAsInterfaceAsync(ActiveEntryWithoutCreatedEventOffset(entry));

        var unclassified = ShouldBeUnclassifiedInterfaceEvent(projected[0]);
        unclassified.Kind.Should().Be(UnclassifiedKind.CreatedEvent);
        unclassified.Offset.Should().Be(LedgerOffset.At(expectedOffset));
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active, 77L)]
    [InlineData(ActiveContractEntry.IncompleteAssigned, 77L)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned, 43L)]
    public async Task ProjectActiveContractEntryAsInterface_frames_a_missing_synchronizer_of_a_Created_without_an_event_offset_at_the_entrys_resume_offset(
        ActiveContractEntry entry, long expectedOffset)
    {
        var projected = await ProjectActiveContractEntryAsInterfaceAsync(
            ActiveEntryWithoutCreatedEventOffset(entry) with
            {
                EntityName = ActiveContractScenario.OtherEntityName,
                InterfaceView = InterfaceViewRendering.Computed,
                Synchronizer = null,
            });

        var unclassified = ShouldBeUnclassifiedInterfaceEvent(projected[0]);
        unclassified.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
        unclassified.Offset.Should().Be(LedgerOffset.At(expectedOffset));
    }

    [Theory]
    [InlineData(ActiveContractEntry.Active, 77L)]
    [InlineData(ActiveContractEntry.IncompleteAssigned, 77L)]
    [InlineData(ActiveContractEntry.IncompleteUnassigned, 43L)]
    public async Task ProjectActiveContractEntryAsInterface_frames_an_unavailable_view_of_a_Created_without_an_event_offset_at_the_entrys_resume_offset(
        ActiveContractEntry entry, long expectedOffset)
    {
        var projected = await ProjectActiveContractEntryAsInterfaceAsync(
            ActiveEntryWithoutCreatedEventOffset(entry) with
            {
                EntityName = ActiveContractScenario.OtherEntityName,
                InterfaceView = InterfaceViewRendering.ComputationFailed,
            });

        var unclassified = ShouldBeUnclassifiedInterfaceEvent(projected[0]);
        unclassified.Kind.Should().Be(UnclassifiedKind.InterfaceViewUnavailable);
        unclassified.Offset.Should().Be(LedgerOffset.At(expectedOffset));
    }

    [Fact]
    public async Task ProjectActiveContractEntryAsInterface_reports_the_Unassigned_of_an_IncompleteUnassigned_without_an_event_offset_at_the_snapshot_offset()
    {
        var projected = await ProjectActiveContractEntryAsInterfaceAsync(new ActiveContractScenario
        {
            Entry = ActiveContractEntry.IncompleteUnassigned,
            EntityName = ActiveContractScenario.OtherEntityName,
            InterfaceView = InterfaceViewRendering.Computed,
            OmitUnassignedEventOffset = true,
            SnapshotOffset = 77L,
        });

        projected.Should().HaveCount(2);
        projected[0].Should().BeOfType<InterfaceEvent.Created>().Which.Offset.Should().Be(LedgerOffset.At(42L));
        projected[1].Should().BeOfType<InterfaceEvent.Unassigned>().Which.Offset.Should().Be(LedgerOffset.At(77L));
    }
}
