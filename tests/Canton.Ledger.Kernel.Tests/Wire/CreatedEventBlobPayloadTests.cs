// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Google.Protobuf;
using Xunit;
using LfTransaction = Com.Digitalasset.Daml.Lf.Transaction;
using LfValue = Com.Digitalasset.Daml.Lf.Value;

namespace Canton.Ledger.Kernel.Tests.Wire;

public class CreatedEventBlobPayloadTests
{
    private const string ExpectedAccountPackageId = "111f6672984fc59edda613fad3fab08a4c25815181d6843312a5c5b46225c337";
    private const string ExpectedAccountPackageName = "contractkeys-h5e25b75e405b";

    private static LfValue.Value Party(string party) => new() { Party = party };

    private static LfValue.Value Text(string text) => new() { Text = text };

    private static LfValue.Value Int64(long number) => new() { Int64 = number };

    private static LfValue.Value Record(params LfValue.Value[] fields)
    {
        var record = new LfValue.Value.Types.Record();
        record.Fields.AddRange(fields.Select(field => new LfValue.Value.Types.Record.Types.Field { Value = field }));
        return new LfValue.Value { Record = record };
    }

    private static LfTransaction.FatContractInstance AccountContract() => new()
    {
        ContractId = ByteString.CopyFrom(0x00, 0xab, 0xcd),
        PackageName = ExpectedAccountPackageName,
        TemplateId = new LfValue.Identifier
        {
            PackageId = ExpectedAccountPackageId,
            ModuleName = { "ContractKeys" },
            Name = { "Account" },
        },
        CreateArg = Record(Party("alice::1"), Text("savings"), Int64(250)).ToByteString(),
        NonMaintainerSignatories = { "alice::1" },
        CreatedAt = 1_000_000,
    };

    private static LfTransaction.FatContractInstance KeyedAccountContract()
    {
        var contract = AccountContract();
        contract.ContractKeyWithMaintainers = new LfTransaction.KeyWithMaintainers
        {
            Key = Record(Party("alice::1"), Text("savings")).ToByteString(),
            Maintainers = { "alice::1" },
            Hash = ByteString.CopyFrom(0xde, 0xad, 0xbe, 0xef),
        };
        return contract;
    }

    private static DecodedCreatedEventBlob Decoded(LfTransaction.FatContractInstance contract) =>
        CreatedEventBlob.Decode(SyntheticCreatedEventBlob.InVersion("2.1", contract));

    [Fact]
    public void ReadCreateArgument_reads_the_record_off_a_live_ExternalPartyAmuletRules_blob()
    {
        var decoded = CreatedEventBlob.Decode(MurmuresLocalNetBlobs.ExternalPartyAmuletRules());

        decoded.ReadCreateArgument().Should().Be(DamlRecord.Create(new DamlField(
            "",
            new DamlParty("DSO::1220b578f5f1361d855d1c3426466e23f7ccfe8a46bdcb15f88e2d98b28d1be7ee34"))));
    }

    [Fact]
    public void ReadCreateArgument_rejects_a_create_argument_that_is_not_a_record()
    {
        var contract = AccountContract();
        contract.CreateArg = Int64(1).ToByteString();

        var act = () => Decoded(contract).ReadCreateArgument();

        act.Should().Throw<MalformedResponseException>().Which.Detail
            .Should().Be("the createdEventBlob's create argument is not a record.");
    }

    [Fact]
    public void ReadCreateArgument_rejects_a_create_argument_that_is_not_a_value()
    {
        var contract = AccountContract();
        contract.CreateArg = ByteString.CopyFrom(0xff, 0xff);

        var act = () => Decoded(contract).ReadCreateArgument();

        act.Should().Throw<MalformedResponseException>();
    }

    [Fact]
    public void ReadContractKey_is_null_for_a_template_without_a_key()
    {
        Decoded(AccountContract()).ReadContractKey().Should().BeNull();
    }

    [Fact]
    public void ReadContractKey_reads_the_key_value()
    {
        Decoded(KeyedAccountContract()).ReadContractKey().Should().Be(
            DamlRecord.Create(new DamlField("", new DamlParty("alice::1")), new DamlField("", new DamlText("savings"))));
    }

    [Fact]
    public void ReadContract_decodes_the_payload_and_the_contract_id_of_a_keyless_read()
    {
        var contract = Decoded(AccountContract()).ReadContract<Account>();

        contract.Id.Value.Should().Be("00abcd");
        contract.Data.Should().Be(new Account(new Party("alice::1"), "savings", 250));
    }

    [Fact]
    public void ReadContract_decodes_the_key_and_its_hash_of_a_keyed_template()
    {
        var contract = Decoded(KeyedAccountContract()).ReadContract<Account, AccountKey>();

        contract.Id.Value.Should().Be("00abcd");
        contract.Data.Should().Be(new Account(new Party("alice::1"), "savings", 250));
        contract.Key.Should().Be(new ContractKey<AccountKey>(new AccountKey(new Party("alice::1"), "savings"), "3q2+7w=="));
    }

    [Fact]
    public void ReadContract_reads_a_key_hash_the_blob_does_not_carry_as_null()
    {
        var keyed = KeyedAccountContract();
        keyed.ContractKeyWithMaintainers.Hash = ByteString.Empty;

        Decoded(keyed).ReadContract<Account, AccountKey>().Key.Hash.Should().BeNull();
    }

    [Fact]
    public void ReadContract_of_a_keyed_template_rejects_a_blob_without_a_key()
    {
        var act = () => Decoded(AccountContract()).ReadContract<Account, AccountKey>();

        act.Should().Throw<MalformedResponseException>().Which.Detail
            .Should().Be("the createdEventBlob's contract instance carries no contract key, though the template is keyed.");
    }

    [Fact]
    public void ReadContract_accepts_the_same_template_under_another_package_id()
    {
        var upgraded = AccountContract();
        upgraded.TemplateId.PackageId = "ffff";

        Decoded(upgraded).ReadContract<Account>().Data.Label.Should().Be("savings");
    }

    [Fact]
    public void ReadContract_rejects_a_blob_of_another_template()
    {
        var act = () => Decoded(AccountContract()).ReadContract<Holiday>();

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Be(
            "the createdEventBlob is a contractkeys-h5e25b75e405b:ContractKeys:Account contract, not a "
            + "contractkeys-h5e25b75e405b:ContractKeys:Holiday one.");
    }

    [Fact]
    public void ReadContract_rejects_a_blob_of_the_same_module_and_entity_under_another_package_name()
    {
        var other = AccountContract();
        other.PackageName = "elsewhere";

        var act = () => Decoded(other).ReadContract<Account>();

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().StartWith(
            "the createdEventBlob is a elsewhere:ContractKeys:Account contract");
    }

    [Fact]
    public void ReadContract_reports_a_payload_the_template_cannot_read_as_malformed()
    {
        var truncated = AccountContract();
        truncated.CreateArg = Record(Party("alice::1")).ToByteString();

        var act = () => Decoded(truncated).ReadContract<Account>();

        act.Should().Throw<MalformedResponseException>();
    }
}
