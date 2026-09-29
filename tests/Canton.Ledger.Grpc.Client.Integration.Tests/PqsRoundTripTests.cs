// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections;
using System.Diagnostics;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Pqs.Client;
using Canton.Ledger.Testing.Localnet;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Peaceful.Canton.Localnet.Testing;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

[Trait("Category", "Integration")]
public class PqsRoundTripTests
{
    private const string PqsConnectionStringEnv = "CANTON_LOCALNET_A_VALIDATOR_1_PQS_CONNECTION_STRING";
    private const string OpenTelemetryParentActivitySourceName = "Canton.Ledger.OpenTelemetry.Tests.SharedTrace";

    private const string LocalnetSkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "(or the legacy un-namespaced CANTON_LOCALNET_* globals) and bring up the localnet "
        + "(canton-localnet up && canton-localnet wait-ready) to run this integration test.";

    private const string PqsSkipMessage =
        "Skipping: set " + PqsConnectionStringEnv + " to a PQS PostgreSQL connection string "
        + "(the LocalNet compose exposes the a-validator-1 store as "
        + "'Host=localhost;Port=5432;Database=pqs-a-validator-1;Username=cnadmin;Password=…') "
        + "to exercise the PQS read path. The integration lane sets this automatically.";

    // The LocalNet scribe JVMs run under tight PQS resource limits
    // (mem_limit 1g / -Xmx768m) and get OOM-killed under the test run's own
    // memory spike on the shared runner; the deadline must ride out a full
    // scribe restart + package re-registration cycle.
    private static readonly TimeSpan ProjectionTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly ActivitySource OpenTelemetryParentSource = new(OpenTelemetryParentActivitySourceName);

    private static string DarPath() => RichTypesDar.Path;

    private static async Task<Party> ScribeReadablePartyAsync(IAdminClient admin, string userId)
    {
        var user = await admin.GetUserAsync(userId, TestContext.Current.CancellationToken);
        Assert.True(
            user?.PrimaryParty is not null,
            $"user '{userId}' has no primary party — the LocalNet PQS scribe user only has ReadAs "
            + "for the validator operator party, so the contract under test must be issued by it "
            + "to ever be projected into PQS");
        return user!.PrimaryParty!.Value;
    }

    private const decimal AssetAmount = 123.45m;
    private const decimal OpenTelemetryAssetAmount = 42.5m;

    [Fact]
    public async Task QueryAsync_projects_and_maps_a_contract_created_via_LedgerClient()
    {
        var pqsConnectionString = RequirePqsConnectionString();

        await using var fixture = LocalnetFixture.FromEnvironment();
        var created = await CreateAssetAsync(fixture);

        await using var services = PqsServices(pqsConnectionString);
        var pqs = services.GetRequiredService<IPqsClient>();

        var createdContractId = new ContractId<Asset>(created.ContractId);
        var projected = await PollForFetchAsync(
            () => pqs.FetchByIdAsync(createdContractId, TestContext.Current.CancellationToken));

        Assert.NotNull(projected);
        Assert.Equal(created.Issuer.Value, projected!.Data.Issuer.Value);
        Assert.Equal(AssetAmount, projected.Data.Amount);
    }

    [Fact]
    public async Task QueryAsync_projects_the_interface_view_of_an_implementing_contract()
    {
        var pqsConnectionString = RequirePqsConnectionString();

        await using var fixture = LocalnetFixture.FromEnvironment();
        var created = await CreateAssetAsync(fixture);

        await using var services = PqsServices(pqsConnectionString);
        var pqs = services.GetRequiredService<IPqsClient>();

        var createdHoldingId = new ContractId<Asset>(created.ContractId).ToInterfaceContractId<Asset, IHolding>();
        var projected = await PollForFetchAsync(
            () => pqs.FetchByIdAsync<IHolding, HoldingView>(createdHoldingId, TestContext.Current.CancellationToken));

        Assert.NotNull(projected);
        Assert.Equal(AssetAmount, projected!.View.Amount);
    }

