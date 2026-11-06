// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing.Localnet;
using Conformance.Probe;
using Daml.Ledger.Abstractions;
using Daml.Ledger.Abstractions.Testing.Conformance;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace Canton.Ledger.Testing.LiveConformance;

/// <summary>
/// Seeds a LocalNet participant once per test class with the canonical conformance scenario, and
/// hands the kit the clients and fixtures it asks for. The scenario lives under a party of its own
/// that no write check touches: active probe contracts, one probe whose amount no decimal can
/// hold, and one archived probe. Subclasses supply the transport.
/// </summary>
/// <remarks>
/// The class holds a stream on the validator user from the end of seeding until it disposes, so no
/// other class in the process changes that user's rights while the kit's streams are open.
/// </remarks>
public abstract class LiveConformanceFixture : IAsyncLifetime
{
    internal const string SkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run the conformance kit live.";

    private const decimal ProbeAmount = 42.5m;
    private const string AmountBeyondDecimal = "9999999999999999999999999999.9999999999";
    private const int RowsKeptBeforeSnapshotFault = 1;
    private const int CommandIdChecks = 4;

    private static readonly TimeSpan StaleAuthorizationBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CompletionDrainTimeout = TimeSpan.FromSeconds(30);
    private const int StaleAuthorizationReopenAttempts = 4;
    private const string StaleStreamAuthorization = "STALE_STREAM_AUTHORIZATION";

    private LocalnetFixture? _localnet;
    private ActAsRightsLease? _actAsRights;
    private StreamHold? _streamHold;
    private ILiveTransport? _transport;
    private Party[] _commandIdWriters = [];
    private int _commandIdWritersTaken;

    internal bool IsAvailable => _transport is not null;

    internal Party Reader { get; private set; }

    internal LedgerOffset EmptySnapshotOffset { get; private set; } = LedgerOffset.Begin;

    private Party Writer { get; set; }

    private Party Outsider { get; set; }

