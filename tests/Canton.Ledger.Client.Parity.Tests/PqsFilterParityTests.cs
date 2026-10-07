// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed record PqsFilterParityLane(IPqsClient Client, PqsFilterSeed Seed);

/// <summary>
/// One table of PQS filter cases run against every <see cref="IPqsClient"/> flavour. Each row names
/// a filter over the seeded conformance contracts and the literal set of seeded contracts it must
/// return, so the in-memory evaluator of the fake and the SQL a live PQS runs cannot drift apart
/// without a row failing. Every query is scoped to the seeded contracts of the run, so contracts
/// already on a shared ledger never change an outcome.
/// </summary>
public abstract class PqsFilterParityTests
{
    private static readonly PqsFilterSeed RejectionSeed = new(
        new Party("rejection::0000"),
        "rejection-run",
        new ContractId<Marker>("marker-a"),
        new ContractId<Marker>("marker-b"),
        new ContractId<IHolding>("holding"),
        new ContractId<IHolding>("other-holding"));

    public static TheoryData<string> CaseNames => [.. PqsFilterParityCases.Names];

    public static TheoryData<string> RejectedCaseNames => [.. PqsFilterParityCases.RejectedNames];

    public static TheoryData<int, int, int> Pages => new()
    {
        { 2, 0, 2 },
        { 5, 2, 1 },
        { 5, 3, 0 },
        { 1, 0, 1 },
    };

    /// <summary>Opens a lane over this provider's <see cref="IPqsClient"/> holding the seeded contracts.</summary>
    protected abstract Task<CapabilityLane<PqsFilterParityLane>> OpenAsync(CancellationToken cancellationToken);

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task PqsFilter_case_returns_exactly_the_expected_seed_contracts(string caseName)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var parityCase = PqsFilterParityCases.All.Single(c => c.Name == caseName);
        await using var lane = await OpenAsync(cancellationToken);
        var (client, seed) = lane.Capability;

        var matched = await MatchedNamesAsync(
            client, seed, parityCase.Target, Filter.And(Scope(parityCase.Target, seed), parityCase.Filter(seed)), null, cancellationToken);

        Assert.Equal(parityCase.Expected.Order(StringComparer.Ordinal), matched.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(RejectedCaseNames))]
    public void PqsFilter_rejected_case_throws_the_unsupported_expression_error(string caseName)
    {
        var rejected = PqsFilterParityCases.Rejected.Single(c => c.Name == caseName);

        var failure = Assert.Throws<ArgumentException>(() => rejected.Build(RejectionSeed));

        Assert.StartsWith("Unsupported expression in a PQS filter", failure.Message);
        Assert.Equal("predicate", failure.ParamName);
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task PqsFilter_page_slices_the_matches_of_the_scoped_filter(int limit, int offset, int expectedCount)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var lane = await OpenAsync(cancellationToken);
        var (client, seed) = lane.Capability;

        var scope = Scope(PqsFilterTarget.RichRecord, seed);
        var inContractIdOrder = (await client.QueryAsync<RichRecord>(scope, cancellationToken))
            .OrderBy(contract => contract.Id.Value, StringComparer.Ordinal)
            .Select(contract => seed.NameOf(contract.Data.Label))
            .ToList();

        var matched = await MatchedNamesAsync(
            client, seed, PqsFilterTarget.RichRecord, scope, new PqsPage(limit, offset), cancellationToken);

        Assert.Equal(expectedCount, matched.Count);
        Assert.Equal(inContractIdOrder.Skip(offset).Take(limit), matched);
    }

    internal static PqsFilter Scope(PqsFilterTarget target, PqsFilterSeed seed) => target switch
    {
        PqsFilterTarget.RichRecord => Filter.Or([.. PqsFilterParitySeed.RichRecordNames.Select(name =>
            Filter.Field<RichRecord>(r => r.Label, seed.Key(name)))]),
        PqsFilterTarget.OptionalCounts => Filter.Or([.. PqsFilterParitySeed.OptionalCountsNames.Select(name =>
            Filter.Field<OptionalCounts>(n => n.Label, seed.Key(name)))]),
        PqsFilterTarget.TypeCorners => Filter.Or([.. PqsFilterParitySeed.TypeCornersNames.Select(name =>
            Filter.Field<TypeCorners>(t => t.Pair._1, seed.Key(name)))]),
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };

    private static async Task<IReadOnlyList<string>> MatchedNamesAsync(
        IPqsClient client, PqsFilterSeed seed, PqsFilterTarget target, PqsFilter filter, PqsPage? page,
        CancellationToken cancellationToken)
    {
        switch (target)
        {
            case PqsFilterTarget.RichRecord:
                var richRecords = page is null
                    ? await client.QueryAsync<RichRecord>(filter, cancellationToken)
                    : await client.QueryAsync<RichRecord>(filter, page, cancellationToken);
                return [.. richRecords.Select(c => seed.NameOf(c.Data.Label))];
            case PqsFilterTarget.OptionalCounts:
                var counts = page is null
                    ? await client.QueryAsync<OptionalCounts>(filter, cancellationToken)
                    : await client.QueryAsync<OptionalCounts>(filter, page, cancellationToken);
                return [.. counts.Select(c => seed.NameOf(c.Data.Label))];
            case PqsFilterTarget.TypeCorners:
                var corners = page is null
                    ? await client.QueryAsync<TypeCorners>(filter, cancellationToken)
                    : await client.QueryAsync<TypeCorners>(filter, page, cancellationToken);
                return [.. corners.Select(c => seed.NameOf(c.Data.Pair._1))];
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }
    }
}
