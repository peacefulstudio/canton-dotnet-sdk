// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class FakePqsClientTests
{
    private static readonly Party Alice = new("alice");

    [Fact]
    public async Task QueryAsync_returns_the_staged_contracts_for_the_template_type()
    {
        var contract = new Contract<DemoHolding>(new ContractId<DemoHolding>("cid1"), new DemoHolding(Alice, 42m));
        var client = FakePqsClient.Create().WithQueryResults(contract).Build();

        var results = await client.QueryAsync<DemoHolding>(TestContext.Current.CancellationToken);

        results.Should().ContainSingle().Which.Should().Be(contract);
    }

    [Fact]
    public async Task QueryAsync_for_unstaged_type_throws_descriptive_NotSupportedException()
    {
        var client = FakePqsClient.Create()
            .WithQueryResults(new Contract<DemoHolding>(new ContractId<DemoHolding>("cid1"), new DemoHolding(Alice, 42m)))
            .Build();

        var act = () => client.QueryAsync<OtherHolding>();

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithQueryResults").And.Contain("OtherHolding");
    }

    [Fact]
    public async Task QueryAsync_with_filter_returns_only_the_staged_contracts_the_filter_matches()
    {
        var alices = new Contract<DemoHolding>(new ContractId<DemoHolding>("cid1"), new DemoHolding(Alice, 42m));
        var bobs = new Contract<DemoHolding>(new ContractId<DemoHolding>("cid2"), new DemoHolding(new Party("bob"), 7m));
        var client = FakePqsClient.Create().WithQueryResults(alices, bobs).Build();

        var results = await client.QueryAsync<DemoHolding>(
            Filter.Field<DemoHolding>(h => h.Owner, "bob"), TestContext.Current.CancellationToken);

        results.Should().Equal(bobs);
    }

    [Fact]
    public async Task QueryAsync_with_filter_returns_nothing_when_no_staged_contract_matches()
    {
        var contract = new Contract<DemoHolding>(new ContractId<DemoHolding>("cid1"), new DemoHolding(Alice, 42m));
        var client = FakePqsClient.Create().WithQueryResults(contract).Build();

        var results = await client.QueryAsync<DemoHolding>(
            Filter.Field<DemoHolding>(h => h.Owner, "bob"), TestContext.Current.CancellationToken);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_with_filter_keeps_staging_order_among_the_matches()
    {
        var client = FakePqsClient.Create().WithQueryResults(StagedHoldings(5)).Build();

        var results = await client.QueryAsync<DemoHolding>(
            Filter.Where<DemoHolding>(h => h.Amount != 2m && h.Amount != 4m), TestContext.Current.CancellationToken);

        results.Select(c => c.Id.Value).Should().Equal("cid1", "cid3", "cid5");
    }

    [Fact]
    public async Task QueryAsync_with_filter_throws_for_null_filter()
    {
        var client = FakePqsClient.Create().WithQueryResults<DemoHolding>().Build();

        var act = () => client.QueryAsync<DemoHolding>(filter: null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task QueryOneAsync_returns_the_first_staged_contract_the_filter_matches()
    {
        var first = new Contract<DemoHolding>(new ContractId<DemoHolding>("cid1"), new DemoHolding(new Party("bob"), 1m));
        var second = new Contract<DemoHolding>(new ContractId<DemoHolding>("cid2"), new DemoHolding(Alice, 2m));
        var third = new Contract<DemoHolding>(new ContractId<DemoHolding>("cid3"), new DemoHolding(Alice, 3m));
        var client = FakePqsClient.Create().WithQueryResults(first, second, third).Build();
        var filter = Filter.Field<DemoHolding>(h => h.Owner, "alice");

        var result = await client.QueryOneAsync<DemoHolding>(filter, TestContext.Current.CancellationToken);

        result.Should().Be(second);
    }

    [Fact]
    public async Task QueryOneAsync_returns_null_when_staged_contracts_exist_but_none_match()
    {
        var contract = new Contract<DemoHolding>(new ContractId<DemoHolding>("cid1"), new DemoHolding(Alice, 1m));
        var client = FakePqsClient.Create().WithQueryResults(contract).Build();
        var filter = Filter.Field<DemoHolding>(h => h.Owner, "bob");

        var result = await client.QueryOneAsync<DemoHolding>(filter, TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task QueryAsync_with_filter_evaluates_every_filter_shape_against_the_serialized_contract_payload()
    {
        var matching = RichContract("rich-1", count: 42, label: "alpha", tags: ["urgent", "blue"]);
        var other = RichContract("rich-2", count: 1, label: "beta", tags: []);
        var client = FakePqsClient.Create().WithQueryResults(matching, other).Build();
        var filter = Filter.Where<RichRecord>(r => r.Count > 5 && r.Tags.Any(tag => tag == "blue")
            && r.Amount == 12.34m && r.Profile.Level == 7 && r.Outcome is Outcome.Win && r.Suit == Suit.Hearts);

        var results = await client.QueryAsync<RichRecord>(filter, TestContext.Current.CancellationToken);

        results.Should().Equal(matching);
    }

    [Fact]
    public async Task QueryOneAsync_returns_null_when_staged_results_are_empty()
    {
        var client = FakePqsClient.Create().WithQueryResults<DemoHolding>().Build();
        var filter = Filter.Field<DemoHolding>(h => h.Owner, "alice");

        var result = await client.QueryOneAsync<DemoHolding>(filter, TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task QueryOneAsync_for_unstaged_type_throws_descriptive_NotSupportedException()
    {
        var client = FakePqsClient.Create().Build();
        var filter = Filter.Field<DemoHolding>(h => h.Owner, "alice");

        var act = () => client.QueryOneAsync<DemoHolding>(filter);

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithQueryResults").And.Contain("DemoHolding");
    }

    [Fact]
    public async Task FetchByIdAsync_returns_the_matching_staged_contract()
    {
        var cid = new ContractId<DemoHolding>("cid1");
        var contract = new Contract<DemoHolding>(cid, new DemoHolding(Alice, 42m));
        var client = FakePqsClient.Create().WithQueryResults(contract).Build();

        var result = await client.FetchByIdAsync(cid, TestContext.Current.CancellationToken);

        result.Should().Be(contract);
    }

    [Fact]
    public async Task FetchByIdAsync_returns_null_when_no_staged_contract_matches()
    {
        var contract = new Contract<DemoHolding>(new ContractId<DemoHolding>("cid1"), new DemoHolding(Alice, 42m));
        var client = FakePqsClient.Create().WithQueryResults(contract).Build();

        var result = await client.FetchByIdAsync(new ContractId<DemoHolding>("cid-missing"), TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchByIdAsync_for_unstaged_type_throws_descriptive_NotSupportedException()
    {
        var client = FakePqsClient.Create().Build();

        var act = () => client.FetchByIdAsync(new ContractId<DemoHolding>("cid1"));

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithQueryResults").And.Contain("DemoHolding");
    }

    [Fact]
    public async Task FetchByIdAsync_interface_returns_the_matching_staged_contract()
    {
        var cid = new ContractId<IDemoHoldingView>("cid1");
        var contract = new InterfaceContract<IDemoHoldingView, DemoHoldingView>(cid, new DemoHoldingView(42m));
        var client = FakePqsClient.Create().WithInterfaceQueryResults(contract).Build();

        var result = await client.FetchByIdAsync<IDemoHoldingView, DemoHoldingView>(
            cid, TestContext.Current.CancellationToken);

        result.Should().Be(contract);
    }

    [Fact]
    public async Task FetchByIdAsync_interface_returns_null_when_no_staged_contract_matches()
    {
        var contract = new InterfaceContract<IDemoHoldingView, DemoHoldingView>(
            new ContractId<IDemoHoldingView>("cid1"), new DemoHoldingView(42m));
        var client = FakePqsClient.Create().WithInterfaceQueryResults(contract).Build();

        var result = await client.FetchByIdAsync<IDemoHoldingView, DemoHoldingView>(
            new ContractId<IDemoHoldingView>("cid-missing"), TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchByIdAsync_interface_for_unstaged_interface_throws_descriptive_NotSupportedException()
    {
        var client = FakePqsClient.Create().Build();

        var act = () => client.FetchByIdAsync<IDemoHoldingView, DemoHoldingView>(
            new ContractId<IDemoHoldingView>("cid1"));

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithInterfaceQueryResults").And.Contain("IDemoHoldingView");
    }

    [Fact]
    public async Task ExistsAsync_returns_true_when_a_staged_contract_matches()
    {
        var cid = new ContractId<DemoHolding>("cid1");
        var contract = new Contract<DemoHolding>(cid, new DemoHolding(Alice, 42m));
        var client = FakePqsClient.Create().WithQueryResults(contract).Build();

        var exists = await client.ExistsAsync(cid, TestContext.Current.CancellationToken);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_returns_false_when_no_staged_contract_matches()
    {
        var contract = new Contract<DemoHolding>(new ContractId<DemoHolding>("cid1"), new DemoHolding(Alice, 42m));
        var client = FakePqsClient.Create().WithQueryResults(contract).Build();

        var exists = await client.ExistsAsync(new ContractId<DemoHolding>("cid-missing"), TestContext.Current.CancellationToken);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_for_unstaged_type_throws_descriptive_NotSupportedException()
    {
        var client = FakePqsClient.Create().Build();

        var act = () => client.ExistsAsync(new ContractId<DemoHolding>("cid1"));

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithQueryResults").And.Contain("DemoHolding");
    }

    [Fact]
    public async Task QueryAsync_interface_returns_the_staged_interface_contracts()
    {
        var contract = new InterfaceContract<IDemoHoldingView, DemoHoldingView>(
            new ContractId<IDemoHoldingView>("cid1"), new DemoHoldingView(42m));
        var client = FakePqsClient.Create().WithInterfaceQueryResults(contract).Build();

        var results = await client.QueryAsync<IDemoHoldingView, DemoHoldingView>(TestContext.Current.CancellationToken);

        results.Should().ContainSingle().Which.Should().Be(contract);
    }

    [Fact]
    public async Task QueryAsync_interface_for_unstaged_interface_throws_descriptive_NotSupportedException()
    {
        var client = FakePqsClient.Create().Build();

        var act = () => client.QueryAsync<IDemoHoldingView, DemoHoldingView>();

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithInterfaceQueryResults").And.Contain("IDemoHoldingView");
    }

    [Theory]
    [InlineData(2, 0, new[] { "cid1", "cid2" })]
    [InlineData(2, 2, new[] { "cid3", "cid4" })]
    [InlineData(2, 4, new[] { "cid5" })]
    [InlineData(2, 6, new string[] { })]
    public async Task QueryAsync_with_page_returns_the_staged_slice(
        int limit, int offset, string[] expectedContractIds)
    {
        var client = FakePqsClient.Create().WithQueryResults(StagedHoldings(5)).Build();

        var page = await client.QueryAsync<DemoHolding>(
            new PqsPage(limit, offset), TestContext.Current.CancellationToken);

        page.Select(c => c.Id.Value).Should().Equal(expectedContractIds);
    }

    [Fact]
    public async Task QueryAsync_with_page_for_unstaged_type_throws_descriptive_NotSupportedException()
    {
        var client = FakePqsClient.Create().Build();

        var act = () => client.QueryAsync<DemoHolding>(new PqsPage(limit: 2));

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithQueryResults").And.Contain("DemoHolding");
    }

    [Fact]
    public async Task QueryAsync_with_page_throws_for_null_page()
    {
        var client = FakePqsClient.Create().WithQueryResults<DemoHolding>().Build();

        var act = () => client.QueryAsync<DemoHolding>(page: null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("page");
    }

    [Fact]
    public async Task QueryAsync_with_filter_and_page_filters_first_then_slices_the_matches_in_contract_id_order()
    {
        var client = FakePqsClient.Create().WithQueryResults(StagedHoldings(7)).Build();
        var filter = Filter.Where<DemoHolding>(h => h.Amount != 2m && h.Amount != 5m);

        var page = await client.QueryAsync<DemoHolding>(
            filter, new PqsPage(limit: 2, offset: 1), TestContext.Current.CancellationToken);

        page.Select(c => c.Id.Value).Should().Equal("cid3", "cid4");
    }

    [Fact]
    public async Task QueryAsync_with_filter_and_page_returns_an_empty_page_past_the_last_match()
    {
        var client = FakePqsClient.Create().WithQueryResults(StagedHoldings(4)).Build();
        var filter = Filter.Where<DemoHolding>(h => h.Amount > 2m);

        var page = await client.QueryAsync<DemoHolding>(
            filter, new PqsPage(limit: 5, offset: 2), TestContext.Current.CancellationToken);

        page.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_with_filter_and_page_throws_for_null_page()
    {
        var client = FakePqsClient.Create().WithQueryResults<DemoHolding>().Build();
        var filter = Filter.Field<DemoHolding>(h => h.Owner, "bob");

        var act = () => client.QueryAsync<DemoHolding>(filter, page: null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("page");
    }

    [Fact]
    public async Task QueryAsync_with_filter_and_page_throws_for_null_filter()
    {
        var client = FakePqsClient.Create().WithQueryResults<DemoHolding>().Build();

        var act = () => client.QueryAsync<DemoHolding>(filter: null!, new PqsPage(limit: 10));

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("filter");
    }

    [Fact]
    public async Task QueryAsync_interface_with_page_throws_for_null_page()
    {
        var client = FakePqsClient.Create().Build();

        var act = () => client.QueryAsync<IDemoHoldingView, DemoHoldingView>(page: null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("page");
    }

    [Fact]
    public async Task QueryAsync_interface_with_page_returns_the_staged_slice()
    {
        var first = new InterfaceContract<IDemoHoldingView, DemoHoldingView>(
            new ContractId<IDemoHoldingView>("cid1"), new DemoHoldingView(1m));
        var second = new InterfaceContract<IDemoHoldingView, DemoHoldingView>(
            new ContractId<IDemoHoldingView>("cid2"), new DemoHoldingView(2m));
        var third = new InterfaceContract<IDemoHoldingView, DemoHoldingView>(
            new ContractId<IDemoHoldingView>("cid3"), new DemoHoldingView(3m));
        var client = FakePqsClient.Create().WithInterfaceQueryResults(first, second, third).Build();

        var page = await client.QueryAsync<IDemoHoldingView, DemoHoldingView>(
            new PqsPage(limit: 2, offset: 1), TestContext.Current.CancellationToken);

        page.Should().Equal(second, third);
    }

    [Fact]
    public async Task QueryAsync_interface_with_page_for_unstaged_interface_throws_descriptive_NotSupportedException()
    {
        var client = FakePqsClient.Create().Build();

        var act = () => client.QueryAsync<IDemoHoldingView, DemoHoldingView>(new PqsPage(limit: 2));

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithInterfaceQueryResults").And.Contain("IDemoHoldingView");
    }

    private static Contract<RichRecord> RichContract(string id, long count, string label, IReadOnlyList<string> tags) =>
        new(
            new ContractId<RichRecord>(id),
            new RichRecord(
                Owner: Alice,
                Count: count,
                Amount: 12.34m,
                Label: label,
                Active: true,
                AsOf: new DateOnly(2026, 5, 29),
                ObservedAt: new DateTimeOffset(2026, 5, 29, 13, 30, 0, TimeSpan.Zero),
                Note: "hello",
                Tags: tags,
                Attributes: new Dictionary<string, string> { ["k1"] = "v1" },
                Marker: new ContractId<Marker>("marker-1"),
                HoldingCid: new ContractId<IHolding>("holding-1"),
                HoldingCids: [new ContractId<IHolding>("holding-1")],
                Profile: new Profile(Nickname: "cdg", Level: 7L),
                Outcome: new Outcome.Win(new Outcome_Win(Prize: 250.50m, Tier: "gold")),
                Suit: Suit.Hearts,
                Fee: 0.05m));

    private static Contract<DemoHolding>[] StagedHoldings(int count) =>
        [.. Enumerable.Range(1, count).Select(i =>
            new Contract<DemoHolding>(new ContractId<DemoHolding>($"cid{i}"), new DemoHolding(Alice, i)))];

    private static Contract<DemoHolding>[] HoldingsStagedOutOfContractIdOrder() =>
    [
        new(new ContractId<DemoHolding>("00cc"), new DemoHolding(Alice, 3m)),
        new(new ContractId<DemoHolding>("00aa"), new DemoHolding(Alice, 1m)),
        new(new ContractId<DemoHolding>("00bb"), new DemoHolding(Alice, 2m)),
    ];

    [Theory]
    [InlineData(1, 0, new[] { "00aa" })]
    [InlineData(1, 1, new[] { "00bb" })]
    [InlineData(2, 1, new[] { "00bb", "00cc" })]
    [InlineData(3, 0, new[] { "00aa", "00bb", "00cc" })]
    public async Task QueryAsync_with_page_slices_in_contract_id_order_not_staging_order(
        int limit, int offset, string[] expectedContractIds)
    {
        var client = FakePqsClient.Create().WithQueryResults(HoldingsStagedOutOfContractIdOrder()).Build();

        var page = await client.QueryAsync<DemoHolding>(
            new PqsPage(limit, offset), TestContext.Current.CancellationToken);

        page.Select(c => c.Id.Value).Should().Equal(expectedContractIds);
    }

    [Theory]
    [InlineData(1, 0, new[] { "00aa" })]
    [InlineData(1, 1, new[] { "00bb" })]
    [InlineData(2, 1, new[] { "00bb", "00cc" })]
    [InlineData(3, 0, new[] { "00aa", "00bb", "00cc" })]
    public async Task QueryAsync_with_filter_and_page_slices_in_contract_id_order_not_staging_order(
        int limit, int offset, string[] expectedContractIds)
    {
        var client = FakePqsClient.Create().WithQueryResults(HoldingsStagedOutOfContractIdOrder()).Build();
        var filter = Filter.Where<DemoHolding>(h => h.Amount > 0m);

        var page = await client.QueryAsync<DemoHolding>(
            filter, new PqsPage(limit, offset), TestContext.Current.CancellationToken);

        page.Select(c => c.Id.Value).Should().Equal(expectedContractIds);
    }

    [Fact]
    public async Task QueryAsync_with_filter_and_page_orders_only_the_matches_by_contract_id()
    {
        var client = FakePqsClient.Create().WithQueryResults(HoldingsStagedOutOfContractIdOrder()).Build();
        var filter = Filter.Where<DemoHolding>(h => h.Amount != 1m);

        var page = await client.QueryAsync<DemoHolding>(
            filter, new PqsPage(limit: 1, offset: 0), TestContext.Current.CancellationToken);

        page.Select(c => c.Id.Value).Should().Equal("00bb");
    }

    [Theory]
    [InlineData(1, 0, new[] { "00aa" })]
    [InlineData(1, 1, new[] { "00bb" })]
    [InlineData(2, 1, new[] { "00bb", "00cc" })]
    public async Task QueryAsync_interface_with_page_slices_in_contract_id_order_not_staging_order(
        int limit, int offset, string[] expectedContractIds)
    {
        var client = FakePqsClient.Create()
            .WithInterfaceQueryResults(
                new InterfaceContract<IDemoHoldingView, DemoHoldingView>(
                    new ContractId<IDemoHoldingView>("00cc"), new DemoHoldingView(3m)),
                new InterfaceContract<IDemoHoldingView, DemoHoldingView>(
                    new ContractId<IDemoHoldingView>("00aa"), new DemoHoldingView(1m)),
                new InterfaceContract<IDemoHoldingView, DemoHoldingView>(
                    new ContractId<IDemoHoldingView>("00bb"), new DemoHoldingView(2m)))
            .Build();

        var page = await client.QueryAsync<IDemoHoldingView, DemoHoldingView>(
            new PqsPage(limit, offset), TestContext.Current.CancellationToken);

        page.Select(c => c.Id.Value).Should().Equal(expectedContractIds);
    }

    [Fact]
    public async Task QueryAsync_with_page_orders_contract_ids_by_ordinal_code_point()
    {
        var client = FakePqsClient.Create()
            .WithQueryResults(
                new Contract<DemoHolding>(new ContractId<DemoHolding>("cid10"), new DemoHolding(Alice, 1m)),
                new Contract<DemoHolding>(new ContractId<DemoHolding>("cid-2"), new DemoHolding(Alice, 2m)),
                new Contract<DemoHolding>(new ContractId<DemoHolding>("cid2"), new DemoHolding(Alice, 3m)))
            .Build();

        var page = await client.QueryAsync<DemoHolding>(
            new PqsPage(limit: 3), TestContext.Current.CancellationToken);

        page.Select(c => c.Id.Value).Should().Equal("cid-2", "cid10", "cid2");
    }

    [Fact]
    public async Task QueryAsync_without_page_keeps_staging_order()
    {
        var client = FakePqsClient.Create().WithQueryResults(HoldingsStagedOutOfContractIdOrder()).Build();

        var all = await client.QueryAsync<DemoHolding>(TestContext.Current.CancellationToken);

        all.Select(c => c.Id.Value).Should().Equal("00cc", "00aa", "00bb");
    }

    [Fact]
    public async Task Build_snapshots_staged_results_so_later_builder_mutation_is_ignored()
    {
        var builder = FakePqsClient.Create()
            .WithQueryResults(new Contract<DemoHolding>(new ContractId<DemoHolding>("cid1"), new DemoHolding(Alice, 1m)));
        var client = builder.Build();
        builder.WithQueryResults(
            new Contract<DemoHolding>(new ContractId<DemoHolding>("cid1"), new DemoHolding(Alice, 1m)),
            new Contract<DemoHolding>(new ContractId<DemoHolding>("cid2"), new DemoHolding(Alice, 2m)));

        var results = await client.QueryAsync<DemoHolding>(TestContext.Current.CancellationToken);

        results.Should().ContainSingle();
    }

    [Fact]
    public void Payload_returning_template_methods_constrain_T_to_IDamlRecord_like_the_client_they_fake()
    {
        var payloadReturning = typeof(FakePqsClient)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1 && m.Name != "ExistsAsync")
            .Append(typeof(FakePqsClientBuilder).GetMethod(nameof(FakePqsClientBuilder.WithQueryResults))!)
            .ToList();

        payloadReturning.Should().HaveCount(7);
        payloadReturning.Should().OnlyContain(m => ConstrainsToDamlRecord(m));
    }

    [Fact]
    public void ExistsAsync_leaves_T_unconstrained_by_IDamlRecord()
    {
        var exists = typeof(FakePqsClient).GetMethod(nameof(FakePqsClient.ExistsAsync))!;

        ConstrainsToDamlRecord(exists).Should().BeFalse();
    }

    private static bool ConstrainsToDamlRecord(MethodInfo method) =>
        method.GetGenericArguments()[0]
            .GetGenericParameterConstraints()
            .Any(c => c.IsGenericType && c.GetGenericTypeDefinition() == typeof(IDamlRecord<>));
}
