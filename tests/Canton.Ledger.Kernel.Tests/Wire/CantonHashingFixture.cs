// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.Daml.Ledger.Api.V2;
using Com.Daml.Ledger.Api.V2.Interactive;
using Google.Protobuf;
using V1 = Com.Daml.Ledger.Api.V2.Interactive.Transaction.V1;

namespace Canton.Ledger.Kernel.Tests.Wire;

/// <summary>
/// The prepared transaction Canton v3.6.1 hashes in
/// <c>community/common/src/test/scala/com/digitalasset/canton/protocol/hash/BaseNodeHashTest.scala</c>
/// and <c>HashUtilsTest.scala</c>, written as the Ledger API protobuf a participant would return for it.
/// The V2 suite hashes it with every node at LF serialization version <c>2.1</c> and no contract keys;
/// the V3 suite at version <c>2</c>, with a key on the create, fetch and exercise nodes and
/// <c>by_key</c> set on the fetch and exercise.
/// </summary>
public sealed class CantonHashingFixture
{
    private readonly string _lfVersion;
    private readonly bool _keyed;

    private CantonHashingFixture(string lfVersion, bool keyed)
    {
        _lfVersion = lfVersion;
        _keyed = keyed;
    }

    public static CantonHashingFixture V2 { get; } = new("2.1", keyed: false);

    public static CantonHashingFixture V3 { get; } = new("2", keyed: true);

    public const string ContractId1 = "0007e7b5534931dfca8e1b485c105bae4e10808bd13ddc8e897f258015f9d921c5";
    public const string ContractId2 = "0059b59ad7a6b6066e77b91ced54b8282f0e24e7089944685cb8f22f32fcbc4e1b";

    public PreparedTransaction PreparedTransaction() =>
        new() { Transaction = Transaction(), Metadata = Metadata() };

    public DamlTransaction Transaction()
    {
        var transaction = new DamlTransaction { Version = _lfVersion, Roots = { "0", "5" } };
        transaction.Nodes.Add(Node("0", new V1.Node { Create = Create(ContractId1) }));
        transaction.Nodes.Add(Node("1", new V1.Node { Create = Create(ContractId2) }));
        transaction.Nodes.Add(Node("2", new V1.Node { Fetch = Fetch(ContractId1) }));
        transaction.Nodes.Add(Node("3", new V1.Node { Fetch = Fetch(ContractId2) }));
        transaction.Nodes.Add(Node("4", new V1.Node { Exercise = Exercise() }));
        transaction.Nodes.Add(Node("5", new V1.Node { Rollback = new V1.Rollback { Children = { "2", "4" } } }));
        transaction.NodeSeeds.Add(Seed(0, "926bbb6f341bc0092ae65d06c6e284024907148cc29543ef6bff0930f5d52c19"));
        transaction.NodeSeeds.Add(Seed(1, "e0c69eae8afb38872fa425c2cdba794176f3b9d97e8eefb7b0e7c831f566458f"));
        transaction.NodeSeeds.Add(Seed(2, "4d2a522e9ee44e31b9bef2e3c8a07d43475db87463c6a13c4ea92f898ac8a930"));
        transaction.NodeSeeds.Add(Seed(3, "b4f0534e651ac8d5e10d95ddfcafdb123550a5e3185e3fe61ec1746a7222a88e"));
        transaction.NodeSeeds.Add(Seed(4, "a867edafa1277f46f879ab92c373a15c2d75c5d86fec741705cee1eb01ef8c9e"));
        transaction.NodeSeeds.Add(Seed(5, "5483d5df9b245e662c0e4368b8062e8a0fd24c17ce4ded1a0e452e4ee879dd81"));
        return transaction;
    }

    public Metadata Metadata() =>
        new()
        {
            SubmitterInfo = new Metadata.Types.SubmitterInfo { ActAs = { "alice", "bob" }, CommandId = "command-id" },
            SynchronizerId = "synchronizer::id",
            MediatorGroup = 0,
            TransactionUuid = "4c6471d3-4e09-49dd-addf-6cd90e19c583",
            PreparationTime = 0,
            MinLedgerEffectiveTime = 0xaaaa,
            MaxLedgerEffectiveTime = 0xbbbb,
            MaxRecordTime = 2_592_000_000_000,
            InputContracts =
            {
                InputContract(ContractId1, "alice", 864_000_000_000, [0x00, 0x10]),
                InputContract(ContractId2, "bob", 1_728_000_000_000, [0x00, 0x50]),
            },
        };

