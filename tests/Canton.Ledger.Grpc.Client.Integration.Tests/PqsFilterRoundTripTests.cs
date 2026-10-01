// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Pqs.Client;
using Canton.Ledger.Testing.Localnet;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Stdlib;
using Microsoft.Extensions.DependencyInjection;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Integration.Tests;

[Trait("Category", "Integration")]
public class PqsFilterRoundTripTests
{
    private const string PqsConnectionStringEnv = "CANTON_LOCALNET_A_VALIDATOR_1_PQS_CONNECTION_STRING";

    private const string LocalnetSkipMessage =
        "Skipping: set CANTON_LOCALNET_A_VALIDATOR_1_JSON_API_URL, _CLIENT_ID, _CLIENT_SECRET "
        + "and bring up the localnet (canton-localnet up && canton-localnet wait-ready) to run this integration test.";

    private const string PqsSkipMessage =
        "Skipping: set " + PqsConnectionStringEnv + " to the a-validator-1 PQS PostgreSQL connection string "
        + "to exercise typed PQS filtering. The integration lane sets this automatically.";

    private static readonly TimeSpan ProjectionTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly SemaphoreSlim SeedGate = new(1, 1);
    private static Seeded? seeded;

    [Fact]
    public async Task Field_matches_a_Numeric_stored_padded_to_its_scale()
    {
        var matches = await QueryRichRecordAsync(Filter.Field<RichRecord>(r => r.Amount, "12.34"));

        Assert.Single(matches);
    }

    [Fact]
    public async Task Where_orders_Numeric_Int64_Date_and_Time_fields_by_value()
    {
        var observedFrom = new DateTimeOffset(2026, 5, 29, 15, 0, 0, TimeSpan.FromHours(2));
        var asOfBefore = new DateOnly(2026, 6, 1);

        Assert.Single(await QueryRichRecordAsync(Filter.Where<RichRecord>(
            r => r.Amount > 12.3m && r.Amount < 12.35m && r.Count >= 42 && r.ObservedAt >= observedFrom && r.AsOf < asOfBefore)));
        Assert.Empty(await QueryRichRecordAsync(Filter.Where<RichRecord>(r => r.Amount > 12.34m)));
    }

    [Fact]
    public async Task Where_matches_nested_record_fields_enums_and_bools()
    {
        Assert.Single(await QueryRichRecordAsync(Filter.Where<RichRecord>(
            r => r.Profile.Level == 7 && r.Profile.Nickname == "cdg" && r.Suit == Suit.Hearts && r.Active)));
        Assert.Single(await QueryRichRecordAsync(Filter.Field<RichRecord>(r => r.Profile.Level, "7")));
        Assert.Empty(await QueryRichRecordAsync(Filter.Where<RichRecord>(r => r.Suit == Suit.Spades)));
    }

    [Fact]
    public async Task Where_matches_flat_and_nested_Optional_fields()
    {
        Assert.Single(await QueryRichRecordAsync(Filter.Where<RichRecord>(r => r.Note != null && r.Note == "hello")));
        Assert.Empty(await QueryRichRecordAsync(Filter.Where<RichRecord>(r => r.Note == null)));

        Assert.Single(await QueryTypeCornersAsync(Filter.Where<TypeCorners>(
            t => t.MaybeMaybeNote.HasValue
                && t.MaybeMaybeNote.GetValueOrDefault()!.GetValueOrDefault() == "deep"
                && t.Crate.Item.GetValueOrDefault() == "crated"
                && !t.NestedNote!.Item.HasValue)));
        Assert.Empty(await QueryTypeCornersAsync(Filter.Where<TypeCorners>(
            t => t.MaybeMaybeNote is Optional<Optional<string>>.None)));
    }

