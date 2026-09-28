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
}
