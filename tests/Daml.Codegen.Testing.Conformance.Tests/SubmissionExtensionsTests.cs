// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class SubmissionExtensionsTests
{
    private const string AlicePartyId = "alice";

    [Fact]
    public async Task Marker_create_async_projects_a_created_contract_id()
    {
        using var client = new FakeLedgerClient(
            create: _ => new ExerciseOutcome<object>.One("marker-cid"));
        var payload = new Marker(new Party(AlicePartyId));

        var outcome = await client.TryCreateAsync(payload, TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<Marker>>.One>();
        ((ExerciseOutcome<ContractId<Marker>>.One)outcome).Result.Value.Should().Be("marker-cid");
    }

    [Fact]
    public async Task Marker_create_async_submits_as_the_owner_carried_by_the_payload()
    {
        using var client = new FakeLedgerClient(
            create: _ => new ExerciseOutcome<object>.One("marker-cid"));
        var payload = new Marker(new Party(AlicePartyId));

        await client.TryCreateAsync(payload, TestContext.Current.CancellationToken);

        client.LastCreateSubmitter.Should().NotBeNull(
            "the payload-derived overload takes no submitter argument, so the wrapper is the only thing that can supply one");
        client.LastCreateSubmitter!.Value.ActAs.Should().ContainSingle().Which.Should().Be(
            new Party(AlicePartyId),
            "the sole Daml signatory is the payload's owner field, so the wrapper must act as that party and no other");
    }

    [Fact]
    public async Task Marker_create_async_throws_on_null_payload()
    {
        using var client = new FakeLedgerClient();

        var act = async () => await client.TryCreateAsync((Marker)null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RichRecord_create_async_projects_a_created_contract_id()
    {
        using var client = new FakeLedgerClient(
            create: _ => new ExerciseOutcome<object>.One("rich-cid"));
        var payload = new RichRecord(
            Owner: new Party(AlicePartyId),
            Count: 1,
            Amount: 1m,
            Label: "l",
            Active: false,
            AsOf: new DateOnly(2026, 1, 1),
            ObservedAt: DateTimeOffset.UnixEpoch,
            Note: null,
            Tags: new List<string>(),
            Attributes: new Dictionary<string, string>(),
            Marker: new ContractId<Marker>("m"),
            HoldingCid: new ContractId<IHolding>("00h"),
            HoldingCids: new List<ContractId<IHolding>>(),
            Profile: new Profile("n", 0),
            Outcome: new Outcome.Pending(),
            Suit: Suit.Diamonds,
            Fee: 0m);

        var outcome = await client.TryCreateAsync(payload, TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<RichRecord>>.One>();
        ((ExerciseOutcome<ContractId<RichRecord>>.One)outcome).Result.Value.Should().Be("rich-cid");
        client.LastCreateSubmitter!.Value.ActAs.Should().ContainSingle().Which.Should().Be(
            new Party(AlicePartyId),
            "a payload carrying many non-party fields must still resolve its submitter from the owner field alone");
    }
}
