// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Testing.Localnet.Tests;

public class ActAsRightsLeaseRaceTests
{
    private const string JsonLedgerApi = "http://localhost:11975";
    private const string UserId = "c87743ab-80e0-4b83-935a-4c0582226691";
    private const string Alice = "alice::ns1";
    private const string StaleStreamAuthorization = "STALE_STREAM_AUTHORIZATION";

    private static readonly TimeSpan PatientWaitLimit = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task GrantAsync_ends_a_stream_opened_outside_the_gate_with_STALE_STREAM_AUTHORIZATION()
    {
        using var participant = new StreamAuthorizingParticipant();
        var gate = new LedgerUserRightsGate(PatientWaitLimit);
        await using var lease = NewLease(participant, gate);
        var ungatedStream = participant.OpenStream();

        await lease.GrantAsync(Alice, TestContext.Current.CancellationToken);

        ungatedStream.EndedWith.Should().Be(StaleStreamAuthorization);
    }

    [Fact]
    public async Task GrantAsync_lands_only_after_a_stream_held_through_the_gate_has_closed()
    {
        using var participant = new StreamAuthorizingParticipant();
        var gate = new LedgerUserRightsGate(PatientWaitLimit);
        await using var lease = NewLease(participant, gate);
        var reader = GatedReader.Start(gate, participant);
        var stream = await reader.Opened.WaitAsync(PatientWaitLimit, TestContext.Current.CancellationToken);

        var grant = lease.GrantAsync(Alice, TestContext.Current.CancellationToken);
        await UntilAsync(() => gate.StateOf(UserId).WaitingChanges == 1);
        participant.RightsChangeArrivals.Should().BeEmpty();
        reader.CloseStream();
        await grant.WaitAsync(PatientWaitLimit, TestContext.Current.CancellationToken);

        stream.EndedWith.Should().BeNull();
        participant.RightsChangeArrivals.Should().ContainSingle()
            .Which.Should().BeGreaterThan(stream.ClosedAt!.Value);
    }

    [Fact]
    public async Task DisposeAsync_revokes_only_after_a_stream_held_through_the_gate_has_closed()
    {
        using var participant = new StreamAuthorizingParticipant();
        var gate = new LedgerUserRightsGate(PatientWaitLimit);
        var lease = NewLease(participant, gate);
        await lease.GrantAsync(Alice, TestContext.Current.CancellationToken);
        var reader = GatedReader.Start(gate, participant);
        var stream = await reader.Opened.WaitAsync(PatientWaitLimit, TestContext.Current.CancellationToken);

        var revoke = lease.DisposeAsync().AsTask();
        await UntilAsync(() => gate.StateOf(UserId).WaitingChanges == 1);
        participant.RightsChangeArrivals.Should().ContainSingle("only the grant has reached the participant");
        reader.CloseStream();
        await revoke.WaitAsync(PatientWaitLimit, TestContext.Current.CancellationToken);

        stream.EndedWith.Should().BeNull();
        participant.RightsChangeArrivals.Should().HaveCount(2)
            .And.Subject.Last().Should().BeGreaterThan(stream.ClosedAt!.Value);
    }

    [Fact]
    public async Task GrantAsync_throws_and_sends_nothing_when_the_calling_flow_holds_a_stream_on_the_user()
    {
        using var participant = new StreamAuthorizingParticipant();
        var gate = new LedgerUserRightsGate(PatientWaitLimit);
        await using var lease = NewLease(participant, gate);
        using var hold = await gate.HoldStreamAsync(UserId, TestContext.Current.CancellationToken);

        var act = () => lease.GrantAsync(Alice, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Contain(UserId).And.Contain(StaleStreamAuthorization);
        participant.RightsChangeArrivals.Should().BeEmpty();
    }

    [Fact]
    public async Task DisposeAsync_throws_and_sends_nothing_when_the_calling_flow_holds_a_stream_on_the_user()
    {
        using var participant = new StreamAuthorizingParticipant();
        var gate = new LedgerUserRightsGate(PatientWaitLimit);
        var lease = NewLease(participant, gate);
        await lease.GrantAsync(Alice, TestContext.Current.CancellationToken);
        using var hold = await gate.HoldStreamAsync(UserId, TestContext.Current.CancellationToken);

        var act = () => lease.DisposeAsync().AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        participant.RightsChangeArrivals.Should().ContainSingle("only the grant has reached the participant");
    }

    private static ActAsRightsLease NewLease(StreamAuthorizingParticipant participant, LedgerUserRightsGate gate) =>
        new(new Uri(JsonLedgerApi), UserId, _ => new ValueTask<string>("tok-123"), participant, gate);

    private static async Task UntilAsync(Func<bool> condition)
    {
        var waited = Stopwatch.StartNew();
        while (!condition())
        {
            if (waited.Elapsed > PatientWaitLimit)
            {
                throw new TimeoutException($"the gate did not reach the expected state within {PatientWaitLimit}");
            }

            await Task.Delay(1, TestContext.Current.CancellationToken);
        }
    }

    private sealed class GatedReader
    {
        private readonly TaskCompletionSource<ModelStream> _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _close = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task<ModelStream> Opened => _opened.Task;

        internal static GatedReader Start(LedgerUserRightsGate gate, StreamAuthorizingParticipant participant)
        {
            var reader = new GatedReader();
            using (ExecutionContext.SuppressFlow())
            {
                _ = Task.Run(() => reader.ReadAsync(gate, participant));
            }

            return reader;
        }

        internal void CloseStream() => _close.SetResult();

        private async Task ReadAsync(LedgerUserRightsGate gate, StreamAuthorizingParticipant participant)
        {
            using var hold = await gate.HoldStreamAsync(UserId);
            var stream = participant.OpenStream();
            _opened.SetResult(stream);
            await _close.Task;
            stream.Close();
        }
    }

    private sealed class ModelStream
    {
        internal string? EndedWith { get; private set; }

        internal long? ClosedAt { get; private set; }

        internal bool IsOpen => EndedWith is null && ClosedAt is null;

        internal void EndStale() => EndedWith = StaleStreamAuthorization;

        internal void Close() => ClosedAt = Stopwatch.GetTimestamp();
    }

    private sealed class StreamAuthorizingParticipant : HttpMessageHandler
    {
        private readonly Lock _sync = new();
        private readonly List<ModelStream> _streams = [];
        private readonly List<long> _rightsChangeArrivals = [];

        internal IReadOnlyList<long> RightsChangeArrivals
        {
            get
            {
                lock (_sync)
                {
                    return [.. _rightsChangeArrivals];
                }
            }
        }

        internal ModelStream OpenStream()
        {
            var stream = new ModelStream();
            lock (_sync)
            {
                _streams.Add(stream);
            }

            return stream;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            lock (_sync)
            {
                _rightsChangeArrivals.Add(Stopwatch.GetTimestamp());
                foreach (var stream in _streams.Where(stream => stream.IsOpen))
                {
                    stream.EndStale();
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.Method == HttpMethod.Patch
                    ? $$"""{"newlyRevokedRights":{{JsonDocument.Parse(body).RootElement.GetProperty("rights").GetRawText()}}}"""
                    : "{}"),
            };
        }
    }
}
