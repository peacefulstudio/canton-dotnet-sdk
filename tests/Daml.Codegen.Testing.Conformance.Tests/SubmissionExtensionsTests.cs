// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Xunit;
using Daml.Runtime.Stdlib;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class SubmissionExtensionsTests
{
    private const string AlicePartyId = "alice";

    [Fact]
    public async Task Marker_create_async_projects_a_created_contract_id()
    {
        using var client = new FakeLedgerClient(
            create: _ => new ExerciseOutcome<object>.One("marker-cid"));
        var payload = new Marker(new Party(AlicePartyId));

        var outcome = await client.TryCreateAsync(payload, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<Marker>>.One>();
        ((ExerciseOutcome<ContractId<Marker>>.One)outcome).Result.Value.Should().Be("marker-cid");
    }

    [Fact]
    public async Task Marker_create_async_submits_as_the_owner_carried_by_the_payload()
    {
        using var client = new FakeLedgerClient(
            create: _ => new ExerciseOutcome<object>.One("marker-cid"));
        var payload = new Marker(new Party(AlicePartyId));

        await client.TryCreateAsync(payload, cancellationToken: TestContext.Current.CancellationToken);

        client.LastCreateSubmitter.Should().NotBeNull(
            "the payload-derived overload takes no submitter argument, so the wrapper is the only thing that can supply one");
        client.LastCreateSubmitter!.Value.ActAs.Should().ContainSingle().Which.Should().Be(
            new Party(AlicePartyId),
            "the sole Daml signatory is the payload's owner field, so the wrapper must act as that party and no other");
    }

    [Fact]
    public async Task Marker_create_async_throws_on_null_payload()
    {
        using var client = new FakeLedgerClient();

        var act = async () => await client.TryCreateAsync((Marker)null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RichRecord_create_async_projects_a_created_contract_id()
    {
        using var client = new FakeLedgerClient(
            create: _ => new ExerciseOutcome<object>.One("rich-cid"));
        var payload = new RichRecord(
            Owner: new Party(AlicePartyId),
            Count: 1,
            Amount: 1m,
            Label: "l",
            Active: false,
            AsOf: new DateOnly(2026, 1, 1),
            ObservedAt: DateTimeOffset.UnixEpoch,
            Note: null,
            NestedNote: new Optional<Optional<string>>.None(),
            Tags: new List<string>(),
            Attributes: new Dictionary<string, string>(),
            GenMap: new Dictionary<Party, long>(),
            UnitField: DamlUnit.Instance,
            Marker: new ContractId<Marker>("m"),
            HoldingCid: new ContractId<IHolding>("00h"),
            HoldingCids: new List<ContractId<IHolding>>(),
            Profile: new Profile("n", 0),
            Outcome: new Outcome.Pending(),
            Suit: Suit.Diamonds,
            Fee: 0m);

        var outcome = await client.TryCreateAsync(payload, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<RichRecord>>.One>();
        ((ExerciseOutcome<ContractId<RichRecord>>.One)outcome).Result.Value.Should().Be("rich-cid");
        client.LastCreateSubmitter!.Value.ActAs.Should().ContainSingle().Which.Should().Be(
            new Party(AlicePartyId),
            "a payload carrying many non-party fields must still resolve its submitter from the owner field alone");
    }

    [Fact]
    public async Task Marker_create_async_forwards_workflow_id_command_id_and_timeout_to_the_writer()
    {
        using var client = new FakeLedgerClient(
            create: _ => new ExerciseOutcome<object>.One("marker-cid"));

        await client.TryCreateAsync(
            new Marker(new Party(AlicePartyId)),
            workflowId: "wf-marker",
            commandId: new CommandId("cmd-marker"),
            timeout: TimeSpan.FromSeconds(7),
            cancellationToken: TestContext.Current.CancellationToken);

        client.LastCreateWorkflowId.Should().Be("wf-marker");
        client.LastCreateCommandId.Should().Be(new CommandId("cmd-marker"));
        client.LastCreateTimeout.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task Marker_create_async_with_configure_puts_workflow_id_and_command_id_on_the_submission_and_forwards_the_timeout()
    {
        using var client = new FakeLedgerClient();
        CommandsSubmission? seenByConfigure = null;

        await client.TryCreateAsync(
            new Marker(new Party(AlicePartyId)),
            workflowId: "wf-configured",
            commandId: new CommandId("cmd-configured"),
            timeout: TimeSpan.FromSeconds(9),
            configure: submission =>
            {
                seenByConfigure = submission;
                return submission;
            },
            cancellationToken: TestContext.Current.CancellationToken);

        seenByConfigure!.WorkflowId.Should().Be(new WorkflowId("wf-configured"));
        seenByConfigure.CommandId.Should().Be(new CommandId("cmd-configured"));
        client.LastSubmission!.WorkflowId.Should().Be(new WorkflowId("wf-configured"));
        client.LastSubmission.CommandId.Should().Be(new CommandId("cmd-configured"));
        client.LastSubmissionTimeout.Should().Be(TimeSpan.FromSeconds(9));
    }

    [Fact]
    public async Task Schedule_create_async_with_a_submitter_forwards_workflow_id_command_id_and_timeout_to_the_writer()
    {
        using var client = new FakeLedgerClient();
        var custodian = new Party("custodian");

        await client.TryCreateAsync(
            new Schedule(new Conformance.KeyBuilders.ScheduleView(custodian, "2026-Q1")),
            (SubmitterInfo)custodian,
            workflowId: "wf-schedule",
            commandId: new CommandId("cmd-schedule"),
            timeout: TimeSpan.FromSeconds(11),
            cancellationToken: TestContext.Current.CancellationToken);

        client.LastCreateSubmitter!.Value.ActAs.Should().ContainSingle().Which.Should().Be(custodian);
        client.LastCreateWorkflowId.Should().Be("wf-schedule");
        client.LastCreateCommandId.Should().Be(new CommandId("cmd-schedule"));
        client.LastCreateTimeout.Should().Be(TimeSpan.FromSeconds(11));
    }

    [Fact]
    public async Task Marker_create_async_without_the_submission_parameters_forwards_null_for_each()
    {
        using var client = new FakeLedgerClient();

        await client.TryCreateAsync(new Marker(new Party(AlicePartyId)), cancellationToken: TestContext.Current.CancellationToken);

        client.LastCreateWorkflowId.Should().BeNull();
        client.LastCreateCommandId.Should().BeNull();
        client.LastCreateTimeout.Should().BeNull();
    }
}
