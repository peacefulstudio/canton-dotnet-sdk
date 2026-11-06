// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Daml.Runtime.Data;
using Google.Protobuf;
using Xunit;
using LfTransaction = Com.Digitalasset.Daml.Lf.Transaction;

namespace Canton.Ledger.Kernel.Tests.Wire;

public class CreatedEventBlobTests
{
    private const string Dso = "DSO::1220b578f5f1361d855d1c3426466e23f7ccfe8a46bdcb15f88e2d98b28d1be7ee34";

    [Fact]
    public void Decode_reads_the_contract_off_a_live_ExternalPartyAmuletRules_blob()
    {
        var decoded = CreatedEventBlob.Decode(MurmuresLocalNetBlobs.ExternalPartyAmuletRules());

        decoded.SerializationVersion.Should().Be("2.1");
        decoded.ContractId.Should().Be(
            "00cd02b5335187da828432f84e15ce30a37165c43c773a47b54a943ad79e241df7ca1212207cc8a64aaffb0d618a9019a6eb7d7efa594d9576be05a80f13dbb134dd9d5ea2");
        decoded.PackageName.Should().Be("splice-amulet");
        decoded.TemplateId.Should().Be(new Identifier(
            "17b6f7c18efec9022dde39a19039783c68490a722e8a5088b22ee034678171a2",
            "Splice.ExternalPartyAmuletRules",
            "ExternalPartyAmuletRules"));
        decoded.Signatories.Should().BeEquivalentTo([Dso]);
        decoded.Observers.Should().BeEmpty();
        decoded.KeyMaintainers.Should().BeEmpty();
        decoded.CreatedAt.Should().Be(new DateTimeOffset(2026, 10, 21, 15, 25, 14, TimeSpan.Zero).AddTicks(4043980));
        Convert.ToHexStringLower(decoded.CreateArgument.Span).Should().Be(
            "6a4f0a4d0a4b3a4944534f3a3a3132323062353738663566313336316438353564316333343236343636653233663763636665386134366264636231356638386532643938623238643162653765653334");
    }

    [Fact]
    public void Decode_counts_the_key_maintainers_as_signatories_and_reports_them_apart()
    {
        var contract = SyntheticCreatedEventBlob.Contract();
        contract.ContractKeyWithMaintainers = new LfTransaction.KeyWithMaintainers
        {
            Key = ByteString.CopyFrom(0x01),
            Maintainers = { "maintainer::3", "admin::1" },
        };

        var decoded = CreatedEventBlob.Decode(SyntheticCreatedEventBlob.InVersion("2", contract));

        decoded.Signatories.Should().BeEquivalentTo(["admin::1", "maintainer::3"]);
        decoded.KeyMaintainers.Should().BeEquivalentTo(["maintainer::3", "admin::1"]);
    }

    [Fact]
    public void Decode_reads_the_stakeholders_that_are_not_signatories_as_observers()
    {
        var contract = SyntheticCreatedEventBlob.Contract();
        contract.NonSignatoryStakeholders.Add(["obs::1", "obs::2"]);

        var decoded = CreatedEventBlob.Decode(SyntheticCreatedEventBlob.InVersion("2.1", contract));

        decoded.Observers.Should().BeEquivalentTo(["obs::1", "obs::2"]);
        decoded.Signatories.Should().BeEquivalentTo(["admin::1"]);
    }

    [Fact]
    public void Decode_reads_the_synthetic_contract_fields()
    {
        var decoded = CreatedEventBlob.Decode(SyntheticCreatedEventBlob.InVersion("3", SyntheticCreatedEventBlob.Contract()));

        decoded.ContractId.Should().Be("00abcd");
        decoded.PackageName.Should().Be("tokens");
        decoded.TemplateId.ToString().Should().Be("pkg1:Tokens.Holding:Holding");
        decoded.CreatedAt.Should().Be(new DateTimeOffset(1970, 1, 1, 0, 0, 1, TimeSpan.Zero));
        decoded.KeyMaintainers.Should().BeEmpty();
    }

