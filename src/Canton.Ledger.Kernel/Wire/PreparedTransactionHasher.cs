// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Com.Daml.Ledger.Api.V2;
using Com.Daml.Ledger.Api.V2.Interactive;
using HashingSchemeVersion = Canton.Ledger.Abstractions.HashingSchemeVersion;
using V1 = Com.Daml.Ledger.Api.V2.Interactive.Transaction.V1;

namespace Canton.Ledger.Kernel.Wire;

internal static class PreparedTransactionHasher
{
    private static ReadOnlySpan<byte> PreparedSubmissionPurpose => [0x00, 0x00, 0x00, 0x30];

    internal static bool Ports(HashingSchemeVersion scheme) => SchemeRules.Ports(scheme);

    internal static byte[] Hash(PreparedTransaction prepared, HashingSchemeVersion scheme)
    {
        var rules = SchemeRules.For(scheme);
        return new HashInput()
            .Raw(PreparedSubmissionPurpose)
            .Byte(rules.VersionByte)
            .Raw(HashTransaction(prepared.Transaction ?? new DamlTransaction(), scheme))
            .Raw(HashMetadata(prepared.Metadata ?? new Metadata(), scheme))
            .Sha256();
    }

    internal static byte[] HashTransaction(DamlTransaction transaction, HashingSchemeVersion scheme)
    {
        var nodes = new TransactionNodes(transaction, SchemeRules.For(scheme));
        return new HashInput()
            .Raw(PreparedSubmissionPurpose)
            .String(transaction.Version)
            .Repeated(transaction.Roots, (input, rootId) => input.Raw(nodes.Hash(rootId)))
            .Sha256();
    }

    internal static byte[] HashMetadata(Metadata metadata, HashingSchemeVersion scheme)
    {
        var rules = SchemeRules.For(scheme);
        var submitter = metadata.SubmitterInfo ?? new Metadata.Types.SubmitterInfo();
        var input = rules.AddEncodingVersion(new HashInput().Raw(PreparedSubmissionPurpose))
            .Repeated(submitter.ActAs, AddString)
            .String(submitter.CommandId)
            .String(metadata.TransactionUuid)
            .Int(unchecked((int)metadata.MediatorGroup))
            .String(metadata.SynchronizerId)
            .OptionalLong(metadata.HasMinLedgerEffectiveTime, unchecked((long)metadata.MinLedgerEffectiveTime))
            .OptionalLong(metadata.HasMaxLedgerEffectiveTime, unchecked((long)metadata.MaxLedgerEffectiveTime))
            .Long(unchecked((long)metadata.PreparationTime))
            .Repeated(metadata.InputContracts, (contract, item) => AddInputContract(contract, item, rules));
        if (rules.HashesMaxRecordTime)
        {
            input.OptionalLong(metadata.HasMaxRecordTime, unchecked((long)metadata.MaxRecordTime));
        }

        return input.Sha256();
    }

    internal static byte[] HashNode(DamlTransaction transaction, string nodeId, HashingSchemeVersion scheme) =>
        new TransactionNodes(transaction, SchemeRules.For(scheme)).Hash(nodeId);

    internal static byte[] EncodeValue(Value value)
    {
        var input = new HashInput();
        AddValue(input, value);
        return input.Written.ToArray();
    }

    private static void AddInputContract(HashInput input, Metadata.Types.InputContract contract, SchemeRules rules)
    {
        var create = contract.V1
            ?? throw MalformedResponse.WithDetail("the prepared transaction carries an input contract of no version this SDK can hash");
        var encodedCreate = new HashInput();
        AddCreate(encodedCreate, create, seed: null, rules);
        input.Long(unchecked((long)contract.CreatedAt)).Raw(encodedCreate.Sha256());
    }

