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

public class ParsedLedgerErrorExtensionsTests
{
    private static ParsedLedgerError.Structured StructuredWith(DamlErrorCategory category) =>
        new(
            category,
            "SOME_ERROR_ID",
            "the participant said no",
            new Dictionary<string, string> { ["category"] = "11" },
            new TransportStatus.Grpc(GrpcStatusCode.NotFound));

    private static ParsedLedgerError.Unstructured UnreachableParticipant() =>
        new(
            "the participant is unreachable",
            new TransportStatus.Grpc(GrpcStatusCode.Unavailable),
            DamlErrorCategory.TransientServerFailure);

    private static LedgerCallKind Kind(string name) => Enum.Parse<LedgerCallKind>(name);

    [Fact]
    public void ToException_carries_a_structured_error_with_its_status_category_error_id_and_metadata()
    {
        var parsed = new ParsedLedgerError.Structured(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "PACKAGE_NOT_FOUND",
            "the package is unknown",
            new Dictionary<string, string> { ["category"] = "11" },
            new TransportStatus.Http(HttpStatusCode.NotFound));

        var exception = parsed.ToException(LedgerCallKind.EffectAppliedWrite);

        exception.Message.Should().Be("the package is unknown");
        exception.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing);
        exception.ErrorId.Should().Be("PACKAGE_NOT_FOUND");
        exception.Metadata.Should().Equal(new Dictionary<string, string> { ["category"] = "11" });
        exception.Status.Should().Be(new TransportStatus.Http(HttpStatusCode.NotFound));
        exception.CommitState.Should().Be(CommitState.NotCommitted);
    }

    [Fact]
    public void ToException_carries_an_unstructured_error_with_its_status_and_category_and_no_error_id_or_metadata()
    {
        var exception = UnreachableParticipant().ToException(LedgerCallKind.AcceptedOnlyWrite);

        exception.Message.Should().Be("the participant is unreachable");
        exception.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        exception.Category.Should().Be(DamlErrorCategory.TransientServerFailure);
        exception.ErrorId.Should().BeNull();
        exception.Metadata.Should().BeNull();
    }

    [Fact]
    public void ToException_hands_on_the_transport_exception_as_the_inner_exception_of_a_structured_error()
    {
        var cause = new InvalidOperationException("transport");

        var exception = StructuredWith(DamlErrorCategory.InvalidIndependentOfSystemState)
            .ToException(LedgerCallKind.Read, cause);

        exception.InnerException.Should().BeSameAs(cause);
    }

    [Fact]
    public void ToException_hands_on_the_transport_exception_as_the_inner_exception_of_an_unstructured_error()
    {
        var cause = new InvalidOperationException("transport");

        var exception = UnreachableParticipant().ToException(LedgerCallKind.AcceptedOnlyWrite, cause);

        exception.InnerException.Should().BeSameAs(cause);
    }

    [Theory]
    [InlineData("Read")]
    [InlineData("AcceptedOnlyWrite")]
    [InlineData("EffectAppliedWrite")]
    public void ToException_leaves_the_inner_exception_empty_when_there_is_no_transport_exception(string kindName)
    {
        UnreachableParticipant().ToException(Kind(kindName)).InnerException.Should().BeNull();
    }

    [Fact]
    public void ToException_reports_an_unreachable_participant_on_a_read_as_not_committed()
    {
        UnreachableParticipant().ToException(LedgerCallKind.Read).CommitState.Should().Be(CommitState.NotCommitted);
    }

    [Theory]
    [InlineData("AcceptedOnlyWrite")]
    [InlineData("EffectAppliedWrite")]
    public void ToException_reports_an_unreachable_participant_on_a_write_as_unknown(string kindName)
    {
        UnreachableParticipant().ToException(Kind(kindName)).CommitState.Should().Be(CommitState.Unknown);
    }

    [Theory]
    [InlineData(DamlErrorCategory.DeadlineExceededRequestStateUnknown)]
    [InlineData(DamlErrorCategory.Unknown)]
    [InlineData(DamlErrorCategory.InvalidIndependentOfSystemState)]
    public void ToException_reports_a_structured_error_on_a_read_as_not_committed(DamlErrorCategory category)
    {
        StructuredWith(category).ToException(LedgerCallKind.Read).CommitState.Should().Be(CommitState.NotCommitted);
    }

    [Theory]
    [InlineData("AcceptedOnlyWrite", DamlErrorCategory.DeadlineExceededRequestStateUnknown)]
    [InlineData("EffectAppliedWrite", DamlErrorCategory.DeadlineExceededRequestStateUnknown)]
    [InlineData("AcceptedOnlyWrite", DamlErrorCategory.Unknown)]
    [InlineData("EffectAppliedWrite", DamlErrorCategory.Unknown)]
    public void ToException_reports_a_write_rejected_with_an_uncertain_category_as_unknown(
        string kindName, DamlErrorCategory category)
    {
        StructuredWith(category).ToException(Kind(kindName)).CommitState.Should().Be(CommitState.Unknown);
    }

    [Theory]
    [InlineData("AcceptedOnlyWrite", DamlErrorCategory.InvalidIndependentOfSystemState)]
    [InlineData("EffectAppliedWrite", DamlErrorCategory.InvalidIndependentOfSystemState)]
    [InlineData("AcceptedOnlyWrite", DamlErrorCategory.TransientServerFailure)]
    [InlineData("EffectAppliedWrite", DamlErrorCategory.ContentionOnSharedResources)]
    public void ToException_reports_a_write_rejected_with_a_definite_category_as_not_committed(
        string kindName, DamlErrorCategory category)
    {
        StructuredWith(category).ToException(Kind(kindName)).CommitState.Should().Be(CommitState.NotCommitted);
    }

    private static ParsedLedgerError.Structured Rejection(
        DamlErrorCategory category, string errorId, params (string Key, string Value)[] metadata) =>
        new(
            category,
            errorId,
            "the participant said so",
            metadata.ToDictionary(entry => entry.Key, entry => entry.Value),
            new TransportStatus.Http(HttpStatusCode.Conflict));

    [Theory]
    [InlineData("AcceptedOnlyWrite")]
    [InlineData("EffectAppliedWrite")]
    public void ToException_reports_a_DUPLICATE_COMMAND_write_as_committed(string kindName)
    {
        var parsed = Rejection(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists,
            "DUPLICATE_COMMAND",
            ("accepted", "true"),
            ("completion_offset", "9663"));

        var exception = parsed.ToException(Kind(kindName));

        exception.CommitState.Should().Be(CommitState.Committed);
        exception.UpdateId.Should().BeNull();
        exception.Metadata.Should().ContainKey("completion_offset").WhoseValue.Should().Be("9663");
    }

    [Fact]
    public void ToException_reports_a_DUPLICATE_COMMAND_without_accepted_metadata_as_committed()
    {
        Rejection(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists, "DUPLICATE_COMMAND")
            .ToException(LedgerCallKind.EffectAppliedWrite)
            .CommitState.Should().Be(CommitState.Committed);
    }

    [Fact]
    public void ToException_reports_a_DUPLICATE_COMMAND_with_accepted_false_as_unknown()
    {
        Rejection(
                DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists,
                "DUPLICATE_COMMAND",
                ("accepted", "false"))
            .ToException(LedgerCallKind.EffectAppliedWrite)
            .CommitState.Should().Be(CommitState.Unknown);
    }

    [Fact]
    public void ToException_reports_a_DUPLICATE_COMMAND_read_as_not_committed()
    {
        Rejection(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists, "DUPLICATE_COMMAND")
            .ToException(LedgerCallKind.Read)
            .CommitState.Should().Be(CommitState.NotCommitted);
    }

    [Fact]
    public void ToException_reports_DUPLICATE_CONTRACT_KEY_in_the_same_category_as_not_committed()
    {
        Rejection(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists, "DUPLICATE_CONTRACT_KEY")
            .ToException(LedgerCallKind.EffectAppliedWrite)
            .CommitState.Should().Be(CommitState.NotCommitted);
    }

    [Theory]
    [InlineData("AcceptedOnlyWrite")]
    [InlineData("EffectAppliedWrite")]
    public void ToException_reports_SUBMISSION_ALREADY_IN_FLIGHT_as_unknown(string kindName)
    {
        Rejection(DamlErrorCategory.ContentionOnSharedResources, "SUBMISSION_ALREADY_IN_FLIGHT")
            .ToException(Kind(kindName))
            .CommitState.Should().Be(CommitState.Unknown);
    }
}
