// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.DefaultTarget;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class FakePqsClientConsumerScenarioTests
{
    private static readonly Party Alice = new("alice");
    private static readonly Party Bob = new("bob");

    [Fact]
    public async Task QueryOneAsync_returns_only_the_draft_of_the_asking_party_when_two_parties_share_a_draft_id()
    {
        var bobsDraft = new Contract<Note>(new ContractId<Note>("bob-draft"), new Note(Bob, "draft-7", 1));
        var alicesDraft = new Contract<Note>(new ContractId<Note>("alice-draft"), new Note(Alice, "draft-7", 1));
        var client = FakePqsClient.Create().WithQueryResults(bobsDraft, alicesDraft).Build();
        const string draftId = "draft-7";
        var requester = Alice;

        var found = await client.QueryOneAsync<Note>(
            Filter.Where<Note>(draft => draft.Body == draftId && draft.Owner == requester),
            TestContext.Current.CancellationToken);

        found.Should().Be(alicesDraft);
    }

    [Fact]
    public async Task QueryOneAsync_returns_null_when_only_another_party_holds_the_draft_id()
    {
        var bobsDraft = new Contract<Note>(new ContractId<Note>("bob-draft"), new Note(Bob, "draft-7", 1));
        var client = FakePqsClient.Create().WithQueryResults(bobsDraft).Build();
        const string draftId = "draft-7";
        var requester = Alice;

        var found = await client.QueryOneAsync<Note>(
            Filter.Where<Note>(draft => draft.Body == draftId && draft.Owner == requester),
            TestContext.Current.CancellationToken);

        found.Should().BeNull();
    }
}