    [Fact]
    public async Task AddCantonLedgerInstrumentation_shares_one_trace_across_a_submit_a_stream_read_and_a_pqs_query()
    {
        var pqsConnectionString = RequirePqsConnectionString();

        await using var fixture = LocalnetFixture.FromEnvironment();
        var darOutcome = await fixture.UploadDarAsync(DarPath(), TestContext.Current.CancellationToken);
        Assert.True(
            darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
            $"Unexpected DAR upload outcome: {darOutcome}");

        var userId = fixture.ValidatorUserId;
        await using var ledgerServices = LocalnetLedgerServices.ForValidator(fixture, userId);
        var admin = ledgerServices.GetRequiredService<IAdminClient>();
        var issuer = await ScribeReadablePartyAsync(admin, userId);

        var exportedActivities = new ConcurrentActivityCollection();
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(OpenTelemetryParentActivitySourceName)
            .AddCantonLedgerInstrumentation()
            .AddInMemoryExporter(exportedActivities)
            .Build();

        await using var pqsServices = PqsServices(pqsConnectionString);
        var pqs = pqsServices.GetRequiredService<IPqsClient>();

        ActivityTraceId sharedTraceId;
        ActivitySpanId sharedParentSpanId;
        using (var parentActivity = OpenTelemetryParentSource.StartActivity("shared-trace-parent"))
        {
            sharedTraceId = parentActivity!.TraceId;
            sharedParentSpanId = parentActivity.SpanId;

            var ledger = ledgerServices.GetRequiredService<ICantonLedgerClient>();
            var beforeOffset = await ledger.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

            var createOutcome = await ledger.TryCreateAsync(
                new Asset(issuer, OpenTelemetryAssetAmount), cancellationToken: TestContext.Current.CancellationToken);
            var created = Assert.IsType<ExerciseOutcome<ContractId<Asset>>.One>(createOutcome).Result;

            var afterOffset = await ledger.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

            using (await LedgerUserRightsGate.Shared.HoldStreamAsync(userId, TestContext.Current.CancellationToken))
            {
                await foreach (var _ in ledger.SubscribeAsync<Asset>(
                    issuer, beforeOffset, afterOffset, TestContext.Current.CancellationToken))
                {
                }
            }

            var createdContractId = new ContractId<Asset>(created.Value);
            var projected = await PollForFetchAsync(
                () => pqs.FetchByIdAsync(createdContractId, TestContext.Current.CancellationToken));
            Assert.NotNull(projected);
        }

        tracerProvider.ForceFlush();

        var traceActivities = exportedActivities.Snapshot()
            .Where(a => a.TraceId == sharedTraceId)
            .ToList();
        Assert.NotEmpty(traceActivities);

        var grpcLedgerClientSpans = traceActivities
            .Where(a => a.Source.Name == "Canton.Ledger.Grpc.Client.LedgerClient")
            .ToList();

        var submitSpan = Assert.Single(grpcLedgerClientSpans, a => a.DisplayName == "SubmissionClient.TryCreateAsync");
        Assert.Equal(sharedParentSpanId, submitSpan.ParentSpanId);

        var subscribeSpan = Assert.Single(grpcLedgerClientSpans, a => a.DisplayName == "LedgerClient.SubscribeAsyncCore");
        Assert.Equal(sharedParentSpanId, subscribeSpan.ParentSpanId);

        var pqsQuerySpans = traceActivities
            .Where(a => a.Source.Name == "Npgsql"
                && a.GetTagItem("db.query.text") is
                    "SELECT contract_id, payload FROM active(@typeId) WHERE contract_id = @contractId LIMIT 1")
            .ToList();
        Assert.True(
            pqsQuerySpans.Count > 0,
            "No Npgsql command span with the PQS active() query text in the shared trace; Npgsql spans seen: "
            + string.Join(", ", traceActivities.Where(a => a.Source.Name == "Npgsql").Select(a => a.DisplayName)));
        var pqsQuerySpan = pqsQuerySpans[0];
        Assert.Equal("postgresql", pqsQuerySpan.GetTagItem("db.system.name"));
        Assert.Equal(
            new NpgsqlConnectionStringBuilder(pqsConnectionString).Database,
            pqsQuerySpan.GetTagItem("db.namespace"));

        var pqsClientSpan = Assert.Single(traceActivities, a => a.SpanId == pqsQuerySpan.ParentSpanId);
        Assert.Equal("Canton.Ledger.Pqs.Client.PqsClient", pqsClientSpan.Source.Name);
        Assert.Equal(sharedParentSpanId, pqsClientSpan.ParentSpanId);
    }