    [Fact]
    public async Task Where_matches_List_elements()
    {
        Assert.Single(await QueryRichRecordAsync(Filter.Where<RichRecord>(
            r => r.Tags.Contains("urgent") && r.Tags.All(tag => tag != "blocked") && r.HoldingCids.Any())));
        Assert.Empty(await QueryRichRecordAsync(Filter.Where<RichRecord>(r => r.Tags.Any(tag => tag == "blocked"))));

        Assert.Single(await QueryTypeCornersAsync(Filter.Where<TypeCorners>(
            t => t.Branch.Children.Any(child => child.Label == "leaf" && !child.Children.Any()))));
    }

    [Fact]
    public async Task Where_matches_TextMap_and_GenMap_entries()
    {
        var owner = (await SeedAsync()).Owner;

        Assert.Single(await QueryRichRecordAsync(Filter.Where<RichRecord>(
            r => r.Attributes["k1"] == "v1" && r.Attributes.ContainsKey("k2"))));
        Assert.Empty(await QueryRichRecordAsync(Filter.Where<RichRecord>(r => r.Attributes.ContainsKey("k9"))));

        Assert.Single(await QueryTypeCornersAsync(Filter.Where<TypeCorners>(
            t => t.LabelByRank[1] == "gold" && t.QuotaByParty[owner] > 4 && t.LabelByRank.ContainsKey(1))));
        Assert.Empty(await QueryTypeCornersAsync(Filter.Where<TypeCorners>(t => t.LabelByRank.ContainsKey(2))));
    }

    [Fact]
    public async Task Where_matches_variant_constructors_and_their_payloads()
    {
        Assert.Single(await QueryRichRecordAsync(Filter.Where<RichRecord>(
            r => r.Outcome is Outcome.Win && ((Outcome.Win)r.Outcome).Value.Prize > 250m)));
        Assert.Empty(await QueryRichRecordAsync(Filter.Where<RichRecord>(r => r.Outcome is Outcome.Pending)));

        Assert.Single(await QueryTypeCornersAsync(Filter.Where<TypeCorners>(
            t => ((Slot<long>.Filled)t.Slot).Value == 11
                && ((Either<long, string>.Right)t.RankOrLabel).Value == "runner-up"
                && t.Pair._2 == 3
                && t.Triple._3)));
        Assert.Empty(await QueryTypeCornersAsync(Filter.Where<TypeCorners>(t => t.RankOrLabel is Either<long, string>.Left)));
    }

    [Fact]
    public async Task Where_reads_an_absent_Optional_Int64_as_zero_under_GetValueOrDefault()
    {
        Assert.Equal([null, 0L], await CountsAsync(n => n.MaybeCount.GetValueOrDefault() == 0));
        Assert.Equal([7L], await CountsAsync(n => n.MaybeCount.GetValueOrDefault() != 0));
        Assert.Equal([null, 0L], await CountsAsync(n => n.MaybeCount.GetValueOrDefault() < 5));
    }

    [Fact]
    public async Task Where_matches_a_missing_variant_constructor_by_comparing_a_cast_to_null()
    {
        var absent = await QueryRichRecordsAsync(r => (r.Outcome as Outcome.Win) == null);
        var present = await QueryRichRecordsAsync(r => (r.Outcome as Outcome.Win) != null);

        Assert.Equal([new Outcome.Pending()], absent.Select(r => r.Data.Outcome));
        Assert.Single(present);
        Assert.IsType<Outcome.Win>(present[0].Data.Outcome);
    }

    [Fact]
    public async Task Where_guards_a_constructor_payload_read_when_constructors_carry_different_types()
    {
        var seed = await SeedAsync();

        var matches = await QueryAsync<TypeCorners>(
            Filter.And(
                Filter.Or(PairKey(seed.PairKey), PairKey(seed.LeftPairKey)),
                Filter.Where<TypeCorners>(t => ((Either<long, string>.Left)t.RankOrLabel).Value == 5)),
            seed);

        Assert.Equal([seed.LeftPairKey], matches.Select(m => m.Data.Pair._1));
    }