    internal abstract ILiveTransport OpenTransport(LocalnetFixture localnet);

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            return;
        }

        _localnet = LocalnetFixture.FromEnvironment();
        await UploadDarsAsync(_localnet, cancellationToken);

        Reader = await AllocateAsync("conformance-reader", cancellationToken);
        Writer = await AllocateAsync("conformance-writer", cancellationToken);
        Outsider = await AllocateAsync("conformance-outsider", cancellationToken);
        _commandIdWriters = await AllocateManyAsync("conformance-command-id", CommandIdChecks, cancellationToken);

        _actAsRights = ActAsRightsLease.ForValidator(_localnet);
        foreach (var party in _commandIdWriters.Append(Reader).Append(Writer))
        {
            await _actAsRights.GrantAsync(party.Value, cancellationToken);
        }

        _streamHold = await LedgerUserRightsGate.Shared.HoldStreamAsync(_localnet.ValidatorUserId, cancellationToken);
        _transport = OpenTransport(_localnet);
        await SeedAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        _streamHold?.Dispose();
        try
        {
            if (_transport is not null)
            {
                await _transport.DisposeAsync();
            }
        }
        finally
        {
            try
            {
                if (_actAsRights is not null)
                {
                    await _actAsRights.DisposeAsync();
                }
            }
            finally
            {
                if (_localnet is not null)
                {
                    await _localnet.DisposeAsync();
                }
            }
        }
    }

    internal ILedgerClient CreateClient() => Transport.CreateClient();

    internal ILedgerClient CreateFaultingSnapshotClient() => Transport.CreateFaultingSnapshotClient();

    internal WriteConformanceFixture CreateWriteFixture() => new(
        Transport.CreateClient(),
        CommandsSubmission.Single(CreateCommand.For(new ProbeContract(Writer, ProbeAmount))),
        Writer,
        Outsider);

    internal CommandIdConformanceFixture CreateCommandIdFixture()
    {
        var writer = TakeCommandIdWriter();
        var client = Transport.CreateClient();
        var offsetBeforeSubmission = LedgerOffset.Begin;

        return new CommandIdConformanceFixture(
            client,
            async (ledger, commandId) =>
            {
                var probe = await CreateProbeAsync(ledger, writer, TestContext.Current.CancellationToken);
                offsetBeforeSubmission = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
                await ledger.TryExerciseAsync<DamlUnit>(
                    probe.ArchiveCommand(), writer, commandId: commandId, cancellationToken: TestContext.Current.CancellationToken);
            },
            async (ledger, commandId) =>
            {
                offsetBeforeSubmission = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
                await ledger.TryCreateAsync(
                    new ProbeContract(writer, ProbeAmount), writer, commandId: commandId, cancellationToken: TestContext.Current.CancellationToken);
            },
            async () => await ReadAcceptedCommandIdAsync(client, writer, offsetBeforeSubmission, TestContext.Current.CancellationToken));
    }

    private ILiveTransport Transport =>
        _transport ?? throw new InvalidOperationException("The fixture has not been initialized.");

    private Party TakeCommandIdWriter()
    {
        var taken = Interlocked.Increment(ref _commandIdWritersTaken) - 1;
        return taken < _commandIdWriters.Length
            ? _commandIdWriters[taken]
            : throw new InvalidOperationException(
                $"The kit asked for command-id fixture number {taken + 1}, and the fixture allocated {_commandIdWriters.Length} writers.");
    }

    private static async Task UploadDarsAsync(LocalnetFixture localnet, CancellationToken cancellationToken)
    {
        foreach (var upload in await localnet.UploadDarsAsync(LiveConformanceDars.Paths, cancellationToken))
        {
            if (upload.Outcome is not (DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown))
            {
                throw new InvalidOperationException($"Unexpected outcome uploading {upload.Source}: {upload.Outcome}");
            }
        }
    }

    private async Task<Party> AllocateAsync(string hint, CancellationToken cancellationToken)
    {
        var allocated = await _localnet!.AllocatePartyAsync(hint, cancellationToken: cancellationToken);
        return new Party(allocated.PartyId);
    }

    private async Task<Party[]> AllocateManyAsync(string hint, int count, CancellationToken cancellationToken)
    {
        var parties = new Party[count];
        for (var index = 0; index < count; index++)
        {
            parties[index] = await AllocateAsync($"{hint}-{index}", cancellationToken);
        }

        return parties;
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        await using var seeder = Transport.CreateClient();
        EmptySnapshotOffset = await seeder.GetLedgerEndAsync(cancellationToken: cancellationToken);

        await CreateProbeAsync(seeder, Reader, cancellationToken);
        var archived = await CreateProbeAsync(seeder, Reader, cancellationToken);
        await CreateProbeWithAmountBeyondDecimalAsync(seeder, cancellationToken);
        await ArchiveAsync(seeder, archived, cancellationToken);
        await CreateProbeAsync(seeder, Reader, cancellationToken);
    }

    private static async Task<ContractId<ProbeContract>> CreateProbeAsync(
        ILedgerWriter writer, Party owner, CancellationToken cancellationToken)
    {
        var outcome = await writer.TryCreateAsync(
            new ProbeContract(owner, ProbeAmount), owner, cancellationToken: cancellationToken);

        return outcome is ExerciseOutcome<ContractId<ProbeContract>>.One created
            ? created.Result
            : throw new InvalidOperationException($"Seeding a probe for {owner.Value} failed: {outcome}");
    }

    private async Task ArchiveAsync(
        ILedgerWriter writer, ContractId<ProbeContract> probe, CancellationToken cancellationToken)
    {
        var outcome = await writer.TryExerciseAsync<DamlUnit>(
            probe.ArchiveCommand(), Reader, cancellationToken: cancellationToken);

        if (outcome is not ExerciseOutcome<DamlUnit>.One)
        {
            throw new InvalidOperationException($"Archiving the seeded probe failed: {outcome}");
        }
    }

    private async Task CreateProbeWithAmountBeyondDecimalAsync(ILedgerWriter writer, CancellationToken cancellationToken)
    {
        if (!DamlNumeric.TryParseCanonical(AmountBeyondDecimal, out var amount))
        {
            throw new InvalidOperationException($"'{AmountBeyondDecimal}' is not a canonical Daml numeric.");
        }

        var arguments = DamlRecord.Create(
            DamlField.Create("owner", Reader.ToDamlValue()),
            DamlField.Create("amount", amount));
        var create = new CreateCommand(ProbeContract.TemplateId, arguments) { PackageName = ProbeContract.PackageName };

        await writer.SubmitAndWaitAsync(
            CommandsSubmission.Single(create), Reader, cancellationToken: cancellationToken);
    }

    private async Task<string?> ReadAcceptedCommandIdAsync(
        ICantonLedgerClient client, Party writer, LedgerOffset fromOffset, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt <= StaleAuthorizationReopenAttempts; attempt++)
        {
            var window = await DrainCompletionWindowAsync(client, writer, fromOffset, cancellationToken);
            if (window.CommandId is not null || !window.EndedOnStaleAuthorization)
            {
                return window.CommandId;
            }

            fromOffset = window.HighestObservedOffset;
            await Task.Delay(StaleAuthorizationBackoff, cancellationToken);
        }

        return null;
    }

    private static async Task<CompletionWindow> DrainCompletionWindowAsync(
        ICantonLedgerClient client, Party writer, LedgerOffset fromOffset, CancellationToken cancellationToken)
    {
        using var windowBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        windowBudget.CancelAfter(CompletionDrainTimeout);

        var highestObserved = fromOffset;
        var endedOnStaleAuthorization = false;
        await foreach (var streamEvent in client.CompletionStreamAsync(writer, fromOffset, windowBudget.Token))
        {
            if (streamEvent is CompletionStreamEvent.CommandAccepted accepted)
            {
                return new CompletionWindow(accepted.Completion.CommandId.Value, highestObserved, false);
            }

            highestObserved = OffsetOf(streamEvent) ?? highestObserved;
            endedOnStaleAuthorization = streamEvent is CompletionStreamEvent.StreamError { ErrorId: StaleStreamAuthorization };
        }

        return new CompletionWindow(null, highestObserved, endedOnStaleAuthorization);
    }

    private static LedgerOffset? OffsetOf(CompletionStreamEvent streamEvent) => streamEvent switch
    {
        CompletionStreamEvent.CommandRejected rejected => rejected.Completion.Offset,
        CompletionStreamEvent.Checkpoint checkpoint => checkpoint.Offset,
        _ => null,
    };

    private sealed record CompletionWindow(string? CommandId, LedgerOffset HighestObservedOffset, bool EndedOnStaleAuthorization);
}