    private static ServiceProvider PqsServices(string pqsConnectionString) =>
        new ServiceCollection()
            .AddPqsClient(options => options.ConnectionString = pqsConnectionString)
            .BuildServiceProvider();

    private static string RequirePqsConnectionString()
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
        {
            Assert.Skip(LocalnetSkipMessage);
        }

        var pqsConnectionString = Environment.GetEnvironmentVariable(PqsConnectionStringEnv);
        if (string.IsNullOrWhiteSpace(pqsConnectionString))
        {
            Assert.Skip(PqsSkipMessage);
        }

        return pqsConnectionString!;
    }

    private static async Task<(Party Issuer, string ContractId)> CreateAssetAsync(LocalnetFixture fixture)
    {
        var darOutcome = await fixture.UploadDarAsync(DarPath(), TestContext.Current.CancellationToken);
        Assert.True(
            darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
            $"Unexpected DAR upload outcome: {darOutcome}");

        var userId = fixture.ValidatorUserId;
        await using var services = LocalnetLedgerServices.ForValidator(fixture, userId);

        var issuer = await ScribeReadablePartyAsync(services.GetRequiredService<IAdminClient>(), userId);
        var ledger = services.GetRequiredService<ICantonLedgerClient>();

        var createOutcome = await ledger.TryCreateAsync(new Asset(issuer, AssetAmount), cancellationToken: TestContext.Current.CancellationToken);
        var createdCid = Assert.IsType<ExerciseOutcome<ContractId<Asset>>.One>(createOutcome).Result;
        Assert.False(string.IsNullOrWhiteSpace(createdCid.Value), "created Asset ContractId is empty");

        return (issuer, createdCid.Value);
    }

    private static async Task<T?> PollForFetchAsync<T>(Func<Task<T?>> fetch)
        where T : class
    {
        var deadline = DateTimeOffset.UtcNow.Add(ProjectionTimeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var projected = await fetch();
            if (projected is not null) return projected;

            try
            {
                await Task.Delay(PollInterval, TestContext.Current.CancellationToken);
            }
            catch (OperationCanceledException) when (TestContext.Current.CancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return null;
    }

    private sealed class ConcurrentActivityCollection : ICollection<Activity>
    {
        private readonly List<Activity> items = [];
        private readonly Lock gate = new();

        public int Count { get { lock (gate) { return items.Count; } } }

        public bool IsReadOnly => false;

        public void Add(Activity item)
        {
            lock (gate) { items.Add(item); }
        }

        public List<Activity> Snapshot()
        {
            lock (gate) { return [.. items]; }
        }

        public void Clear()
        {
            lock (gate) { items.Clear(); }
        }

        public bool Contains(Activity item)
        {
            lock (gate) { return items.Contains(item); }
        }

        public void CopyTo(Activity[] array, int arrayIndex)
        {
            lock (gate) { items.CopyTo(array, arrayIndex); }
        }

        public bool Remove(Activity item)
        {
            lock (gate) { return items.Remove(item); }
        }

        public IEnumerator<Activity> GetEnumerator() => Snapshot().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
