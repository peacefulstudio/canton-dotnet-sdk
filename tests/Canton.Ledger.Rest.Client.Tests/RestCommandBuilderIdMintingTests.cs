// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Data;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestCommandBuilderIdMintingTests
{
    private static readonly Party Alice = new("party::alice");
    private static readonly SynchronizerId Source = new("sync::source");
    private static readonly SynchronizerId Target = new("sync::target");

    private readonly List<string> _mintedIds = [];

    private string MintNext()
    {
        var id = $"minted-{_mintedIds.Count + 1}";
        _mintedIds.Add(id);
        return id;
    }

    private static RuntimeCommands.CommandsSubmission Submission() =>
        RuntimeCommands.CommandsSubmission.Single(
                new RuntimeCommands.CreateCommand(new Identifier("pkg", "Module", "Template"), new DamlRecord(null, [])))
            .WithActAs(Alice);

    private static ReassignmentSubmission Reassignment() =>
        ReassignmentSubmission.Of(new AssignCommand("reassign-1", Source, Target), Alice);

    [Fact]
    public void BuildCommands_mints_exactly_one_command_id_when_the_submission_carries_none()
    {
        var commands = RestCommandBuilder.BuildCommands(Submission(), userId: null, MintNext);

        commands.CommandId.Should().Be("minted-1");
        _mintedIds.Should().Equal("minted-1");
    }

    [Fact]
    public void BuildCommands_mints_nothing_when_the_submission_pins_its_command_id()
    {
        var commands = RestCommandBuilder.BuildCommands(
            Submission().WithCommandId(new RuntimeCommands.CommandId("pinned")), userId: null, MintNext);

        commands.CommandId.Should().Be("pinned");
        _mintedIds.Should().BeEmpty();
    }

    [Fact]
    public void BuildReassignmentCommands_mints_one_command_id_then_one_submission_id_when_neither_is_pinned()
    {
        var commands = RestCommandBuilder.BuildReassignmentCommands(Reassignment(), userId: null, MintNext);

        commands.CommandId.Should().Be("minted-1");
        commands.SubmissionId.Should().Be("minted-2");
        _mintedIds.Should().Equal("minted-1", "minted-2");
    }

    [Fact]
    public void BuildReassignmentCommands_mints_nothing_when_both_ids_are_pinned()
    {
        var commands = RestCommandBuilder.BuildReassignmentCommands(
            Reassignment().WithCommandId(new RuntimeCommands.CommandId("pinned-cmd")).WithSubmissionId("pinned-sub"),
            userId: null,
            MintNext);

        commands.CommandId.Should().Be("pinned-cmd");
        commands.SubmissionId.Should().Be("pinned-sub");
        _mintedIds.Should().BeEmpty();
    }
}
