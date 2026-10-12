// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Wire;

public class LedgerCallOutcomeTests
{
    private static readonly Exception TransportCause = new InvalidOperationException("the transport said so");

    private static readonly IReadOnlyDictionary<string, LedgerCallEnding> Endings =
        new Dictionary<string, LedgerCallEnding>
        {
            ["unanswered"] = new LedgerCallEnding.Unanswered("connection refused", TransportCause),
            ["unreadable"] = new LedgerCallEnding.Unreadable("could not be decoded", TransportCause, UpdateId: null),
            ["rejected, unstructured"] = Rejected(new ParsedLedgerError.Unstructured(
                "the participant is unreachable",
                new TransportStatus.Grpc(GrpcStatusCode.Unavailable),
                DamlErrorCategory.TransientServerFailure)),
            ["rejected, definite category"] = Rejected(Structured(
                DamlErrorCategory.InvalidIndependentOfSystemState, "COMMAND_PREPROCESSING_FAILED")),
            ["rejected, contention"] = Rejected(Structured(
                DamlErrorCategory.ContentionOnSharedResources, "LOCAL_VERDICT_LOCKED_CONTRACTS")),
            ["rejected, request state unknown"] = Rejected(Structured(
                DamlErrorCategory.DeadlineExceededRequestStateUnknown, "REQUEST_TIME_OUT")),
            ["rejected, unrecognised category"] = Rejected(Structured(DamlErrorCategory.Unknown, "SOME_ERROR")),
            ["rejected, DUPLICATE_COMMAND"] = Rejected(Structured(
                DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists,
                "DUPLICATE_COMMAND",
                ("accepted", "true"),
                ("completion_offset", "9663"))),
            ["rejected, DUPLICATE_COMMAND without accepted"] = Rejected(Structured(
                DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists, "DUPLICATE_COMMAND")),
            ["rejected, DUPLICATE_COMMAND not accepted"] = Rejected(Structured(
                DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists,
                "DUPLICATE_COMMAND",
                ("accepted", "false"))),
            ["rejected, DUPLICATE_CONTRACT_KEY"] = Rejected(Structured(
                DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists, "DUPLICATE_CONTRACT_KEY")),
            ["rejected, SUBMISSION_ALREADY_IN_FLIGHT"] = Rejected(Structured(
                DamlErrorCategory.ContentionOnSharedResources, "SUBMISSION_ALREADY_IN_FLIGHT")),
        };

    private static LedgerCallEnding.Rejected Rejected(ParsedLedgerError error) => new(error, TransportCause);

    private static ParsedLedgerError.Structured Structured(
        DamlErrorCategory category, string errorId, params (string Key, string Value)[] metadata) =>
        new(
            category,
            errorId,
            "the participant said no",
            metadata.ToDictionary(entry => entry.Key, entry => entry.Value),
            new TransportStatus.Http(HttpStatusCode.Conflict));

