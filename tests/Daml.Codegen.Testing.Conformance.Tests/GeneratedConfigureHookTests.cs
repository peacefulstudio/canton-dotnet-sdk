// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class GeneratedConfigureHookTests
{
    private static readonly Party Alice = new("alice");
    private static readonly ContractId<RichRecord> RichTarget = new("rich-cid");
    private static readonly ContractId<IHolding> HoldingTarget = new("holding-cid");
    private static readonly ContractId<Marker> MarkerTarget = new("marker-cid");
    private static readonly RichRecord.Relabel RelabelArgument = new("renamed");

    private static readonly DisclosedContract Disclosed = new(
        "00disclosed",
        new Identifier("disclosed-pkg", "Disclosed", "Contract"),
        new byte[] { 0x01, 0x02, 0x03, 0xFA });

    private static readonly DeduplicationPeriod.Duration FiveMinutes = new(TimeSpan.FromMinutes(5));

    private static readonly SynchronizerId Synchronizer = new("global-domain::1220abcd");

    private static CommandsSubmission AddDisclosureDeduplicationAndSynchronizer(CommandsSubmission submission) =>
        submission
            .WithDisclosedContracts(Disclosed)
            .WithDeduplicationPeriod(FiveMinutes)
            .WithSynchronizerId(Synchronizer);

    private static void AssertReachedTheLedger(CommandsSubmission? submission)
    {
        submission.Should().NotBeNull();
        submission!.DisclosedContracts.Should().ContainSingle().Which.Should().Be(Disclosed);
        submission.DeduplicationPeriod.Should().Be(FiveMinutes);
        submission.SynchronizerId.Should().Be(Synchronizer);
    }

    private static void AssertNothingWasAdded(CommandsSubmission? submission)
    {
        submission.Should().NotBeNull();
        submission!.DisclosedContracts.Should().BeNull();
        submission.DeduplicationPeriod.Should().BeNull();
        submission.SynchronizerId.Should().BeNull();
        submission.MinLedgerTime.Should().BeNull();
    }

    [Fact]
    public async Task Exerciser_with_a_party_submitter_forwards_configure_to_the_submission()
    {
        using var client = new FakeLedgerClient();

        await RichTarget.TryRelabelAsync(client, RelabelArgument, Alice,
            configure: AddDisclosureDeduplicationAndSynchronizer,
            cancellationToken: TestContext.Current.CancellationToken);

        AssertReachedTheLedger(client.LastSubmission);
    }

    [Fact]
    public async Task Exerciser_with_a_submitter_info_applies_configure_to_the_submission()
    {
        using var client = new FakeLedgerClient();

        await RichTarget.TryRelabelAsync(client, RelabelArgument, (SubmitterInfo)Alice,
            configure: AddDisclosureDeduplicationAndSynchronizer,
            cancellationToken: TestContext.Current.CancellationToken);

        AssertReachedTheLedger(client.LastSubmission);
    }

    [Fact]
    public async Task Exerciser_without_configure_adds_nothing_to_the_submission()
    {
        using var client = new FakeLedgerClient();

        await RichTarget.TryRelabelAsync(client, RelabelArgument, Alice,
            cancellationToken: TestContext.Current.CancellationToken);

        AssertNothingWasAdded(client.LastSubmission);
    }

    [Fact]
    public async Task Exerciser_with_explicit_null_configure_adds_nothing_to_the_submission()
    {
        using var client = new FakeLedgerClient();

        await RichTarget.TryRelabelAsync(client, RelabelArgument, Alice,
            configure: null,
            cancellationToken: TestContext.Current.CancellationToken);

        AssertNothingWasAdded(client.LastSubmission);
    }

    [Fact]
    public async Task Exerciser_configure_sees_the_workflow_id_and_command_id_the_caller_supplied()
    {
        using var client = new FakeLedgerClient();
        CommandsSubmission? seen = null;

        await RichTarget.TryRelabelAsync(client, RelabelArgument, Alice,
            workflowId: "wf-9",
            commandId: new CommandId("cmd-9"),
            configure: submission => seen = submission,
            cancellationToken: TestContext.Current.CancellationToken);

        seen.Should().NotBeNull();
        seen!.WorkflowId.Should().Be(new WorkflowId("wf-9"));
        seen.CommandId.Should().Be(new CommandId("cmd-9"));
    }

    [Fact]
    public async Task Exerciser_configure_that_replaces_the_command_is_rejected_before_submitting()
    {
        using var client = new FakeLedgerClient();

        var act = async () => await RichTarget.TryRelabelAsync(client, RelabelArgument, Alice,
            configure: submission => submission with { Commands = [] },
            cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        client.LastSubmission.Should().BeNull("a rejected configure must never reach the ledger");
    }

    [Fact]
    public async Task Interface_choice_helper_applies_configure_to_the_submission()
    {
        using var client = new FakeLedgerClient();

        await HoldingTarget.TryDescribeAsync(client, new Describe("balance: "), (SubmitterInfo)Alice,
            configure: AddDisclosureDeduplicationAndSynchronizer,
            cancellationToken: TestContext.Current.CancellationToken);

        AssertReachedTheLedger(client.LastSubmission);
    }

    [Fact]
    public async Task Interface_choice_helper_without_configure_adds_nothing_to_the_submission()
    {
        using var client = new FakeLedgerClient();

        await HoldingTarget.TryDescribeAsync(client, new Describe("balance: "), (SubmitterInfo)Alice,
            cancellationToken: TestContext.Current.CancellationToken);

        AssertNothingWasAdded(client.LastSubmission);
    }

    [Fact]
    public async Task Archive_helper_applies_configure_to_the_submission()
    {
        using var client = new FakeLedgerClient();

        await MarkerTarget.TryArchiveAsync(client, (SubmitterInfo)Alice,
            configure: AddDisclosureDeduplicationAndSynchronizer,
            cancellationToken: TestContext.Current.CancellationToken);

        AssertReachedTheLedger(client.LastSubmission);
    }

    [Fact]
    public async Task Create_helper_applies_configure_to_the_submission()
    {
        using var client = new FakeLedgerClient();

        await client.TryCreateAsync(new Marker(Alice),
            configure: AddDisclosureDeduplicationAndSynchronizer,
            cancellationToken: TestContext.Current.CancellationToken);

        AssertReachedTheLedger(client.LastSubmission);
        client.LastSubmission!.Commands.Should().ContainSingle().Which.Should().BeOfType<CreateCommand>();
        client.LastSubmission.ActAs.Should().ContainSingle().Which.Should().Be(Alice);
    }

    [Fact]
    public async Task Create_helper_without_configure_takes_the_ledger_writers_own_create_path()
    {
        using var client = new FakeLedgerClient();

        await client.TryCreateAsync(new Marker(Alice),
            cancellationToken: TestContext.Current.CancellationToken);

        client.LastCreateSubmitter.Should().NotBeNull();
        client.LastSubmission.Should().BeNull("with no configure the writer's own TryCreateAsync handles the call");
    }
}
