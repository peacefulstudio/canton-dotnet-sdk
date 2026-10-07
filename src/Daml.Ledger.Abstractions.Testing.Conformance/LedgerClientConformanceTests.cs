// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Xunit;

namespace Daml.Ledger.Abstractions.Testing.Conformance;

/// <summary>
/// The documented behavioral contract for an <see cref="ILedgerClient"/> implementation.
/// Adopters subclass with a concrete client factory and a probe Daml marker; the seeded
/// client must expose the canonical scenario: at least one active contract, one
/// unclassifiable row, a terminal checkpoint, one archived <typeparamref name="TProbe"/>
/// reachable on both the ACS-delta subscription
/// (<see cref="ILedgerStreamer.SubscribeAsync{T}(SubmitterInfo, LedgerOffset?, LedgerOffset?, CancellationToken)"/>,
/// which surfaces archival as a first-class <see cref="ContractStreamEvent{T}.Archived"/> and never an
/// <see cref="ContractStreamEvent{T}.Exercised"/>) and the ledger-effects subscription
/// (<see cref="ILedgerStreamer.SubscribeLedgerEffectsAsync{T}"/>, which signals archival
/// with a consuming <see cref="ContractStreamEvent{T}.Exercised"/> and never an
/// <see cref="ContractStreamEvent{T}.Archived"/>), honored <c>(fromOffset, toOffset]</c>
/// bounds, and cancellation-honoring streams. Every stream check runs once against the template
/// reads and once against the interface reads, where the probe is read through
/// <see cref="IConformanceProbe"/> and each probe contract carries a
/// <see cref="ConformanceProbeView"/> under the same contract id and offset.
/// </summary>
/// <typeparam name="TProbe">The Daml template the seeded snapshot/stream is filtered to. It
/// implements <see cref="IConformanceProbe"/>, so the same seeded rows are served through the
/// interface reads.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "These are xUnit test methods shipped as an abstract conformance base; they follow the repo-wide Subject_scenario_expectation naming that test readers and failure output depend on.")]
public abstract class LedgerClientConformanceTests<TProbe>
    where TProbe : ITemplate, IDamlRecord<TProbe>, IImplements<IConformanceProbe>
{
    /// <summary>
    /// Creates a client seeded with the canonical conformance scenario. The seed must include
    /// one archived <typeparamref name="TProbe"/> at an offset no later than the offset
    /// <see cref="ILedgerReader.GetLedgerEndAsync"/> returns, surfaced as a
    /// <see cref="ContractStreamEvent{T}.Archived"/> on the ACS-delta subscription and as a
    /// consuming <see cref="ContractStreamEvent{T}.Exercised"/> on the ledger-effects
    /// subscription, so both stream-shape checks read a real archival signal.
    /// </summary>
    protected abstract ILedgerClient CreateClient();

    /// <summary>The submitter whose visibility scopes the reads.</summary>
    protected abstract SubmitterInfo Reader { get; }

    /// <summary>
    /// The budget within which a stream the contract requires to terminate (an ACS
    /// snapshot, a bounded subscription) must complete. Adopters whose transport is
    /// slower to seed may widen it.
    /// </summary>
    protected virtual TimeSpan StreamTimeout => TimeSpan.FromSeconds(30);

    /// <summary>
    /// The offset at which the seeded client exposes an empty active-contract-set
    /// snapshot. Defaults to <see cref="LedgerOffset.Begin"/> (offset 0). Adopters whose
    /// transport rejects an active-contract-set query at offset 0 with
    /// <c>INVALID_ARGUMENT</c> override this to a known-empty offset their transport accepts.
    /// </summary>
    protected virtual LedgerOffset EmptySnapshotOffset => LedgerOffset.Begin;

    /// <summary>
    /// A client whose active-contract-set snapshot faults mid-stream, or <c>null</c> if
    /// the adopter's transport cannot induce a mid-snapshot transport fault
    /// deterministically. When non-null, the returned client's
    /// <see cref="ILedgerStreamer.SubscribeActiveAsync{T}"/> and its interface counterpart
    /// <see cref="ILedgerStreamer.SubscribeActiveAsync{TInterface, TView}"/> must each terminate
    /// with a single <see cref="AcsSnapshotEntry{T}.StreamError"/> and yield no terminal
    /// <see cref="AcsSnapshotEntry{T}.Checkpoint"/>. Defaults to <c>null</c>, which skips
    /// both fault-path conformance checks.
    /// </summary>
    protected virtual ILedgerClient? CreateFaultingSnapshotClient() => null;

    /// <summary>
    /// A write-capable client and submission proving the submitter-authority contract of
    /// <see cref="ILedgerWriter.SubmitAndWaitAsync"/> / <see cref="ILedgerWriter.TrySubmitAndWaitForTransactionAsync"/>,
    /// or <c>null</c> if the adopter cannot seed a deterministic authorization boundary
    /// (e.g. a fake with no notion of authorized/unauthorized parties). Defaults to
    /// <c>null</c>, which skips the write-path checks below.
    /// </summary>
    protected virtual WriteConformanceFixture? CreateWriteFixture() => null;

    /// <summary>
    /// A write-capable client and the submissions proving the <c>commandId</c> deduplication
    /// contract of
    /// <see cref="ILedgerWriter.TryExerciseAsync{TResult}(ExerciseCommand, SubmitterInfo, string?, CommandId?, TimeSpan?, CancellationToken)"/> /
    /// <see cref="ILedgerWriter.TryCreateAsync{TTemplate}(TTemplate, SubmitterInfo, string?, CommandId?, TimeSpan?, CancellationToken)"/>,
    /// or <c>null</c> if the adopter cannot read back the <c>command_id</c> the participant
    /// recorded for a submission. Defaults to <c>null</c>, which skips the command-id checks
    /// below. Called afresh by each check, which then submits exactly once.
    /// </summary>
    protected virtual CommandIdConformanceFixture? CreateCommandIdFixture() => null;

    /// <summary>A cancelled live subscription surfaces cancellation, not an in-band error.</summary>
    [Fact]
    public Task Cancelling_a_live_subscription_throws_OperationCanceledException() =>
        VerifyCancellationThrows(TemplateFamily.Instance);

    /// <summary>An unclassifiable snapshot row is surfaced, never silently dropped.</summary>
    [Fact]
    public Task Active_snapshot_surfaces_unclassifiable_rows_as_Unclassified() =>
        VerifySnapshotSurfacesUnclassifiedRows(TemplateFamily.Instance);

    /// <summary>The snapshot's final entry is the terminal checkpoint.</summary>
    [Fact]
    public Task Active_snapshot_ends_with_a_terminal_Checkpoint() =>
        VerifySnapshotEndsWithCheckpoint(TemplateFamily.Instance);

    /// <summary>Seeded active contracts arrive before the checkpoint; the stream is not truncated.</summary>
    [Fact]
    public Task Active_snapshot_yields_seeded_rows_before_the_checkpoint() =>
        VerifySnapshotYieldsSeededRowsBeforeCheckpoint(TemplateFamily.Instance);

    /// <summary>An empty snapshot still terminates with the single terminal checkpoint.</summary>
    [Fact]
    public Task Empty_active_snapshot_still_ends_with_a_terminal_Checkpoint() =>
        VerifyEmptySnapshotEndsWithCheckpoint(TemplateFamily.Instance);

    /// <summary>
    /// A mid-snapshot transport fault surfaces in-band as a terminal
    /// <see cref="AcsSnapshotEntry{T}.StreamError"/>, never thrown, and in place of the
    /// terminal <see cref="AcsSnapshotEntry{T}.Checkpoint"/> a successful snapshot ends with.
    /// Opt-in: skipped unless the adopter overrides <see cref="CreateFaultingSnapshotClient"/>.
    /// </summary>
    [Fact]
    public Task Active_snapshot_surfaces_a_mid_snapshot_fault_as_StreamError() =>
        VerifySnapshotSurfacesMidSnapshotFault(TemplateFamily.Instance);

    /// <summary>An unclassifiable interface snapshot row is surfaced, never silently dropped.</summary>
    [Fact]
    public Task Interface_active_snapshot_surfaces_unclassifiable_rows_as_Unclassified() =>
        VerifySnapshotSurfacesUnclassifiedRows(InterfaceFamily.Instance);

    /// <summary>The interface snapshot's final entry is the terminal checkpoint.</summary>
    [Fact]
    public Task Interface_active_snapshot_ends_with_a_terminal_Checkpoint() =>
        VerifySnapshotEndsWithCheckpoint(InterfaceFamily.Instance);

    /// <summary>Seeded interface contracts arrive before the checkpoint; the stream is not truncated.</summary>
    [Fact]
    public Task Interface_active_snapshot_yields_seeded_rows_before_the_checkpoint() =>
        VerifySnapshotYieldsSeededRowsBeforeCheckpoint(InterfaceFamily.Instance);

    /// <summary>An empty interface snapshot still terminates with the single terminal checkpoint.</summary>
    [Fact]
    public Task Interface_empty_active_snapshot_still_ends_with_a_terminal_Checkpoint() =>
        VerifyEmptySnapshotEndsWithCheckpoint(InterfaceFamily.Instance);

    /// <summary>
    /// A mid-snapshot transport fault on the interface snapshot surfaces in-band as a terminal
    /// <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}.StreamError"/>, never thrown, and in
    /// place of the terminal <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}.Checkpoint"/>
    /// a successful snapshot ends with. Opt-in: skipped unless the adopter overrides
    /// <see cref="CreateFaultingSnapshotClient"/>.
    /// </summary>
    [Fact]
    public Task Interface_active_snapshot_surfaces_a_mid_snapshot_fault_as_StreamError() =>
        VerifySnapshotSurfacesMidSnapshotFault(InterfaceFamily.Instance);

    /// <summary>
    /// The interface snapshot serves the template snapshot's <c>Created</c> contracts, each
    /// coerced through <c>ToInterfaceContractId</c>, under the same offsets and the same
    /// <c>SynchronizerId</c>, with a <see cref="ConformanceProbeView"/> whose
    /// <see cref="ConformanceProbeView.Amount"/> is 42.5. A transport that shapes the rows correctly
    /// but corrupts the decoded view, assigns the wrong contract id or the wrong synchronizer would
    /// pass every structure check; this check catches that regression.
    /// </summary>
    [Fact]
    public async Task Interface_active_snapshot_serves_the_template_contracts_with_the_view_amount_42_5()
    {
        await using var client = CreateClient();

        await VerifyInterfaceServesTemplateContractsWithRenderedView(
            family => CollectSnapshot(family, client), "interface SubscribeActiveAsync");
    }

    /// <summary>
    /// The interface ACS-delta window serves the template window's <c>Created</c> contracts, each
    /// coerced through <c>ToInterfaceContractId</c>, under the same offsets, with a
    /// <see cref="ConformanceProbeView"/> whose <see cref="ConformanceProbeView.Amount"/> is 42.5.
    /// </summary>
    [Fact]
    public async Task Interface_acs_delta_subscription_serves_the_template_contracts_with_the_view_amount_42_5()
    {
        await using var client = CreateClient();
        var end = await client.GetLedgerEndAsync();

        await VerifyInterfaceServesTemplateContractsWithRenderedView(
            family => CollectBounded(family, client, LedgerOffset.Begin, end), "interface SubscribeAsync");
    }

    /// <summary>
    /// The interface ledger-effects window serves the template window's <c>Created</c> contracts,
    /// each coerced through <c>ToInterfaceContractId</c>, under the same offsets, with a
    /// <see cref="ConformanceProbeView"/> whose <see cref="ConformanceProbeView.Amount"/> is 42.5.
    /// </summary>
    [Fact]
    public async Task Interface_ledger_effects_subscription_serves_the_template_contracts_with_the_view_amount_42_5()
    {
        await using var client = CreateClient();
        var end = await client.GetLedgerEndAsync();

        await VerifyInterfaceServesTemplateContractsWithRenderedView(
            family => CollectWithinBudget(
                family.LedgerEffects(client, Reader, LedgerOffset.Begin, end, CancellationToken.None),
                $"A bounded {family.SubscribeLedgerEffectsName} (toOffset {end.Value}) must complete"),
            "interface SubscribeLedgerEffectsAsync");
    }

    /// <summary>fromOffset is exclusive: resuming from an offset does not re-deliver the event at it.</summary>
    [Fact]
    public Task Subscribing_from_an_offset_excludes_the_event_at_that_offset() =>
        VerifyFromOffsetIsExclusive(TemplateFamily.Instance);

    /// <summary>toOffset is inclusive and terminal: the event at toOffset is delivered, then the stream completes.</summary>
    [Fact]
    public Task Bounded_subscription_delivers_the_event_at_toOffset_then_completes() =>
        VerifyToOffsetIsInclusiveAndTerminal(TemplateFamily.Instance);

    /// <summary>
    /// The ledger-effects subscription signals archival with a consuming
    /// <see cref="ContractStreamEvent{T}.Exercised"/>, never an
    /// <see cref="ContractStreamEvent{T}.Archived"/> variant — the shape's defining contract.
    /// The seeded scenario's archived <typeparamref name="TProbe"/> must reach this stream as
    /// that consuming <see cref="ContractStreamEvent{T}.Exercised"/>: a projector that drops
    /// archival altogether satisfies the exclusion vacuously and conveys nothing.
    /// </summary>
    [Fact]
    public Task Ledger_effects_subscription_never_yields_Archived() =>
        VerifyLedgerEffectsNeverYieldArchived(TemplateFamily.Instance);

    /// <summary>
    /// The ACS-delta subscription surfaces archival as a first-class
    /// <see cref="ContractStreamEvent{T}.Archived"/> event, never an
    /// <see cref="ContractStreamEvent{T}.Exercised"/> variant — the shape's defining contract.
    /// The seeded scenario's archived <typeparamref name="TProbe"/> must reach this stream as
    /// that <see cref="ContractStreamEvent{T}.Archived"/>: a projector that drops archival
    /// altogether satisfies the exclusion vacuously and conveys nothing.
    /// </summary>
    [Fact]
    public Task Acs_delta_subscription_never_yields_Exercised() =>
        VerifyAcsDeltaNeverYieldsExercised(TemplateFamily.Instance);

    /// <summary>A cancelled live interface subscription surfaces cancellation, not an in-band error.</summary>
    [Fact]
    public Task Interface_cancelling_a_live_subscription_throws_OperationCanceledException() =>
        VerifyCancellationThrows(InterfaceFamily.Instance);

    /// <summary>
    /// fromOffset is exclusive on the interface subscription: resuming from an offset does not
    /// re-deliver the event at it.
    /// </summary>
    [Fact]
    public Task Interface_subscribing_from_an_offset_excludes_the_event_at_that_offset() =>
        VerifyFromOffsetIsExclusive(InterfaceFamily.Instance);

    /// <summary>
    /// toOffset is inclusive and terminal on the interface subscription: the event at toOffset is
    /// delivered, then the stream completes.
    /// </summary>
    [Fact]
    public Task Interface_bounded_subscription_delivers_the_event_at_toOffset_then_completes() =>
        VerifyToOffsetIsInclusiveAndTerminal(InterfaceFamily.Instance);

    /// <summary>
    /// The interface ledger-effects subscription signals archival with a consuming
    /// <see cref="InterfaceStreamEvent{TInterface, TView}.Exercised"/>, never an
    /// <see cref="InterfaceStreamEvent{TInterface, TView}.Archived"/> variant, and the seeded
    /// scenario's archived <typeparamref name="TProbe"/> must reach it as that consuming event.
    /// </summary>
    [Fact]
    public Task Interface_ledger_effects_subscription_never_yields_Archived() =>
        VerifyLedgerEffectsNeverYieldArchived(InterfaceFamily.Instance);

    /// <summary>
    /// The interface ACS-delta subscription surfaces archival as a first-class
    /// <see cref="InterfaceStreamEvent{TInterface, TView}.Archived"/> event, never an
    /// <see cref="InterfaceStreamEvent{TInterface, TView}.Exercised"/> variant, and the seeded
    /// scenario's archived <typeparamref name="TProbe"/> must reach it as that event.
    /// </summary>
    [Fact]
    public Task Interface_acs_delta_subscription_never_yields_Exercised() =>
        VerifyAcsDeltaNeverYieldsExercised(InterfaceFamily.Instance);

    /// <summary>
    /// <see cref="ILedgerStreamer.SubscribeActiveAsync{TInterface, TView}"/> rejects a <c>null</c>
    /// <see cref="ViewDescriptor{TInterface, TView}"/> with an <see cref="ArgumentNullException"/>
    /// thrown at the call, before the stream is enumerated.
    /// </summary>
    [Fact]
    public Task Interface_SubscribeActiveAsync_throws_ArgumentNullException_for_a_null_ViewDescriptor() =>
        VerifyNullDescriptorIsRejectedAtTheCall(
            client => client.SubscribeActiveAsync<IConformanceProbe, ConformanceProbeView>(null!, Reader),
            "interface SubscribeActiveAsync");

    /// <summary>
    /// <see cref="ILedgerStreamer.SubscribeAsync{TInterface, TView}(ViewDescriptor{TInterface, TView}, SubmitterInfo, LedgerOffset?, LedgerOffset?, CancellationToken)"/>
    /// rejects a <c>null</c> <see cref="ViewDescriptor{TInterface, TView}"/> with an
    /// <see cref="ArgumentNullException"/> thrown at the call, before the stream is enumerated.
    /// </summary>
    [Fact]
    public Task Interface_SubscribeAsync_throws_ArgumentNullException_for_a_null_ViewDescriptor() =>
        VerifyNullDescriptorIsRejectedAtTheCall(
            client => client.SubscribeAsync<IConformanceProbe, ConformanceProbeView>(
                null!, Reader, LedgerOffset.Begin, null, CancellationToken.None),
            "interface SubscribeAsync");

    /// <summary>
    /// <see cref="ILedgerStreamer.SubscribeLedgerEffectsAsync{TInterface, TView}"/> rejects a
    /// <c>null</c> <see cref="ViewDescriptor{TInterface, TView}"/> with an
    /// <see cref="ArgumentNullException"/> thrown at the call, before the stream is enumerated.
    /// </summary>
    [Fact]
    public Task Interface_SubscribeLedgerEffectsAsync_throws_ArgumentNullException_for_a_null_ViewDescriptor() =>
        VerifyNullDescriptorIsRejectedAtTheCall(
            client => client.SubscribeLedgerEffectsAsync<IConformanceProbe, ConformanceProbeView>(
                null!, Reader, LedgerOffset.Begin, null, CancellationToken.None),
            "interface SubscribeLedgerEffectsAsync");

    /// <summary>
    /// <see cref="ILedgerWriter.TrySubmitAndWaitForTransactionAsync"/> must apply the
    /// <c>submitter</c> parameter authoritatively via <c>CommandsSubmission.WithSubmitter</c>,
    /// overwriting any <see cref="CommandsSubmission.ActAs"/> already set on the submission —
    /// not dispatch whatever the caller pre-set. Opt-in: skipped unless the adopter overrides
    /// <see cref="CreateWriteFixture"/>.
    /// </summary>
    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_submitter_parameter_overrides_pre_set_ActAs()
    {
        var maybeFixture = CreateWriteFixture();
        Assert.SkipWhen(maybeFixture is null, WriteFixtureSkipReason);
        await using var fixture = maybeFixture!;

        var submissionWithWrongActAs = fixture.Submission.WithActAs(fixture.Unauthorized);

        var outcome = await fixture.Client.TrySubmitAndWaitForTransactionAsync(submissionWithWrongActAs, fixture.Authorized);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>(
            "the submitter parameter must win over the pre-set (unauthorized) ActAs; an implementation " +
            "that dispatches the pre-set ActAs instead of applying the submitter authoritatively would be " +
            "rejected by the seeded client as unauthorized");
    }

    /// <summary>
    /// <see cref="ILedgerWriter.SubmitAndWaitAsync"/> must apply the <c>submitter</c> parameter
    /// authoritatively via <c>CommandsSubmission.WithSubmitter</c>, overwriting any
    /// <see cref="CommandsSubmission.ActAs"/> already set on the submission — not dispatch
    /// whatever the caller pre-set. Opt-in: skipped unless the adopter overrides
    /// <see cref="CreateWriteFixture"/>.
    /// </summary>
    [Fact]
    public async Task SubmitAndWaitAsync_submitter_parameter_overrides_pre_set_ActAs()
    {
        var maybeFixture = CreateWriteFixture();
        Assert.SkipWhen(maybeFixture is null, WriteFixtureSkipReason);
        await using var fixture = maybeFixture!;

        var submissionWithWrongActAs = fixture.Submission.WithActAs(fixture.Unauthorized);

        var act = () => fixture.Client.SubmitAndWaitAsync(submissionWithWrongActAs, fixture.Authorized);

        await act.Should().NotThrowAsync(
            "the submitter parameter must win over the pre-set (unauthorized) ActAs; an implementation " +
            "that dispatches the pre-set ActAs instead of applying the submitter authoritatively would be " +
            "rejected by the seeded client as unauthorized");
    }

    /// <summary>
    /// <see cref="ILedgerWriter.TrySubmitAndWaitForTransactionAsync"/> must not merge the
    /// <c>submitter</c> parameter with <see cref="CommandsSubmission.ActAs"/> already set on
    /// the submission — an authorized pre-set <c>ActAs</c> must not leak through and rescue an
    /// unauthorized <c>submitter</c>. Opt-in: skipped unless the adopter overrides
    /// <see cref="CreateWriteFixture"/>. Uses its own fresh <see cref="CreateWriteFixture"/>
    /// call (a distinct seeded client/contract from the sibling override check) so a stateful
    /// adopter's already-consumed contract from that check cannot masquerade as this one's
    /// authorization failure.
    /// </summary>
    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_submitter_parameter_is_not_merged_with_pre_set_ActAs()
    {
        var maybeFixture = CreateWriteFixture();
        Assert.SkipWhen(maybeFixture is null, WriteFixtureSkipReason);
        await using var fixture = maybeFixture!;

        var submissionWithAuthorizedActAs = fixture.Submission.WithActAs(fixture.Authorized);

        var outcome = await fixture.Client.TrySubmitAndWaitForTransactionAsync(submissionWithAuthorizedActAs, fixture.Unauthorized);

        outcome.Should().NotBeOfType<ExerciseOutcome<TransactionResult>.One>(
            "the submitter parameter must replace, not merge with, the pre-set ActAs; an implementation " +
            "that unions the submitter into the existing ActAs instead of overwriting it would let the " +
            "authorized pre-set ActAs rescue an unauthorized submitter");
    }

    /// <summary>
    /// <see cref="ILedgerWriter.SubmitAndWaitAsync"/> must not merge the <c>submitter</c>
    /// parameter with <see cref="CommandsSubmission.ActAs"/> already set on the submission — an
    /// authorized pre-set <c>ActAs</c> must not leak through and rescue an unauthorized
    /// <c>submitter</c>. Opt-in: skipped unless the adopter overrides <see cref="CreateWriteFixture"/>.
    /// Uses its own fresh <see cref="CreateWriteFixture"/> call (a distinct seeded client/contract
    /// from the sibling override check) so a stateful adopter's already-consumed contract from
    /// that check cannot masquerade as this one's authorization failure.
    /// </summary>
    [Fact]
    public async Task SubmitAndWaitAsync_submitter_parameter_is_not_merged_with_pre_set_ActAs()
    {
        var maybeFixture = CreateWriteFixture();
        Assert.SkipWhen(maybeFixture is null, WriteFixtureSkipReason);
        await using var fixture = maybeFixture!;

        var submissionWithAuthorizedActAs = fixture.Submission.WithActAs(fixture.Authorized);

        var act = () => fixture.Client.SubmitAndWaitAsync(submissionWithAuthorizedActAs, fixture.Unauthorized);

        await act.Should().ThrowAsync<LedgerOperationException>(
            "the submitter parameter must replace, not merge with, the pre-set ActAs; an implementation " +
            "that unions the submitter into the existing ActAs instead of overwriting it would let the " +
            "authorized pre-set ActAs rescue an unauthorized submitter");
    }

    /// <summary>
    /// <see cref="ILedgerWriter.TryExerciseAsync{TResult}(ExerciseCommand, SubmitterInfo, string?, CommandId?, TimeSpan?, CancellationToken)"/>
    /// must dispatch a caller-supplied <c>commandId</c> to the participant verbatim, never
    /// substituting one of its own — a caller replaying a lost-but-accepted submission under the
    /// same id gets deduplication only if the id survives the call. Opt-in: skipped unless the
    /// adopter overrides <see cref="CreateCommandIdFixture"/>.
    /// </summary>
    [Fact]
    public async Task TryExerciseAsync_dispatches_the_caller_supplied_commandId_verbatim()
    {
        var maybeFixture = CreateCommandIdFixture();
        Assert.SkipWhen(maybeFixture is null, CommandIdFixtureSkipReason);
        await using var fixture = maybeFixture!;

        await fixture.Exercise(fixture.Client, CallerSuppliedCommandId);

        var recorded = await fixture.ReadRecordedCommandId();

        recorded.Should().Be(
            CallerSuppliedCommandId.Value,
            "the caller-supplied commandId must reach the participant unchanged; an implementation " +
            "that mints its own id anyway silently breaks deduplication across a retry that reuses the id");
    }

    /// <summary>
    /// <see cref="ILedgerWriter.TryExerciseAsync{TResult}(ExerciseCommand, SubmitterInfo, string?, CommandId?, TimeSpan?, CancellationToken)"/>
    /// must mint a command id when the caller omits one, rather than leaving the participant's
    /// <c>command_id</c> unset — an unset id is not deduplicable and leaves the completion
    /// uncorrelatable. Opt-in: skipped unless the adopter overrides
    /// <see cref="CreateCommandIdFixture"/>. Takes its own fresh fixture and submits once, so a
    /// leftover id recorded by the sibling check cannot pass for a minted one.
    /// </summary>
    [Fact]
    public async Task TryExerciseAsync_mints_a_command_id_when_the_caller_omits_one()
    {
        var maybeFixture = CreateCommandIdFixture();
        Assert.SkipWhen(maybeFixture is null, CommandIdFixtureSkipReason);
        await using var fixture = maybeFixture!;

        await fixture.Exercise(fixture.Client, null);

        var recorded = await fixture.ReadRecordedCommandId();

        recorded.Should().NotBeNullOrWhiteSpace(
            "an omitted commandId obliges the implementation to mint one; an implementation that " +
            "forwards the omission and leaves the participant's command_id unset yields a " +
            "submission that can neither be deduplicated nor correlated to its completion");
    }

    /// <summary>
    /// <see cref="ILedgerWriter.TryCreateAsync{TTemplate}(TTemplate, SubmitterInfo, string?, CommandId?, TimeSpan?, CancellationToken)"/>
    /// must dispatch a caller-supplied <c>commandId</c> to the participant verbatim, never
    /// substituting one of its own. Opt-in: skipped unless the adopter overrides
    /// <see cref="CreateCommandIdFixture"/>.
    /// </summary>
    [Fact]
    public async Task TryCreateAsync_dispatches_the_caller_supplied_commandId_verbatim()
    {
        var maybeFixture = CreateCommandIdFixture();
        Assert.SkipWhen(maybeFixture is null, CommandIdFixtureSkipReason);
        await using var fixture = maybeFixture!;

        await fixture.Create(fixture.Client, CallerSuppliedCommandId);

        var recorded = await fixture.ReadRecordedCommandId();

        recorded.Should().Be(
            CallerSuppliedCommandId.Value,
            "the caller-supplied commandId must reach the participant unchanged; an implementation " +
            "that mints its own id anyway silently breaks deduplication across a retry that reuses the id");
    }

    /// <summary>
    /// <see cref="ILedgerWriter.TryCreateAsync{TTemplate}(TTemplate, SubmitterInfo, string?, CommandId?, TimeSpan?, CancellationToken)"/>
    /// must mint a command id when the caller omits one, rather than leaving the participant's
    /// <c>command_id</c> unset. Opt-in: skipped unless the adopter overrides
    /// <see cref="CreateCommandIdFixture"/>. Takes its own fresh fixture and submits once, so a
    /// leftover id recorded by the sibling check cannot pass for a minted one.
    /// </summary>
    [Fact]
    public async Task TryCreateAsync_mints_a_command_id_when_the_caller_omits_one()
    {
        var maybeFixture = CreateCommandIdFixture();
        Assert.SkipWhen(maybeFixture is null, CommandIdFixtureSkipReason);
        await using var fixture = maybeFixture!;

        await fixture.Create(fixture.Client, null);

        var recorded = await fixture.ReadRecordedCommandId();

        recorded.Should().NotBeNullOrWhiteSpace(
            "an omitted commandId obliges the implementation to mint one; an implementation that " +
            "forwards the omission and leaves the participant's command_id unset yields a " +
            "submission that can neither be deduplicated nor correlated to its completion");
    }

    private static readonly CommandId CallerSuppliedCommandId = new("conformance-caller-supplied-command-id");

    private const string CommandIdFixtureSkipReason =
        "adopter opted out of the command-id check: CreateCommandIdFixture() returned null";

    private const string WriteFixtureSkipReason =
        "adopter opted out of the submitter-authority check: CreateWriteFixture() returned null";

    private async Task VerifyNullDescriptorIsRejectedAtTheCall<TItem>(
        Func<ILedgerClient, IAsyncEnumerable<TItem>> read, string readName)
    {
        await using var client = CreateClient();

        var call = () => read(client);

        call.Should().Throw<ArgumentNullException>(
            $"{readName} must reject a null ViewDescriptor at the call, before the stream is enumerated, "
            + "so a missing descriptor fails fast instead of on the first MoveNextAsync");
    }

    private static async Task VerifyInterfaceServesTemplateContractsWithRenderedView(
        Func<IReadFamily, Task<IReadOnlyList<ReadRow>>> collect, string readName)
    {
        var templateCreated = CreatedRows(await collect(TemplateFamily.Instance));
        var interfaceCreated = CreatedRows(await collect(InterfaceFamily.Instance));

        interfaceCreated.Select(r => (r.ContractId, r.Offset)).Should().BeEquivalentTo(
            templateCreated.Select(r => (AsInterfaceContractId(r.ContractId), r.Offset)),
            $"{readName} must serve the template family's Created contracts under the same contract ids as the template family, at the same offsets");
        interfaceCreated.Should().NotBeEmpty(
            $"the conformance scenario must seed at least one probe contract that {readName} classifies");
        interfaceCreated.Select(r => (r.ContractId, r.SynchronizerId)).Should().BeEquivalentTo(
            templateCreated.Select(r => (AsInterfaceContractId(r.ContractId), r.SynchronizerId)),
            $"{readName} must serve each Created row under the same SynchronizerId as the template family");
        interfaceCreated.Should().OnlyContain(
            r => r.ViewAmount == 42.5m,
            $"every Created row of {readName} must carry the view amount 42.5 the kit documents");
    }

    private static List<ReadRow> CreatedRows(IReadOnlyList<ReadRow> rows) =>
        rows.Where(r => r.Kind == RowKind.Created).ToList();

    private static string? AsInterfaceContractId(string? templateContractId) =>
        new ContractId<TProbe>(templateContractId!).ToInterfaceContractId<TProbe, IConformanceProbe>().Value;

    private async Task VerifyCancellationThrows(IReadFamily family)
    {
        await using var client = CreateClient();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => DrainWithinBudget(
            family.AcsDelta(client, Reader, null, null, cts.Token),
            $"a cancelled {family.LiveSubscriptionName} must throw OperationCanceledException, not ignore the token");

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private async Task VerifySnapshotSurfacesUnclassifiedRows(IReadFamily family)
    {
        await using var client = CreateClient();

        var entries = await CollectSnapshot(family, client);

        entries.Should().Contain(e => e.Kind == RowKind.Unclassified);
    }

    private async Task VerifySnapshotEndsWithCheckpoint(IReadFamily family)
    {
        await using var client = CreateClient();

        var entries = await CollectSnapshot(family, client);

        entries.Should().NotBeEmpty();
        entries[^1].Kind.Should().Be(RowKind.Checkpoint);
    }

    private async Task VerifySnapshotYieldsSeededRowsBeforeCheckpoint(IReadFamily family)
    {
        await using var client = CreateClient();

        var entries = await CollectSnapshot(family, client);

        entries.Count(e => e.Kind == RowKind.Created).Should().BeGreaterThan(0);
        entries.SkipLast(1).Should().NotContain(e => e.Kind == RowKind.Checkpoint);
    }

    private async Task VerifyEmptySnapshotEndsWithCheckpoint(IReadFamily family)
    {
        await using var client = CreateClient();

        var entries = await CollectSnapshot(family, client, EmptySnapshotOffset);

        entries.Should().ContainSingle()
            .Which.Kind.Should().Be(RowKind.Checkpoint);
    }

    private async Task VerifySnapshotSurfacesMidSnapshotFault(IReadFamily family)
    {
        var faultingClient = CreateFaultingSnapshotClient();
        Assert.SkipWhen(
            faultingClient is null,
            $"adopter opted out of the {family.FaultPathCheckName}: its transport cannot induce a deterministic mid-snapshot fault");
        await using var client = faultingClient!;

        var entries = await CollectSnapshot(family, client);

        entries.Should().NotBeEmpty();
        entries[^1].Kind.Should().Be(
            RowKind.StreamError,
            "a mid-snapshot transport fault must surface in-band as a terminal StreamError, not be thrown");
        entries.Should().NotContain(
            e => e.Kind == RowKind.Checkpoint,
            "a faulted snapshot yields no terminal Checkpoint — there is no valid snapshot offset to hand over to a live subscription");
    }

    private async Task VerifyFromOffsetIsExclusive(IReadFamily family)
    {
        await using var client = CreateClient();
        var end = await client.GetLedgerEndAsync();

        var all = await CollectBounded(family, client, LedgerOffset.Begin, end);
        all.Should().NotBeEmpty(
            "the conformance scenario must seed at least one event on the subscription stream");
        var resumeFrom = FirstPosition(all);

        var resumed = await CollectBounded(family, client, resumeFrom, end);

        resumed.Should().NotContain(
            e => PositionOf(e) == resumeFrom,
            "fromOffset is exclusive: resuming from an offset must not re-deliver the event at it");
    }

    private async Task VerifyToOffsetIsInclusiveAndTerminal(IReadFamily family)
    {
        await using var client = CreateClient();
        var end = await client.GetLedgerEndAsync();

        var all = await CollectBounded(family, client, LedgerOffset.Begin, end);
        all.Should().NotBeEmpty(
            "the conformance scenario must seed at least one event on the subscription stream");
        var boundary = FirstPosition(all);

        var bounded = await CollectBounded(family, client, LedgerOffset.Begin, boundary);

        bounded.Should().Contain(
            e => PositionOf(e) == boundary,
            "toOffset is inclusive: the event at toOffset must be delivered");
        bounded.Should().OnlyContain(
            e => SitsAtOrBefore(e, boundary),
            "a bounded subscription must complete at toOffset and deliver nothing past it; an event "
            + "carrying no ledger position cannot sit past the boundary because it sits nowhere");
    }

    private async Task VerifyLedgerEffectsNeverYieldArchived(IReadFamily family)
    {
        await using var client = CreateClient();
        var end = await client.GetLedgerEndAsync();

        var events = await CollectWithinBudget(
            family.LedgerEffects(client, Reader, LedgerOffset.Begin, end, CancellationToken.None),
            $"A bounded {family.SubscribeLedgerEffectsName} (toOffset {end.Value}) must complete");

        events.Should().NotBeEmpty(
            "the conformance scenario must seed at least one event on the ledger-effects stream");
        events.Should().NotContain(
            e => e.Kind == RowKind.Archived,
            "the ledger-effects shape signals archival via a consuming Exercised, never an Archived variant");
        events.Where(e => e.Kind == RowKind.Exercised).Should().Contain(
            x => x.Consuming,
            "the ledger-effects shape conveys archival as a consuming Exercised event, so the seeded "
            + "scenario must archive one TProbe at an offset within the seeded ledger end; a stream "
            + "carrying no archival signal certifies nothing on this axis");
    }

    private async Task VerifyAcsDeltaNeverYieldsExercised(IReadFamily family)
    {
        await using var client = CreateClient();
        var end = await client.GetLedgerEndAsync();

        var events = await CollectBounded(family, client, LedgerOffset.Begin, end);

        events.Should().NotBeEmpty(
            "the conformance scenario must seed at least one event on the subscription stream");
        events.Should().NotContain(
            e => e.Kind == RowKind.Exercised,
            "the ACS-delta shape surfaces archival as a first-class Archived event, never an Exercised variant");
        events.Should().Contain(
            e => e.Kind == RowKind.Archived,
            "the ACS-delta shape conveys archival as a first-class Archived event, so the seeded "
            + "scenario must archive one TProbe at an offset within the seeded ledger end; a stream "
            + "carrying no archival signal certifies nothing on this axis");
    }

    private Task<IReadOnlyList<ReadRow>> CollectSnapshot(
        IReadFamily family, ILedgerClient client, LedgerOffset? activeAtOffset = null) =>
        CollectWithinBudget(
            family.Snapshot(client, Reader, activeAtOffset, CancellationToken.None),
            $"{family.SubscribeActiveName} must terminate with a terminal Checkpoint");

    private Task<IReadOnlyList<ReadRow>> CollectBounded(
        IReadFamily family, ILedgerClient client, LedgerOffset? fromOffset, LedgerOffset toOffset) =>
        CollectWithinBudget(
            family.AcsDelta(client, Reader, fromOffset, toOffset, CancellationToken.None),
            $"A bounded {family.SubscribeName} (toOffset {toOffset.Value}) must complete");

    private async Task DrainWithinBudget<TItem>(
        IAsyncEnumerable<TItem> stream, string cancellationContract)
    {
        using var timer = new CancellationTokenSource();

        var drain = DrainToCompletion(stream);

        if (await Task.WhenAny(drain, Task.Delay(StreamTimeout, timer.Token)) != drain)
        {
            ObserveFault(drain);

            throw new TimeoutException($"{cancellationContract}; nothing observed within {StreamTimeout}.");
        }

        await timer.CancelAsync();
        await drain;
    }

    private static async Task DrainToCompletion<TItem>(IAsyncEnumerable<TItem> stream)
    {
        await foreach (var _ in stream)
        {
        }
    }

    private async Task<IReadOnlyList<TItem>> CollectWithinBudget<TItem>(
        IAsyncEnumerable<TItem> stream, string terminationContract)
    {
        using var enumeration = new CancellationTokenSource();
        using var timer = new CancellationTokenSource();

        var deadline = Task.Delay(StreamTimeout, timer.Token);
        var enumerator = stream.GetAsyncEnumerator(enumeration.Token);
        var items = new List<TItem>();
        var abandoned = false;

        try
        {
            while (true)
            {
                var step = enumerator.MoveNextAsync().AsTask();

                if (await Task.WhenAny(step, deadline) != step)
                {
                    abandoned = true;
                    await enumeration.CancelAsync();
                    ObserveFault(step);
                    ObserveFault(DisposeOnceSettled(enumerator, step));

                    throw new TimeoutException(
                        $"{terminationContract}; not observed within {StreamTimeout}.");
                }

                if (!await step)
                {
                    return items;
                }

                items.Add(enumerator.Current);
            }
        }
        finally
        {
            await timer.CancelAsync();

            if (!abandoned)
            {
                await enumerator.DisposeAsync();
            }
        }
    }

    private async Task DisposeOnceSettled<TItem>(
        IAsyncEnumerator<TItem> enumerator, Task<bool> abandonedStep)
    {
        if (!await SettlesWithinBudget(abandonedStep))
        {
            return;
        }

        var disposal = enumerator.DisposeAsync().AsTask();
        ObserveFault(disposal);

        await SettlesWithinBudget(disposal);
    }

    private async Task<bool> SettlesWithinBudget(Task task)
    {
        using var timer = new CancellationTokenSource();

        var settled = await Task.WhenAny(task, Task.Delay(StreamTimeout, timer.Token)) == task;
        await timer.CancelAsync();

        return settled;
    }

    private static void ObserveFault(Task task) =>
        _ = task.ContinueWith(
            static settled => _ = settled.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private static LedgerOffset? PositionOf(ReadRow row) =>
        row.Kind == RowKind.StreamError
            ? throw new InvalidOperationException(
                "a bounded conformance subscription must not surface a transport StreamError")
            : row.Offset;

    private static bool SitsAtOrBefore(ReadRow row, LedgerOffset boundary) =>
        PositionOf(row) is not { } position || position.Value <= boundary.Value;

    private static LedgerOffset FirstPosition(IReadOnlyList<ReadRow> rows)
    {
        var positioned = rows.Select(PositionOf).OfType<LedgerOffset>().ToList();

        positioned.Should().NotBeEmpty(
            "the conformance scenario must seed at least one event that carries a ledger position "
            + "(Created, Archived, Checkpoint, …); an Unclassified event may legitimately carry none, "
            + "so it alone cannot anchor the offset-boundary checks");

        return positioned[0];
    }

    private static async IAsyncEnumerable<ReadRow> Project<TSource>(
        IAsyncEnumerable<TSource> source,
        Func<TSource, ReadRow> toRow,
        [EnumeratorCancellation] CancellationToken enumerationToken = default)
    {
        await foreach (var item in source.WithCancellation(enumerationToken))
        {
            yield return toRow(item);
        }
    }

    private enum RowKind
    {
        Created,
        Archived,
        Exercised,
        Assigned,
        Unassigned,
        Checkpoint,
        Unclassified,
        StreamError,
    }

    private readonly record struct ReadRow(
        RowKind Kind,
        LedgerOffset? Offset = null,
        bool Consuming = false,
        string? ContractId = null,
        decimal? ViewAmount = null,
        string? SynchronizerId = null);

    private interface IReadFamily
    {
        string SubscribeActiveName { get; }

        string SubscribeName { get; }

        string SubscribeLedgerEffectsName { get; }

        string FaultPathCheckName { get; }

        string LiveSubscriptionName { get; }

        IAsyncEnumerable<ReadRow> Snapshot(
            ILedgerClient client, SubmitterInfo reader, LedgerOffset? activeAtOffset,
            CancellationToken cancellationToken);

        IAsyncEnumerable<ReadRow> AcsDelta(
            ILedgerClient client, SubmitterInfo reader, LedgerOffset? fromOffset, LedgerOffset? toOffset,
            CancellationToken cancellationToken);

        IAsyncEnumerable<ReadRow> LedgerEffects(
            ILedgerClient client, SubmitterInfo reader, LedgerOffset? fromOffset, LedgerOffset? toOffset,
            CancellationToken cancellationToken);
    }

    private sealed class TemplateFamily : IReadFamily
    {
        public static TemplateFamily Instance { get; } = new();

        public string SubscribeActiveName => "SubscribeActiveAsync";

        public string SubscribeName => "SubscribeAsync";

        public string SubscribeLedgerEffectsName => "SubscribeLedgerEffectsAsync";

        public string FaultPathCheckName => "fault-path check";

        public string LiveSubscriptionName => "live subscription";

        public IAsyncEnumerable<ReadRow> Snapshot(
            ILedgerClient client, SubmitterInfo reader, LedgerOffset? activeAtOffset,
            CancellationToken cancellationToken) =>
            Project(
                client.SubscribeActiveAsync<TProbe>(reader, activeAtOffset, cancellationToken: cancellationToken),
                SnapshotRow,
                CancellationToken.None);

        public IAsyncEnumerable<ReadRow> AcsDelta(
            ILedgerClient client, SubmitterInfo reader, LedgerOffset? fromOffset, LedgerOffset? toOffset,
            CancellationToken cancellationToken) =>
            Project(
                client.SubscribeAsync<TProbe>(reader, fromOffset, toOffset, cancellationToken),
                StreamRow,
                CancellationToken.None);

        public IAsyncEnumerable<ReadRow> LedgerEffects(
            ILedgerClient client, SubmitterInfo reader, LedgerOffset? fromOffset, LedgerOffset? toOffset,
            CancellationToken cancellationToken) =>
            Project(
                client.SubscribeLedgerEffectsAsync<TProbe>(reader, fromOffset, toOffset, cancellationToken),
                StreamRow,
                CancellationToken.None);

        private static ReadRow SnapshotRow(AcsSnapshotEntry<TProbe> entry) => entry switch
        {
            AcsSnapshotEntry<TProbe>.Created c =>
                new(RowKind.Created, c.Offset, ContractId: c.ContractId.Value, SynchronizerId: c.SynchronizerId.Value),
            AcsSnapshotEntry<TProbe>.Unclassified u => new(RowKind.Unclassified, u.Offset),
            AcsSnapshotEntry<TProbe>.Checkpoint => new(RowKind.Checkpoint),
            AcsSnapshotEntry<TProbe>.StreamError => new(RowKind.StreamError),
            _ => throw new InvalidOperationException("unrecognized AcsSnapshotEntry variant"),
        };

        private static ReadRow StreamRow(ContractStreamEvent<TProbe> e) => e switch
        {
            ContractStreamEvent<TProbe>.Created c => new(RowKind.Created, c.Offset, ContractId: c.ContractId.Value),
            ContractStreamEvent<TProbe>.Archived a => new(RowKind.Archived, a.Offset, ContractId: a.ContractId.Value),
            ContractStreamEvent<TProbe>.Exercised x =>
                new(RowKind.Exercised, x.Offset, x.Consuming, x.ContractId.Value),
            ContractStreamEvent<TProbe>.Assigned a => new(RowKind.Assigned, a.Offset, ContractId: a.ContractId.Value),
            ContractStreamEvent<TProbe>.Unassigned u =>
                new(RowKind.Unassigned, u.Offset, ContractId: u.ContractId.Value),
            ContractStreamEvent<TProbe>.Checkpoint cp => new(RowKind.Checkpoint, cp.Offset),
            ContractStreamEvent<TProbe>.Unclassified u => new(RowKind.Unclassified, u.Offset),
            ContractStreamEvent<TProbe>.StreamError => new(RowKind.StreamError),
            _ => throw new InvalidOperationException("unrecognized ContractStreamEvent variant"),
        };
    }

    private sealed class InterfaceFamily : IReadFamily
    {
        public static InterfaceFamily Instance { get; } = new();

        public string SubscribeActiveName => "interface SubscribeActiveAsync";

        public string SubscribeName => "interface SubscribeAsync";

        public string SubscribeLedgerEffectsName => "interface SubscribeLedgerEffectsAsync";

        public string FaultPathCheckName => "interface fault-path check";

        public string LiveSubscriptionName => "live interface subscription";

        public IAsyncEnumerable<ReadRow> Snapshot(
            ILedgerClient client, SubmitterInfo reader, LedgerOffset? activeAtOffset,
            CancellationToken cancellationToken) =>
            Project(
                client.SubscribeActiveAsync(
                    IConformanceProbe.View, reader, activeAtOffset, cancellationToken: cancellationToken),
                SnapshotRow,
                CancellationToken.None);

        public IAsyncEnumerable<ReadRow> AcsDelta(
            ILedgerClient client, SubmitterInfo reader, LedgerOffset? fromOffset, LedgerOffset? toOffset,
            CancellationToken cancellationToken) =>
            Project(
                client.SubscribeAsync(IConformanceProbe.View, reader, fromOffset, toOffset, cancellationToken),
                StreamRow,
                CancellationToken.None);

        public IAsyncEnumerable<ReadRow> LedgerEffects(
            ILedgerClient client, SubmitterInfo reader, LedgerOffset? fromOffset, LedgerOffset? toOffset,
            CancellationToken cancellationToken) =>
            Project(
                client.SubscribeLedgerEffectsAsync(
                    IConformanceProbe.View, reader, fromOffset, toOffset, cancellationToken),
                StreamRow,
                CancellationToken.None);

        private static ReadRow SnapshotRow(InterfaceAcsSnapshotEntry<IConformanceProbe, ConformanceProbeView> entry) =>
            entry switch
            {
                InterfaceAcsSnapshotEntry<IConformanceProbe, ConformanceProbeView>.Created c =>
                    new(
                        RowKind.Created,
                        c.Offset,
                        ContractId: c.ContractId.Value,
                        ViewAmount: c.Payload.Amount,
                        SynchronizerId: c.SynchronizerId.Value),
                InterfaceAcsSnapshotEntry<IConformanceProbe, ConformanceProbeView>.Unclassified u =>
                    new(RowKind.Unclassified, u.Offset),
                InterfaceAcsSnapshotEntry<IConformanceProbe, ConformanceProbeView>.Checkpoint =>
                    new(RowKind.Checkpoint),
                InterfaceAcsSnapshotEntry<IConformanceProbe, ConformanceProbeView>.StreamError =>
                    new(RowKind.StreamError),
                _ => throw new InvalidOperationException("unrecognized InterfaceAcsSnapshotEntry variant"),
            };

        private static ReadRow StreamRow(InterfaceStreamEvent<IConformanceProbe, ConformanceProbeView> e) =>
            e switch
            {
                InterfaceStreamEvent<IConformanceProbe, ConformanceProbeView>.Created c =>
                    new(RowKind.Created, c.Offset, ContractId: c.ContractId.Value, ViewAmount: c.Payload.Amount),
                InterfaceStreamEvent<IConformanceProbe, ConformanceProbeView>.Archived a =>
                    new(RowKind.Archived, a.Offset, ContractId: a.ContractId.Value),
                InterfaceStreamEvent<IConformanceProbe, ConformanceProbeView>.Exercised x =>
                    new(RowKind.Exercised, x.Offset, x.Consuming, x.ContractId.Value),
                InterfaceStreamEvent<IConformanceProbe, ConformanceProbeView>.Assigned a =>
                    new(RowKind.Assigned, a.Offset, ContractId: a.ContractId.Value),
                InterfaceStreamEvent<IConformanceProbe, ConformanceProbeView>.Unassigned u =>
                    new(RowKind.Unassigned, u.Offset, ContractId: u.ContractId.Value),
                InterfaceStreamEvent<IConformanceProbe, ConformanceProbeView>.Checkpoint cp =>
                    new(RowKind.Checkpoint, cp.Offset),
                InterfaceStreamEvent<IConformanceProbe, ConformanceProbeView>.Unclassified u =>
                    new(RowKind.Unclassified, u.Offset),
                InterfaceStreamEvent<IConformanceProbe, ConformanceProbeView>.StreamError =>
                    new(RowKind.StreamError),
                _ => throw new InvalidOperationException("unrecognized InterfaceStreamEvent variant"),
            };
    }
}
