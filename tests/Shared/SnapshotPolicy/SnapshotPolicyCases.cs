// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Time.Testing;

namespace Daml.Testing.SnapshotPolicy;

internal static class SnapshotPolicyCases
{
    private static readonly SnapshotPolicyCase[] Cases =
    [
        new("empty_snapshot_returns_no_rows", async driver =>
        {
            var ids = await Drain(driver, new SnapshotStep.Checkpoint(7));

            ids.Should().BeEmpty();
        }),

        new("empty_snapshot_at_ledger_begin_returns_no_rows", async driver =>
        {
            var ids = await Drain(driver, new SnapshotStep.Checkpoint(0));

            ids.Should().BeEmpty();
        }),

        new("rows_are_returned_in_order_and_the_checkpoint_ends_the_drain", async driver =>
        {
            var ids = await Drain(
                driver,
                new SnapshotStep.Row("cid-1", 1),
                new SnapshotStep.Row("cid-2", 2),
                new SnapshotStep.Checkpoint(2),
                new SnapshotStep.Row("cid-3", 3),
                new SnapshotStep.Fault(new TransportStatus.Grpc(GrpcStatusCode.Internal), "after the checkpoint"));

            ids.Should().Equal("cid-1", "cid-2");
        }),

        new("fault_reports_CommitState_NotCommitted", async driver =>
        {
            var thrown = await FailureOf(driver, new SnapshotStep.Row("cid-1", 1), StaleAuthorizationFault());

            thrown.CommitState.Should().Be(CommitState.NotCommitted);
        }),

        new("fault_carries_the_status_the_category_and_the_error_id", async driver =>
        {
            var thrown = await FailureOf(driver, new SnapshotStep.Row("cid-1", 1), StaleAuthorizationFault());

            thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Aborted));
            thrown.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
            thrown.ErrorId.Should().Be("STALE_STREAM_AUTHORIZATION");
        }),

        new("fault_keeps_the_transport_exception_as_the_inner_exception", async driver =>
        {
            var transportFault = new InvalidOperationException("the channel went away");

            var thrown = await FailureOf(
                driver,
                new SnapshotStep.Row("cid-1", 1),
                new SnapshotStep.Fault(
                    new TransportStatus.Grpc(GrpcStatusCode.Unavailable),
                    "unavailable",
                    DamlErrorCategory.TransientServerFailure,
                    SourceException: transportFault));

            thrown.InnerException.Should().BeSameAs(transportFault);
        }),

        new("fault_without_classification_carries_no_category_error_id_or_inner_exception", async driver =>
        {
            var thrown = await FailureOf(
                driver,
                new SnapshotStep.Fault(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "unavailable"));

            thrown.CommitState.Should().Be(CommitState.NotCommitted);
            thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
            thrown.Category.Should().BeNull();
            thrown.ErrorId.Should().BeNull();
            thrown.InnerException.Should().BeNull();
        }),

        new("fault_message_names_the_subject_the_rows_collected_and_the_alternative", async driver =>
        {
            var thrown = await FailureOf(driver, new SnapshotStep.Row("cid-1", 1), StaleAuthorizationFault());

            thrown.Message.Should().Be(Worded(
                driver,
                "The active-contract-set snapshot for {name} faulted after 1 {noun}(s): "
                + "the stream authorization is stale. Use {alternative} for value-shaped fault handling."));
        }),

        new("fault_before_any_row_reports_zero_rows_collected", async driver =>
        {
            var thrown = await FailureOf(
                driver,
                new SnapshotStep.Fault(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "unavailable"));

            thrown.Message.Should().Be(Worded(
                driver,
                "The active-contract-set snapshot for {name} faulted after 0 {noun}(s): "
                + "unavailable. Use {alternative} for value-shaped fault handling."));
        }),

        new("unclassified_row_names_its_kind_its_raw_descriptor_and_its_offset", async driver =>
        {
            var thrown = await FailureOf(
                driver,
                new SnapshotStep.Row("cid-1", 1),
                new SnapshotStep.Unclassified(42, UnclassifiedKind.Unknown, "ACTIVE_CONTRACT"),
                new SnapshotStep.Checkpoint(42));

            thrown.Message.Should().Be(Worded(
                driver,
                "The active-contract-set snapshot for {name} carried an unclassified row "
                + "(Unknown: 'ACTIVE_CONTRACT') at offset 42, so the returned {noun}s would be incomplete. "
                + "Use {alternative} to handle it as a value."));
        }, UnreadableRows.Fail),

        new("unclassified_row_of_an_enumerated_kind_renders_the_bare_kind", async driver =>
        {
            var thrown = await FailureOf(
                driver,
                new SnapshotStep.Unclassified(88, UnclassifiedKind.DecodeFailure),
                new SnapshotStep.Checkpoint(88));

            thrown.Message.Should().Be(Worded(
                driver,
                "The active-contract-set snapshot for {name} carried an unclassified row "
                + "(DecodeFailure) at offset 88, so the returned {noun}s would be incomplete. "
                + "Use {alternative} to handle it as a value."));
        }, UnreadableRows.Fail),

        new("unclassified_row_without_an_offset_reads_at_an_unreported_offset", async driver =>
        {
            var thrown = await FailureOf(
                driver,
                new SnapshotStep.Unclassified(null, UnclassifiedKind.DecodeFailure),
                new SnapshotStep.Checkpoint(2));

            thrown.Message.Should().Be(Worded(
                driver,
                "The active-contract-set snapshot for {name} carried an unclassified row "
                + "(DecodeFailure) at an unreported offset, so the returned {noun}s would be incomplete. "
                + "Use {alternative} to handle it as a value."));
        }, UnreadableRows.Fail),

        new("row_followed_by_an_unassigned_row_is_returned_without_failing_or_listing_the_unassigned_row", async driver =>
        {
            var read = await Read(
                driver,
                new SnapshotStep.Row("cid-1", 1),
                new SnapshotStep.Unclassified(5, UnclassifiedKind.UnassignedEvent),
                new SnapshotStep.Checkpoint(9));

            read.Ids.Should().Equal("cid-1");
            read.Unreadable.Should().BeEmpty();
        }),

        new("an_unassigned_row_after_each_of_several_rows_leaves_every_row_in_delivery_order", async driver =>
        {
            var read = await Read(
                driver,
                new SnapshotStep.Row("cid-1", 1),
                new SnapshotStep.Unclassified(5, UnclassifiedKind.UnassignedEvent),
                new SnapshotStep.Row("cid-2", 2),
                new SnapshotStep.Row("cid-3", 3),
                new SnapshotStep.Unclassified(6, UnclassifiedKind.UnassignedEvent),
                new SnapshotStep.Checkpoint(9));

            read.Ids.Should().Equal("cid-1", "cid-2", "cid-3");
            read.Unreadable.Should().BeEmpty();
        }),

        new("an_unassigned_row_with_no_row_before_it_is_unreadable", async driver =>
        {
            var thrown = await FailureOf(
                driver,
                new SnapshotStep.Unclassified(5, UnclassifiedKind.UnassignedEvent),
                new SnapshotStep.Checkpoint(9));

            thrown.Message.Should().Be(Worded(
                driver,
                "The active-contract-set snapshot for {name} carried an unclassified row "
                + "(UnassignedEvent) at offset 5, so the returned {noun}s would be incomplete. "
                + "Use {alternative} to handle it as a value."));
        }, UnreadableRows.Fail),

        new("an_unassigned_row_with_no_row_before_it_is_listed_as_unreadable", async driver =>
        {
            var read = await Read(
                driver,
                new SnapshotStep.Unclassified(5, UnclassifiedKind.UnassignedEvent),
                new SnapshotStep.Checkpoint(9));

            read.Ids.Should().BeEmpty();
            read.Unreadable.Should().Equal(new UnreadableRow(UnclassifiedKind.UnassignedEvent, null, LedgerOffset.At(5)));
        }, UnreadableRows.Return),

        new("a_second_unassigned_row_after_one_row_is_unreadable", async driver =>
        {
            var thrown = await FailureOf(
                driver,
                new SnapshotStep.Row("cid-1", 1),
                new SnapshotStep.Unclassified(5, UnclassifiedKind.UnassignedEvent),
                new SnapshotStep.Unclassified(6, UnclassifiedKind.UnassignedEvent),
                new SnapshotStep.Checkpoint(9));

            thrown.Message.Should().Contain("(UnassignedEvent) at offset 6");
        }, UnreadableRows.Fail),

        new("a_second_unassigned_row_after_one_row_is_listed_as_unreadable", async driver =>
        {
            var read = await Read(
                driver,
                new SnapshotStep.Row("cid-1", 1),
                new SnapshotStep.Unclassified(5, UnclassifiedKind.UnassignedEvent),
                new SnapshotStep.Unclassified(6, UnclassifiedKind.UnassignedEvent),
                new SnapshotStep.Checkpoint(9));

            read.Ids.Should().Equal("cid-1");
            read.Unreadable.Should().Equal(new UnreadableRow(UnclassifiedKind.UnassignedEvent, null, LedgerOffset.At(6)));
        }, UnreadableRows.Return),

        new("an_unassigned_row_after_another_unreadable_row_is_unreadable_too", async driver =>
        {
            var read = await Read(
                driver,
                new SnapshotStep.Row("cid-1", 1),
                new SnapshotStep.Unclassified(4, UnclassifiedKind.DecodeFailure),
                new SnapshotStep.Unclassified(5, UnclassifiedKind.UnassignedEvent),
                new SnapshotStep.Checkpoint(9));

            read.Ids.Should().Equal("cid-1");
            read.Unreadable.Should().Equal(
                new UnreadableRow(UnclassifiedKind.DecodeFailure, null, LedgerOffset.At(4)),
                new UnreadableRow(UnclassifiedKind.UnassignedEvent, null, LedgerOffset.At(5)));
        }, UnreadableRows.Return),

        new("missing_checkpoint_fails_naming_the_rows_collected", async driver =>
        {
            var thrown = await FailureOf(driver, new SnapshotStep.Row("cid-1", 1), new SnapshotStep.Row("cid-2", 2));

            thrown.Message.Should().Be(Worded(
                driver,
                "The active-contract-set snapshot for {name} ended after 2 {noun}(s) without its terminal "
                + "checkpoint, so the returned {noun}s would be incomplete."));
        }),

        new("missing_checkpoint_on_an_empty_stream_fails_with_zero_rows_collected", async driver =>
        {
            var thrown = await FailureOf(driver);

            thrown.Message.Should().Be(Worded(
                driver,
                "The active-contract-set snapshot for {name} ended after 0 {noun}(s) without its terminal "
                + "checkpoint, so the returned {noun}s would be incomplete."));
        }),

        new("cancellation_wins_over_an_in_band_fault", async driver =>
        {
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();

            var drain = async () => await driver.DrainAsync(
                [new SnapshotStep.Fault(new TransportStatus.Grpc(GrpcStatusCode.Cancelled), "CANCELLED")],
                streamHonoursCancellation: false,
                timeout: null,
                TimeProvider.System,
                cancelled.Token);

            await drain.Should().ThrowAsync<OperationCanceledException>();
        }),

        new("cancellation_wins_over_a_truncated_snapshot", async driver =>
        {
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();

            var drain = async () => await driver.DrainAsync(
                [new SnapshotStep.Row("cid-1", 1)],
                streamHonoursCancellation: false,
                timeout: null,
                TimeProvider.System,
                cancelled.Token);

            await drain.Should().ThrowAsync<OperationCanceledException>();
        }),

        new("snapshot_finishing_within_the_timeout_returns_its_rows", async driver =>
        {
            var ids = await driver.DrainAsync(
                [new SnapshotStep.Row("cid-1", 1), new SnapshotStep.Checkpoint(1)],
                streamHonoursCancellation: true,
                TimeSpan.FromSeconds(5),
                new FakeTimeProvider(),
                CancellationToken.None);

            ids.Ids.Should().Equal("cid-1");
        }),

        new("timeout_expiry_is_a_deadline_failure_that_reports_NotCommitted_with_its_category_and_status", async driver =>
        {
            var thrown = await DeadlineFailureOf(driver, new SnapshotStep.Row("cid-1", 1), new SnapshotStep.Stall());

            thrown.CommitState.Should().Be(CommitState.NotCommitted);
            thrown.Category.Should().Be(DamlErrorCategory.DeadlineExceededRequestStateUnknown);
            thrown.Status.Should().Be(new TransportStatus.NoResponse());
            thrown.ErrorId.Should().BeNull();
        }),

        new("timeout_expiry_message_names_the_subject_the_timeout_and_the_rows_collected", async driver =>
        {
            var thrown = await DeadlineFailureOf(driver, new SnapshotStep.Row("cid-1", 1), new SnapshotStep.Stall());

            thrown.Message.Should().Be(Worded(
                driver,
                "The active-contract-set snapshot for {name} did not reach its terminal checkpoint within "
                + "00:00:05 after 1 {noun}(s)."));
        }),

        new("timeout_expiry_wins_over_an_in_band_cancelled_fault", async driver =>
        {
            var thrown = await DeadlineFailureOf(driver, new SnapshotStep.Stall(StallEnding.ReportsCancelledFaultInBand));

            thrown.Category.Should().Be(DamlErrorCategory.DeadlineExceededRequestStateUnknown);
        }),

        new("timeout_expiry_wins_over_a_truncated_snapshot", async driver =>
        {
            var thrown = await DeadlineFailureOf(driver, new SnapshotStep.Row("cid-1", 1), new SnapshotStep.Stall(StallEnding.EndsWithoutCheckpoint));

            thrown.Status.Should().Be(new TransportStatus.NoResponse());
        }),

        new("caller_cancellation_with_a_pending_timeout_is_not_a_deadline_failure", async driver =>
        {
            using var cancelling = new CancellationTokenSource();
            var time = new FakeTimeProvider();

            var drain = driver.DrainAsync(
                [new SnapshotStep.Stall()], streamHonoursCancellation: true, TimeSpan.FromSeconds(5), time, cancelling.Token);
            await driver.WhenStalled;
            await cancelling.CancelAsync();

            await FluentActions.Awaiting(() => drain).Should().ThrowAsync<OperationCanceledException>();
        }),

        new("caller_cancellation_wins_over_an_expired_timeout_on_an_in_band_fault", async driver =>
        {
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();

            var drain = async () => await driver.DrainAsync(
                [new SnapshotStep.Fault(new TransportStatus.Grpc(GrpcStatusCode.Cancelled), "CANCELLED")],
                streamHonoursCancellation: false,
                TimeSpan.Zero,
                new FakeTimeProvider(),
                cancelled.Token);

            await drain.Should().ThrowAsync<OperationCanceledException>();
        }),

        new("caller_cancellation_wins_over_an_expired_timeout_on_a_truncated_snapshot", async driver =>
        {
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();

            var drain = async () => await driver.DrainAsync(
                [new SnapshotStep.Row("cid-1", 1)],
                streamHonoursCancellation: false,
                TimeSpan.Zero,
                new FakeTimeProvider(),
                cancelled.Token);

            await drain.Should().ThrowAsync<OperationCanceledException>();
        }),

        new("zero_timeout_is_an_immediate_deadline_failure", async driver =>
        {
            var drain = async () => await driver.DrainAsync(
                [new SnapshotStep.Row("cid-1", 1), new SnapshotStep.Checkpoint(1)],
                streamHonoursCancellation: true,
                TimeSpan.Zero,
                new FakeTimeProvider(),
                CancellationToken.None);

            var thrown = await drain.Should().ThrowAsync<LedgerOperationException>();
            thrown.Which.Category.Should().Be(DamlErrorCategory.DeadlineExceededRequestStateUnknown);
        }),

        new("snapshot_without_a_timeout_stays_pending_however_much_time_passes", async driver =>
        {
            using var cancelling = new CancellationTokenSource();
            var time = new FakeTimeProvider();

            var drain = driver.DrainAsync(
                [new SnapshotStep.Stall()], streamHonoursCancellation: true, timeout: null, time, cancelling.Token);
            await driver.WhenStalled;
            time.Advance(TimeSpan.FromDays(1));
            await Task.Delay(50, CancellationToken.None);

            drain.IsCompleted.Should().BeFalse();
            await cancelling.CancelAsync();
            await FluentActions.Awaiting(() => drain).Should().ThrowAsync<OperationCanceledException>();
        }),

        new("readable_only_snapshot_reports_no_unreadable_rows", async driver =>
        {
            var read = await Read(driver, new SnapshotStep.Row("cid-1", 1), new SnapshotStep.Checkpoint(1));

            read.Ids.Should().Equal("cid-1");
            read.Unreadable.Should().BeEmpty();
        }),

        new("unreadable_row_is_returned_with_its_kind_its_raw_descriptor_and_its_offset", async driver =>
        {
            var read = await Read(
                driver,
                new SnapshotStep.Row("cid-1", 1),
                new SnapshotStep.Unclassified(42, UnclassifiedKind.Unknown, "ACTIVE_CONTRACT"),
                new SnapshotStep.Checkpoint(42));

            read.Ids.Should().Equal("cid-1");
            read.Unreadable.Should().Equal(new UnreadableRow(UnclassifiedKind.Unknown, "ACTIVE_CONTRACT", LedgerOffset.At(42)));
        }, UnreadableRows.Return),

        new("unreadable_row_of_an_enumerated_kind_carries_no_raw_descriptor", async driver =>
        {
            var read = await Read(
                driver,
                new SnapshotStep.Unclassified(88, UnclassifiedKind.DecodeFailure),
                new SnapshotStep.Checkpoint(88));

            read.Unreadable.Should().Equal(new UnreadableRow(UnclassifiedKind.DecodeFailure, null, LedgerOffset.At(88)));
        }, UnreadableRows.Return),

        new("unreadable_row_without_an_offset_carries_no_offset", async driver =>
        {
            var read = await Read(
                driver,
                new SnapshotStep.Unclassified(null, UnclassifiedKind.InterfaceViewUnavailable),
                new SnapshotStep.Checkpoint(2));

            read.Unreadable.Should().Equal(new UnreadableRow(UnclassifiedKind.InterfaceViewUnavailable, null, null));
        }, UnreadableRows.Return),

        new("unreadable_rows_keep_delivery_order_and_do_not_end_the_drain", async driver =>
        {
            var read = await Read(
                driver,
                new SnapshotStep.Row("cid-1", 1),
                new SnapshotStep.Unclassified(5, UnclassifiedKind.DecodeFailure),
                new SnapshotStep.Row("cid-2", 6),
                new SnapshotStep.Unclassified(7, UnclassifiedKind.InterfaceViewUnavailable),
                new SnapshotStep.Row("cid-3", 8),
                new SnapshotStep.Checkpoint(8));

            read.Ids.Should().Equal("cid-1", "cid-2", "cid-3");
            read.Unreadable.Select(row => row.Kind).Should()
                .Equal(UnclassifiedKind.DecodeFailure, UnclassifiedKind.InterfaceViewUnavailable);
        }, UnreadableRows.Return),

        new("snapshot_of_only_unreadable_rows_returns_no_rows", async driver =>
        {
            var read = await Read(
                driver,
                new SnapshotStep.Unclassified(3, UnclassifiedKind.DecodeFailure),
                new SnapshotStep.Checkpoint(3));

            read.Ids.Should().BeEmpty();
            read.Unreadable.Should().HaveCount(1);
        }, UnreadableRows.Return),

        new("unreadable_row_after_the_checkpoint_is_not_read", async driver =>
        {
            var read = await Read(
                driver,
                new SnapshotStep.Checkpoint(1),
                new SnapshotStep.Unclassified(2, UnclassifiedKind.DecodeFailure));

            read.Unreadable.Should().BeEmpty();
        }, UnreadableRows.Return),

        new("fault_after_an_unreadable_row_still_fails_counting_only_the_readable_rows", async driver =>
        {
            var thrown = await FailureOf(
                driver,
                new SnapshotStep.Unclassified(1, UnclassifiedKind.DecodeFailure),
                new SnapshotStep.Row("cid-1", 2),
                StaleAuthorizationFault());

            thrown.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
            thrown.Message.Should().Be(Worded(
                driver,
                "The active-contract-set snapshot for {name} faulted after 1 {noun}(s): "
                + "the stream authorization is stale. Use {alternative} for value-shaped fault handling."));
        }, UnreadableRows.Return),

        new("missing_checkpoint_after_an_unreadable_row_still_fails", async driver =>
        {
            var thrown = await FailureOf(
                driver,
                new SnapshotStep.Row("cid-1", 1),
                new SnapshotStep.Unclassified(2, UnclassifiedKind.DecodeFailure));

            thrown.Message.Should().Be(Worded(
                driver,
                "The active-contract-set snapshot for {name} ended after 1 {noun}(s) without its terminal "
                + "checkpoint, so the returned {noun}s would be incomplete."));
        }, UnreadableRows.Return),

        new("timeout_expiry_after_an_unreadable_row_is_still_a_deadline_failure", async driver =>
        {
            var thrown = await DeadlineFailureOf(
                driver,
                new SnapshotStep.Unclassified(1, UnclassifiedKind.DecodeFailure),
                new SnapshotStep.Stall());

            thrown.Category.Should().Be(DamlErrorCategory.DeadlineExceededRequestStateUnknown);
        }, UnreadableRows.Return),

        new("caller_cancellation_after_an_unreadable_row_is_still_cancellation", async driver =>
        {
            using var cancelling = new CancellationTokenSource();

            var drain = driver.DrainAsync(
                [new SnapshotStep.Unclassified(1, UnclassifiedKind.DecodeFailure), new SnapshotStep.Stall()],
                streamHonoursCancellation: true,
                timeout: null,
                TimeProvider.System,
                cancelling.Token);
            await Task.WhenAny(driver.WhenStalled, drain);
            await cancelling.CancelAsync();

            await FluentActions.Awaiting(() => drain).Should().ThrowAsync<OperationCanceledException>();
        }, UnreadableRows.Return),
    ];

    public static IEnumerable<(string Name, UnreadableRows Policy)> Combinations =>
        Cases.SelectMany(@case => @case.Policies.Select(policy => (@case.Name, policy)));

    public static Task RunAsync(string name, ISnapshotPolicyDriver driver) =>
        Cases.Single(@case => @case.Name == name).Run(driver);

    private static SnapshotStep.Fault StaleAuthorizationFault() =>
        new(
            new TransportStatus.Grpc(GrpcStatusCode.Aborted),
            "the stream authorization is stale",
            DamlErrorCategory.ContentionOnSharedResources,
            "STALE_STREAM_AUTHORIZATION");

    private static Task<SnapshotRead> Read(ISnapshotPolicyDriver driver, params SnapshotStep[] script) =>
        driver.DrainAsync(script, streamHonoursCancellation: true, timeout: null, TimeProvider.System, CancellationToken.None);

    private static async Task<IReadOnlyList<string>> Drain(ISnapshotPolicyDriver driver, params SnapshotStep[] script) =>
        (await Read(driver, script)).Ids;

    private static async Task<LedgerOperationException> FailureOf(
        ISnapshotPolicyDriver driver,
        params SnapshotStep[] script)
    {
        var drain = async () => await Read(driver, script);

        var thrown = await drain.Should().ThrowAsync<LedgerOperationException>();
        return thrown.Which;
    }

    private static async Task<LedgerOperationException> DeadlineFailureOf(
        ISnapshotPolicyDriver driver,
        params SnapshotStep[] script)
    {
        var time = new FakeTimeProvider();
        var drain = driver.DrainAsync(
            script, streamHonoursCancellation: true, TimeSpan.FromSeconds(5), time, CancellationToken.None);
        await Task.WhenAny(driver.WhenStalled, drain);
        time.Advance(TimeSpan.FromSeconds(5));

        var thrown = await FluentActions.Awaiting(() => drain).Should().ThrowAsync<LedgerOperationException>();
        return thrown.Which;
    }

    private static string Worded(ISnapshotPolicyDriver driver, string template) =>
        template
            .Replace("{name}", driver.Subject.Name, StringComparison.Ordinal)
            .Replace("{noun}", driver.Subject.RowNoun, StringComparison.Ordinal)
            .Replace("{alternative}", driver.Subject.Alternative, StringComparison.Ordinal);

    private sealed record SnapshotPolicyCase(
        string Name,
        Func<ISnapshotPolicyDriver, Task> Run,
        params UnreadableRows[] AppliesTo)
    {
        public IReadOnlyList<UnreadableRows> Policies => AppliesTo.Length == 0 ? BothPolicies : AppliesTo;

        private static readonly UnreadableRows[] BothPolicies = [UnreadableRows.Fail, UnreadableRows.Return];
    }
}
