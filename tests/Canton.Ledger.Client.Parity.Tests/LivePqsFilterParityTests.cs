// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Pqs.Client;
using Canton.Ledger.Testing.Localnet;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Contracts;
using Daml.Runtime;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Canton.Ledger.Grpc.Client.Integration.Tests;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

[Trait("Category", "Integration")]
public sealed class LivePqsFilterParityTests : PqsFilterParityTests
{
    private const string PqsConnectionStringEnv = "CANTON_LOCALNET_A_VALIDATOR_1_PQS_CONNECTION_STRING";

    private const string LocalnetSkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "and bring up the localnet (canton-localnet up && canton-localnet wait-ready) to run this parity test.";

    private const string PqsSkipMessage =
        "Skipping: set " + PqsConnectionStringEnv + " to the a-validator-1 PQS PostgreSQL connection string "
        + "to compare the fake's filter evaluation with live PQS. The integration lane sets this automatically.";

    private static readonly TimeSpan ProjectionTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly SemaphoreSlim SeedGate = new(1, 1);
    private static SeededLedger? seeded;

    protected override async Task<CapabilityLane<PqsFilterParityLane>> OpenAsync(CancellationToken cancellationToken)
    {
        var ledger = await SeedAsync(cancellationToken);
        var services = new ServiceCollection()
            .AddPqsClient(options => options.ConnectionString = ledger.ConnectionString)
            .BuildServiceProvider();
        return new CapabilityLane<PqsFilterParityLane>(
            new PqsFilterParityLane(services.GetRequiredService<IPqsClient>(), ledger.Seed),
            services.DisposeAsync);
    }

    private static async Task<SeededLedger> SeedAsync(CancellationToken cancellationToken)
    {
        var connectionString = RequirePqsConnectionString();
        await SeedGate.WaitAsync(cancellationToken);
        try
        {
            return seeded ??= await CreateAndAwaitProjectionAsync(connectionString, cancellationToken);
        }
        finally
        {
            SeedGate.Release();
        }
    }

    private static async Task<SeededLedger> CreateAndAwaitProjectionAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var fixture = LocalnetFixture.FromEnvironment();
        var darOutcome = await fixture.UploadDarAsync(RichTypesDar.Path, cancellationToken);
        Assert.True(
            darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
            $"Unexpected DAR upload outcome: {darOutcome}");

        var userId = fixture.ValidatorUserId;
        await using var ledgerServices = LocalnetLedgerServices.ForValidator(fixture, userId);
        var owner = await ScribeReadablePartyAsync(ledgerServices.GetRequiredService<IAdminClient>(), userId, cancellationToken);
        var ledger = ledgerServices.GetRequiredService<ICantonLedgerClient>();

        var seed = new PqsFilterSeed(
            owner,
            $"pqs-parity-{Guid.NewGuid():N}",
            await Created(ledger.TryCreateAsync(new Marker(owner), cancellationToken: cancellationToken)),
            await Created(ledger.TryCreateAsync(new Marker(owner), cancellationToken: cancellationToken)),
            new ContractId<IHolding>((await Created(ledger.TryCreateAsync(new Asset(owner, 100m), cancellationToken: cancellationToken))).Value),
            new ContractId<IHolding>((await Created(ledger.TryCreateAsync(new Asset(owner, 200m), cancellationToken: cancellationToken))).Value));

        foreach (var record in PqsFilterParitySeed.RichRecords(seed))
            await Created(ledger.TryCreateAsync(record, cancellationToken: cancellationToken));
        foreach (var counts in PqsFilterParitySeed.OptionalCountsRows(seed))
            await Created(ledger.TryCreateAsync(counts, cancellationToken: cancellationToken));
        foreach (var corners in PqsFilterParitySeed.TypeCornersRows(seed))
            await Created(ledger.TryCreateAsync(corners, cancellationToken: cancellationToken));

        var ledgerSeed = new SeededLedger(connectionString, seed);
        await AwaitProjectionAsync<RichRecord>(PqsFilterTarget.RichRecord, PqsFilterParitySeed.RichRecordNames.Length, ledgerSeed, cancellationToken);
        await AwaitProjectionAsync<OptionalCounts>(PqsFilterTarget.OptionalCounts, PqsFilterParitySeed.OptionalCountsNames.Length, ledgerSeed, cancellationToken);
        await AwaitProjectionAsync<TypeCorners>(PqsFilterTarget.TypeCorners, PqsFilterParitySeed.TypeCornersNames.Length, ledgerSeed, cancellationToken);
        return ledgerSeed;
    }

    private static async Task<ContractId<T>> Created<T>(Task<ExerciseOutcome<ContractId<T>>> outcome)
        where T : IDamlType =>
        Assert.IsType<ExerciseOutcome<ContractId<T>>.One>(await outcome).Result;

    private static async Task AwaitProjectionAsync<T>(
        PqsFilterTarget target, int expectedCount, SeededLedger ledger, CancellationToken cancellationToken)
        where T : ITemplate, IDamlRecord<T>
    {
        await using var services = new ServiceCollection()
            .AddPqsClient(options => options.ConnectionString = ledger.ConnectionString)
            .BuildServiceProvider();
        var client = services.GetRequiredService<IPqsClient>();
        var deadline = DateTimeOffset.UtcNow.Add(ProjectionTimeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if ((await client.QueryAsync<T>(Scope(target, ledger.Seed), cancellationToken)).Count == expectedCount) return;
            await Task.Delay(PollInterval, cancellationToken);
        }

        Assert.Fail($"The seeded {typeof(T).Name} contracts were not all projected into PQS within {ProjectionTimeout}.");
    }

    private static async Task<Party> ScribeReadablePartyAsync(IAdminClient admin, string userId, CancellationToken cancellationToken)
    {
        var user = await admin.GetUserAsync(userId, cancellationToken);
        Assert.True(
            user?.PrimaryParty is not null,
            $"user '{userId}' has no primary party; the LocalNet PQS scribe only reads the validator operator party");
        return user!.PrimaryParty!.Value;
    }

    private static string RequirePqsConnectionString()
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
            Assert.Skip(LocalnetSkipMessage);

        var connectionString = Environment.GetEnvironmentVariable(PqsConnectionStringEnv);
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Skip(PqsSkipMessage);

        return connectionString!;
    }

    private sealed record SeededLedger(string ConnectionString, PqsFilterSeed Seed);
}
