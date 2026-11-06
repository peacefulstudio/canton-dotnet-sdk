// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Abstractions.Tests;

public class ExerciseOutcomeExtensionsTests
{
    private static readonly IReadOnlyDictionary<string, string> SampleMetadata =
        new Dictionary<string, string> { ["participant"] = "participant1" };

    private static readonly Identifier Agreement = new("pkg-1", "Agreements.Agreement", "Agreement");

    private static LedgerOperation Operation(string name) => new LedgerOperation.Command(Agreement, name);

    [Fact]
    public void OneOrThrow_returns_the_result_for_One()
    {
        ExerciseOutcome<int> outcome = new ExerciseOutcome<int>.One(42);

        outcome.OneOrThrow().Should().Be(42);
    }

    [Fact]
    public void OneOrThrow_throws_for_DamlError_carrying_the_structured_payload_and_the_outcome_operation()
    {
        ExerciseOutcome<int> outcome = new ExerciseOutcome<int>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "contract abc is not active",
            SampleMetadata)
        {
            Operation = Operation("Accept"),
        };

        var act = () => outcome.OneOrThrow();

        var exception = act.Should().Throw<LedgerOperationException>().Which;
        exception.Operation.Should().Be("Agreement.Accept");
        exception.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing);
        exception.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
        exception.Metadata.Should().BeEquivalentTo(SampleMetadata);
        exception.Message.Should().Be(
            "Agreement.Accept: Daml error [InvalidGivenCurrentSystemStateResourceMissing/CONTRACT_NOT_FOUND]: contract abc is not active");
    }

    [Fact]
    public void OneOrThrow_names_a_create_after_its_template()
    {
        ExerciseOutcome<int> outcome = new ExerciseOutcome<int>.None { Operation = Operation("Create") };

        var act = () => outcome.OneOrThrow();

        var exception = act.Should().Throw<LedgerOperationException>().Which;
        exception.Operation.Should().Be("Agreement.Create");
        exception.CommitState.Should().Be(CommitState.Committed);
        exception.Message.Should().Be("Agreement.Create: expected exactly one result but the operation produced none (None).");
    }

    [Fact]
    public void OneOrThrow_throws_without_an_operation_when_the_outcome_carries_none()
    {
        ExerciseOutcome<int> outcome = new ExerciseOutcome<int>.DamlError(
            DamlErrorCategory.ContentionOnSharedResources,
            "LOCAL_VERDICT_LOCKED_CONTRACTS",
            "contended",
            SampleMetadata);

        var act = () => outcome.OneOrThrow();

        var exception = act.Should().Throw<LedgerOperationException>().Which;
        exception.Operation.Should().BeNull();
        exception.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
        exception.Message.Should().Be("Daml error [ContentionOnSharedResources/LOCAL_VERDICT_LOCKED_CONTRACTS]: contended");
    }

    [Fact]
    public void OneOrThrow_reports_a_Many_without_an_operation_as_committed()
    {
        ExerciseOutcome<int> outcome = new ExerciseOutcome<int>.Many(["00aa", "00bb"]);

        var act = () => outcome.OneOrThrow();

        var exception = act.Should().Throw<LedgerOperationException>().Which;
        exception.Operation.Should().BeNull();
        exception.CommitState.Should().Be(CommitState.Committed);
        exception.Message.Should().Be("The operation produced 2 results; expected exactly one.");
    }

    [Fact]
    public void OneOrThrow_carries_the_command_id_of_the_outcome()
    {
        ExerciseOutcome<int> outcome = new ExerciseOutcome<int>.None { CommandId = "cmd-5", Operation = Operation("Accept") };

        var act = () => outcome.OneOrThrow();

        act.Should().Throw<LedgerOperationException>().Which.CommandId!.Value.Value.Should().Be("cmd-5");
    }

    [Fact]
    public void OneOrThrow_rejects_a_null_outcome()
    {
        ExerciseOutcome<int> outcome = null!;

        var act = () => outcome.OneOrThrow();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task OneOrThrowAsync_unwraps_the_awaited_outcome()
    {
        var outcomeTask = Task.FromResult<ExerciseOutcome<string>>(new ExerciseOutcome<string>.One("ok"));

        (await outcomeTask.OneOrThrowAsync()).Should().Be("ok");
    }

    [Fact]
    public async Task OneOrThrowAsync_throws_with_the_operation_of_the_awaited_outcome()
    {
        var outcomeTask = Task.FromResult<ExerciseOutcome<string>>(
            new ExerciseOutcome<string>.None { Operation = Operation("Expire") });

        var act = () => outcomeTask.OneOrThrowAsync();

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.Operation.Should().Be("Agreement.Expire");
    }

    [Fact]
    public async Task OneOrThrowAsync_rejects_a_null_outcome_task()
    {
        Task<ExerciseOutcome<int>> outcomeTask = null!;

        var act = () => outcomeTask.OneOrThrowAsync();

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void ThrowIfError_accepts_None_because_the_command_committed()
    {
        ExerciseOutcome<int> outcome = new ExerciseOutcome<int>.None { Operation = Operation("Accept") };

        var act = () => outcome.ThrowIfError();

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfError_throws_for_DamlError_with_the_outcome_operation()
    {
        ExerciseOutcome<int> outcome = new ExerciseOutcome<int>.DamlError(
            DamlErrorCategory.ContentionOnSharedResources,
            "LOCAL_VERDICT_LOCKED_CONTRACTS",
            "contended",
            SampleMetadata)
        {
            Operation = Operation("Default"),
        };

        var act = () => outcome.ThrowIfError();

        var exception = act.Should().Throw<LedgerOperationException>().Which;
        exception.Operation.Should().Be("Agreement.Default");
        exception.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
    }

    [Fact]
    public void ThrowIfError_throws_without_an_operation_when_the_outcome_carries_none()
    {
        ExerciseOutcome<int> outcome = new ExerciseOutcome<int>.InfraError(new TransportStatus.NoResponse(), "reset");

        var act = () => outcome.ThrowIfError();

        var exception = act.Should().Throw<LedgerOperationException>().Which;
        exception.Operation.Should().BeNull();
        exception.Message.Should().StartWith("Infrastructure error [").And.EndWith("]: reset");
    }

    [Fact]
    public void ThrowIfError_rejects_a_null_outcome()
    {
        ExerciseOutcome<int> outcome = null!;

        var act = () => outcome.ThrowIfError();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Operation_is_null_on_an_exception_thrown_without_operation_context()
    {
        var exception = new LedgerOperationException("no context");

        exception.Operation.Should().BeNull();
    }
}
