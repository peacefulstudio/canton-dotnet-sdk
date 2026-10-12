// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AwesomeAssertions;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

public class OutcomeUnwrapPolicyTests
{
    private const string OperationDataKeyLiteral = "Canton.Ledger.Operation";

    private static readonly IReadOnlyDictionary<string, string> SampleMetadata =
        new Dictionary<string, string> { ["participant"] = "participant1" };

    private static readonly CancellationToken Cancelled = new(canceled: true);

    private static readonly Func<string> DescribeNone = static () => "caller says none";

    private static readonly Func<int, string> DescribeMany = static count => $"caller says many {count}";

    private static ExerciseOutcome<int> One => new ExerciseOutcome<int>.One(42);

    private static ExerciseOutcome<int> None => new ExerciseOutcome<int>.None();

    private static ExerciseOutcome<int> Many => new ExerciseOutcome<int>.Many(["cid-1", "cid-2"]);

    private static ExerciseOutcome<int> Contended => new ExerciseOutcome<int>.DamlError(
        DamlErrorCategory.ContentionOnSharedResources,
        "LOCAL_VERDICT_LOCKED_CONTRACTS",
        "contended",
        SampleMetadata);

    private static ExerciseOutcome<int> DeadlineUnknown => new ExerciseOutcome<int>.DamlError(
        DamlErrorCategory.DeadlineExceededRequestStateUnknown,
        "REQUEST_TIME_OUT",
        "timed out",
        SampleMetadata);

    private static ExerciseOutcome<int> UnclassifiedDamlError => new ExerciseOutcome<int>.DamlError(
        DamlErrorCategory.Unknown,
        "SOME_ERROR_ID",
        "unclassified",
        SampleMetadata);

    private static ExerciseOutcome<int> Unreachable(Exception source) => new ExerciseOutcome<int>.InfraError(
        new TransportStatus.Grpc(GrpcStatusCode.Unavailable),
        "participant unreachable",
        DamlErrorCategory.TransientServerFailure,
        source);

    private static ExerciseOutcome<int> Undecodable(Exception source) =>
        new ExerciseOutcome<int>.CommittedUndecodable("update-1", "bad payload", source);

    private static ExerciseOutcome<int> UndecodableBeforeUpdateId(Exception source) =>
        new ExerciseOutcome<int>.CommittedUndecodable(null, "no transaction", source);

    private static readonly Dictionary<string, Action> Rows = new()
    {
        ["RequireOne_returns_the_result_of_One"] = () =>
            OutcomeUnwrapPolicy.Value(One, ValueIntent.RequireOne, "TryExerciseAsync", DescribeNone, DescribeMany)
                .Should().Be(42),

        ["RequireOne_throws_committed_with_the_caller_message_for_None"] = () =>
        {
            var thrown = ThrownBy(() => ValueOf(None, ValueIntent.RequireOne, "TryExerciseAsync"));

            thrown.Message.Should().Be("caller says none");
            thrown.CommitState.Should().Be(CommitState.Committed);
            thrown.UpdateId.Should().BeNull();
            thrown.InnerException.Should().BeNull();
            thrown.Category.Should().BeNull();
            thrown.Status.Should().BeNull();
            thrown.Data.Contains(OperationDataKeyLiteral).Should().BeFalse();
        },

        ["RequireOne_throws_committed_with_the_caller_message_and_count_for_Many"] = () =>
        {
            var thrown = ThrownBy(() => ValueOf(Many, ValueIntent.RequireOne, "TryExerciseAsync"));

            thrown.Message.Should().Be("caller says many 2");
            thrown.CommitState.Should().Be(CommitState.Committed);
            thrown.UpdateId.Should().BeNull();
            thrown.Category.Should().BeNull();
            thrown.Status.Should().BeNull();
            thrown.Data.Contains(OperationDataKeyLiteral).Should().BeFalse();
        },

        ["RequireOne_throws_not_committed_with_the_structured_payload_for_DamlError"] = () =>
        {
            var thrown = ThrownBy(() => ValueOf(Contended, ValueIntent.RequireOne, "TryExerciseAsync"));

            thrown.Message.Should().Be(
                "Daml error [ContentionOnSharedResources/LOCAL_VERDICT_LOCKED_CONTRACTS]: contended");
            thrown.CommitState.Should().Be(CommitState.NotCommitted);
            thrown.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
            thrown.ErrorId.Should().Be("LOCAL_VERDICT_LOCKED_CONTRACTS");
            thrown.Metadata.Should().BeEquivalentTo(SampleMetadata);
            thrown.Status.Should().BeNull();
            thrown.Data.Contains(OperationDataKeyLiteral).Should().BeFalse();
        },

        ["RequireOne_throws_unknown_for_a_DamlError_the_participant_could_not_resolve"] = () =>
            ThrownBy(() => ValueOf(DeadlineUnknown, ValueIntent.RequireOne, "TryExerciseAsync"))
                .CommitState.Should().Be(CommitState.Unknown),

        ["RequireOne_throws_unknown_for_a_DamlError_of_category_Unknown"] = () =>
            ThrownBy(() => ValueOf(UnclassifiedDamlError, ValueIntent.RequireOne, "TryExerciseAsync"))
                .CommitState.Should().Be(CommitState.Unknown),

        ["RequireOne_leaves_the_category_and_inner_exception_empty_when_InfraError_carries_neither"] = () =>
        {
            var thrown = ThrownBy(() => ValueOf(
                new ExerciseOutcome<int>.InfraError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "Connection reset"),
                ValueIntent.RequireOne,
                "TryExerciseAsync"));

            thrown.Category.Should().BeNull();
            thrown.InnerException.Should().BeNull();
        },