    private static void AddCreate(HashInput input, V1.Create create, byte[]? seed, SchemeRules rules)
    {
        rules.AddEncodingVersion(input)
            .String(create.LfVersion)
            .Byte(0x00)
            .Optional(seed, (present, value) => present.Raw(value))
            .HexString(create.ContractId, "the contract id of a created contract")
            .String(create.PackageName);
        AddIdentifier(input, create.TemplateId ?? new Identifier());
        AddValue(input, create.Argument);
        input.Repeated(create.Signatories, AddString).Repeated(create.Stakeholders, AddString);
        if (rules.HashesContractKeys) input.Optional(create.Key, AddKeyWithMaintainers);
    }

    private static void AddKeyWithMaintainers(HashInput input, GlobalKeyWithMaintainers keyWithMaintainers)
    {
        var key = keyWithMaintainers.Key ?? new GlobalKey();
        input.String(key.PackageName);
        AddIdentifier(input, key.TemplateId ?? new Identifier());
        AddValue(input, key.Key);
        input.Raw(key.Hash.Span).Repeated(keyWithMaintainers.Maintainers, AddString);
    }

    private static void AddValue(HashInput input, Value? value)
    {
        switch (value?.SumCase ?? Value.SumOneofCase.None)
        {
            case Value.SumOneofCase.Unit:
                input.Byte(0x00);
                break;
            case Value.SumOneofCase.Bool:
                input.Byte(0x01).Bool(value!.Bool);
                break;
            case Value.SumOneofCase.Int64:
                input.Byte(0x02).Long(value!.Int64);
                break;
            case Value.SumOneofCase.Numeric:
                input.Byte(0x03).String(value!.Numeric);
                break;
            case Value.SumOneofCase.Timestamp:
                input.Byte(0x04).Long(value!.Timestamp);
                break;
            case Value.SumOneofCase.Date:
                input.Byte(0x05).Int(value!.Date);
                break;
            case Value.SumOneofCase.Party:
                input.Byte(0x06).String(value!.Party);
                break;
            case Value.SumOneofCase.Text:
                input.Byte(0x07).String(value!.Text);
                break;
            case Value.SumOneofCase.ContractId:
                input.Byte(0x08).HexString(value!.ContractId, "the contract id value");
                break;
            case Value.SumOneofCase.Optional:
                input.Byte(0x09).Optional(value!.Optional.Value, AddValue);
                break;
            case Value.SumOneofCase.List:
                input.Byte(0x0a).Repeated(value!.List.Elements, AddValue);
                break;
            case Value.SumOneofCase.TextMap:
                input.Byte(0x0b).Repeated(value!.TextMap.Entries, (entry, item) => AddValue(entry.String(item.Key), item.Value));
                break;
            case Value.SumOneofCase.Record:
                input.Byte(0x0c)
                    .Optional(value!.Record.RecordId, AddIdentifier)
                    .Repeated(value.Record.Fields, (field, item) => AddValue(field.Byte(0x01).String(item.Label), item.Value));
                break;
            case Value.SumOneofCase.Variant:
                input.Byte(0x0d).Optional(value!.Variant.VariantId, AddIdentifier).String(value.Variant.Constructor);
                AddValue(input, value.Variant.Value);
                break;
            case Value.SumOneofCase.Enum:
                input.Byte(0x0e).Optional(value!.Enum.EnumId, AddIdentifier).String(value.Enum.Constructor);
                break;
            case Value.SumOneofCase.GenMap:
                input.Byte(0x0f).Repeated(value!.GenMap.Entries, (entry, item) =>
                {
                    AddValue(entry, item.Key);
                    AddValue(entry, item.Value);
                });
                break;
            default:
                throw MalformedResponse.WithDetail("the prepared transaction carries a value of no kind");
        }
    }

    private static void AddIdentifier(HashInput input, Identifier identifier) =>
        input.String(identifier.PackageId)
            .Repeated(identifier.ModuleName.Split('.'), AddString)
            .Repeated(identifier.EntityName.Split('.'), AddString);

    private static void AddString(HashInput input, string value) => input.String(value);

