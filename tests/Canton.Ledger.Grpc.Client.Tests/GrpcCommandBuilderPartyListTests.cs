// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Data;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client.Tests;

public class GrpcCommandBuilderPartyListTests
{
    private static readonly Party Alice = new("party::alice");
    private static readonly Party Bob = new("party::bob");

    private static GrpcCommandBuilder Builder() =>
        new(new LedgerClientOptions { GrpcAddress = "https://localhost:5001", UserId = "test-user" });

    private static RuntimeCommands.CommandsSubmission Submission() =>
        RuntimeCommands.CommandsSubmission.Single(
            new RuntimeCommands.CreateCommand(
                new RuntimeIdentifier("pkg", "Module", "Template"), new DamlRecord(null, [])));

    [Fact]
    public void BuildCommands_leaves_act_as_and_read_as_empty_when_the_submission_names_no_parties()
    {
        var commands = Builder().BuildCommands(Submission());

        commands.ActAs.Should().BeEmpty();
        commands.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public void BuildCommands_leaves_act_as_empty_for_an_explicitly_empty_party_list()
    {
        var commands = Builder().BuildCommands(Submission().WithActAs());

        commands.ActAs.Should().BeEmpty();
    }

    [Fact]
    public void BuildCommands_forwards_duplicate_act_as_parties_unchanged_and_in_order()
    {
        var commands = Builder().BuildCommands(Submission().WithActAs(Alice, Bob, Alice));

        commands.ActAs.Should().Equal("party::alice", "party::bob", "party::alice");
    }

    [Fact]
    public void BuildCommands_forwards_duplicate_read_as_parties_unchanged_and_in_order()
    {
        var commands = Builder().BuildCommands(Submission().WithActAs(Alice).WithReadAs(Bob, Bob));

        commands.ReadAs.Should().Equal("party::bob", "party::bob");
    }

    [Fact]
    public void BuildCommands_keeps_a_party_listed_in_both_act_as_and_read_as_in_each_list()
    {
        var commands = Builder().BuildCommands(Submission().WithActAs(Alice).WithReadAs(Alice));

        commands.ActAs.Should().Equal("party::alice");
        commands.ReadAs.Should().Equal("party::alice");
    }

    [Fact]
    public void BuildCommands_leaves_the_submission_id_unset_on_the_ordinary_path()
    {
        var commands = Builder().BuildCommands(Submission().WithActAs(Alice));

        commands.SubmissionId.Should().BeEmpty();
    }

    [Fact]
    public void BuildCommands_does_not_mint_a_submission_id_across_repeated_builds()
    {
        var submission = Submission().WithActAs(Alice);

        var first = Builder().BuildCommands(submission);
        var second = Builder().BuildCommands(submission);

        first.SubmissionId.Should().BeEmpty();
        second.SubmissionId.Should().BeEmpty();
    }
}