        ["RequireOne_throws_unknown_with_status_category_and_source_but_no_error_id_for_InfraError"] = () =>
        {
            var source = new TimeoutException("boom");

            var thrown = ThrownBy(() => ValueOf(Unreachable(source), ValueIntent.RequireOne, "TryExerciseAsync"));

            thrown.Message.Should().Be(
                "Infrastructure error [Grpc { StatusCode = Unavailable }]: participant unreachable");
            thrown.CommitState.Should().Be(CommitState.Unknown);
            thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
            thrown.Category.Should().Be(DamlErrorCategory.TransientServerFailure);
            thrown.ErrorId.Should().BeNull();
            thrown.Metadata.Should().BeNull();
            thrown.InnerException.Should().BeSameAs(source);
            thrown.Data.Contains(OperationDataKeyLiteral).Should().BeFalse();
        },

        ["RequireOne_lets_a_cancelled_token_beat_InfraError"] = () =>
        {
            var act = () => OutcomeUnwrapPolicy.Value(
                Unreachable(new TimeoutException("boom")),
                ValueIntent.RequireOne,
                "TryExerciseAsync",
                DescribeNone,
                DescribeMany,
                Cancelled);

            act.Should().Throw<OperationCanceledException>();
        },

        ["RequireOne_keeps_a_cancelled_token_from_beating_DamlError"] = () =>
            ThrownBy(() => OutcomeUnwrapPolicy.Value(
                    Contended, ValueIntent.RequireOne, "TryExerciseAsync", DescribeNone, DescribeMany, Cancelled))
                .CommitState.Should().Be(CommitState.NotCommitted),

        ["RequireOne_throws_committed_with_the_update_id_for_CommittedUndecodable"] = () =>
        {
            var source = new FormatException("bad offset");

            var thrown = ThrownBy(() => ValueOf(Undecodable(source), ValueIntent.RequireOne, "TryExerciseAsync"));

            thrown.Message.Should().Be("Committed but undecodable [update-1]: bad payload");
            thrown.CommitState.Should().Be(CommitState.Committed);
            thrown.UpdateId.Should().Be("update-1");
            thrown.InnerException.Should().BeSameAs(source);
            thrown.Data.Contains(OperationDataKeyLiteral).Should().BeFalse();
        },

        ["RequireOne_names_unknown_when_CommittedUndecodable_never_read_an_update_id"] = () =>
        {
            var thrown = ThrownBy(() => ValueOf(
                UndecodableBeforeUpdateId(new FormatException("bad offset")), ValueIntent.RequireOne, "TryExerciseAsync"));

            thrown.Message.Should().Be("Committed but undecodable [unknown]: no transaction");
            thrown.CommitState.Should().Be(CommitState.Committed);
            thrown.UpdateId.Should().BeNull();
        },

        ["RequireOneForOperation_returns_the_result_of_One"] = () =>
            OutcomeUnwrapPolicy.Value(One, ValueIntent.RequireOneForOperation, "Mint").Should().Be(42),

        ["RequireOneForOperation_throws_committed_naming_the_operation_for_None"] = () =>
        {
            var thrown = ThrownBy(() => ValueOf(None, ValueIntent.RequireOneForOperation, "Mint"));

            thrown.Message.Should().Be("Mint: expected exactly one result but the operation produced none (None).");
            thrown.CommitState.Should().Be(CommitState.Committed);
            thrown.UpdateId.Should().BeNull();
            thrown.InnerException.Should().BeNull();
            thrown.Data[OperationDataKeyLiteral].Should().Be("Mint");
        },

