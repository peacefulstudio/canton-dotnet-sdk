// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Holds a <see cref="TransactionResult"/> a host cached as JSON to behave, once read back, exactly as the
/// result a ledger client returned: equal to it, and still carrying the node numbering and the requested
/// interfaces that <c>Single</c> and <c>ProjectByKeyExercise</c> depend on.
/// </summary>
public class TransactionResultCachedJsonTests
{
    private const string DefaultOptions = "default options";
    private const string DamlConverterOptions = "AddDamlConverters options";

    private static JsonSerializerOptions? OptionsFor(string leg) =>
        leg == DamlConverterOptions ? new JsonSerializerOptions().AddDamlConverters() : null;

    private static readonly Party Alice = new("Alice::122012ab");

    private static readonly Identifier AccountTemplateId = new("pkg-v1", "Test.Ledger", "Account");

    private sealed record Holding(string Owner) : IDamlInterface
    {
        public static Identifier InterfaceId { get; } = new("iface-pkg", "Acme.Iface", "Holding");
        public static string PackageId => "iface-pkg";
        public static string PackageName => "iface-package";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("owner", new DamlParty(Owner)));
    }

    private sealed class LedgerAccount : IDamlType
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(AccountTemplateId, DamlTypeKind.Template, "ledger");
    }

    private static Choice<LedgerAccount, DamlUnit, long> GetBalance { get; } = new()
    {
        Name = new ChoiceName("GetBalance"),
        Consuming = false,
        ArgumentEncoder = _ => DamlUnit.Instance,
        ArgumentDecoder = _ => DamlUnit.Instance,
        ResultDecoder = value => value.As<DamlInt64>().Value,
        ArgumentJsonReader = (_, _) => throw new NotSupportedException(),
        ResultJsonReader = (_, _) => throw new NotSupportedException(),
    };

    private static CreatedContract HoldingContract(int? nodeId) =>
        new(
            "event-1",
            "00holding",
            new Identifier("impl-pkg", "Acme.Impl", "Concrete"),
            new DamlRecord(null, [new DamlField("amount", new DamlInt64(42))]),
            [Alice],
            [Alice],
            [])
        {
            InterfaceIds = [Holding.InterfaceId],
            NodeId = nodeId,
        };

    private static ExercisedEvent Exercise(string contractId, int nodeId, int lastDescendantNodeId, long balance) =>
        new(
            ContractId: contractId,
            TemplateId: AccountTemplateId,
            InterfaceId: null,
            ChoiceName: new ChoiceName("GetBalance"),
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: new DamlInt64(balance),
            Consuming: false,
            ActingParties: [Alice],
            WitnessParties: [Alice])
        {
            PackageName = "ledger",
            NodeId = nodeId,
            LastDescendantNodeId = lastDescendantNodeId,
        };

    private static TransactionResult ResultWithNodeNumbering() =>
        new(
            "update-1",
            LedgerOffset.At(6057),
            [HoldingContract(nodeId: 2)],
            ["contract-2"],
            new CommandId("cmd-1"))
        {
            ExercisedEvents =
            [
                Exercise("nested-contract", nodeId: 1, lastDescendantNodeId: 1, balance: 99),
                Exercise("root-contract", nodeId: 0, lastDescendantNodeId: 1, balance: 10),
            ],
            RequestedInterfaces = [Holding.InterfaceId],
        };

    private static TransactionResult CachedAndReadBack(TransactionResult result, string leg) =>
        JsonSerializer.Deserialize<TransactionResult>(JsonSerializer.Serialize(result, OptionsFor(leg)), OptionsFor(leg))!;

    [Theory]
    [InlineData(DefaultOptions)]
    [InlineData(DamlConverterOptions)]
    public void TransactionResult_carrying_node_numbering_and_requested_interfaces_reads_back_equal(string leg)
    {
        var result = ResultWithNodeNumbering();

        var restored = CachedAndReadBack(result, leg);

        restored.Should().Be(result);
        restored.GetHashCode().Should().Be(result.GetHashCode());
    }

    [Theory]
    [InlineData(DefaultOptions)]
    [InlineData(DamlConverterOptions)]
    public void TransactionResult_read_back_keeps_each_exercise_node_id_and_last_descendant_node_id(string leg)
    {
        var restored = CachedAndReadBack(ResultWithNodeNumbering(), leg);

        restored.ExercisedEvents.Select(exercised => (exercised.NodeId, exercised.LastDescendantNodeId))
            .Should().Equal((1, 1), (0, 1));
    }

    [Theory]
    [InlineData(DefaultOptions)]
    [InlineData(DamlConverterOptions)]
    public void TransactionResult_read_back_keeps_each_created_contract_node_id(string leg)
    {
        var restored = CachedAndReadBack(ResultWithNodeNumbering(), leg);

        restored.CreatedContracts.Single().NodeId.Should().Be(2);
    }

    [Theory]
    [InlineData(DefaultOptions)]
    [InlineData(DamlConverterOptions)]
    public void TransactionResult_read_back_keeps_the_requested_interfaces(string leg)
    {
        var restored = CachedAndReadBack(ResultWithNodeNumbering(), leg);

        restored.RequestedInterfaces.Should().Equal(new Identifier("iface-pkg", "Acme.Iface", "Holding"));
    }

    [Theory]
    [InlineData(DefaultOptions)]
    [InlineData(DamlConverterOptions)]
    public void Single_on_an_interface_resolves_the_contract_of_a_result_read_back_from_json(string leg)
    {
        var restored = CachedAndReadBack(ResultWithNodeNumbering(), leg);

        restored.Single<Holding>().Value.Should().Be("00holding");
    }

    [Theory]
    [InlineData(DefaultOptions)]
    [InlineData(DamlConverterOptions)]
    public void ProjectByKeyExercise_resolves_the_root_exercise_of_a_result_read_back_from_json(string leg)
    {
        var restored = CachedAndReadBack(ResultWithNodeNumbering(), leg);

        var outcome = restored.ProjectByKeyExercise(GetBalance);

        var exercise = outcome.Should().BeOfType<ExerciseOutcome<ByKeyExercise<LedgerAccount, long>>.One>().Which.Result;
        exercise.ResolvedContractId.Value.Should().Be("root-contract");
        exercise.Result.Should().Be(10L);
    }

    [Theory]
    [InlineData(DefaultOptions)]
    [InlineData(DamlConverterOptions)]
    public void TransactionResult_json_cached_before_node_numbering_was_written_reads_back_without_it(string leg)
    {
        const string cachedEarlier =
            """
            {"UpdateId":"update-1","CompletionOffset":6057,"CreatedContracts":[{"EventId":"event-1","ContractId":"00holding","TemplateId":{"PackageId":"impl-pkg","ModuleName":"Acme.Impl","EntityName":"Concrete","FullyQualifiedName":"Acme.Impl:Concrete"},"Payload":{"RecordId":null,"Fields":[{"Label":"amount","Value":{"$case":"DamlInt64","Value":42}}]},"WitnessParties":["Alice::122012ab"],"Signatories":["Alice::122012ab"],"Observers":[],"ContractKey":null,"CreatedAt":null,"InterfaceIds":[{"PackageId":"iface-pkg","ModuleName":"Acme.Iface","EntityName":"Holding","FullyQualifiedName":"Acme.Iface:Holding"}],"PackageName":null,"UndecodedPayload":null}],"ArchivedContractIds":["contract-2"],"CommandId":"cmd-1","ExercisedEvents":[{"ContractId":"root-contract","TemplateId":{"PackageId":"pkg-v1","ModuleName":"Test.Ledger","EntityName":"Account","FullyQualifiedName":"Test.Ledger:Account"},"InterfaceId":null,"ChoiceName":"GetBalance","ChoiceArgument":{"$case":"DamlUnit"},"ExerciseResult":{"$case":"DamlInt64","Value":10},"Consuming":false,"ActingParties":["Alice::122012ab"],"WitnessParties":["Alice::122012ab"],"PackageName":"ledger"}],"PaidTrafficCost":null}
            """;

        var restored = JsonSerializer.Deserialize<TransactionResult>(cachedEarlier, OptionsFor(leg))!;

        restored.ExercisedEvents.Single().NodeId.Should().BeNull();
        restored.ExercisedEvents.Single().LastDescendantNodeId.Should().BeNull();
        restored.CreatedContracts.Single().NodeId.Should().BeNull();
        restored.RequestedInterfaces.Should().BeEmpty();
        restored.ExercisedEvents.Single().ChoiceArgument.Should().Be(DamlUnit.Instance);
    }
}