    [Fact]
    public async Task Field_matches_a_Numeric_37_by_every_digit_of_its_stored_value()
    {
        var seed = await SeedAsync();

        Assert.Single(await QueryAsync<TypeCorners>(
            Filter.And(PairKey(seed.LeftPairKey), Filter.Field<TypeCorners>(t => t.Finest, "0.1234567890123456789012345678000000000")),
            seed));
        Assert.Empty(await QueryAsync<TypeCorners>(
            Filter.And(PairKey(seed.LeftPairKey), Filter.Field<TypeCorners>(t => t.Finest, "0.1234567890123456789012345678901234567")),
            seed));
    }

    private static async Task<IReadOnlyList<long?>> CountsAsync(System.Linq.Expressions.Expression<Func<OptionalCounts, bool>> predicate)
    {
        var seed = await SeedAsync();
        var matches = await QueryAsync<OptionalCounts>(
            Filter.And(Filter.Where<OptionalCounts>(n => n.Label == seed.CountsLabel), Filter.Where(predicate)),
            seed);
        return matches.Select(m => m.Data.MaybeCount).OrderBy(count => count ?? long.MinValue).ToList();
    }

    private static async Task<IReadOnlyList<Contract<RichRecord>>> QueryRichRecordsAsync(
        System.Linq.Expressions.Expression<Func<RichRecord, bool>> predicate)
    {
        var seed = await SeedAsync();
        return await QueryAsync<RichRecord>(
            Filter.And(
                Filter.Or(Filter.Field<RichRecord>(r => r.Label, seed.Label), Filter.Field<RichRecord>(r => r.Label, seed.PendingLabel)),
                Filter.Where(predicate)),
            seed);
    }

    private static async Task<IReadOnlyList<Contract<RichRecord>>> QueryRichRecordAsync(PqsFilter filter)
    {
        var seed = await SeedAsync();
        return await QueryAsync<RichRecord>(Filter.And(Filter.Field<RichRecord>(r => r.Label, seed.Label), filter), seed);
    }

    private static async Task<IReadOnlyList<Contract<TypeCorners>>> QueryTypeCornersAsync(PqsFilter filter)
    {
        var seed = await SeedAsync();
        return await QueryAsync<TypeCorners>(Filter.And(PairKey(seed.PairKey), filter), seed);
    }

    private static PqsFilter PairKey(string key) => Filter.Where<TypeCorners>(t => t.Pair._1 == key);

    private static async Task<IReadOnlyList<Contract<T>>> QueryAsync<T>(PqsFilter filter, Seeded seed)
        where T : ITemplate, IDamlRecord<T>
    {
        await using var services = PqsServices(seed.ConnectionString);
        return await services.GetRequiredService<IPqsClient>()
            .QueryAsync<T>(filter, TestContext.Current.CancellationToken);
    }

