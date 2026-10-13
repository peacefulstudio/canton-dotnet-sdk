// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Wire;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Wire;

public class TopologyMultiHashTests
{
    private static readonly byte[] TransactionA = [0x0A, 0x01];
    private static readonly byte[] TransactionB = [0x0A, 0x02];

    [Fact]
    public void TransactionHash_is_the_purpose_11_sha256_multihash_of_the_serialized_transaction()
    {
        Convert.ToHexStringLower(TopologyMultiHash.TransactionHash(TransactionA))
            .Should().Be("1220fb7c0f2e526cdb197a7bd342a1fdabe1a934bb7d3bb45ab817deecb32020e543");
    }

    [Fact]
    public void Of_one_transaction_matches_the_value_Canton_reference_script_computes()
    {
        Convert.ToHexStringLower(TopologyMultiHash.Of([TransactionA]))
            .Should().Be("1220ad04f928ef32d01b61d7aa10ebc44f84e797ea332c8675a1107912ec2336d318");
    }

    [Fact]
    public void Of_two_transactions_matches_the_value_Canton_reference_script_computes_in_either_order()
    {
        Convert.ToHexStringLower(TopologyMultiHash.Of([TransactionA, TransactionB]))
            .Should().Be("1220e16e2136dd740dc3507bf301fb3359c886d6bb2014ac943fe65c4c6718bb727a");
        Convert.ToHexStringLower(TopologyMultiHash.Of([TransactionB, TransactionA]))
            .Should().Be("1220e16e2136dd740dc3507bf301fb3359c886d6bb2014ac943fe65c4c6718bb727a");
    }

    [Fact]
    public void Of_hashes_a_repeated_transaction_once_as_Canton_combines_a_set_of_hashes()
    {
        Convert.ToHexStringLower(TopologyMultiHash.Of([TransactionA, TransactionA]))
            .Should().Be("1220ad04f928ef32d01b61d7aa10ebc44f84e797ea332c8675a1107912ec2336d318");
    }
}
