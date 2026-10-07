// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using Daml.Runtime.Outcomes;
using AwesomeAssertions;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

public class LedgerOperationExceptionTests
{
    [Fact]
    public void CommittedWithoutDetail_preserves_committed_state_without_synthetic_error_detail()
    {
        var exception = LedgerOperationException.CommittedWithoutDetail("No created contract.");

        exception.Message.Should().Be("No created contract.");
        exception.CommitState.Should().Be(CommitState.Committed);
        exception.UpdateId.Should().BeNull();
        exception.InnerException.Should().BeNull();
        exception.Category.Should().BeNull();
        exception.ErrorId.Should().BeNull();
        exception.Metadata.Should().BeNull();
        exception.Status.Should().BeNull();
    }

    [Fact]
    public void LedgerOperationException_message_and_inner_exception_constructor_preserves_both()
    {
        var inner = new TimeoutException("transport gave up");

        var exception = new LedgerOperationException("operation failed", inner);

        exception.Message.Should().Be("operation failed");
        exception.InnerException.Should().BeSameAs(inner);
        exception.Category.Should().BeNull();
        exception.ErrorId.Should().BeNull();
        exception.Metadata.Should().BeNull();
        exception.Status.Should().BeNull();
        exception.CommitState.Should().Be(CommitState.NotCommitted);
    }

