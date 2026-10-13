// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Wire;
using Com.Daml.Ledger.Api.V2.Interactive;
using Xunit;
using HashingSchemeVersion = Canton.Ledger.Abstractions.HashingSchemeVersion;
using V1 = Com.Daml.Ledger.Api.V2.Interactive.Transaction.V1;

namespace Canton.Ledger.Kernel.Tests.Wire;

/// <summary>
/// Pins the node, transaction, metadata and prepared-transaction hashes of <see cref="CantonHashingFixture"/>
/// to the vectors of Canton v3.6.1 <c>community/common/src/test/scala/com/digitalasset/canton/protocol/hash/</c>
/// <c>v2/NodeHashTest.scala</c>, <c>v2/MetadataHashTest.scala</c>, <c>v3/NodeHashTest.scala</c> and
/// <c>v3/MetadataHashTest.scala</c>.
/// </summary>
public class PreparedTransactionHasherTests
{
    [Theory]
    [InlineData("0", "6d2cfe58c2294000592034f4bdfe397fe246901bb8b63e3b9e041bb478e174b7")]
    [InlineData("2", "c962c6098394f3d11cd6f0c795de9517d32a8e3e1979cec76cd2f66254efc610")]
    [InlineData("4", "070970eb4b2de72561dafb67017ca33850650a8103e5134e16044ba78991f48c")]
    [InlineData("5", "7264d5da2fd714427453bedc0d1cdb21f52ac7aec8d4bb5ac0598d25c5fcaed9")]
    public void HashNode_under_V2_matches_the_Canton_vector(string nodeId, string expectedHex)
    {
        var hash = PreparedTransactionHasher.HashNode(CantonHashingFixture.V2.Transaction(), nodeId, HashingSchemeVersion.V2);

        Convert.ToHexStringLower(hash).Should().Be(expectedHex);
    }

    [Fact]
    public void HashTransaction_under_V2_matches_the_Canton_vector()
    {
        var hash = PreparedTransactionHasher.HashTransaction(CantonHashingFixture.V2.Transaction(), HashingSchemeVersion.V2);

        Convert.ToHexStringLower(hash).Should().Be("154f334d24a8a5e4d0ce51ac87d93821b3256f885f21d3f779a1640abf481983");
    }

    [Fact]
    public void HashMetadata_under_V2_matches_the_Canton_vector()
    {
        var hash = PreparedTransactionHasher.HashMetadata(CantonHashingFixture.V2.Metadata(), HashingSchemeVersion.V2);

        Convert.ToHexStringLower(hash).Should().Be("6e89fcbcc9605179a47919b5e65a864e470e7a133f4f9f39b1e4545b223db769");
    }

    [Fact]
    public void Hash_under_V2_matches_the_Canton_vector()
    {
        var hash = PreparedTransactionHasher.Hash(CantonHashingFixture.V2.PreparedTransaction(), HashingSchemeVersion.V2);

        Convert.ToHexStringLower(hash).Should().Be("8c311c848db25d36b36fbd59f9483714a11688c2214e3c7cae3e028763520250");
    }

    [Theory]
    [InlineData("0", "0120d370509f54c07d8209f5af2d23f7f972997deb5fc5e0ebe886354395a3bc")]
    [InlineData("2", "20437df1980bb7d4d94ad53181dc6005bae37b5990719070c48a3960e8dda3a4")]
    [InlineData("4", "516c4689acabf9d1f0087a02a7dafad884d9080c48112a57a6d20b9f72d4b697")]
    [InlineData("5", "ea61ab05f9ab9769ab5c91493a601ccad9e784bd4c0b3bc4b7777f0da68eab4d")]
    public void HashNode_under_V3_matches_the_Canton_vector(string nodeId, string expectedHex)
    {
        var hash = PreparedTransactionHasher.HashNode(CantonHashingFixture.V3.Transaction(), nodeId, HashingSchemeVersion.V3);

        Convert.ToHexStringLower(hash).Should().Be(expectedHex);
    }

    [Fact]
    public void HashNode_under_V3_matches_the_Canton_vector_for_a_query_by_key_node()
    {
        var transaction = new DamlTransaction { Version = "2", Roots = { "0" } };
        transaction.Nodes.Add(new DamlTransaction.Types.Node { NodeId = "0", V1 = new V1.Node { QueryByKey = CantonHashingFixture.QueryByKey() } });

        var hash = PreparedTransactionHasher.HashNode(transaction, "0", HashingSchemeVersion.V3);

        Convert.ToHexStringLower(hash).Should().Be("72c45c5a05a875621f16f2fe64d6c6db662ff41ee124d6c4f281409f5899b96c");
    }

    [Fact]
    public void HashTransaction_under_V3_matches_the_Canton_vector()
    {
        var hash = PreparedTransactionHasher.HashTransaction(CantonHashingFixture.V3.Transaction(), HashingSchemeVersion.V3);

        Convert.ToHexStringLower(hash).Should().Be("36f0d1f4e9742a5cf54795fd1633f46b8235fafa93da006dafe10eb358d465c4");
    }

    [Fact]
    public void HashMetadata_under_V3_matches_the_Canton_vector()
    {
        var hash = PreparedTransactionHasher.HashMetadata(CantonHashingFixture.V3.Metadata(), HashingSchemeVersion.V3);

        Convert.ToHexStringLower(hash).Should().Be("5a4c6aa89bb80d0af05a3f0614ba07bc7469795fff66d7b9e87c876eb0137e0a");
    }

    [Fact]
    public void Hash_under_V3_matches_the_Canton_vector()
    {
        var hash = PreparedTransactionHasher.Hash(CantonHashingFixture.V3.PreparedTransaction(), HashingSchemeVersion.V3);

        Convert.ToHexStringLower(hash).Should().Be("db37e4e251b83196260900fd43aacf934e1893b7ac58db4c1bfa8ced432b3b84");
    }
}
