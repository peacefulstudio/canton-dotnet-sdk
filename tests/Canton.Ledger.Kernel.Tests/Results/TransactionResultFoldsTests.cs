// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Results;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Results;

public class TransactionResultFoldsTests
{
    private static readonly Identifier TemplateId = TemplateMarker.TemplateId;

    [Fact]
    public void ToCreatedContractId_reports_None_when_nothing_matches_the_marker()
    {
        var outcome = TransactionResultFolds.ToCreatedContractId<TemplateMarker>(
            Result(Created("00a"), Created("00b")), _ => false);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<TemplateMarker>>.None>();
    }

    [Fact]
    public void ToCreatedContractId_reports_the_single_match_as_a_typed_contract_id()
    {
        var outcome = TransactionResultFolds.ToCreatedContractId<TemplateMarker>(
            Result(Created("00a"), Created("00b")), created => created.ContractId == "00b");

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<TemplateMarker>>.One>()
            .Which.Result.Should().Be(new ContractId<TemplateMarker>("00b"));
    }

    [Fact]
    public void ToCreatedContractId_reports_every_match_and_its_count_when_more_than_one_matches()
    {
        var outcome = TransactionResultFolds.ToCreatedContractId<TemplateMarker>(
            Result(Created("00a"), Created("00b"), Created("00c")), _ => true);

        var many = outcome.Should().BeOfType<ExerciseOutcome<ContractId<TemplateMarker>>.Many>().Subject;
        many.Count.Should().Be(3);
        many.ContractIds.Should().Equal("00a", "00b", "00c");
    }

    [Fact]
    public void Project_hands_a_successful_outcome_to_the_success_projection()
    {
        var projected = TransactionResultFolds.Project(
            new ExerciseOutcome<TransactionResult>.One(Result(Created("00a"))),
            result => new ExerciseOutcome<int>.One(result.CreatedContracts.Count));

        projected.Should().BeOfType<ExerciseOutcome<int>.One>().Which.Result.Should().Be(1);
    }

    public static TheoryData<string, ExerciseOutcome<TransactionResult>> OutcomesCarryingACommandId() => new()
    {
        { "One", new ExerciseOutcome<TransactionResult>.One(Result(Created("00a"))) { CommandId = "cmd-4" } },
        {
            "DamlError",
            new ExerciseOutcome<TransactionResult>.DamlError(
                DamlErrorCategory.ContentionOnSharedResources, "CONTRACT_NOT_FOUND", "gone", new Dictionary<string, string>())
            {
                CommandId = "cmd-4",
            }
        },
        {
            "InfraError",
            new ExerciseOutcome<TransactionResult>.InfraError(new TransportStatus.NoResponse(), "reset") { CommandId = "cmd-4" }
        },
        {
            "CommittedUndecodable",
            new ExerciseOutcome<TransactionResult>.CommittedUndecodable("u1", "bad") { CommandId = "cmd-4" }
        },
    };

    [Theory]
    [MemberData(nameof(OutcomesCarryingACommandId))]
    public void Project_keeps_the_command_id_of_the_outcome_it_projects(string arm, ExerciseOutcome<TransactionResult> outcome)
    {
        var projected = TransactionResultFolds.Project(outcome, _ => new ExerciseOutcome<int>.One(1));

        projected.CommandId.Should().Be("cmd-4", because: arm);
    }

