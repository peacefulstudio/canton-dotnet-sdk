// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing.Helpers;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client.Tests;

public class SynchronizerIdWireFormatTests
{
    private const string ThreeFourSource = "global-domain::12204457ac942c4d839331d402f82ecc941c6232de06a88097ade653350a2d6fc9c5";
    private const string ThreeFiveSource = "global-domain::12204457ac942c4d839331d402f82ecc941c6232de06a88097ade653350a2d6fc9c5::35-0";
    private const string ThreeFourTarget = "app-domain::1220b1c2d3e4f5061728394a5b6c7d8e9fa0b1c2d3e4f5061728394a5b6c7d8e9f0a";
    private const string ThreeFiveTarget = "app-domain::1220b1c2d3e4f5061728394a5b6c7d8e9fa0b1c2d3e4f5061728394a5b6c7d8e9f0a::35-0";

    private static readonly ProtoIdentifier HoldingTemplateId = new()
    {
        PackageId = "tmpl-pkg",
        ModuleName = "Sample.Token",
        EntityName = "Holding",
    };

    private static CreatedEvent HoldingCreated(long offset) => new()
    {
        ContractId = "00holding",
        TemplateId = HoldingTemplateId,
        CreateArguments = LedgerClientTestFixtures.OwnerArguments(),
        Offset = offset,
    };

    private static UnassignedEvent HoldingUnassigned(string source, string target) => new()
    {
        ContractId = "00holding",
        TemplateId = HoldingTemplateId,
        Source = source,
        Target = target,
        Offset = 70L,
    };

    [Theory]
    [InlineData(ThreeFourSource)]
    [InlineData(ThreeFiveSource)]
    public void ProjectTransactionEvents_carries_the_created_events_synchronizer_id_verbatim(string wireSynchronizerId)
    {
        var transaction = new Transaction { Offset = 42L, SynchronizerId = wireSynchronizerId };
        transaction.Events.Add(new Event { Created = HoldingCreated(42L) });

        var projected = GrpcContractStreamProjector.ProjectTransactionEvents<TemplateMarker>(transaction)
            .Should().ContainSingle().Subject;

        projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>()
            .Which.SynchronizerId.Value.Should().Be(wireSynchronizerId);
    }

    [Theory]
    [InlineData(ThreeFourSource, ThreeFourTarget)]
    [InlineData(ThreeFiveSource, ThreeFiveTarget)]
    [InlineData(ThreeFourSource, ThreeFiveTarget)]
    public void ProjectReassignmentEvents_carries_the_assigned_events_source_and_target_verbatim(string wireSource, string wireTarget)
    {
        var reassignment = new Reassignment { Offset = 60L };
        reassignment.Events.Add(new ReassignmentEvent
        {
            Assigned = new AssignedEvent
            {
                Source = wireSource,
                Target = wireTarget,
                CreatedEvent = HoldingCreated(60L),
            },
        });

        var projected = GrpcContractStreamProjector.ProjectReassignmentEvents<TemplateMarker>(reassignment)
            .Should().ContainSingle().Subject;

        var assigned = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Assigned>().Subject;
        assigned.Source.Value.Should().Be(wireSource);
        assigned.Target.Value.Should().Be(wireTarget);
    }

    [Theory]
    [InlineData(ThreeFourSource, ThreeFourTarget)]
    [InlineData(ThreeFiveSource, ThreeFiveTarget)]
    [InlineData(ThreeFourSource, ThreeFiveTarget)]
    public void ProjectReassignmentEvents_carries_the_unassigned_events_source_and_target_verbatim(string wireSource, string wireTarget)
    {
        var reassignment = new Reassignment { Offset = 70L };
        reassignment.Events.Add(new ReassignmentEvent { Unassigned = HoldingUnassigned(wireSource, wireTarget) });

        var projected = GrpcContractStreamProjector.ProjectReassignmentEvents<TemplateMarker>(reassignment)
            .Should().ContainSingle().Subject;

        var unassigned = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unassigned>().Subject;
        unassigned.Source.Value.Should().Be(wireSource);
        unassigned.Target.Value.Should().Be(wireTarget);
    }

    [Theory]
    [InlineData(ThreeFourSource)]
    [InlineData(ThreeFiveSource)]
    public void ProjectActiveContractEntry_carries_an_active_contracts_synchronizer_id_verbatim(string wireSynchronizerId)
    {
        var response = new GetActiveContractsResponse
        {
            ActiveContract = new ActiveContract
            {
                CreatedEvent = HoldingCreated(20L),
                SynchronizerId = wireSynchronizerId,
            },
        };

        var projected = GrpcContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response)
            .Should().ContainSingle().Subject;

        projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>()
            .Which.SynchronizerId.Value.Should().Be(wireSynchronizerId);
    }

    [Theory]
    [InlineData(ThreeFourSource, ThreeFourTarget)]
    [InlineData(ThreeFiveSource, ThreeFiveTarget)]
    [InlineData(ThreeFourSource, ThreeFiveTarget)]
    public void ProjectActiveContractEntry_carries_an_incomplete_unassigned_entrys_source_and_target_verbatim(string wireSource, string wireTarget)
    {
        var response = new GetActiveContractsResponse
        {
            IncompleteUnassigned = new IncompleteUnassigned
            {
                CreatedEvent = HoldingCreated(20L),
                UnassignedEvent = HoldingUnassigned(wireSource, wireTarget),
            },
        };

        var projected = GrpcContractStreamProjector.ProjectActiveContractEntry<TemplateMarker>(response).ToList();

        projected.Should().HaveCount(2);
        projected[0].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Created>()
            .Which.SynchronizerId.Value.Should().Be(wireSource);
        var unassigned = projected[1].Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unassigned>().Subject;
        unassigned.Source.Value.Should().Be(wireSource);
        unassigned.Target.Value.Should().Be(wireTarget);
    }

    [Theory]
    [InlineData(ThreeFourSource)]
    [InlineData(ThreeFiveSource)]
    public void BuildCommands_writes_the_submissions_synchronizer_id_verbatim(string synchronizerId)
    {
        var submission = RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(new RuntimeIdentifier("pkg", "Module", "Template"), new DamlRecord(null, [])))
            .WithActAs(new Party("party::alice"))
            .WithCommandId(new RuntimeCommands.CommandId("wire-format-cmd"))
            .WithSynchronizerId(new SynchronizerId(synchronizerId));

        var commands = new GrpcCommandBuilder(new LedgerClientOptions { GrpcAddress = "https://localhost:5001" })
            .BuildCommands(submission);

        commands.SynchronizerId.Should().Be(synchronizerId);
    }

    [Theory]
    [InlineData(ThreeFourSource)]
    [InlineData(ThreeFiveSource)]
    public void BuildCommands_writes_a_disclosed_contracts_synchronizer_id_verbatim(string synchronizerId)
    {
        var submission = RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(new RuntimeIdentifier("pkg", "Module", "Template"), new DamlRecord(null, [])))
            .WithActAs(new Party("party::alice"))
            .WithCommandId(new RuntimeCommands.CommandId("wire-format-cmd"))
            .WithDisclosedContracts(new RuntimeCommands.DisclosedContract(
                "00disclosed", new RuntimeIdentifier("disclosed-pkg", "Disclosed", "Contract"), new byte[] { 0x01 })
            {
                SynchronizerId = new SynchronizerId(synchronizerId),
            });

        var commands = new GrpcCommandBuilder(new LedgerClientOptions { GrpcAddress = "https://localhost:5001" })
            .BuildCommands(submission);

        commands.DisclosedContracts.Should().ContainSingle().Which.SynchronizerId.Should().Be(synchronizerId);
    }

    [Theory]
    [InlineData(ThreeFourSource, ThreeFourTarget)]
    [InlineData(ThreeFiveSource, ThreeFiveTarget)]
    [InlineData(ThreeFourSource, ThreeFiveTarget)]
    public void BuildReassignmentCommands_writes_the_unassign_source_and_target_verbatim(string wireSource, string wireTarget)
    {
        var submission = ReassignmentSubmission
            .Of(new Canton.Ledger.Abstractions.UnassignCommand("00contract", new SynchronizerId(wireSource), new SynchronizerId(wireTarget)), new Party("party::alice"))
            .WithCommandId(new RuntimeCommands.CommandId("wire-format-cmd"));

        var commands = new GrpcCommandBuilder(new LedgerClientOptions { GrpcAddress = "https://localhost:5001" })
            .BuildReassignmentCommands(submission);

        var unassign = commands.Commands.Should().ContainSingle().Subject.UnassignCommand;
        unassign.Source.Should().Be(wireSource);
        unassign.Target.Should().Be(wireTarget);
    }

    [Theory]
    [InlineData(ThreeFourSource, ThreeFourTarget)]
    [InlineData(ThreeFiveSource, ThreeFiveTarget)]
    [InlineData(ThreeFourSource, ThreeFiveTarget)]
    public void BuildReassignmentCommands_writes_the_assign_source_and_target_verbatim(string wireSource, string wireTarget)
    {
        var submission = ReassignmentSubmission
            .Of(new Canton.Ledger.Abstractions.AssignCommand("reassign-42", new SynchronizerId(wireSource), new SynchronizerId(wireTarget)), new Party("party::alice"))
            .WithCommandId(new RuntimeCommands.CommandId("wire-format-cmd"));

        var commands = new GrpcCommandBuilder(new LedgerClientOptions { GrpcAddress = "https://localhost:5001" })
            .BuildReassignmentCommands(submission);

        var assign = commands.Commands.Should().ContainSingle().Subject.AssignCommand;
        assign.Source.Should().Be(wireSource);
        assign.Target.Should().Be(wireTarget);
    }
}
