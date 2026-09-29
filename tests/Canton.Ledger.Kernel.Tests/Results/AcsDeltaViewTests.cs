// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Results;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Results;

public class AcsDeltaViewTests
{
    private static readonly Identifier TemplateId = new("pkg", "Module", "Template");

    private static readonly IReadOnlySet<string> NoContracts = new HashSet<string>();

    [Fact]
    public void Of_keeps_only_the_created_contracts_flagged_as_acs_delta()
    {
        var effects = Effects(
            created: [Created("00kept"), Created("00transient"), Created("00witnessed")],
            exercised: []);

        var view = AcsDeltaView.Of(effects, createdInAcsDelta: Ids("00kept"), exercisedInAcsDelta: NoContracts);

        view.CreatedContracts.Select(c => c.ContractId).Should().Equal("00kept");
    }

    [Fact]
    public void Of_reports_a_consuming_exercise_flagged_as_acs_delta_as_archived()
    {
        var effects = Effects(
            created: [],
            exercised: [Exercise("00earlier", consuming: true)]);

        var view = AcsDeltaView.Of(effects, createdInAcsDelta: NoContracts, exercisedInAcsDelta: Ids("00earlier"));

        view.ArchivedContractIds.Should().Equal("00earlier");
    }

    [Fact]
    public void Of_omits_a_consuming_exercise_that_is_not_flagged_as_acs_delta_from_the_archived_ids()
    {
        var effects = Effects(
            created: [],
            exercised: [Exercise("00transient", consuming: true), Exercise("00witnessed", consuming: true)]);

        var view = AcsDeltaView.Of(effects, createdInAcsDelta: NoContracts, exercisedInAcsDelta: NoContracts);

        view.ArchivedContractIds.Should().BeEmpty();
    }

    [Fact]
    public void Of_does_not_archive_the_target_of_a_nonconsuming_exercise()
    {
        var effects = Effects(
            created: [Created("00kept")],
            exercised: [Exercise("00target", consuming: false)]);

        var view = AcsDeltaView.Of(effects, createdInAcsDelta: Ids("00kept"), exercisedInAcsDelta: Ids("00target"));

        view.ArchivedContractIds.Should().BeEmpty();
        view.CreatedContracts.Select(c => c.ContractId).Should().Equal("00kept");
    }

    [Fact]
    public void Of_keeps_every_exercised_event_in_transaction_order()
    {
        var effects = Effects(
            created: [Created("00transient")],
            exercised:
            [
                Exercise("00first", consuming: false),
                Exercise("00transient", consuming: true),
                Exercise("00last", consuming: true),
            ]);

        var view = AcsDeltaView.Of(effects, createdInAcsDelta: NoContracts, exercisedInAcsDelta: Ids("00last"));

        view.ExercisedEvents.Select(e => e.ContractId).Should().Equal("00first", "00transient", "00last");
    }

    [Fact]
    public void Of_reports_each_archived_contract_once()
    {
        var effects = Effects(
            created: [],
            exercised: [Exercise("00earlier", consuming: true), Exercise("00earlier", consuming: true)]);

        var view = AcsDeltaView.Of(effects, createdInAcsDelta: NoContracts, exercisedInAcsDelta: Ids("00earlier"));

        view.ArchivedContractIds.Should().Equal("00earlier");
    }

    [Fact]
    public void Of_preserves_the_transaction_identity()
    {
        var view = AcsDeltaView.Of(
            Effects(created: [], exercised: []), createdInAcsDelta: NoContracts, exercisedInAcsDelta: NoContracts);

        view.UpdateId.Should().Be("u-effects");
        view.CompletionOffset.Should().Be(LedgerOffset.At(7));
        view.CommandId.Should().Be(new CommandId("cmd-1"));
    }

    private static IReadOnlySet<string> Ids(params string[] contractIds) => contractIds.ToHashSet(StringComparer.Ordinal);

    private static CreatedContract Created(string contractId) =>
        new("0", contractId, TemplateId, DamlRecord.Create(), [], [], [], ContractKey: null);

    private static ExercisedEvent Exercise(string contractId, bool consuming) =>
        new(
            contractId, TemplateId, InterfaceId: null, new ChoiceName("Choice"),
            DamlUnit.Instance, DamlUnit.Instance, consuming, [], []);

    private static TransactionResult Effects(CreatedContract[] created, ExercisedEvent[] exercised) =>
        new(
            UpdateId: "u-effects",
            CompletionOffset: LedgerOffset.At(7),
            CreatedContracts: EquatableArray.Create(created),
            ArchivedContractIds: [],
            CommandId: new CommandId("cmd-1"))
        {
            ExercisedEvents = EquatableArray.Create(exercised),
        };
}
