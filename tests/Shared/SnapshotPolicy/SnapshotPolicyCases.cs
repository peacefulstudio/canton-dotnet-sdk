// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;

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
        }),

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
        }),

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
        }),

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
                cancelled.Token);

            await drain.Should().ThrowAsync<OperationCanceledException>();
        }),
    ];

    public static IEnumerable<string> Names => Cases.Select(@case => @case.Name);

    public static Task RunAsync(string name, ISnapshotPolicyDriver driver) =>
        Cases.Single(@case => @case.Name == name).Run(driver);

    private static SnapshotStep.Fault StaleAuthorizationFault() =>
        new(
            new TransportStatus.Grpc(GrpcStatusCode.Aborted),
            "the stream authorization is stale",
            DamlErrorCategory.ContentionOnSharedResources,
            "STALE_STREAM_AUTHORIZATION");

    private static Task<IReadOnlyList<string>> Drain(ISnapshotPolicyDriver driver, params SnapshotStep[] script) =>
        driver.DrainAsync(script, streamHonoursCancellation: true, CancellationToken.None);

    private static async Task<LedgerOperationException> FailureOf(
        ISnapshotPolicyDriver driver,
        params SnapshotStep[] script)
    {
        var drain = async () => await Drain(driver, script);

        var thrown = await drain.Should().ThrowAsync<LedgerOperationException>();
        return thrown.Which;
    }

    private static string Worded(ISnapshotPolicyDriver driver, string template) =>
        template
            .Replace("{name}", driver.Subject.Name, StringComparison.Ordinal)
            .Replace("{noun}", driver.Subject.RowNoun, StringComparison.Ordinal)
            .Replace("{alternative}", driver.Subject.Alternative, StringComparison.Ordinal);

    private sealed record SnapshotPolicyCase(string Name, Func<ISnapshotPolicyDriver, Task> Run);
}
