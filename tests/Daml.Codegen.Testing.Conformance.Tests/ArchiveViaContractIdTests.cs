// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Stdlib;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// Archive is synthesised by the emitter rather than declared in Daml, and it takes
/// no arguments. Canton's gRPC preprocessor rejects a Unit choice argument, so the
/// synthesised choice has to encode an empty record instead. These tests pin that
/// encoding on the corpus's own generated types, for both the template-typed and
/// the interface-typed contract-id path.
/// </summary>
public class ArchiveViaContractIdTests
{
    private static readonly ContractId<Marker> MarkerCid = new("marker-cid");
    private static readonly ContractId<IHolding> HoldingCid = new("holding-cid");

    private static readonly Identifier ConcreteHoldingTemplateId = new("impl-pkg-id", "Impl.Holding", "Holding");

    private static TransactionResult ArchivingTransaction(string contractId, Identifier templateId, Identifier? interfaceId = null) =>
        new(
            UpdateId: "upd-1",
            CompletionOffset: LedgerOffset.At(1),
            CreatedContracts: [],
            ArchivedContractIds: [contractId],
            CommandId: default)
        {
            ExercisedEvents =
            [
                new ExercisedEvent(
                    ContractId: contractId,
                    TemplateId: templateId,
                    InterfaceId: interfaceId,
                    ChoiceName: new ChoiceName("Archive"),
                    ChoiceArgument: DamlRecord.Create(),
                    ExerciseResult: DamlUnit.Instance,
                    Consuming: true,
                    ActingParties: [],
                    WitnessParties: []),
            ],
        };

    [Fact]
    public void ArchiveCommand_on_a_template_contract_id_encodes_an_empty_record_not_unit()
    {
        var command = MarkerCid.ArchiveCommand();

        command.ChoiceArgument.Should().BeOfType<DamlRecord>().Subject.Fields.Should().BeEmpty();
        command.ChoiceArgument.Should().NotBeOfType<DamlUnit>();
        command.Choice.Should().Be(new ChoiceName("Archive"));
        command.TemplateId.Should().Be(Marker.TemplateId);
    }

    [Fact]
    public void ArchiveCommand_on_an_interface_contract_id_encodes_an_empty_record_not_unit()
    {
        var command = HoldingCid.ArchiveCommand();

        command.ChoiceArgument.Should().BeOfType<DamlRecord>().Subject.Fields.Should().BeEmpty();
        command.ChoiceArgument.Should().NotBeOfType<DamlUnit>();
        command.TemplateId.Should().Be(IHolding.InterfaceId);
    }

    [Fact]
    public void ChoiceArchive_descriptor_encodes_an_empty_record_not_unit()
    {
        Marker.ChoiceArchive.ArgumentEncoder(DamlUnit.Instance)
            .Should().BeOfType<DamlRecord>().Subject.Fields.Should().BeEmpty();
        TypeCorners.ChoiceArchive.ArgumentEncoder(DamlUnit.Instance)
            .Should().BeOfType<DamlRecord>().Subject.Fields.Should().BeEmpty();
    }

    [Fact]
    public async Task TryArchiveAsync_submits_the_empty_record_argument_for_the_target_contract_id()
    {
        using var client = new FakeLedgerClient(
            _ => new ExerciseOutcome<TransactionResult>.One(ArchivingTransaction("marker-cid", Marker.TemplateId)));

        await MarkerCid.TryArchiveAsync(client, new Party("alice"),
            cancellationToken: TestContext.Current.CancellationToken);

        var command = client.LastSubmission!.Commands.Should().ContainSingle().Which
            .Should().BeOfType<ExerciseCommand>().Subject;
        command.ContractId.Value.Should().Be("marker-cid");
        command.Choice.Should().Be(new ChoiceName("Archive"));
        command.ChoiceArgument.Should().BeOfType<DamlRecord>().Subject.Fields.Should().BeEmpty();
    }

    [Fact]
    public async Task TryArchiveAsync_on_an_interface_contract_id_targets_the_interface_id()
    {
        using var client = new FakeLedgerClient(
            _ => new ExerciseOutcome<TransactionResult>.One(
                ArchivingTransaction("holding-cid", ConcreteHoldingTemplateId, IHolding.InterfaceId)));

        var outcome = await HoldingCid.TryArchiveAsync(client, new Party("alice"),
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<DamlUnit>.One>();
        var command = client.LastSubmission!.Commands.Should().ContainSingle().Which
            .Should().BeOfType<ExerciseCommand>().Subject;
        command.TemplateId.Should().Be(IHolding.InterfaceId);
        command.ChoiceArgument.Should().BeOfType<DamlRecord>().Subject.Fields.Should().BeEmpty();
    }
}
