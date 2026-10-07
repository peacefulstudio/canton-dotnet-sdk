// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Streams;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Streams;

public class ContractArmsTests
{
    private static readonly ContractId<TemplateMarker> Id = new("00cid");
    private static readonly TemplateMarker Payload = new((Party)"alice");
    private static readonly ContractKey Key = new(new DamlText("key-1"));
    private static readonly SynchronizerId Source = new("source::sync");
    private static readonly SynchronizerId Target = new("target::sync");
    private static readonly EquatableArray<Party> Witnesses = EquatableArray.Create([(Party)"alice", (Party)"bob"]);

    [Fact]
    public void Created_carries_every_field_into_the_created_arm()
    {
        var projected = ContractArms<TemplateMarker>.Created(Id, Payload, Key, LedgerOffset.At(7), Target, Witnesses);

        projected.Should().Be(new ContractStreamEvent<TemplateMarker>.Created(
            Id, Payload, Key, LedgerOffset.At(7), Target, Witnesses));
    }

    [Fact]
    public void Archived_carries_every_field_into_the_archived_arm()
    {
        var projected = ContractArms<TemplateMarker>.Archived(Id, LedgerOffset.At(8), Target, Witnesses);

        projected.Should().Be(new ContractStreamEvent<TemplateMarker>.Archived(
            Id, LedgerOffset.At(8), Target, Witnesses));
    }

    [Fact]
    public void Exercised_keeps_the_choice_argument_apart_from_the_exercise_result()
    {
        var projected = ContractArms<TemplateMarker>.Exercised(
            Id, new ChoiceName("Transfer"), new DamlText("the-argument"), new DamlText("the-result"),
            consuming: true, LedgerOffset.At(9), Target, Witnesses);

        var exercised = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Exercised>().Subject;
        exercised.ChoiceArgument.Should().Be(new DamlText("the-argument"));
        exercised.ExerciseResult.Should().Be(new DamlText("the-result"));
        exercised.ChoiceName.Should().Be(new ChoiceName("Transfer"));
        exercised.Consuming.Should().BeTrue();
        exercised.ContractId.Should().Be(Id);
        exercised.Offset.Should().Be(LedgerOffset.At(9));
        exercised.SynchronizerId.Should().Be(Target);
        exercised.WitnessParties.Should().Equal((Party)"alice", (Party)"bob");
    }

    [Fact]
    public void Assigned_keeps_the_source_apart_from_the_target()
    {
        var projected = ContractArms<TemplateMarker>.Assigned(
            Id, Payload, Key, LedgerOffset.At(10), Source, Target, "reassign-1", 3, Witnesses);

        var assigned = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Assigned>().Subject;
        assigned.Source.Should().Be(Source);
        assigned.Target.Should().Be(Target);
        assigned.ContractId.Should().Be(Id);
        assigned.Payload.Should().Be(Payload);
        assigned.Key.Should().Be(Key);
        assigned.Offset.Should().Be(LedgerOffset.At(10));
        assigned.ReassignmentId.Should().Be("reassign-1");
        assigned.ReassignmentCounter.Should().Be(3);
        assigned.WitnessParties.Should().Equal((Party)"alice", (Party)"bob");
    }

    [Fact]
    public void Unassigned_keeps_the_source_apart_from_the_target()
    {
        var projected = ContractArms<TemplateMarker>.Unassigned(
            Id, LedgerOffset.At(11), Source, Target, "reassign-2", 4, Witnesses);

        var unassigned = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unassigned>().Subject;
        unassigned.Source.Should().Be(Source);
        unassigned.Target.Should().Be(Target);
        unassigned.ContractId.Should().Be(Id);
        unassigned.Offset.Should().Be(LedgerOffset.At(11));
        unassigned.ReassignmentId.Should().Be("reassign-2");
        unassigned.ReassignmentCounter.Should().Be(4);
        unassigned.WitnessParties.Should().Equal((Party)"alice", (Party)"bob");
    }

    [Fact]
    public void Unclassified_carries_the_offset_kind_and_raw_kind()
    {
        var projected = ContractArms<TemplateMarker>.Unclassified(LedgerOffset.At(12), UnclassifiedKind.Unknown, "Mystery");

        projected.Should().Be(new ContractStreamEvent<TemplateMarker>.Unclassified(
            LedgerOffset.At(12), UnclassifiedKind.Unknown, "Mystery"));
    }

    [Fact]
    public void Unclassified_without_an_offset_stays_without_one()
    {
        var projected = ContractArms<TemplateMarker>.Unclassified(null, UnclassifiedKind.MissingSynchronizerId, null);

        var unclassified = projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Unclassified>().Subject;
        unclassified.Offset.Should().BeNull();
        unclassified.Kind.Should().Be(UnclassifiedKind.MissingSynchronizerId);
        unclassified.RawKind.Should().BeNull();
    }

    [Fact]
    public void Checkpoint_carries_the_offset_into_the_checkpoint_arm()
    {
        var projected = ContractArms<TemplateMarker>.Checkpoint(LedgerOffset.At(13));

        projected.Should().Be(new ContractStreamEvent<TemplateMarker>.Checkpoint(LedgerOffset.At(13)));
        projected.Should().BeOfType<ContractStreamEvent<TemplateMarker>.Checkpoint>().Which.Offset.Value.Should().Be(13);
    }
}