    public static TheoryData<string, string, CommitState> CommitStateTable() => new()
    {
        { "unanswered", "Read", CommitState.NotCommitted },
        { "unanswered", "AcceptedOnlyWrite", CommitState.Unknown },
        { "unanswered", "EffectAppliedWrite", CommitState.Unknown },
        { "unreadable", "Read", CommitState.NotCommitted },
        { "unreadable", "AcceptedOnlyWrite", CommitState.Unknown },
        { "unreadable", "EffectAppliedWrite", CommitState.Committed },
        { "rejected, unstructured", "Read", CommitState.NotCommitted },
        { "rejected, unstructured", "AcceptedOnlyWrite", CommitState.Unknown },
        { "rejected, unstructured", "EffectAppliedWrite", CommitState.Unknown },
        { "rejected, definite category", "Read", CommitState.NotCommitted },
        { "rejected, definite category", "AcceptedOnlyWrite", CommitState.NotCommitted },
        { "rejected, definite category", "EffectAppliedWrite", CommitState.NotCommitted },
        { "rejected, contention", "EffectAppliedWrite", CommitState.NotCommitted },
        { "rejected, request state unknown", "Read", CommitState.NotCommitted },
        { "rejected, request state unknown", "AcceptedOnlyWrite", CommitState.Unknown },
        { "rejected, request state unknown", "EffectAppliedWrite", CommitState.Unknown },
        { "rejected, unrecognised category", "Read", CommitState.NotCommitted },
        { "rejected, unrecognised category", "AcceptedOnlyWrite", CommitState.Unknown },
        { "rejected, unrecognised category", "EffectAppliedWrite", CommitState.Unknown },
        { "rejected, DUPLICATE_COMMAND", "Read", CommitState.NotCommitted },
        { "rejected, DUPLICATE_COMMAND", "AcceptedOnlyWrite", CommitState.Committed },
        { "rejected, DUPLICATE_COMMAND", "EffectAppliedWrite", CommitState.Committed },
        { "rejected, DUPLICATE_COMMAND without accepted", "EffectAppliedWrite", CommitState.Committed },
        { "rejected, DUPLICATE_COMMAND not accepted", "EffectAppliedWrite", CommitState.Unknown },
        { "rejected, DUPLICATE_CONTRACT_KEY", "EffectAppliedWrite", CommitState.NotCommitted },
        { "rejected, SUBMISSION_ALREADY_IN_FLIGHT", "AcceptedOnlyWrite", CommitState.Unknown },
        { "rejected, SUBMISSION_ALREADY_IN_FLIGHT", "EffectAppliedWrite", CommitState.Unknown },
    };

    [Theory]
    [MemberData(nameof(CommitStateTable))]
    public void ToException_reports_the_commit_state_the_ending_and_the_call_kind_decide(
        string ending, string kind, CommitState expected)
    {
        Endings[ending].ToException(Enum.Parse<LedgerCallKind>(kind)).CommitState.Should().Be(expected);
    }

    [Theory]
    [InlineData("Read")]
    [InlineData("AcceptedOnlyWrite")]
    [InlineData("EffectAppliedWrite")]
    public void ToException_reports_an_unanswered_call_with_a_no_response_status_and_its_cause(string kind)
    {
        var exception = Endings["unanswered"].ToException(Enum.Parse<LedgerCallKind>(kind));

        exception.Message.Should().Be("connection refused");
        exception.Status.Should().Be(new TransportStatus.NoResponse());
        exception.InnerException.Should().BeSameAs(TransportCause);
        exception.Category.Should().BeNull();
        exception.ErrorId.Should().BeNull();
        exception.Metadata.Should().BeNull();
    }

    [Theory]
    [InlineData("Read")]
    [InlineData("AcceptedOnlyWrite")]
    [InlineData("EffectAppliedWrite")]
    public void ToException_reports_an_unreadable_response_with_an_undecodable_body_status_and_its_cause(string kind)
    {
        var exception = Endings["unreadable"].ToException(Enum.Parse<LedgerCallKind>(kind));

        exception.Message.Should().Be("could not be decoded");
        exception.Status.Should().Be(new TransportStatus.UndecodableBody());
        exception.InnerException.Should().BeSameAs(TransportCause);
        exception.Category.Should().BeNull();
        exception.ErrorId.Should().BeNull();
        exception.UpdateId.Should().BeNull();
    }

    [Fact]
    public void ToException_accepts_an_unreadable_response_that_has_no_exception_to_blame()
    {
        var exception = new LedgerCallEnding.Unreadable("no body was present", Cause: null, UpdateId: null)
            .ToException(LedgerCallKind.EffectAppliedWrite);

        exception.InnerException.Should().BeNull();
        exception.CommitState.Should().Be(CommitState.Committed);
    }