    private sealed class SchemeRules
    {
        private static readonly SchemeRules V2 = new(versionByte: 0x02, hashesContractKeys: false);
        private static readonly SchemeRules V3 = new(versionByte: 0x03, hashesContractKeys: true);

        private readonly bool _prefixesEncodingVersion;

        private SchemeRules(byte versionByte, bool hashesContractKeys)
        {
            VersionByte = versionByte;
            HashesContractKeys = hashesContractKeys;
            _prefixesEncodingVersion = !hashesContractKeys;
        }

        internal byte VersionByte { get; }

        internal bool HashesContractKeys { get; }

        internal bool HashesMaxRecordTime => HashesContractKeys;

        internal static bool Ports(HashingSchemeVersion scheme) => Find(scheme) is not null;

        internal static SchemeRules For(HashingSchemeVersion scheme) =>
            Find(scheme) ?? throw new ArgumentOutOfRangeException(nameof(scheme), scheme, "no hashing rules are ported for this scheme");

        private static SchemeRules? Find(HashingSchemeVersion scheme) =>
            scheme switch
            {
                HashingSchemeVersion.V2 => V2,
                HashingSchemeVersion.V3 => V3,
                _ => null,
            };

        internal HashInput AddEncodingVersion(HashInput input) =>
            _prefixesEncodingVersion ? input.Byte(0x01) : input;
    }

    private sealed class TransactionNodes
    {
        private readonly Dictionary<string, DamlTransaction.Types.Node> _nodes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> _seeds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> _hashes = new(StringComparer.Ordinal);
        private readonly HashSet<string> _inProgress = new(StringComparer.Ordinal);
        private readonly SchemeRules _rules;

        internal TransactionNodes(DamlTransaction transaction, SchemeRules rules)
        {
            _rules = rules;
            foreach (var node in transaction.Nodes)
            {
                if (!_nodes.TryAdd(node.NodeId, node))
                {
                    throw MalformedResponse.WithDetail($"the prepared transaction carries node '{node.NodeId}' twice");
                }
            }

            foreach (var seed in transaction.NodeSeeds)
            {
                _seeds.TryAdd(seed.NodeId.ToString(CultureInfo.InvariantCulture), seed.Seed.ToByteArray());
            }
        }

        internal byte[] Hash(string rootId)
        {
            var pending = new Stack<(string NodeId, bool ChildrenHashed)>();
            pending.Push((rootId, false));
            while (pending.Count > 0)
            {
                var (nodeId, childrenHashed) = pending.Pop();
                if (_hashes.ContainsKey(nodeId)) continue;

                var node = VersionedNode(nodeId);
                if (childrenHashed)
                {
                    _inProgress.Remove(nodeId);
                    _hashes[nodeId] = HashOne(nodeId, node);
                    continue;
                }

                _inProgress.Add(nodeId);
                pending.Push((nodeId, true));
                foreach (var childId in ChildrenOf(node).Reverse())
                {
                    if (_inProgress.Contains(childId))
                    {
                        throw MalformedResponse.WithDetail(
                            $"the prepared transaction's node '{childId}' is its own descendant");
                    }

                    if (!_hashes.ContainsKey(childId)) pending.Push((childId, false));
                }
            }

            return _hashes[rootId];
        }

        private byte[] HashOne(string nodeId, V1.Node node)
        {
            var input = new HashInput();
            switch (node.NodeTypeCase)
            {
                case V1.Node.NodeTypeOneofCase.Create:
                    AddCreate(input, node.Create, _seeds.GetValueOrDefault(nodeId), _rules);
                    break;
                case V1.Node.NodeTypeOneofCase.Fetch:
                    AddFetch(input, node.Fetch);
                    break;
                case V1.Node.NodeTypeOneofCase.Exercise:
                    AddExercise(input, nodeId, node.Exercise);
                    break;
                case V1.Node.NodeTypeOneofCase.Rollback:
                    _rules.AddEncodingVersion(input).Byte(0x03).Repeated(node.Rollback.Children, AddChild);
                    break;
                case V1.Node.NodeTypeOneofCase.QueryByKey when _rules.HashesContractKeys:
                    AddQueryByKey(input, node.QueryByKey);
                    break;
                default:
                    throw MalformedResponse.WithDetail($"the prepared transaction's node '{nodeId}' is of no kind this SDK can hash");
            }

            return input.Sha256();
        }