    [Fact]
    public void LedgerOperationException_infra_error_constructor_leaves_category_null_when_omitted()
    {
        var exception = new LedgerOperationException(
            "transport failed", new TransportStatus.Http(HttpStatusCode.ServiceUnavailable));

        exception.Status.Should().Be(new TransportStatus.Http(HttpStatusCode.ServiceUnavailable));
        exception.Category.Should().BeNull();
        exception.ErrorId.Should().BeNull();
        exception.Metadata.Should().BeNull();
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void LedgerOperationException_infra_error_constructor_keeps_the_category_alongside_the_status_code()
    {
        var inner = new TimeoutException("transport gave up");

        var exception = new LedgerOperationException(
            "transport failed",
            new TransportStatus.Http(HttpStatusCode.BadRequest),
            DamlErrorCategory.InvalidIndependentOfSystemState,
            inner);

        exception.Status.Should().Be(new TransportStatus.Http(HttpStatusCode.BadRequest));
        exception.Category.Should().Be(DamlErrorCategory.InvalidIndependentOfSystemState);
        exception.InnerException.Should().BeSameAs(inner);
        exception.ErrorId.Should().BeNull();
    }

    [Fact]
    public void LedgerOperationException_infra_error_constructor_keeps_an_error_id_without_any_metadata()
    {
        var exception = new LedgerOperationException(
            "the snapshot faulted",
            new TransportStatus.Grpc(GrpcStatusCode.Aborted),
            DamlErrorCategory.ContentionOnSharedResources,
            errorId: "STALE_STREAM_AUTHORIZATION");

        exception.ErrorId.Should().Be(
            "STALE_STREAM_AUTHORIZATION",
            "a faulted stream is the one path that carries an error id without a structured write-path "
            + "error behind it, and this constructor is where that id enters the exception");
        exception.Metadata.Should().BeNull(
            "the stream fault has no ErrorInfo metadata to carry, so a catch site reading Metadata off "
            + "the strength of a non-null ErrorId — an implication that held while only DamlError set "
            + "the id — now has to null-check it");
    }

    [Fact]
    public void LedgerOperationException_daml_error_constructor_rejects_null_metadata()
    {
        var act = () => new LedgerOperationException(
            "exercise failed",
            DamlErrorCategory.InvalidGivenCurrentSystemStateOther,
            "SOME_ERROR_ID",
            metadata: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("metadata");
    }

    [Fact]
    public void LedgerOperationException_daml_error_constructor_keeps_supplied_metadata()
    {
        var metadata = new Dictionary<string, string> { ["key"] = "value" };

        var exception = new LedgerOperationException(
            "exercise failed",
            DamlErrorCategory.InvalidGivenCurrentSystemStateOther,
            "SOME_ERROR_ID",
            metadata);

        exception.Metadata.Should().BeSameAs(metadata);
    }

    [Fact]
    public void LedgerOperationException_committed_undecodable_constructor_keeps_the_update_id_alongside_the_inner_exception()
    {
        var inner = new InvalidOperationException("decode failed");

        var exception = new LedgerOperationException("committed but undecodable", "u1", inner);

        exception.UpdateId.Should().Be("u1");
        exception.InnerException.Should().BeSameAs(inner);
        exception.CommitState.Should().Be(CommitState.Committed);
        exception.Category.Should().BeNull();
        exception.ErrorId.Should().BeNull();
        exception.Metadata.Should().BeNull();
        exception.Status.Should().BeNull();
    }

    [Fact]
    public void LedgerOperationException_committed_undecodable_constructor_leaves_update_id_null_when_the_decode_failure_precedes_it()
    {
        var inner = new InvalidOperationException("decode failed");

        var exception = new LedgerOperationException("committed but undecodable", null, inner);

        exception.UpdateId.Should().BeNull();
        exception.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void LedgerOperationException_committed_undecodable_constructor_keeps_CommitState_Committed_even_when_the_update_id_is_null()
    {
        var exception = new LedgerOperationException(
            "committed but undecodable", null, new InvalidOperationException("decode failed"));

        exception.CommitState.Should().Be(
            CommitState.Committed,
            "the command committed regardless of whether the update id was readable before decoding "
            + "failed, so a catch site must not read a null UpdateId as \"nothing committed\" the way "
            + "it can for a None/Many exception");
    }

    [Fact]
    public void LedgerOperationException_infra_error_constructor_sets_CommitState_Unknown()
    {
        var exception = new LedgerOperationException(
            "transport failed", new TransportStatus.Http(HttpStatusCode.ServiceUnavailable));

        exception.CommitState.Should().Be(
            CommitState.Unknown,
            "the transport failure happened after the command was sent, so the ledger may already "
            + "have committed it");
    }

    [Fact]
    public void LedgerOperationException_daml_error_constructor_sets_CommitState_NotCommitted_for_an_ordinary_category()
    {
        var exception = new LedgerOperationException(
            "exercise failed",
            DamlErrorCategory.InvalidGivenCurrentSystemStateOther,
            "SOME_ERROR_ID",
            new Dictionary<string, string>());

        exception.CommitState.Should().Be(CommitState.NotCommitted);
    }

    [Theory]
    [InlineData(null, CommitState.Committed)]
    [InlineData("true", CommitState.Committed)]
    [InlineData("false", CommitState.Unknown)]
    public void LedgerOperationException_daml_error_constructor_maps_DUPLICATE_COMMAND_by_its_accepted_metadata(
        string? accepted, CommitState expected)
    {
        var metadata = accepted is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { ["accepted"] = accepted };

        var exception = new LedgerOperationException(
            "duplicate",
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists,
            "DUPLICATE_COMMAND",
            metadata);

        exception.CommitState.Should().Be(expected);
    }

    [Fact]
    public void LedgerOperationException_daml_error_constructor_maps_DUPLICATE_COMMAND_to_Committed_whatever_its_category()
    {
        var exception = new LedgerOperationException(
            "duplicate",
            DamlErrorCategory.ContentionOnSharedResources,
            "DUPLICATE_COMMAND",
            new Dictionary<string, string>());

        exception.CommitState.Should().Be(CommitState.Committed);
    }

    [Fact]
    public void LedgerOperationException_daml_error_constructor_keeps_DUPLICATE_COMMAND_update_id_null_and_offset_in_metadata()
    {
        var exception = new LedgerOperationException(
            "duplicate",
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists,
            "DUPLICATE_COMMAND",
            new Dictionary<string, string> { ["accepted"] = "true", ["completion_offset"] = "9663" });

        exception.UpdateId.Should().BeNull();
        exception.Metadata.Should().ContainKey("completion_offset").WhoseValue.Should().Be("9663");
    }

    [Fact]
    public void LedgerOperationException_daml_error_constructor_keeps_DUPLICATE_CONTRACT_KEY_NotCommitted_in_the_same_category()
    {
        var exception = new LedgerOperationException(
            "duplicate key",
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists,
            "DUPLICATE_CONTRACT_KEY",
            new Dictionary<string, string>());

        exception.CommitState.Should().Be(CommitState.NotCommitted);
    }

    [Theory]
    [InlineData(DamlErrorCategory.ContentionOnSharedResources)]
    [InlineData(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists)]
    public void LedgerOperationException_daml_error_constructor_maps_SUBMISSION_ALREADY_IN_FLIGHT_to_Unknown(
        DamlErrorCategory category)
    {
        var exception = new LedgerOperationException(
            "in flight", category, "SUBMISSION_ALREADY_IN_FLIGHT", new Dictionary<string, string>());

        exception.CommitState.Should().Be(CommitState.Unknown);
    }

    [Fact]
    public void LedgerOperationException_daml_error_constructor_sets_CommitState_Unknown_for_DeadlineExceededRequestStateUnknown()
    {
        var exception = new LedgerOperationException(
            "exercise failed",
            DamlErrorCategory.DeadlineExceededRequestStateUnknown,
            "SOME_ERROR_ID",
            new Dictionary<string, string>());

        exception.CommitState.Should().Be(
            CommitState.Unknown,
            "a DeadlineExceededRequestStateUnknown DamlError means the ledger itself reported that "
            + "the outcome of the request is unknown");
    }

    [Fact]
    public void LedgerOperationException_daml_error_constructor_sets_CommitState_Unknown_for_an_unclassified_category()
    {
        var exception = new LedgerOperationException(
            "exercise failed",
            DamlErrorCategory.Unknown,
            "SOME_ERROR_ID",
            new Dictionary<string, string>());

        exception.CommitState.Should().Be(
            CommitState.Unknown,
            "DamlErrorCategory.Unknown means the transport trailer was missing or unparseable, so the "
            + "hidden category could have been DeadlineExceededRequestStateUnknown — treating it as "
            + "NotCommitted would risk resubmitting a command that may have already committed");
    }

    [Fact]
    public void LedgerOperationException_status_and_commit_state_constructor_sets_every_field_it_is_given()
    {
        var inner = new TimeoutException("transport gave up");
        var metadata = new Dictionary<string, string> { ["key"] = "value" };

        var exception = new LedgerOperationException(
            "read failed",
            new TransportStatus.Http(HttpStatusCode.BadGateway),
            CommitState.NotCommitted,
            DamlErrorCategory.TransientServerFailure,
            "SERVICE_NOT_RUNNING",
            metadata,
            inner);

        exception.Message.Should().Be("read failed");
        exception.Status.Should().Be(new TransportStatus.Http(HttpStatusCode.BadGateway));
        exception.CommitState.Should().Be(CommitState.NotCommitted);
        exception.Category.Should().Be(DamlErrorCategory.TransientServerFailure);
        exception.ErrorId.Should().Be("SERVICE_NOT_RUNNING");
        exception.Metadata.Should().BeSameAs(metadata);
        exception.InnerException.Should().BeSameAs(inner);
        exception.UpdateId.Should().BeNull();
    }

    [Fact]
    public void LedgerOperationException_status_and_commit_state_constructor_leaves_the_optional_fields_null_when_omitted()
    {
        var exception = new LedgerOperationException(
            "read got no answer", new TransportStatus.NoResponse(), CommitState.NotCommitted);

        exception.Status.Should().Be(new TransportStatus.NoResponse());
        exception.CommitState.Should().Be(CommitState.NotCommitted);
        exception.Category.Should().BeNull();
        exception.ErrorId.Should().BeNull();
        exception.Metadata.Should().BeNull();
        exception.InnerException.Should().BeNull();
        exception.UpdateId.Should().BeNull();
    }

    [Theory]
    [InlineData(CommitState.NotCommitted)]
    [InlineData(CommitState.Unknown)]
    [InlineData(CommitState.Committed)]
    public void LedgerOperationException_status_and_commit_state_constructor_keeps_the_commit_state_it_is_given(
        CommitState commitState)
    {
        var exception = new LedgerOperationException(
            "failed", new TransportStatus.Grpc(GrpcStatusCode.Unavailable), commitState);

        exception.CommitState.Should().Be(commitState);
    }

    [Fact]
    public void LedgerOperationException_infra_error_constructor_still_binds_when_the_category_is_a_null_literal()
    {
        var exception = new LedgerOperationException(
            "transport failed", new TransportStatus.Grpc(GrpcStatusCode.Unavailable), null);

        exception.CommitState.Should().Be(CommitState.Unknown);
        exception.Category.Should().BeNull();
    }

    [Fact]
    public void LedgerOperationException_infra_error_constructor_still_binds_when_given_only_named_optional_arguments()
    {
        var exception = new LedgerOperationException(
            "transport failed",
            new TransportStatus.Grpc(GrpcStatusCode.Unavailable),
            errorId: "SOME_ERROR_ID",
            innerException: null);

        exception.CommitState.Should().Be(CommitState.Unknown);
        exception.ErrorId.Should().Be("SOME_ERROR_ID");
    }
}