        ["RequireOneForOperation_throws_committed_listing_the_contract_ids_for_Many"] = () =>
        {
            var thrown = ThrownBy(() => ValueOf(Many, ValueIntent.RequireOneForOperation, "Mint"));

            thrown.Message.Should().Be(
                "Mint: expected exactly one result but the operation produced 2 (contract ids: cid-1, cid-2).");
            thrown.CommitState.Should().Be(CommitState.Committed);
            thrown.Data[OperationDataKeyLiteral].Should().Be("Mint");
        },

        ["RequireOneForOperation_throws_not_committed_with_the_structured_payload_for_DamlError"] = () =>
        {
            var thrown = ThrownBy(() => ValueOf(Contended, ValueIntent.RequireOneForOperation, "Transfer"));

            thrown.Message.Should().Be(
                "Transfer: Daml error [ContentionOnSharedResources/LOCAL_VERDICT_LOCKED_CONTRACTS]: contended");
            thrown.CommitState.Should().Be(CommitState.NotCommitted);
            thrown.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
            thrown.ErrorId.Should().Be("LOCAL_VERDICT_LOCKED_CONTRACTS");
            thrown.Metadata.Should().BeEquivalentTo(SampleMetadata);
            thrown.Data[OperationDataKeyLiteral].Should().Be("Transfer");
        },

        ["RequireOneForOperation_throws_unknown_with_status_category_and_source_for_InfraError"] = () =>
        {
            var source = new TimeoutException("boom");

            var thrown = ThrownBy(() => ValueOf(Unreachable(source), ValueIntent.RequireOneForOperation, "Mint"));

            thrown.Message.Should().Be(
                "Mint: infrastructure error [Grpc { StatusCode = Unavailable }]: participant unreachable");
            thrown.CommitState.Should().Be(CommitState.Unknown);
            thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
            thrown.Category.Should().Be(DamlErrorCategory.TransientServerFailure);
            thrown.InnerException.Should().BeSameAs(source);
            thrown.Data[OperationDataKeyLiteral].Should().Be("Mint");
        },

        ["RequireOneForOperation_keeps_a_cancelled_token_from_beating_InfraError"] = () =>
        {
            var thrown = ThrownBy(() => OutcomeUnwrapPolicy.Value(
                Unreachable(new TimeoutException("boom")), ValueIntent.RequireOneForOperation, "Mint", cancellationToken: Cancelled));

            thrown.CommitState.Should().Be(CommitState.Unknown);
        },

        ["RequireOneForOperation_throws_committed_with_the_update_id_for_CommittedUndecodable"] = () =>
        {
            var source = new FormatException("bad offset");

            var thrown = ThrownBy(() => ValueOf(Undecodable(source), ValueIntent.RequireOneForOperation, "Mint"));

            thrown.Message.Should().Be("Mint: committed but undecodable: bad payload");
            thrown.CommitState.Should().Be(CommitState.Committed);
            thrown.UpdateId.Should().Be("update-1");
            thrown.InnerException.Should().BeSameAs(source);
            thrown.Data[OperationDataKeyLiteral].Should().Be("Mint");
        },

        ["RequireOneForOperation_reports_no_update_id_when_CommittedUndecodable_never_read_one"] = () =>
        {
            var thrown = ThrownBy(() => ValueOf(
                UndecodableBeforeUpdateId(new FormatException("bad offset")), ValueIntent.RequireOneForOperation, "Mint"));

            thrown.CommitState.Should().Be(CommitState.Committed);
            thrown.UpdateId.Should().BeNull();
        },

        ["RejectNone_passes_One_Many_and_CommittedUndecodable"] = () =>
        {
            foreach (var outcome in new[] { One, Many, Undecodable(new FormatException("bad offset")) })
            {
                var act = () => OutcomeUnwrapPolicy.Discard(outcome, DiscardIntent.RejectNone, "TrySubmitSingleAsync");

                act.Should().NotThrow();
            }
        },

        ["RejectNone_throws_committed_naming_the_label_for_None"] = () =>
        {
            var thrown = ThrownBy(() => DiscardOf(None, DiscardIntent.RejectNone, "TrySubmitSingleAsync"));

            thrown.Message.Should().Be(
                "TrySubmitSingleAsync returned no transaction (None); the command committed, but produced no transaction. "
                + "Use TrySubmitSingleAsync for the non-throwing path.");
            thrown.CommitState.Should().Be(CommitState.Committed);
            thrown.UpdateId.Should().BeNull();
            thrown.InnerException.Should().BeNull();
            thrown.Data.Contains(OperationDataKeyLiteral).Should().BeFalse();
        },

