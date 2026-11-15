// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Testing.Helpers;
using Daml.Runtime.Outcomes;
using Xunit;
using Xunit.Sdk;

namespace Canton.Ledger.Grpc.Client.Tests;

public class ExerciseOutcomeAssertTests
{
    [Fact]
    public void IsOne_returns_the_one_case_with_its_result()
    {
        ExerciseOutcome<string> outcome = new ExerciseOutcome<string>.One("created-42");

        var one = ExerciseOutcomeAssert.IsOne(outcome);

        one.Result.Should().Be("created-42");
    }

    [Fact]
    public void IsOne_names_the_infra_error_status_and_message_when_the_write_timed_out()
    {
        ExerciseOutcome<string> outcome = new ExerciseOutcome<string>.InfraError(
            new TransportStatus.Grpc(GrpcStatusCode.DeadlineExceeded),
            "deadline exceeded after 30s");

        var failure = Assert.Throws<XunitException>(() => ExerciseOutcomeAssert.IsOne(outcome));

        failure.Message.Should().Contain("InfraError")
            .And.Contain("DeadlineExceeded")
            .And.Contain("deadline exceeded after 30s");
    }

    [Fact]
    public void IsOne_names_the_daml_error_category_id_message_and_metadata()
    {
        ExerciseOutcome<string> outcome = new ExerciseOutcome<string>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "Contract could not be found",
            new Dictionary<string, string> { ["contract_id"] = "00abc" });

        var failure = Assert.Throws<XunitException>(() => ExerciseOutcomeAssert.IsOne(outcome));

        failure.Message.Should().Contain("DamlError")
            .And.Contain("InvalidGivenCurrentSystemStateResourceMissing")
            .And.Contain("CONTRACT_NOT_FOUND")
            .And.Contain("Contract could not be found")
            .And.Contain("contract_id=00abc");
    }

    [Fact]
    public void IsOne_names_the_none_case()
    {
        ExerciseOutcome<string> outcome = new ExerciseOutcome<string>.None();

        var failure = Assert.Throws<XunitException>(() => ExerciseOutcomeAssert.IsOne(outcome));

        failure.Message.Should().Contain("None");
    }

    [Fact]
    public void IsOne_lists_the_competing_contract_ids_of_the_many_case()
    {
        ExerciseOutcome<string> outcome = new ExerciseOutcome<string>.Many(["00aa", "00bb"]);

        var failure = Assert.Throws<XunitException>(() => ExerciseOutcomeAssert.IsOne(outcome));

        failure.Message.Should().Contain("Many").And.Contain("00aa").And.Contain("00bb");
    }

    [Fact]
    public void IsOne_names_the_update_id_and_message_of_the_committed_undecodable_case()
    {
        ExerciseOutcome<string> outcome = new ExerciseOutcome<string>.CommittedUndecodable(
            "update-7",
            "result did not decode");

        var failure = Assert.Throws<XunitException>(() => ExerciseOutcomeAssert.IsOne(outcome));

        failure.Message.Should().Contain("CommittedUndecodable")
            .And.Contain("update-7")
            .And.Contain("result did not decode");
    }

    [Fact]
    public void IsOne_includes_the_source_exception_of_an_infra_error()
    {
        ExerciseOutcome<string> outcome = new ExerciseOutcome<string>.InfraError(
            new TransportStatus.NoResponse(),
            "no response",
            SourceException: new InvalidOperationException("socket closed"));

        var failure = Assert.Throws<XunitException>(() => ExerciseOutcomeAssert.IsOne(outcome));

        failure.Message.Should().Contain("socket closed");
    }
}