    [Fact]
    public void Decode_reports_an_unknown_creation_time_as_null()
    {
        var contract = SyntheticCreatedEventBlob.Contract();
        contract.CreatedAt = long.MinValue;

        var decoded = CreatedEventBlob.Decode(SyntheticCreatedEventBlob.InVersion("2.1", contract));

        decoded.CreatedAt.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("2.2")]
    [InlineData("dev")]
    public void Decode_names_the_serialization_version_it_does_not_read(string version)
    {
        var blob = SyntheticCreatedEventBlob.InVersion(version, SyntheticCreatedEventBlob.Contract());

        var decode = () => CreatedEventBlob.Decode(blob);

        decode.Should().Throw<MalformedResponseException>()
            .Which.Detail.Should().Be(
                $"the createdEventBlob is serialization version '{version}', which this SDK does not read; it reads '2.1', '2', '3'.");
    }

    [Theory]
    [InlineData(new byte[] { 0x0a, 0x03, 0x32, 0x2e, 0x31 })]
    [InlineData(new byte[] { 0x0a, 0x03, 0x32, 0x2e, 0x31, 0x12, 0x7f, 0x01 })]
    [InlineData(new byte[] { 0xff })]
    public void Decode_rejects_a_blob_without_a_readable_contract_instance(byte[] blob)
    {
        var decode = () => CreatedEventBlob.Decode(blob);

        decode.Should().Throw<MalformedResponseException>();
    }

    [Fact]
    public void Decode_rejects_a_contract_instance_that_names_no_signatory()
    {
        var contract = SyntheticCreatedEventBlob.Contract();
        contract.NonMaintainerSignatories.Clear();

        var decode = () => CreatedEventBlob.Decode(SyntheticCreatedEventBlob.InVersion("2.1", contract));

        decode.Should().Throw<MalformedResponseException>()
            .Which.Detail.Should().Be("the createdEventBlob's contract instance names no signatory.");
    }

    [Fact]
    public void Decode_rejects_a_contract_instance_without_a_contract_id()
    {
        var contract = SyntheticCreatedEventBlob.Contract();
        contract.ContractId = ByteString.Empty;

        var decode = () => CreatedEventBlob.Decode(SyntheticCreatedEventBlob.InVersion("2.1", contract));

        decode.Should().Throw<MalformedResponseException>()
            .Which.Detail.Should().Be("the createdEventBlob's contract instance has no contract id.");
    }

    [Fact]
    public void Decode_rejects_a_contract_instance_without_a_package_name()
    {
        var contract = SyntheticCreatedEventBlob.Contract();
        contract.PackageName = string.Empty;

        var decode = () => CreatedEventBlob.Decode(SyntheticCreatedEventBlob.InVersion("2.1", contract));

        decode.Should().Throw<MalformedResponseException>()
            .Which.Detail.Should().Be("the createdEventBlob's contract instance has no package name.");
    }

    [Fact]
    public void Decode_rejects_a_contract_instance_without_a_template_id()
    {
        var contract = SyntheticCreatedEventBlob.Contract();
        contract.TemplateId = null;

        var decode = () => CreatedEventBlob.Decode(SyntheticCreatedEventBlob.InVersion("2.1", contract));

        decode.Should().Throw<MalformedResponseException>()
            .Which.Detail.Should().Be("the createdEventBlob's contract instance has no complete template id.");
    }

    [Fact]
    public void Decode_rejects_a_creation_time_beyond_the_representable_range()
    {
        var contract = SyntheticCreatedEventBlob.Contract();
        contract.CreatedAt = long.MaxValue;

        var decode = () => CreatedEventBlob.Decode(SyntheticCreatedEventBlob.InVersion("2.1", contract));

        decode.Should().Throw<MalformedResponseException>()
            .Which.Detail.Should().Be(
                "the createdEventBlob's creation time of 9223372036854775807 microseconds is out of range.");
    }

    [Fact]
    public void Decode_copies_the_bytes_it_is_given()
    {
        var blob = MurmuresLocalNetBlobs.ExternalPartyAmuletRules();
        var decoded = CreatedEventBlob.Decode(blob);
        var before = decoded.CreateArgument.ToArray();

        Array.Clear(blob);

        decoded.CreateArgument.ToArray().Should().Equal(before);
    }
}
