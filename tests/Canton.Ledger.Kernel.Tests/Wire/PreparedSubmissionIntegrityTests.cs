// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Google.Protobuf;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Wire;

public class PreparedSubmissionIntegrityTests
{
    private const string V2FixtureHash = "8c311c848db25d36b36fbd59f9483714a11688c2214e3c7cae3e028763520250";
    private const string V3FixtureHash = "db37e4e251b83196260900fd43aacf934e1893b7ac58db4c1bfa8ced432b3b84";

    private static PreparedSubmission Prepared(int hashLength, HashingSchemeVersion version) =>
        new(new byte[] { 1 }, new byte[hashLength], version, HashingDetails: null, CostEstimate: null);

    private static PreparedSubmission PreparedFixture(CantonHashingFixture fixture, string hashHex, HashingSchemeVersion version) =>
        new(fixture.PreparedTransaction().ToByteArray(), Convert.FromHexString(hashHex), version, HashingDetails: null, CostEstimate: null);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(33)]
    public void RequireWellFormed_refuses_a_hash_that_is_not_32_bytes(int hashLength)
    {
        var act = () => PreparedSubmissionIntegrity.RequireWellFormed(
            Prepared(hashLength, HashingSchemeVersion.V2), requested: null);

        act.Should().Throw<MalformedResponseException>()
            .Which.Detail.Should().Be(
                $"the participant returned a prepared-transaction hash of {hashLength} bytes, though a hash is 32 bytes");
    }

    [Theory]
    [InlineData(HashingSchemeVersion.V2, HashingSchemeVersion.V3)]
    [InlineData(HashingSchemeVersion.V3, HashingSchemeVersion.V2)]
    [InlineData(HashingSchemeVersion.V4, HashingSchemeVersion.V3)]
    public void RequireWellFormed_refuses_a_scheme_other_than_the_requested_one(
        HashingSchemeVersion requested, HashingSchemeVersion reported)
    {
        var act = () => PreparedSubmissionIntegrity.RequireWellFormed(Prepared(32, reported), requested);

        act.Should().Throw<MalformedResponseException>()
            .Which.Detail.Should().Be(
                $"the participant hashed the prepared transaction with scheme {reported}, though {requested} was requested");
    }

    [Fact]
    public void RequireWellFormed_returns_the_same_submission_when_its_V2_hash_matches_the_transaction()
    {
        var prepared = PreparedFixture(CantonHashingFixture.V2, V2FixtureHash, HashingSchemeVersion.V2);

        PreparedSubmissionIntegrity.RequireWellFormed(prepared, requested: null).Should().BeSameAs(prepared);
    }

    [Fact]
    public void RequireWellFormed_returns_the_same_submission_when_its_V3_hash_matches_the_transaction()
    {
        var prepared = PreparedFixture(CantonHashingFixture.V3, V3FixtureHash, HashingSchemeVersion.V3);

        PreparedSubmissionIntegrity.RequireWellFormed(prepared, HashingSchemeVersion.V3).Should().BeSameAs(prepared);
    }

    [Fact]
    public void RequireWellFormed_refuses_a_hash_that_does_not_match_the_transaction()
    {
        var reported = "00" + V2FixtureHash[2..];
        var prepared = PreparedFixture(CantonHashingFixture.V2, reported, HashingSchemeVersion.V2);

        var act = () => PreparedSubmissionIntegrity.RequireWellFormed(prepared, requested: null);

        var mismatch = act.Should().Throw<PreparedHashMismatchException>().Which;
        mismatch.HashingSchemeVersion.Should().Be(HashingSchemeVersion.V2);
        Convert.ToHexStringLower(mismatch.ReportedHash.Span).Should().Be(reported);
        Convert.ToHexStringLower(mismatch.RecomputedHash.Span).Should().Be(V2FixtureHash);
        mismatch.Message.Should().Be(
            $"The participant's prepared-transaction hash {reported} does not match {V2FixtureHash}, "
            + "the hash of the prepared transaction it returned under hashing scheme V2.");
    }

    [Fact]
    public void RequireWellFormed_refuses_a_hash_computed_under_a_scheme_other_than_the_reported_one()
    {
        var prepared = PreparedFixture(CantonHashingFixture.V2, V2FixtureHash, HashingSchemeVersion.V3);

        var act = () => PreparedSubmissionIntegrity.RequireWellFormed(prepared, requested: null);

        act.Should().Throw<PreparedHashMismatchException>()
            .Which.HashingSchemeVersion.Should().Be(HashingSchemeVersion.V3);
    }

    [Fact]
    public void RequireWellFormed_refuses_a_scheme_it_cannot_recompute()
    {
        var prepared = PreparedFixture(CantonHashingFixture.V3, V3FixtureHash, HashingSchemeVersion.V4);

        var act = () => PreparedSubmissionIntegrity.RequireWellFormed(prepared, requested: null);

        act.Should().Throw<MalformedResponseException>()
            .Which.Detail.Should().Be(
                "the participant hashed the prepared transaction with scheme V4, which this SDK cannot recompute; "
                + "it verifies V2 and V3 only");
    }

    [Fact]
    public void RequireWellFormed_refuses_a_prepared_transaction_that_is_not_protobuf()
    {
        var prepared = new PreparedSubmission(
            new byte[] { 0xff }, Convert.FromHexString(V2FixtureHash), HashingSchemeVersion.V2, HashingDetails: null, CostEstimate: null);

        var act = () => PreparedSubmissionIntegrity.RequireWellFormed(prepared, requested: null);

        act.Should().Throw<MalformedResponseException>()
            .Which.Detail.Should().StartWith("the prepared transaction could not be decoded: ");
    }
}