    public static V1.QueryByKey QueryByKey() =>
        new()
        {
            LfVersion = "2",
            PackageName = "package-name-0",
            TemplateId = PackageIdentifier("module", "name"),
            Exhaustive = true,
            Key = HelloKey(),
            Result = { ContractId1 },
        };

    private V1.Create Create(string contractId)
    {
        var create = new V1.Create
        {
            LfVersion = _lfVersion,
            ContractId = contractId,
            PackageName = "package-name-0",
            TemplateId = PackageIdentifier("module", "name"),
            Argument = new Value { Text = "hello" },
            Signatories = { "alice", "bob" },
            Stakeholders = { "alice", "charlie" },
        };
        if (_keyed) create.Key = HelloKey();
        return create;
    }

    private V1.Fetch Fetch(string contractId)
    {
        var fetch = new V1.Fetch
        {
            LfVersion = _lfVersion,
            ContractId = contractId,
            PackageName = "package-name-0",
            TemplateId = PackageIdentifier("module", "name"),
            Signatories = { "alice" },
            Stakeholders = { "charlie" },
            ActingParties = { "alice", "bob" },
        };
        if (_keyed)
        {
            fetch.Key = HelloKey();
            fetch.ByKey = true;
        }

        return fetch;
    }

    private V1.Exercise Exercise()
    {
        var exercise = new V1.Exercise
        {
            LfVersion = _lfVersion,
            ContractId = ContractId1,
            PackageName = "package-name-0",
            TemplateId = PackageIdentifier("module", "name"),
            Signatories = { "alice" },
            Stakeholders = { "charlie" },
            ActingParties = { "alice", "bob" },
            InterfaceId = PackageIdentifier("interface_module", "interface_name"),
            ChoiceId = "choice",
            ChosenValue = new Value { Int64 = 31380 },
            Consuming = true,
            Children = { "0", "2" },
            ExerciseResult = new Value { Text = "result" },
            ChoiceObservers = { "david" },
        };
        if (_keyed)
        {
            exercise.Key = HelloKey();
            exercise.ByKey = true;
        }

        return exercise;
    }

    private Metadata.Types.InputContract InputContract(string contractId, string party, ulong createdAt, byte[] eventBlob) =>
        new()
        {
            V1 = new V1.Create
            {
                LfVersion = _lfVersion,
                ContractId = contractId,
                PackageName = "PkgName",
                TemplateId = new Identifier { PackageId = "-dummyPkg-", ModuleName = "DummyModule", EntityName = "dummyName" },
                Argument = new Value { ContractId = "0097a092402108f5593bac7fb3c909cd316910197dd98d603042a45ab85c81e0fd" },
                Signatories = { party },
                Stakeholders = { party },
            },
            CreatedAt = createdAt,
            EventBlob = ByteString.CopyFrom(eventBlob),
        };

    private static GlobalKeyWithMaintainers HelloKey() =>
        new()
        {
            Key = new GlobalKey
            {
                TemplateId = PackageIdentifier("module_key", "name"),
                PackageName = "package_name_key",
                Key = new Value { Text = "hello" },
                Hash = ByteString.CopyFrom(Convert.FromHexString("4d1741d27564ab1f1e74873034760583a1a71edaa90dbad6be3a78c0bf3eec7c")),
            },
            Maintainers = { "david" },
        };

    private static Identifier PackageIdentifier(string module, string entity) =>
        new() { PackageId = "package", ModuleName = module, EntityName = entity };

    private static DamlTransaction.Types.Node Node(string nodeId, V1.Node node) =>
        new() { NodeId = nodeId, V1 = node };

    private static DamlTransaction.Types.NodeSeed Seed(int nodeId, string seedHex) =>
        new() { NodeId = nodeId, Seed = ByteString.CopyFrom(Convert.FromHexString(seedHex)) };
}
