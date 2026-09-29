// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing.Localnet;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime;
using Daml.Runtime.Data;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

/// <summary>
/// Reproduces, on the LocalNet participant, the race <see cref="LedgerUserRightsGate"/> closes: a
/// change to a Ledger user's rights ends every stream that user has open with
/// <c>STALE_STREAM_AUTHORIZATION</c>. The first test leaves its stream outside the gate and watches
/// the grant end it; the second holds its stream through the gate and shows the grant reaching the
/// participant only after the stream has closed.
/// </summary>
/// <remarks>
/// Each test proves its stream is open before it grants, by submitting a marker command and seeing
/// that command's completion arrive on the stream. A completion stream opened moments after its
/// party was allocated can be answered with a <c>STALE_STREAM_AUTHORIZATION</c> of its own, from the
/// topology snapshot the allocation moved past; the reader reopens on that until the first marker
/// arrives, and treats every stream error after it as the one under test.
/// </remarks>
[Trait("Category", "Integration")]
public class LedgerUserRightsGateRaceTests
{
    private const string StaleStreamAuthorization = "STALE_STREAM_AUTHORIZATION";
    private const int ReopensBeforeTheFirstMarker = 4;

    private const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this integration test.";

    private static readonly TimeSpan StreamBudget = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ReopenBackoff = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task GrantAsync_ends_a_completion_stream_left_outside_the_gate_with_STALE_STREAM_AUTHORIZATION()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await RaceLane.OpenAsync(cancellationToken);
        await using var grantUnderTest = ActAsRightsLease.ForValidator(lane.Fixture);
        var tail = new CompletionTail(lane.Client, lane.Owner, lane.StartOffset, stopAtSecondMarker: false);

        var reading = tail.ReadAsync(cancellationToken);
        await lane.SubmitMarkerAsync(tail.FirstMarker, cancellationToken);
        await tail.FirstMarkerSeen.WaitAsync(StreamBudget, cancellationToken);
        await grantUnderTest.GrantAsync(lane.SecondParty, cancellationToken);
        var ending = await reading.WaitAsync(StreamBudget, cancellationToken);

