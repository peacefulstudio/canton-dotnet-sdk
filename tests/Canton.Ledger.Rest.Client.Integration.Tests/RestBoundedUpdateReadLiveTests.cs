// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using AwesomeAssertions;
using Canton.Ledger.Grpc.Client.Integration.Tests;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// Live conformance for a bounded update read that sends no <c>stream_idle_timeout_ms</c>: the
/// participant must close the window at the end offset, or at the window limit, and never hold it
/// open. The read stops on an empty window or on reaching the end offset, so a window the
/// participant held open past the end offset would hang it.
/// </summary>
[Trait("Category", "Integration")]
public class RestBoundedUpdateReadLiveTests(ITestOutputHelper output)
{
    private const int SparseMarkers = 2;
    private const int BusyCommands = 7;
    private const int BusyCreatesPerCommand = 30;
    private const int MaxRetries = 9;
    private const long OffsetsAboveLedgerEnd = 1_000;
    private static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(45);

    [Fact]
    public async Task SubscribeAsync_bounded_at_the_ledger_end_returns_the_sparse_matches_and_completes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await RestLiveLane.OpenAsync(cancellationToken);
        var sparseOwner = await AllocateOwnerAsync(lane, "rest-bounded-sparse", cancellationToken);
        var busyOwner = await AllocateOwnerAsync(lane, "rest-bounded-busy", cancellationToken);
        var startOffset = await lane.LedgerClient.GetLedgerEndAsync(cancellationToken: cancellationToken);

        var sparseContractIds = await CreateSparseMarkersAsync(lane, sparseOwner, cancellationToken);
        for (var command = 0; command < BusyCommands; command++)
        {
            await CreateBusyMarkersAsync(lane, busyOwner, cancellationToken);
        }

        var endOffset = await lane.LedgerClient.GetLedgerEndAsync(cancellationToken: cancellationToken);
        var read = await ReadBoundedAsync(lane, sparseOwner, startOffset, endOffset, cancellationToken);

        output.WriteLine($"read ({startOffset}, {endOffset}] in {read.Elapsed}: {read.Events.Count} events");
        read.Events.OfType<ContractStreamEvent<Marker>.StreamError>().Should().BeEmpty();
        read.Events.OfType<ContractStreamEvent<Marker>.Created>()
            .Select(created => created.ContractId.Value)
            .Should().Equal(sparseContractIds);
        sparseContractIds.Should().HaveCount(2);
    }

    [Fact]
    public async Task SubscribeAsync_bounded_above_the_ledger_end_ends_in_a_terminal_StreamError_without_blocking()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await RestLiveLane.OpenAsync(cancellationToken);
        var sparseOwner = await AllocateOwnerAsync(lane, "rest-bounded-above-end", cancellationToken);
        var startOffset = await lane.LedgerClient.GetLedgerEndAsync(cancellationToken: cancellationToken);
        await CreateSparseMarkersAsync(lane, sparseOwner, cancellationToken);

        var ledgerEnd = await lane.LedgerClient.GetLedgerEndAsync(cancellationToken: cancellationToken);
        var endAboveLedgerEnd = LedgerOffset.At(ledgerEnd.Value + OffsetsAboveLedgerEnd);
        var read = await ReadBoundedAsync(lane, sparseOwner, startOffset, endAboveLedgerEnd, cancellationToken);

        var faults = read.Events.OfType<ContractStreamEvent<Marker>.StreamError>().ToList();
        output.WriteLine(
            $"read ({startOffset}, {endAboveLedgerEnd}] over ledger end {ledgerEnd} in {read.Elapsed}: "
            + $"{read.Events.Count} events, faults: {string.Join(" | ", faults.Select(f => $"{f.Status} {f.Message}"))}");
        faults.Should().HaveCount(1);
        read.Events.OfType<ContractStreamEvent<Marker>.Created>().Should().BeEmpty();
    }

    private static async Task<BoundedRead> ReadBoundedAsync(
        RestLiveLane lane,
        Party reader,
        LedgerOffset fromExclusive,
        LedgerOffset toInclusive,
        CancellationToken cancellationToken)
    {
        var events = new List<ContractStreamEvent<Marker>>();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(ReadBudget);
        using var streamHold = await lane.HoldStreamAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await foreach (var streamEvent in lane.LedgerClient.SubscribeAsync<Marker>(
                reader, fromExclusive, toInclusive, budget.Token))
            {
                events.Add(streamEvent);
            }
        }
        catch (OperationCanceledException) when (
            budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            Assert.Fail(
                $"the bounded read over ({fromExclusive}, {toInclusive}] did not complete within "
                + $"{ReadBudget.TotalSeconds:0}s; the participant held the window open. "
                + $"Events so far: {events.Count}");
        }

        return new BoundedRead(events, stopwatch.Elapsed);
    }

    private static async Task<Party> AllocateOwnerAsync(
        RestLiveLane lane, string hint, CancellationToken cancellationToken)
    {
        var darOutcome = await lane.Fixture.UploadDarAsync(RichTypesDar.Path, cancellationToken);
        Assert.True(
            darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
            $"Unexpected DAR upload outcome: {darOutcome}");

        var party = await lane.Fixture.AllocatePartyAsync(hint, cancellationToken: cancellationToken);
        await lane.GrantActAsAsync(party.PartyId, cancellationToken);
        return new Party(party.PartyId);
    }

    private static async Task<IReadOnlyList<string>> CreateSparseMarkersAsync(
        RestLiveLane lane, Party owner, CancellationToken cancellationToken)
    {
        var contractIds = new List<string>();
        for (var marker = 0; marker < SparseMarkers; marker++)
        {
            var outcome = await lane.LedgerClient.TryCreateAsync(
                new Marker(owner), owner, cancellationToken: cancellationToken);
            var created = Assert.IsType<ExerciseOutcome<ContractId<Marker>>.One>(outcome);
            contractIds.Add(created.Result.Value);
        }

        return contractIds;
    }

    private static async Task CreateBusyMarkersAsync(
        RestLiveLane lane, Party owner, CancellationToken cancellationToken)
    {
        var creates = Enumerable.Range(0, BusyCreatesPerCommand)
            .Select(ICommand (_) => CreateCommand.For(new Marker(owner)))
            .ToArray();
        var submission = CommandsSubmission.Multiple(creates).WithActAs(owner);
        for (var attempt = 0; ; attempt++)
        {
            var outcome = await lane.LedgerClient.TrySubmitAndWaitForTransactionAsync(
                submission.WithCommandId(new CommandId(Guid.NewGuid().ToString())),
                cancellationToken: cancellationToken);
            switch (outcome)
            {
                case ExerciseOutcome<TransactionResult>.DamlError error
                    when IsRetryable(error.Category) && attempt < MaxRetries:
                    await Task.Delay(TimeSpan.FromMilliseconds(250 << attempt), cancellationToken);
                    break;
                case ExerciseOutcome<TransactionResult>.DamlError or ExerciseOutcome<TransactionResult>.InfraError:
                    Assert.Fail($"seeding {BusyCreatesPerCommand} busy markers did not commit: {outcome}");
                    return;
                default:
                    return;
            }
        }
    }

    private static bool IsRetryable(DamlErrorCategory category) =>
        category is DamlErrorCategory.ContentionOnSharedResources or DamlErrorCategory.TransientServerFailure;

    private sealed record BoundedRead(IReadOnlyList<ContractStreamEvent<Marker>> Events, TimeSpan Elapsed);
}