    private static async Task<Seeded> SeedAsync()
    {
        var connectionString = RequirePqsConnectionString();
        await SeedGate.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            return seeded ??= await CreateAndAwaitProjectionAsync(connectionString);
        }
        finally
        {
            SeedGate.Release();
        }
    }

    private static async Task<Seeded> CreateAndAwaitProjectionAsync(string connectionString)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = LocalnetFixture.FromEnvironment();
        var darOutcome = await fixture.UploadDarAsync(RichTypesDar.Path, cancellationToken);
        Assert.True(
            darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
            $"Unexpected DAR upload outcome: {darOutcome}");

        var userId = fixture.ValidatorUserId;
        await using var ledgerServices = LocalnetLedgerServices.ForValidator(fixture, userId);
        var owner = await ScribeReadablePartyAsync(ledgerServices.GetRequiredService<IAdminClient>(), userId);
        var ledger = ledgerServices.GetRequiredService<ICantonLedgerClient>();

        var marker = Assert.IsType<ExerciseOutcome<ContractId<Marker>>.One>(
            await ledger.TryCreateAsync(new Marker(owner), cancellationToken: cancellationToken)).Result;
        var asset = Assert.IsType<ExerciseOutcome<ContractId<Asset>>.One>(
            await ledger.TryCreateAsync(new Asset(owner, 100m), cancellationToken: cancellationToken)).Result;
        var holding = new ContractId<IHolding>(asset.Value);

        var label = $"pqs-filter-{Guid.NewGuid():N}";
        Assert.IsType<ExerciseOutcome<ContractId<RichRecord>>.One>(await ledger.TryCreateAsync(
            new RichRecord(
                Owner: owner,
                Count: 42L,
                Amount: 12.34m,
                Label: label,
                Active: true,
                AsOf: new DateOnly(2026, 5, 29),
                ObservedAt: new DateTimeOffset(2026, 5, 29, 13, 30, 0, TimeSpan.Zero),
                Note: "hello",
                Tags: ["urgent", "blue"],
                Attributes: new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" },
                Marker: marker,
                HoldingCid: holding,
                HoldingCids: [holding],
                Profile: new Profile(Nickname: "cdg", Level: 7L),
                Outcome: new Outcome.Win(new Outcome_Win(Prize: 250.50m, Tier: "gold")),
                Suit: Suit.Hearts,
                Fee: 0.05m),
            cancellationToken: cancellationToken));

        var pendingLabel = $"pqs-filter-{Guid.NewGuid():N}";
        Assert.IsType<ExerciseOutcome<ContractId<RichRecord>>.One>(await ledger.TryCreateAsync(
            new RichRecord(
                Owner: owner,
                Count: 1L,
                Amount: 1m,
                Label: pendingLabel,
                Active: false,
                AsOf: new DateOnly(2026, 5, 29),
                ObservedAt: new DateTimeOffset(2026, 5, 29, 13, 30, 0, TimeSpan.Zero),
                Note: null,
                Tags: [],
                Attributes: new Dictionary<string, string>(),
                Marker: marker,
                HoldingCid: holding,
                HoldingCids: [],
                Profile: new Profile(Nickname: "pending", Level: 1L),
                Outcome: new Outcome.Pending(),
                Suit: Suit.Clubs,
                Fee: 0.01m),
            cancellationToken: cancellationToken));

        var countsLabel = $"pqs-filter-{Guid.NewGuid():N}";
        foreach (long? count in new long?[] { null, 0L, 7L })
        {
            Assert.IsType<ExerciseOutcome<ContractId<OptionalCounts>>.One>(await ledger.TryCreateAsync(
                new OptionalCounts(owner, countsLabel, count), cancellationToken: cancellationToken));
        }

        var leftPairKey = $"pqs-filter-{Guid.NewGuid():N}";
        Assert.IsType<ExerciseOutcome<ContractId<TypeCorners>>.One>(await ledger.TryCreateAsync(
            new TypeCorners(
                Owner: owner,
                BoxedText: new Box<string>("boxed"),
                BoxedProfile: new Box<Profile>(new Profile("ace", 7)),
                Slot: new Slot<long>.Filled(11),
                NestedNote: new Box<Optional<string>>(new Optional<string>.None()),
                MaybeMaybeNote: new Optional<Optional<string>>.Some(new Optional<string>.Some("deep")),
                Crate: new Crate<string>(new Optional<string>.Some("crated")),
                QuotaByParty: new Dictionary<Party, long> { [owner] = 5 },
                LabelByRank: new Dictionary<long, string> { [1] = "gold" },
                RankOrLabel: new Either<long, string>.Left(5),
                NoteOrRank: new Either<Optional<string>, long>.Left(new Optional<string>.Some("noted")),
                Pair: new Tuple2<string, long>(leftPairKey, 3),
                Triple: new Tuple3<string, long, bool>("triple", 4, true),
                Branch: new Branch("root", [new Branch("leaf", [])]),
                Whole: 42m,
                Finest: 0.1234567890123456789012345678m),
            cancellationToken: cancellationToken));

        var pairKey = $"pqs-filter-{Guid.NewGuid():N}";
        Assert.IsType<ExerciseOutcome<ContractId<TypeCorners>>.One>(await ledger.TryCreateAsync(
            new TypeCorners(
                Owner: owner,
                BoxedText: new Box<string>("boxed"),
                BoxedProfile: new Box<Profile>(new Profile("ace", 7)),
                Slot: new Slot<long>.Filled(11),
                NestedNote: new Box<Optional<string>>(new Optional<string>.None()),
                MaybeMaybeNote: new Optional<Optional<string>>.Some(new Optional<string>.Some("deep")),
                Crate: new Crate<string>(new Optional<string>.Some("crated")),
                QuotaByParty: new Dictionary<Party, long> { [owner] = 5 },
                LabelByRank: new Dictionary<long, string> { [1] = "gold" },
                RankOrLabel: new Either<long, string>.Right("runner-up"),
                NoteOrRank: new Either<Optional<string>, long>.Left(new Optional<string>.Some("noted")),
                Pair: new Tuple2<string, long>(pairKey, 3),
                Triple: new Tuple3<string, long, bool>("triple", 4, true),
                Branch: new Branch("root", [new Branch("leaf", [])]),
                Whole: 42m,
                Finest: 0.5m),
            cancellationToken: cancellationToken));

        var seed = new Seeded(connectionString, owner, label, pairKey, pendingLabel, countsLabel, leftPairKey);
        await AwaitProjectionAsync<RichRecord>(Filter.Field<RichRecord>(r => r.Label, label), seed);
        await AwaitProjectionAsync<RichRecord>(Filter.Field<RichRecord>(r => r.Label, pendingLabel), seed);
        await AwaitProjectionAsync<TypeCorners>(PairKey(pairKey), seed);
        await AwaitProjectionAsync<TypeCorners>(PairKey(leftPairKey), seed);
        await AwaitCountsAsync(seed);
        return seed;
    }

    private static async Task AwaitProjectionAsync<T>(PqsFilter filter, Seeded seed)
        where T : ITemplate, IDamlRecord<T>
    {
        var deadline = DateTimeOffset.UtcNow.Add(ProjectionTimeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if ((await QueryAsync<T>(filter, seed)).Count > 0) return;
            await Task.Delay(PollInterval, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"The seeded {typeof(T).Name} contract was not projected into PQS within {ProjectionTimeout}.");
    }

    private static async Task AwaitCountsAsync(Seeded seed)
    {
        var deadline = DateTimeOffset.UtcNow.Add(ProjectionTimeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if ((await QueryAsync<OptionalCounts>(Filter.Where<OptionalCounts>(n => n.Label == seed.CountsLabel), seed)).Count == 3) return;
            await Task.Delay(PollInterval, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"The seeded OptionalCounts contracts were not projected into PQS within {ProjectionTimeout}.");
    }

    private static async Task<Party> ScribeReadablePartyAsync(IAdminClient admin, string userId)
    {
        var user = await admin.GetUserAsync(userId, TestContext.Current.CancellationToken);
        Assert.True(
            user?.PrimaryParty is not null,
            $"user '{userId}' has no primary party; the LocalNet PQS scribe only reads the validator operator party");
        return user!.PrimaryParty!.Value;
    }

    private static ServiceProvider PqsServices(string connectionString) =>
        new ServiceCollection()
            .AddPqsClient(options => options.ConnectionString = connectionString)
            .BuildServiceProvider();

    private static string RequirePqsConnectionString()
    {
        if (!EndpointDiscovery.IsLocalnetAvailable())
            Assert.Skip(LocalnetSkipMessage);

        var connectionString = Environment.GetEnvironmentVariable(PqsConnectionStringEnv);
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Skip(PqsSkipMessage);

        return connectionString!;
    }

    private sealed record Seeded(
        string ConnectionString,
        Party Owner,
        string Label,
        string PairKey,
        string PendingLabel,
        string CountsLabel,
        string LeftPairKey);
}
