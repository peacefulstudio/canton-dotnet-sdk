// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class ExerciseOutcomeProjectionTests
{
    private static readonly TransactionResult SampleTransaction =
        new("u1", LedgerOffset.At(1), [], [], default);

    [Fact]
    public void ProjectCommitted_invokes_projector_on_the_committed_transaction_and_returns_its_result()
    {
        TransactionResult? seen = null;
        var projected = new ExerciseOutcome<int>.One(7);
        var outcome = new ExerciseOutcome<TransactionResult>.One(SampleTransaction);

        var result = outcome.ProjectCommitted<int>(tx =>
        {
            seen = tx;
            return projected;
        });

        seen.Should().BeSameAs(SampleTransaction);
        result.Should().BeSameAs(projected);
    }

    public static TheoryData<string, ExerciseOutcome<TransactionResult>> OutcomesCarryingACommandId() => new()
    {
        { "One", new ExerciseOutcome<TransactionResult>.One(SampleTransaction) { CommandId = "cmd-9" } },
        { "None", new ExerciseOutcome<TransactionResult>.None { CommandId = "cmd-9" } },
        { "Many", new ExerciseOutcome<TransactionResult>.Many(EquatableArray.Create(["00a", "00b"])) { CommandId = "cmd-9" } },
        {
            "DamlError",
            new ExerciseOutcome<TransactionResult>.DamlError(
                DamlErrorCategory.ContentionOnSharedResources, "CONTRACT_NOT_FOUND", "gone", new Dictionary<string, string>())
            {
                CommandId = "cmd-9",
            }
        },
        {
            "InfraError",
            new ExerciseOutcome<TransactionResult>.InfraError(new TransportStatus.NoResponse(), "reset") { CommandId = "cmd-9" }
        },
        {
            "CommittedUndecodable",
            new ExerciseOutcome<TransactionResult>.CommittedUndecodable("u1", "bad") { CommandId = "cmd-9" }
        },
    };

    [Theory]
    [MemberData(nameof(OutcomesCarryingACommandId))]
    public void ProjectCommitted_keeps_the_command_id_of_the_outcome_it_projects(
        string arm, ExerciseOutcome<TransactionResult> outcome)
    {
        var result = outcome.ProjectCommitted<int>(_ => new ExerciseOutcome<int>.One(7));

        result.CommandId.Should().Be("cmd-9", because: arm);
    }

    [Fact]
    public void ProjectCommitted_stamps_the_command_id_on_a_projector_result_that_has_none()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.One(SampleTransaction) { CommandId = "cmd-9" };

        var result = outcome.ProjectCommitted<int>(_ => new ExerciseOutcome<int>.CommittedUndecodable("u1", "bad"));

        result.Should().Be(new ExerciseOutcome<int>.CommittedUndecodable("u1", "bad") { CommandId = "cmd-9" });
    }

    [Fact]
    public void ProjectCommitted_passes_through_a_projector_None_result()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.One(SampleTransaction);

        var result = outcome.ProjectCommitted<int>(_ => new ExerciseOutcome<int>.None());

        result.Should().BeOfType<ExerciseOutcome<int>.None>();
    }

    [Fact]
    public void ProjectCommitted_maps_writer_None_without_invoking_the_projector()
    {
        var projectorInvoked = false;
        var outcome = new ExerciseOutcome<TransactionResult>.None();

        var result = outcome.ProjectCommitted<int>(_ =>
        {
            projectorInvoked = true;
            return new ExerciseOutcome<int>.One(0);
        });

        projectorInvoked.Should().BeFalse();
        result.Should().BeOfType<ExerciseOutcome<int>.None>();
    }

    [Fact]
    public void ProjectCommitted_maps_writer_Many_preserving_count_and_contract_ids_without_invoking_the_projector()
    {
        var projectorInvoked = false;
        var ids = new[] { "00a", "00b" };
        var outcome = new ExerciseOutcome<TransactionResult>.Many(EquatableArray.Create(ids));

        var result = outcome.ProjectCommitted<int>(_ =>
        {
            projectorInvoked = true;
            return new ExerciseOutcome<int>.One(0);
        });

        projectorInvoked.Should().BeFalse();
        var many = result.Should().BeOfType<ExerciseOutcome<int>.Many>().Subject;
        many.Count.Should().Be(2);
        many.ContractIds.Should().Equal(ids);
    }

    [Fact]
    public void ProjectCommitted_maps_DamlError_preserving_every_field()
    {
        var metadata = new Dictionary<string, string> { ["category"] = "ContentionOnSharedResources" };
        var outcome = new ExerciseOutcome<TransactionResult>.DamlError(
            DamlErrorCategory.ContentionOnSharedResources,
            "CONTRACT_NOT_FOUND",
            "contract not found",
            metadata);

        var result = outcome.ProjectCommitted<int>(_ => new ExerciseOutcome<int>.One(0));

        var damlError = result.Should().BeOfType<ExerciseOutcome<int>.DamlError>().Subject;
        damlError.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
        damlError.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
        damlError.Message.Should().Be("contract not found");
        damlError.Metadata.Should().Equal(metadata);
    }

    [Fact]
    public void ProjectCommitted_maps_InfraError_preserving_status_code_message_and_source_exception()
    {
        var sourceException = new InvalidOperationException("transport failed");
        var outcome = new ExerciseOutcome<TransactionResult>.InfraError(
            new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "network down", SourceException: sourceException);

        var result = outcome.ProjectCommitted<int>(_ => new ExerciseOutcome<int>.One(0));

        var infraError = result.Should().BeOfType<ExerciseOutcome<int>.InfraError>().Subject;
        infraError.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        infraError.Message.Should().Be("network down");
        infraError.SourceException.Should().BeSameAs(sourceException);
    }

    [Fact]
    public void ProjectCommitted_carries_the_InfraError_category_across_the_projection()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.InfraError(
            new TransportStatus.Http(HttpStatusCode.BadRequest), "bad request", DamlErrorCategory.InvalidIndependentOfSystemState);

        var result = outcome.ProjectCommitted<int>(_ => new ExerciseOutcome<int>.One(0));

        result.Should().BeOfType<ExerciseOutcome<int>.InfraError>()
            .Which.Category.Should().Be(
                DamlErrorCategory.InvalidIndependentOfSystemState,
                "a projection that re-wraps the outcome without forwarding the category silently discards a " +
                "classification the transport determined without a structured Canton error to carry it");
    }

    [Fact]
    public void ProjectCommitted_maps_CommittedUndecodable_preserving_update_id_message_and_source_exception()
    {
        var sourceException = new InvalidOperationException("decode failed");
        var outcome = new ExerciseOutcome<TransactionResult>.CommittedUndecodable(
            "u1", "could not decode result", sourceException);

        var result = outcome.ProjectCommitted<int>(_ => new ExerciseOutcome<int>.One(0));

        var committedUndecodable = result.Should().BeOfType<ExerciseOutcome<int>.CommittedUndecodable>().Subject;
        committedUndecodable.UpdateId.Should().Be("u1");
        committedUndecodable.Message.Should().Be("could not decode result");
        committedUndecodable.SourceException.Should().BeSameAs(sourceException);
    }

    [Fact]
    public void ProjectCommitted_throws_when_projector_is_null()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.One(SampleTransaction);

        var act = () => outcome.ProjectCommitted<int>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ProjectCommitted_throws_when_outcome_is_null()
    {
        var act = () => ((ExerciseOutcome<TransactionResult>)null!).ProjectCommitted<int>(_ => new ExerciseOutcome<int>.One(0));

        act.Should().Throw<ArgumentNullException>();
    }
}