        ["RejectNone_throws_not_committed_with_the_structured_payload_for_DamlError"] = () =>
        {
            var thrown = ThrownBy(() => DiscardOf(Contended, DiscardIntent.RejectNone, "TrySubmitSingleAsync"));

            thrown.Message.Should().Be(
                "Daml error [ContentionOnSharedResources/LOCAL_VERDICT_LOCKED_CONTRACTS]: contended");
            thrown.CommitState.Should().Be(CommitState.NotCommitted);
            thrown.ErrorId.Should().Be("LOCAL_VERDICT_LOCKED_CONTRACTS");
            thrown.Data.Contains(OperationDataKeyLiteral).Should().BeFalse();
        },

        ["RejectNone_throws_unknown_for_InfraError"] = () =>
        {
            var thrown = ThrownBy(() => DiscardOf(
                Unreachable(new TimeoutException("boom")), DiscardIntent.RejectNone, "TrySubmitSingleAsync"));

            thrown.Message.Should().Be(
                "Infrastructure error [Grpc { StatusCode = Unavailable }]: participant unreachable");
            thrown.CommitState.Should().Be(CommitState.Unknown);
            thrown.Data.Contains(OperationDataKeyLiteral).Should().BeFalse();
        },

        ["RejectNone_lets_a_cancelled_token_beat_InfraError"] = () =>
        {
            var act = () => OutcomeUnwrapPolicy.Discard(
                Unreachable(new TimeoutException("boom")), DiscardIntent.RejectNone, "TrySubmitSingleAsync", Cancelled);

            act.Should().Throw<OperationCanceledException>();
        },

        ["AcceptNone_passes_One_None_Many_and_CommittedUndecodable"] = () =>
        {
            foreach (var outcome in new[] { One, None, Many, Undecodable(new FormatException("bad offset")) })
            {
                var act = () => OutcomeUnwrapPolicy.Discard(outcome, DiscardIntent.AcceptNone, "Settle");

                act.Should().NotThrow();
            }
        },

        ["AcceptNone_throws_not_committed_naming_the_operation_for_DamlError"] = () =>
        {
            var thrown = ThrownBy(() => DiscardOf(Contended, DiscardIntent.AcceptNone, "Settle"));

            thrown.Message.Should().Be(
                "Settle: Daml error [ContentionOnSharedResources/LOCAL_VERDICT_LOCKED_CONTRACTS]: contended");
            thrown.CommitState.Should().Be(CommitState.NotCommitted);
            thrown.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
            thrown.Data[OperationDataKeyLiteral].Should().Be("Settle");
        },

        ["AcceptNone_throws_unknown_naming_the_operation_for_InfraError"] = () =>
        {
            var thrown = ThrownBy(() => DiscardOf(
                Unreachable(new TimeoutException("boom")), DiscardIntent.AcceptNone, "Settle"));

            thrown.Message.Should().Be(
                "Settle: infrastructure error [Grpc { StatusCode = Unavailable }]: participant unreachable");
            thrown.CommitState.Should().Be(CommitState.Unknown);
            thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
            thrown.Data[OperationDataKeyLiteral].Should().Be("Settle");
        },

        ["AcceptNone_keeps_a_cancelled_token_from_beating_InfraError"] = () =>
        {
            var thrown = ThrownBy(() => OutcomeUnwrapPolicy.Discard(
                Unreachable(new TimeoutException("boom")), DiscardIntent.AcceptNone, "Settle", Cancelled));

            thrown.CommitState.Should().Be(CommitState.Unknown);
        },
    };

    public static IEnumerable<TheoryDataRow<string>> RowNames =>
        Rows.Keys.Select(name => new TheoryDataRow<string>(name) { TestDisplayName = name });

    [Theory]
    [MemberData(nameof(RowNames))]
    public void OutcomeUnwrapPolicy_resolves_the_row(string rowName) => Rows[rowName]();

    [Fact]
    public void Value_requires_the_caller_descriptions_when_the_intent_is_RequireOne()
    {
        var act = () => OutcomeUnwrapPolicy.Value(None, ValueIntent.RequireOne, "TryExerciseAsync");

        act.Should().Throw<ArgumentNullException>();
    }

    private static int ValueOf(ExerciseOutcome<int> outcome, ValueIntent intent, string label) =>
        OutcomeUnwrapPolicy.Value(outcome, intent, label, DescribeNone, DescribeMany);

    private static void DiscardOf(ExerciseOutcome<int> outcome, DiscardIntent intent, string label) =>
        OutcomeUnwrapPolicy.Discard(outcome, intent, label);

    private static LedgerOperationException ThrownBy(Action act) =>
        act.Should().Throw<LedgerOperationException>().Which;
}