    [Fact]
    public void ToException_carries_a_structured_rejection_with_its_status_category_error_id_metadata_and_cause()
    {
        var exception = Endings["rejected, DUPLICATE_COMMAND"].ToException(LedgerCallKind.EffectAppliedWrite);

        exception.Message.Should().Be("the participant said no");
        exception.Status.Should().Be(new TransportStatus.Http(HttpStatusCode.Conflict));
        exception.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists);
        exception.ErrorId.Should().Be("DUPLICATE_COMMAND");
        exception.Metadata.Should().Equal(
            new Dictionary<string, string> { ["accepted"] = "true", ["completion_offset"] = "9663" });
        exception.InnerException.Should().BeSameAs(TransportCause);
        exception.UpdateId.Should().BeNull();
    }

    [Fact]
    public void ToException_carries_an_unstructured_rejection_with_its_status_category_and_cause_and_no_error_id()
    {
        var exception = Endings["rejected, unstructured"].ToException(LedgerCallKind.AcceptedOnlyWrite);

        exception.Message.Should().Be("the participant is unreachable");
        exception.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        exception.Category.Should().Be(DamlErrorCategory.TransientServerFailure);
        exception.ErrorId.Should().BeNull();
        exception.Metadata.Should().BeNull();
        exception.InnerException.Should().BeSameAs(TransportCause);
    }

    [Fact]
    public void ToException_leaves_the_inner_exception_empty_for_a_rejection_with_no_transport_exception()
    {
        new LedgerCallEnding.Rejected(Structured(DamlErrorCategory.InvalidIndependentOfSystemState, "X"), Cause: null)
            .ToException(LedgerCallKind.Read)
            .InnerException.Should().BeNull();
    }

    public static TheoryData<string, string> OutcomeArmTable() => new()
    {
        { "unanswered", "InfraError" },
        { "unreadable", "CommittedUndecodable" },
        { "rejected, unstructured", "InfraError" },
        { "rejected, definite category", "DamlError" },
        { "rejected, request state unknown", "DamlError" },
        { "rejected, DUPLICATE_COMMAND", "DamlError" },
        { "rejected, DUPLICATE_COMMAND not accepted", "DamlError" },
        { "rejected, SUBMISSION_ALREADY_IN_FLIGHT", "DamlError" },
    };

    [Theory]
    [MemberData(nameof(OutcomeArmTable))]
    public void ToOutcome_reports_the_arm_the_ending_decides(string ending, string expectedArm)
    {
        Endings[ending].ToOutcome<string>().GetType().Name.Should().StartWith(expectedArm);
    }

    [Fact]
    public void ToOutcome_reports_a_structured_rejection_as_a_DamlError_carrying_its_category_error_id_and_metadata()
    {
        var outcome = Endings["rejected, DUPLICATE_COMMAND"].ToOutcome<string>();

        outcome.Should().Be(new ExerciseOutcome<string>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists,
            "DUPLICATE_COMMAND",
            "the participant said no",
            new Dictionary<string, string> { ["accepted"] = "true", ["completion_offset"] = "9663" }));
    }

    [Fact]
    public void ToOutcome_reports_an_unstructured_rejection_as_an_InfraError_carrying_its_status_category_and_cause()
    {
        var infraError = Endings["rejected, unstructured"].ToOutcome<string>()
            .Should().BeOfType<ExerciseOutcome<string>.InfraError>().Subject;

        infraError.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        infraError.Message.Should().Be("the participant is unreachable");
        infraError.Category.Should().Be(DamlErrorCategory.TransientServerFailure);
        infraError.SourceException.Should().BeSameAs(TransportCause);
    }

    [Fact]
    public void ToOutcome_reports_an_unanswered_call_as_a_no_response_InfraError_carrying_its_cause()
    {
        var infraError = Endings["unanswered"].ToOutcome<string>()
            .Should().BeOfType<ExerciseOutcome<string>.InfraError>().Subject;

        infraError.Status.Should().Be(new TransportStatus.NoResponse());
        infraError.Message.Should().Be("connection refused");
        infraError.Category.Should().BeNull();
        infraError.SourceException.Should().BeSameAs(TransportCause);
    }

    [Fact]
    public void ToOutcome_reports_an_unreadable_response_as_CommittedUndecodable_carrying_its_update_id_and_cause()
    {
        var undecodable = new LedgerCallEnding.Unreadable("could not be decoded", TransportCause, "update-1")
            .ToOutcome<string>()
            .Should().BeOfType<ExerciseOutcome<string>.CommittedUndecodable>().Subject;

        undecodable.UpdateId.Should().Be("update-1");
        undecodable.Message.Should().Be("could not be decoded");
        undecodable.SourceException.Should().BeSameAs(TransportCause);
    }

    [Fact]
    public void ToOutcome_blames_an_unreadable_response_with_no_exception_on_an_InvalidOperationException_naming_it()
    {
        var undecodable = new LedgerCallEnding.Unreadable("no body was present", Cause: null, UpdateId: null)
            .ToOutcome<string>()
            .Should().BeOfType<ExerciseOutcome<string>.CommittedUndecodable>().Subject;

        undecodable.SourceException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("no body was present");
    }

    [Fact]
    public void ToStreamFault_carries_a_structured_rejection_with_its_status_category_error_id_and_cause()
    {
        var fault = Endings["rejected, definite category"].ToStreamFault();

        fault.Status.Should().Be(new TransportStatus.Http(HttpStatusCode.Conflict));
        fault.Message.Should().Be("the participant said no");
        fault.Category.Should().Be(DamlErrorCategory.InvalidIndependentOfSystemState);
        fault.ErrorId.Should().Be("COMMAND_PREPROCESSING_FAILED");
        fault.SourceException.Should().BeSameAs(TransportCause);
    }

    [Fact]
    public void ToStreamFault_leaves_out_an_unrecognised_category_and_an_empty_error_id()
    {
        var fault = Rejected(Structured(DamlErrorCategory.Unknown, "")).ToStreamFault();

        fault.Category.Should().BeNull();
        fault.ErrorId.Should().BeNull();
    }

    [Fact]
    public void ToStreamFault_carries_an_unstructured_rejection_with_its_status_category_and_cause_and_no_error_id()
    {
        var fault = Endings["rejected, unstructured"].ToStreamFault();

        fault.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        fault.Message.Should().Be("the participant is unreachable");
        fault.Category.Should().Be(DamlErrorCategory.TransientServerFailure);
        fault.ErrorId.Should().BeNull();
        fault.SourceException.Should().BeSameAs(TransportCause);
    }

    [Fact]
    public void ToStreamFault_reports_an_unanswered_window_with_a_no_response_status_and_its_cause()
    {
        var fault = Endings["unanswered"].ToStreamFault();

        fault.Status.Should().Be(new TransportStatus.NoResponse());
        fault.Message.Should().Be("connection refused");
        fault.Category.Should().BeNull();
        fault.ErrorId.Should().BeNull();
        fault.SourceException.Should().BeSameAs(TransportCause);
    }

    [Fact]
    public void ToStreamFault_reports_an_unreadable_window_with_an_undecodable_body_status_and_its_cause()
    {
        var fault = Endings["unreadable"].ToStreamFault();

        fault.Status.Should().Be(new TransportStatus.UndecodableBody());
        fault.Message.Should().Be("could not be decoded");
        fault.Category.Should().BeNull();
        fault.ErrorId.Should().BeNull();
        fault.SourceException.Should().BeSameAs(TransportCause);
    }

    private static LedgerCallEnding.Rejected RetriedDuplicate(params (string Key, string Value)[] metadata) =>
        new(
            Structured(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists, "DUPLICATE_COMMAND", metadata),
            TransportCause,
            AfterRetry: true);

    [Theory]
    [InlineData("true", "9663", 9663L)]
    [InlineData("TRUE", "1", 1L)]
    [InlineData(null, "42", 42L)]
    [InlineData("true", "9223372036854775807", 9223372036854775807L)]
    public void ToRetriedDuplicateVerdict_resolves_a_retried_DUPLICATE_COMMAND_at_its_completion_offset(
        string? accepted, string completionOffset, long expectedOffset)
    {
        var ending = accepted is null
            ? RetriedDuplicate(("completion_offset", completionOffset))
            : RetriedDuplicate(("accepted", accepted), ("completion_offset", completionOffset));

        ending.ToRetriedDuplicateVerdict().Should().Be(new RetriedDuplicateVerdict.CommittedAt(expectedOffset));
    }

    [Theory]
    [InlineData("false", "9663")]
    [InlineData("FALSE", "9663")]
    [InlineData("true", null)]
    [InlineData("true", "0")]
    [InlineData("true", "-5")]
    [InlineData("true", "not-an-offset")]
    [InlineData("true", "")]
    [InlineData("true", "9663.5")]
    [InlineData("true", "9223372036854775808")]
    public void ToRetriedDuplicateVerdict_finds_a_retried_DUPLICATE_COMMAND_unresolvable_without_an_accepted_offset(
        string accepted, string? completionOffset)
    {
        var ending = completionOffset is null
            ? RetriedDuplicate(("accepted", accepted))
            : RetriedDuplicate(("accepted", accepted), ("completion_offset", completionOffset));

        ending.ToRetriedDuplicateVerdict().Should().BeOfType<RetriedDuplicateVerdict.Unresolvable>();
    }

    [Fact]
    public void ToRetriedDuplicateVerdict_ignores_a_DUPLICATE_COMMAND_answering_the_first_attempt()
    {
        new LedgerCallEnding.Rejected(
                Structured(
                    DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists,
                    "DUPLICATE_COMMAND",
                    ("accepted", "true"),
                    ("completion_offset", "9663")),
                TransportCause,
                AfterRetry: false)
            .ToRetriedDuplicateVerdict()
            .Should().BeOfType<RetriedDuplicateVerdict.NotARetriedDuplicate>();
    }

    [Theory]
    [InlineData("rejected, DUPLICATE_CONTRACT_KEY")]
    [InlineData("rejected, unstructured")]
    [InlineData("unanswered")]
    [InlineData("unreadable")]
    public void ToRetriedDuplicateVerdict_ignores_any_other_ending_after_a_retry(string ending)
    {
        var retried = Endings[ending] is LedgerCallEnding.Rejected rejected
            ? rejected with { AfterRetry = true }
            : Endings[ending];

        retried.ToRetriedDuplicateVerdict().Should().BeOfType<RetriedDuplicateVerdict.NotARetriedDuplicate>();
    }

    public static TheoryData<TransportStatus?, DamlErrorCategory?, bool> AbsenceTable() => new()
    {
        { new TransportStatus.Grpc(GrpcStatusCode.NotFound), null, true },
        { new TransportStatus.Grpc(GrpcStatusCode.NotFound), DamlErrorCategory.InvalidIndependentOfSystemState, true },
        { new TransportStatus.Grpc(GrpcStatusCode.FailedPrecondition), DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing, false },
        { new TransportStatus.Grpc(GrpcStatusCode.Unavailable), null, false },
        { new TransportStatus.Http(HttpStatusCode.NotFound), null, true },
        { new TransportStatus.Http(HttpStatusCode.BadRequest), DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing, true },
        { new TransportStatus.Http(HttpStatusCode.Conflict), DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists, false },
        { new TransportStatus.Http(HttpStatusCode.ServiceUnavailable), null, false },
        { new TransportStatus.NoResponse(), null, false },
        { new TransportStatus.UndecodableBody(), null, false },
        { null, DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing, false },
    };

    [Theory]
    [MemberData(nameof(AbsenceTable))]
    public void ReportsAbsence_reads_not_found_by_the_gRPC_status_alone_and_over_HTTP_by_the_category_or_a_404(
        TransportStatus? status, DamlErrorCategory? category, bool expected)
    {
        LedgerCallOutcome.ReportsAbsence(status, category).Should().Be(expected);
    }
}