    [Fact]
    public void Project_carries_a_DamlError_across_unchanged()
    {
        var metadata = new Dictionary<string, string> { ["participant"] = "p1" };

        var projected = TransactionResultFolds.Project(
            new ExerciseOutcome<TransactionResult>.DamlError(
                DamlErrorCategory.InvalidGivenCurrentSystemStateOther, "SOME_ERROR", "gone", metadata),
            _ => new ExerciseOutcome<int>.One(0));

        var damlError = projected.Should().BeOfType<ExerciseOutcome<int>.DamlError>().Subject;
        damlError.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateOther);
        damlError.ErrorId.Should().Be("SOME_ERROR");
        damlError.Message.Should().Be("gone");
        damlError.Metadata.Should().BeEquivalentTo(metadata);
    }

    [Fact]
    public void Project_carries_an_InfraError_across_unchanged()
    {
        var sourceException = new InvalidOperationException("transport down");

        var projected = TransactionResultFolds.Project(
            new ExerciseOutcome<TransactionResult>.InfraError(
                new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "participant unavailable", SourceException: sourceException),
            _ => new ExerciseOutcome<int>.One(0));

        var infraError = projected.Should().BeOfType<ExerciseOutcome<int>.InfraError>().Subject;
        infraError.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        infraError.Message.Should().Be("participant unavailable");
        infraError.SourceException.Should().BeSameAs(sourceException);
    }

    [Fact]
    public void Project_carries_a_CommittedUndecodable_across_unchanged()
    {
        var sourceException = new FormatException("Cannot parse wire Int64 value 'x' as a 64-bit integer.");

        var projected = TransactionResultFolds.Project(
            new ExerciseOutcome<TransactionResult>.CommittedUndecodable(
                "update-1", "the transaction could not be decoded", sourceException),
            _ => new ExerciseOutcome<int>.One(0));

        var undecodable = projected.Should().BeOfType<ExerciseOutcome<int>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().Be("update-1");
        undecodable.Message.Should().Be("the transaction could not be decoded");
        undecodable.SourceException.Should().BeSameAs(sourceException);
    }

    [Fact]
    public void Project_rejects_a_null_outcome()
    {
        var act = () => TransactionResultFolds.Project<int>(null!, _ => new ExerciseOutcome<int>.One(0));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToChoiceResult_throws_when_the_committed_transaction_declares_no_update_id_and_no_exercise()
    {
        var result = Result() with { UpdateId = "" };
        var command = new ExerciseCommand(TemplateId, new ContractId<TemplateMarker>("00a"), new ChoiceName("Archive"), DamlUnit.Instance);

        var act = () => TransactionResultFolds.ToChoiceResult<DamlUnit>(result, command);

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().StartWith(
            "Submission succeeded but no 'Archive' exercise on contract '00a' was recorded on a transaction that declares no update id.");
    }

    [Fact]
    public void ToChoiceResult_reports_a_null_update_id_when_the_committed_transaction_declares_none_and_the_result_does_not_decode()
    {
        var exercised = new ExercisedEvent(
            "00a", TemplateId, null, new ChoiceName("GetCount"), DamlUnit.Instance, new DamlText("not a number"),
            false, [], []);
        var result = Result() with { UpdateId = "", ExercisedEvents = [exercised] };
        var command = new ExerciseCommand(TemplateId, new ContractId<TemplateMarker>("00a"), new ChoiceName("GetCount"), DamlUnit.Instance);

        var outcome = TransactionResultFolds.ToChoiceResult<long>(result, command);

        outcome.Should().BeOfType<ExerciseOutcome<long>.CommittedUndecodable>().Which.UpdateId.Should().BeNull();
    }

    [Fact]
    public void ToChoiceResult_matches_an_exercise_of_another_package_id_when_the_command_names_the_package_and_the_event_agrees()
    {
        var exercised = new ExercisedEvent(
            "00a", new Identifier("upgraded-pkg", TemplateId.ModuleName, TemplateId.EntityName), null, new ChoiceName("GetCount"),
            DamlUnit.Instance, new DamlInt64(5), false, [], [])
        {
            PackageName = "pkg-name",
        };
        var result = Result() with { ExercisedEvents = [exercised] };
        var command = new ExerciseCommand(TemplateId, new ContractId<TemplateMarker>("00a"), new ChoiceName("GetCount"), DamlUnit.Instance)
        {
            PackageName = "pkg-name",
        };

        var outcome = TransactionResultFolds.ToChoiceResult<long>(result, command);

        outcome.Should().BeOfType<ExerciseOutcome<long>.One>().Which.Result.Should().Be(5L);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-no-package-metadata")]
    [InlineData("UnknownPackage")]
    public void ToChoiceResult_matches_by_package_id_when_the_command_names_a_placeholder_package(string placeholderName)
    {
        var exercised = new ExercisedEvent(
            "00a", TemplateId, null, new ChoiceName("GetCount"), DamlUnit.Instance, new DamlInt64(5), false, [], []);
        var result = Result() with { ExercisedEvents = [exercised] };
        var command = new ExerciseCommand(TemplateId, new ContractId<TemplateMarker>("00a"), new ChoiceName("GetCount"), DamlUnit.Instance)
        {
            PackageName = placeholderName,
        };

        var outcome = TransactionResultFolds.ToChoiceResult<long>(result, command);

        outcome.Should().BeOfType<ExerciseOutcome<long>.One>().Which.Result.Should().Be(5L);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("other-name")]
    public void ToChoiceResult_does_not_match_an_exercise_whose_package_name_differs_or_is_missing_when_the_command_names_the_package(
        string? eventPackageName)
    {
        var exercised = new ExercisedEvent(
            "00a", TemplateId, null, new ChoiceName("GetCount"), DamlUnit.Instance, new DamlInt64(5), false, [], [])
        {
            PackageName = eventPackageName,
        };
        var result = Result() with { ExercisedEvents = [exercised] };
        var command = new ExerciseCommand(TemplateId, new ContractId<TemplateMarker>("00a"), new ChoiceName("GetCount"), DamlUnit.Instance)
        {
            PackageName = "pkg-name",
        };

        var act = () => TransactionResultFolds.ToChoiceResult<long>(result, command);

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().StartWith(
            "Submission succeeded but no 'GetCount' exercise on contract '00a' was recorded on transaction u1.");
    }

    private static CreatedContract Created(string contractId) =>
        new("0", contractId, TemplateId, DamlRecord.Create(), [], [], [], ContractKey: null);

    private static TransactionResult Result(params CreatedContract[] created) =>
        new(
            UpdateId: "u1",
            CompletionOffset: LedgerOffset.At(1),
            CreatedContracts: EquatableArray.Create(created),
            ArchivedContractIds: [],
            CommandId: default);
}
