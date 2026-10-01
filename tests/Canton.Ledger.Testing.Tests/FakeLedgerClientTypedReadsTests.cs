// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class FakeLedgerClientTypedReadsTests
{
    private static readonly Party Alice = new("alice");
    private static readonly SynchronizerId Sync = (SynchronizerId)"sync1";
    private static readonly DemoAsset Payload = new(Alice, Alice, "GOLD", 1m);

    private static AcsSnapshotEntry<DemoAsset> CreatedAt(long offset) =>
        LedgerEvents.Created(new ContractId<DemoAsset>($"cid{offset}"), Payload, null, LedgerOffset.At(offset), Sync, [Alice]);

    private static CreatedContract<DemoAsset> ContractWithId(string id) =>
        new(new ContractId<DemoAsset>(id), Payload, null, [Alice]);

    [Fact]
    public async Task GetContractAsync_returns_the_staged_contract()
    {
        var contract = ContractWithId("cid1");
        var client = FakeLedgerClient.Create().WithContract(contract).Build();

        var result = await client.GetContractAsync(new ContractId<DemoAsset>("cid1"), (SubmitterInfo)Alice, cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be(contract);
    }

    [Fact]
    public async Task GetContractAsync_for_an_unstaged_id_throws_descriptive_NotSupportedException()
    {
        var client = FakeLedgerClient.Create().WithContract(ContractWithId("cid1")).Build();

        var act = () => client.GetContractAsync(new ContractId<DemoAsset>("cid2"), (SubmitterInfo)Alice, cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("cid2").And.Contain("WithContract<DemoAsset>");
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_returns_the_staged_lifecycle()
    {
        var lifecycle = new ContractLifecycle<DemoAsset>(
            new ContractStreamEvent<DemoAsset>.Created(new ContractId<DemoAsset>("cid1"), Payload, null, LedgerOffset.At(5), Sync, [Alice]),
            null);
        var client = FakeLedgerClient.Create().WithContractLifecycle(new ContractId<DemoAsset>("cid1"), lifecycle).Build();

        var result = await client.GetEventsByContractIdAsync(new ContractId<DemoAsset>("cid1"), (SubmitterInfo)Alice, cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be(lifecycle);
    }

    [Fact]
    public async Task GetEventsByContractIdAsync_unstaged_throws_descriptive_NotSupportedException()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.GetEventsByContractIdAsync(new ContractId<DemoAsset>("cid1"), (SubmitterInfo)Alice, cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithContractLifecycle<DemoAsset>");
    }

    [Fact]
    public async Task GetActiveContractsPageAsync_serves_first_page_then_the_page_after_the_requested_token()
    {
        var first = new AcsPage<DemoAsset>([CreatedAt(1)], LedgerOffset.At(9), new LedgerPageToken("dG9rZW4x"));
        var second = new AcsPage<DemoAsset>([CreatedAt(2)], LedgerOffset.At(9), null);
        var client = FakeLedgerClient.Create().WithActiveContractsPages(first, second).Build();

        var page1 = await client.GetActiveContractsPageAsync<DemoAsset>((SubmitterInfo)Alice, cancellationToken: TestContext.Current.CancellationToken);
        var page2 = await client.GetActiveContractsPageAsync<DemoAsset>((SubmitterInfo)Alice, pageToken: new LedgerPageToken("dG9rZW4x"), cancellationToken: TestContext.Current.CancellationToken);

        page1.Should().Be(first);
        page2.Should().Be(second);
    }

    [Fact]
    public async Task GetActiveContractsPageAsync_at_ledger_begin_answers_the_empty_snapshot_without_a_staged_page()
    {
        var client = FakeLedgerClient.Create().WithActiveContractsPages(new AcsPage<DemoAsset>([CreatedAt(1)], LedgerOffset.At(9), null)).Build();

        var page = await client.GetActiveContractsPageAsync<DemoAsset>(
            (SubmitterInfo)Alice, LedgerOffset.Begin, cancellationToken: TestContext.Current.CancellationToken);

        page.Should().Be(new AcsPage<DemoAsset>([], LedgerOffset.Begin, null));
    }

    [Fact]
    public async Task GetActiveContractsPageAsync_with_an_unknown_token_throws_ArgumentException()
    {
        var first = new AcsPage<DemoAsset>([CreatedAt(1)], LedgerOffset.At(9), null);
        var client = FakeLedgerClient.Create().WithActiveContractsPages(first).Build();

        var act = () => client.GetActiveContractsPageAsync<DemoAsset>((SubmitterInfo)Alice, pageToken: new LedgerPageToken("bm9wZQ=="), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetActiveContractsPageAsync_unstaged_throws_descriptive_NotSupportedException()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.GetActiveContractsPageAsync<DemoAsset>((SubmitterInfo)Alice, cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithActiveContractsPages<DemoAsset>");
    }

    [Fact]
    public async Task GetLatestPrunedOffsetsAsync_returns_the_staged_offsets()
    {
        var offsets = new PrunedOffsets(LedgerOffset.At(3), LedgerOffset.At(2));
        var client = FakeLedgerClient.Create().WithPrunedOffsets(offsets).Build();

        var result = await client.GetLatestPrunedOffsetsAsync(cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be(offsets);
    }

    [Fact]
    public async Task GetLatestPrunedOffsetsAsync_unstaged_throws_descriptive_NotSupportedException()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.GetLatestPrunedOffsetsAsync(cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithPrunedOffsets");
    }

    [Fact]
    public async Task GetUpdatesPageAsync_serves_first_page_then_the_page_after_the_requested_token()
    {
        var first = new UpdatesPage([], LedgerOffset.At(0), LedgerOffset.At(4), new LedgerPageToken("dG9rZW4y"));
        var second = new UpdatesPage([], LedgerOffset.At(4), LedgerOffset.At(8), null);
        var client = FakeLedgerClient.Create().WithUpdatesPages(first, second).Build();

        var page1 = await client.GetUpdatesPageAsync((SubmitterInfo)Alice, cancellationToken: TestContext.Current.CancellationToken);
        var page2 = await client.GetUpdatesPageAsync((SubmitterInfo)Alice, pageToken: new LedgerPageToken("dG9rZW4y"), cancellationToken: TestContext.Current.CancellationToken);

        page1.Should().Be(first);
        page2.Should().Be(second);
    }

    [Fact]
    public async Task GetUpdatesPageAsync_unstaged_throws_descriptive_NotSupportedException()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.GetUpdatesPageAsync((SubmitterInfo)Alice, cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithUpdatesPages");
    }

    [Fact]
    public async Task GetCompletionsAsync_replays_the_staged_completions_after_the_offset()
    {
        var accepted = new CompletionStreamEvent.CommandAccepted(CompletionAt("cmd-1", 7), "update-1");
        var checkpoint = new CompletionStreamEvent.Checkpoint(LedgerOffset.At(8));
        var client = FakeLedgerClient.Create().WithCompletionEvents(accepted, checkpoint).Build();

        var events = new List<CompletionStreamEvent>();
        await foreach (var item in client.GetCompletionsAsync([Alice], LedgerOffset.At(7), TestContext.Current.CancellationToken))
        {
            events.Add(item);
        }

        events.Should().Equal(checkpoint);
    }

    [Fact]
    public void GetCompletionsAsync_without_staged_events_throws_descriptive_NotSupportedException()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.GetCompletionsAsync([Alice]);

        act.Should().Throw<NotSupportedException>().WithMessage("*WithCompletionEvents*");
    }

    private static Completion CompletionAt(string commandId, long offset) => new(
        new CommandId(commandId),
        LedgerOffset.At(offset),
        [Alice],
        new SynchronizerTime("sync-1", DateTimeOffset.UnixEpoch),
        SubmissionId: null,
        UserId: null,
        DeduplicationPeriod: null,
        PaidTrafficCost: 0L,
        TraceContext: null);
}