        private void AddFetch(HashInput input, V1.Fetch fetch)
        {
            _rules.AddEncodingVersion(input)
                .String(fetch.LfVersion)
                .Byte(0x02)
                .HexString(fetch.ContractId, "the contract id of a fetched contract")
                .String(fetch.PackageName);
            AddIdentifier(input, fetch.TemplateId ?? new Identifier());
            input.Repeated(fetch.Signatories, AddString)
                .Repeated(fetch.Stakeholders, AddString)
                .Optional(fetch.InterfaceId, AddIdentifier)
                .Repeated(fetch.ActingParties, AddString);
            if (_rules.HashesContractKeys) input.Bool(fetch.ByKey).Optional(fetch.Key, AddKeyWithMaintainers);
        }

        private void AddExercise(HashInput input, string nodeId, V1.Exercise exercise)
        {
            var seed = _seeds.GetValueOrDefault(nodeId)
                ?? throw MalformedResponse.WithDetail($"the prepared transaction carries no seed for its exercise node '{nodeId}'");
            _rules.AddEncodingVersion(input)
                .String(exercise.LfVersion)
                .Byte(0x01)
                .Raw(seed)
                .HexString(exercise.ContractId, "the contract id of an exercised contract")
                .String(exercise.PackageName);
            AddIdentifier(input, exercise.TemplateId ?? new Identifier());
            input.Repeated(exercise.Signatories, AddString)
                .Repeated(exercise.Stakeholders, AddString)
                .Repeated(exercise.ActingParties, AddString)
                .Optional(exercise.InterfaceId, AddIdentifier)
                .String(exercise.ChoiceId);
            AddValue(input, exercise.ChosenValue);
            input.Bool(exercise.Consuming)
                .Optional(exercise.ExerciseResult, AddValue)
                .Repeated(exercise.ChoiceObservers, AddString);
            if (_rules.HashesContractKeys) input.Bool(exercise.ByKey).Optional(exercise.Key, AddKeyWithMaintainers);
            input.Repeated(exercise.Children, AddChild);
        }

        private static void AddQueryByKey(HashInput input, V1.QueryByKey query)
        {
            input.String(query.LfVersion).Byte(0x04).String(query.PackageName);
            AddIdentifier(input, query.TemplateId ?? new Identifier());
            input.Bool(query.Exhaustive);
            AddKeyWithMaintainers(input, query.Key ?? new GlobalKeyWithMaintainers());
            input.Repeated(query.Result, (result, contractId) => result.HexString(contractId, "the contract id of a key query result"));
        }

        private void AddChild(HashInput input, string childId) => input.Raw(_hashes[childId]);

        private static IEnumerable<string> ChildrenOf(V1.Node node) =>
            node.NodeTypeCase switch
            {
                V1.Node.NodeTypeOneofCase.Exercise => node.Exercise.Children,
                V1.Node.NodeTypeOneofCase.Rollback => node.Rollback.Children,
                _ => [],
            };

        private V1.Node VersionedNode(string nodeId) =>
            Lookup(nodeId).V1
            ?? throw MalformedResponse.WithDetail($"the prepared transaction's node '{nodeId}' is of no version this SDK can hash");

        private DamlTransaction.Types.Node Lookup(string nodeId) =>
            _nodes.TryGetValue(nodeId, out var node)
                ? node
                : throw MalformedResponse.WithDetail($"the prepared transaction references node '{nodeId}', which it does not carry");
    }
}
