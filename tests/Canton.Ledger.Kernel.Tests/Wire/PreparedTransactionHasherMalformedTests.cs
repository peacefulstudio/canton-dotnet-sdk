// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Com.Daml.Ledger.Api.V2;
using Com.Daml.Ledger.Api.V2.Interactive;
using Google.Protobuf;
using Xunit;
using HashingSchemeVersion = Canton.Ledger.Abstractions.HashingSchemeVersion;
using V1 = Com.Daml.Ledger.Api.V2.Interactive.Transaction.V1;

namespace Canton.Ledger.Kernel.Tests.Wire;

public class PreparedTransactionHasherMalformedTests
{
    [Fact]
    public void HashTransaction_refuses_a_root_the_transaction_does_not_carry()
    {
        var transaction = new DamlTransaction { Version = "2.1", Roots = { "7" } };

        RefusalOf(transaction).Should().Be("the prepared transaction references node '7', which it does not carry");
    }

    [Fact]
    public void HashTransaction_refuses_an_exercise_node_without_a_seed()
    {
        var transaction = CantonHashingFixture.V2.Transaction();
        transaction.NodeSeeds.Remove(transaction.NodeSeeds.Single(seed => seed.NodeId == 4));

        RefusalOf(transaction).Should().Be("the prepared transaction carries no seed for its exercise node '4'");
    }

    [Fact]
    public void HashTransaction_refuses_a_node_that_is_its_own_descendant()
    {
        var transaction = CantonHashingFixture.V2.Transaction();
        transaction.Nodes.Single(node => node.NodeId == "4").V1.Exercise.Children.Add("5");

        RefusalOf(transaction).Should().Be("the prepared transaction's node '5' is its own descendant");
    }

    [Fact]
    public void HashTransaction_refuses_a_node_carried_twice()
    {
        var transaction = CantonHashingFixture.V2.Transaction();
        transaction.Nodes.Add(transaction.Nodes.Single(node => node.NodeId == "3").Clone());

        RefusalOf(transaction).Should().Be("the prepared transaction carries node '3' twice");
    }

    [Fact]
    public void HashTransaction_refuses_a_node_of_no_version()
    {
        var transaction = new DamlTransaction { Version = "2.1", Roots = { "0" } };
        transaction.Nodes.Add(new DamlTransaction.Types.Node { NodeId = "0" });

        RefusalOf(transaction).Should().Be("the prepared transaction's node '0' is of no version this SDK can hash");
    }

    [Fact]
    public void HashTransaction_refuses_a_node_of_no_kind()
    {
        var transaction = new DamlTransaction { Version = "2.1", Roots = { "0" } };
        transaction.Nodes.Add(new DamlTransaction.Types.Node { NodeId = "0", V1 = new V1.Node() });

        RefusalOf(transaction).Should().Be("the prepared transaction's node '0' is of no kind this SDK can hash");
    }

    [Fact]
    public void HashTransaction_refuses_a_query_by_key_node_under_V2()
    {
        var transaction = new DamlTransaction { Version = "2.1", Roots = { "0" } };
        transaction.Nodes.Add(new DamlTransaction.Types.Node
        {
            NodeId = "0",
            V1 = new V1.Node { QueryByKey = CantonHashingFixture.QueryByKey() },
        });

        RefusalOf(transaction).Should().Be("the prepared transaction's node '0' is of no kind this SDK can hash");
    }

    [Fact]
    public void HashTransaction_refuses_a_create_without_an_argument()
    {
        var transaction = CantonHashingFixture.V2.Transaction();
        transaction.Nodes.Single(node => node.NodeId == "0").V1.Create.Argument = null;

        RefusalOf(transaction).Should().Be("the prepared transaction carries a value of no kind");
    }

    [Fact]
    public void HashTransaction_refuses_a_contract_id_that_is_not_hexadecimal()
    {
        var transaction = CantonHashingFixture.V2.Transaction();
        transaction.Nodes.Single(node => node.NodeId == "0").V1.Create.ContractId = "00zz";

        RefusalOf(transaction).Should().Be(
            "the prepared transaction carries the contract id of a created contract '00zz', which is not a hexadecimal string");
    }

    [Fact]
    public void HashMetadata_refuses_an_input_contract_of_no_version()
    {
        var metadata = CantonHashingFixture.V2.Metadata();
        metadata.InputContracts.Add(new Metadata.Types.InputContract { CreatedAt = 1, EventBlob = ByteString.CopyFrom(1) });

        var act = () => PreparedTransactionHasher.HashMetadata(metadata, HashingSchemeVersion.V2);

        act.Should().Throw<MalformedResponseException>().Which.Detail.Should().Be(
            "the prepared transaction carries an input contract of no version this SDK can hash");
    }

    [Fact]
    public void HashTransaction_hashes_a_chain_of_nodes_deeper_than_the_call_stack_could_recurse()
    {
        const int depth = 200_000;
        var transaction = new DamlTransaction { Version = "2.1", Roots = { "0" } };
        for (var nodeId = 0; nodeId < depth; nodeId++)
        {
            var rollback = new V1.Rollback();
            if (nodeId + 1 < depth) rollback.Children.Add((nodeId + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            transaction.Nodes.Add(new DamlTransaction.Types.Node
            {
                NodeId = nodeId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                V1 = new V1.Node { Rollback = rollback },
            });
        }

        PreparedTransactionHasher.HashTransaction(transaction, HashingSchemeVersion.V2).Should().HaveCount(32);
    }

    private static string RefusalOf(DamlTransaction transaction)
    {
        var act = () => PreparedTransactionHasher.HashTransaction(transaction, HashingSchemeVersion.V2);
        return act.Should().Throw<MalformedResponseException>().Which.Detail;
    }
}