        Assert.NotNull(ending.Error);
        Assert.Equal(StaleStreamAuthorization, ending.Error!.ErrorId);
    }

    [Fact]
    public async Task GrantAsync_waits_for_a_completion_stream_held_through_the_gate_and_reaches_the_participant_after_it_closes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await RaceLane.OpenAsync(cancellationToken);
        using var grantTimestamps = new RequestTimestampingHandler();
        await using var grantUnderTest = new ActAsRightsLease(
            lane.Fixture.Endpoints.JsonLedgerApi,
            lane.Fixture.ValidatorUserId,
            lane.Fixture.TokenProvider.GetAccessTokenAsync,
            grantTimestamps);
        var tail = new CompletionTail(lane.Client, lane.Owner, lane.StartOffset, stopAtSecondMarker: true);

        var reading = InAFlowOfItsOwn(() => tail.ReadHeldAsync(lane.Fixture.ValidatorUserId, cancellationToken));
        await lane.SubmitMarkerAsync(tail.FirstMarker, cancellationToken);
        await tail.FirstMarkerSeen.WaitAsync(StreamBudget, cancellationToken);
        var grantAskedAt = Stopwatch.GetTimestamp();
        var granting = grantUnderTest.GrantAsync(lane.SecondParty, cancellationToken);
        await lane.SubmitMarkerAsync(tail.SecondMarker, cancellationToken);
        var ending = await reading.WaitAsync(StreamBudget, cancellationToken);
        await granting.WaitAsync(StreamBudget, cancellationToken);

        Assert.Null(ending.Error);
        Assert.True(
            grantAskedAt < ending.ClosedAt,
            "the grant has to be asked for while the stream is still open, or the test proves nothing");
        Assert.True(
            ending.ClosedAt < grantTimestamps.FirstSentAt,
            "the gate has to hold the grant back until the stream it overlaps has closed");
    }

    private static Task<T> InAFlowOfItsOwn<T>(Func<Task<T>> work)
    {
        using (ExecutionContext.SuppressFlow())
        {
            return Task.Run(work);
        }
    }

    private sealed record StreamEnding(CompletionStreamEvent.StreamError? Error, long ClosedAt);

    private sealed class CompletionTail(
        ICantonLedgerClient client, Party owner, long startOffset, bool stopAtSecondMarker)
    {
        private readonly TaskCompletionSource _firstMarkerSeen = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal RuntimeCommands.CommandId FirstMarker { get; } = new(Guid.NewGuid().ToString());

        internal RuntimeCommands.CommandId SecondMarker { get; } = new(Guid.NewGuid().ToString());

        internal Task FirstMarkerSeen => _firstMarkerSeen.Task;

        internal async Task<StreamEnding> ReadHeldAsync(string userId, CancellationToken cancellationToken)
        {
            using var streamHold = await LedgerUserRightsGate.Shared.HoldStreamAsync(userId, cancellationToken);
            return await ReadAsync(cancellationToken);
        }

        internal async Task<StreamEnding> ReadAsync(CancellationToken cancellationToken)
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(StreamBudget);
            var fromOffset = startOffset;

            for (var reopen = 0; ; reopen++)
            {
                var window = await ReadOneWindowAsync(fromOffset, budget.Token);
                var closedAt = Stopwatch.GetTimestamp();
                if (window.Error is { ErrorId: StaleStreamAuthorization }
                    && !FirstMarkerSeen.IsCompleted
                    && reopen < ReopensBeforeTheFirstMarker)
                {
                    fromOffset = window.HighestObservedOffset;
                    await Task.Delay(ReopenBackoff, budget.Token);
                    continue;
                }

                return new StreamEnding(window.Error, closedAt);
            }
        }

        private async Task<(CompletionStreamEvent.StreamError? Error, long HighestObservedOffset)> ReadOneWindowAsync(
            long fromOffset, CancellationToken cancellationToken)
        {
            var highestObservedOffset = fromOffset;
            await foreach (var streamEvent in client.CompletionStreamAsync(
                owner, LedgerOffset.At(fromOffset), cancellationToken))
            {
                switch (streamEvent)
                {
                    case CompletionStreamEvent.CommandAccepted accepted:
                        highestObservedOffset = Math.Max(highestObservedOffset, accepted.Completion.Offset.Value);
                        if (accepted.Completion.CommandId.Value == FirstMarker.Value)
                        {
                            _firstMarkerSeen.TrySetResult();
                        }
                        else if (stopAtSecondMarker && accepted.Completion.CommandId.Value == SecondMarker.Value)
                        {
                            return (null, highestObservedOffset);
                        }

                        break;
                    case CompletionStreamEvent.Checkpoint checkpoint:
                        highestObservedOffset = Math.Max(highestObservedOffset, checkpoint.Offset.Value);
                        break;
                    case CompletionStreamEvent.StreamError error:
                        return (error, highestObservedOffset);
                }
            }

            return (null, highestObservedOffset);
        }
    }

    private sealed class RaceLane : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly ActAsRightsLease _ownerRights;

        private RaceLane(
            LocalnetFixture fixture,
            ServiceProvider services,
            ActAsRightsLease ownerRights,
            Party owner,
            string secondParty,
            long startOffset)
        {
            Fixture = fixture;
            _services = services;
            _ownerRights = ownerRights;
            Owner = owner;
            SecondParty = secondParty;
            StartOffset = startOffset;
        }

        internal LocalnetFixture Fixture { get; }

        internal ICantonLedgerClient Client => _services.GetRequiredService<ICantonLedgerClient>();

        internal Party Owner { get; }

        internal string SecondParty { get; }

        internal long StartOffset { get; }

        internal static async Task<RaceLane> OpenAsync(CancellationToken cancellationToken)
        {
            if (!EndpointDiscovery.IsLocalnetAvailable())
            {
                Assert.Skip(SkipMessage);
            }

            var fixture = LocalnetFixture.FromEnvironment();
            var ownerRights = ActAsRightsLease.ForValidator(fixture);
            ServiceProvider? services = null;
            try
            {
                var darOutcome = await fixture.UploadDarAsync(RichTypesDar.Path, cancellationToken);
                Assert.True(
                    darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
                    $"Unexpected DAR upload outcome: {darOutcome}");

                var owner = await fixture.AllocatePartyAsync("gate-owner", cancellationToken: cancellationToken);
                var secondParty = await fixture.AllocatePartyAsync("gate-granted", cancellationToken: cancellationToken);
                await ownerRights.GrantAsync(owner.PartyId, cancellationToken);

                services = LocalnetLedgerServices.ForValidator(fixture, fixture.ValidatorUserId);
                var startOffset = await services.GetRequiredService<ICantonLedgerClient>()
                    .GetLedgerEndAsync(cancellationToken: cancellationToken);

                return new RaceLane(
                    fixture, services, ownerRights, new Party(owner.PartyId), secondParty.PartyId, startOffset.Value);
            }
            catch
            {
                if (services is not null)
                {
                    await services.DisposeAsync();
                }

                await ownerRights.DisposeAsync();
                await fixture.DisposeAsync();
                throw;
            }
        }

        internal async Task SubmitMarkerAsync(RuntimeCommands.CommandId commandId, CancellationToken cancellationToken)
        {
            var submission = RuntimeCommands.CommandsSubmission
                .Single(RuntimeCommands.CreateCommand.For(new Marker(Owner)))
                .WithActAs(Owner)
                .WithCommandId(commandId);
            await Client.SubmitAsync(submission, cancellationToken: cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _services.DisposeAsync();
            }
            finally
            {
                try
                {
                    await _ownerRights.DisposeAsync();
                }
                finally
                {
                    await Fixture.DisposeAsync();
                }
            }
        }
    }

    private sealed class RequestTimestampingHandler() : DelegatingHandler(new HttpClientHandler())
    {
        private long _firstSentAt;

        internal long FirstSentAt => Interlocked.Read(ref _firstSentAt);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.CompareExchange(ref _firstSentAt, Stopwatch.GetTimestamp(), 0);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
