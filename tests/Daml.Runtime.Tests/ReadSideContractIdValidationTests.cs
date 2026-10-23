// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Daml.Runtime.Tests;

public class ReadSideContractIdValidationTests
{
    private static readonly Identifier Template = new("pkg", "Module", "Template");

    public static TheoryData<string?> BlankIds => new() { null!, "", "   ", "\t" };

    [Theory]
    [MemberData(nameof(BlankIds))]
    public void CreatedEvent_rejects_blank_contract_id(string? contractId)
    {
        var construct = () => new CreatedEvent(
            "event-1", contractId!, Template, DamlRecord.Create(), [], [], [], null);

        construct.Should().Throw<ArgumentException>().WithParameterName("ContractId");
    }

    [Fact]
    public void CreatedEvent_rejects_blank_contract_id_through_with_expression()
    {
        var created = new CreatedEvent(
            "event-1", "cid-1", Template, DamlRecord.Create(), [], [], [], null);

        var rewrite = () => created with { ContractId = " " };

        rewrite.Should().Throw<ArgumentException>().WithParameterName("ContractId");
    }

    [Theory]
    [MemberData(nameof(BlankIds))]
    public void ArchivedEvent_rejects_blank_contract_id(string? contractId)
    {
        var construct = () => new ArchivedEvent("event-1", contractId!, Template, []);

        construct.Should().Throw<ArgumentException>().WithParameterName("ContractId");
    }

    [Fact]
    public void ArchivedEvent_rejects_blank_contract_id_through_with_expression()
    {
        var archived = new ArchivedEvent("event-1", "cid-1", Template, []);

        var rewrite = () => archived with { ContractId = " " };

        rewrite.Should().Throw<ArgumentException>().WithParameterName("ContractId");
    }

    [Theory]
    [MemberData(nameof(BlankIds))]
    public void ExercisedEvent_rejects_blank_contract_id(string? contractId)
    {
        var construct = () => NewExercisedEvent(contractId!);

        construct.Should().Throw<ArgumentException>().WithParameterName("ContractId");
    }

    [Fact]
    public void ExercisedEvent_rejects_blank_contract_id_through_with_expression()
    {
        var exercised = NewExercisedEvent("cid-1");

        var rewrite = () => exercised with { ContractId = " " };

        rewrite.Should().Throw<ArgumentException>().WithParameterName("ContractId");
    }

    [Theory]
    [MemberData(nameof(BlankIds))]
    public void CreatedContract_rejects_blank_contract_id(string? contractId)
    {
        var construct = () => NewCreatedContract(contractId!);

        construct.Should().Throw<ArgumentException>().WithParameterName("ContractId");
    }

    [Fact]
    public void CreatedContract_rejects_blank_contract_id_through_with_expression()
    {
        var created = NewCreatedContract("cid-1");

        var rewrite = () => created with { ContractId = " " };

        rewrite.Should().Throw<ArgumentException>().WithParameterName("ContractId");
    }

    [Theory]
    [MemberData(nameof(BlankIds))]
    public void TreeEvent_Created_rejects_blank_contract_id(string? contractId)
    {
        var construct = () => NewTreeCreated(contractId!);

        construct.Should().Throw<ArgumentException>().WithParameterName("ContractId");
    }

    [Fact]
    public void TreeEvent_Created_rejects_blank_contract_id_through_with_expression()
    {
        var created = NewTreeCreated("cid-1");

        var rewrite = () => created with { ContractId = " " };

        rewrite.Should().Throw<ArgumentException>().WithParameterName("ContractId");
    }

    [Theory]
    [MemberData(nameof(BlankIds))]
    public void TreeEvent_Exercised_rejects_blank_contract_id(string? contractId)
    {
        var construct = () => NewTreeExercised(contractId!);

        construct.Should().Throw<ArgumentException>().WithParameterName("ContractId");
    }

    [Fact]
    public void TreeEvent_Exercised_rejects_blank_contract_id_through_with_expression()
    {
        var exercised = NewTreeExercised("cid-1");

        var rewrite = () => exercised with { ContractId = " " };

        rewrite.Should().Throw<ArgumentException>().WithParameterName("ContractId");
    }

    [Theory]
    [MemberData(nameof(BlankIds))]
    public void DisclosedContract_rejects_blank_contract_id(string? contractId)
    {
        var construct = () => new DisclosedContract(contractId!, Template, new byte[] { 1 });

        construct.Should().Throw<ArgumentException>().WithParameterName("ContractId");
    }

    [Fact]
    public void DisclosedContract_rejects_blank_contract_id_through_with_expression()
    {
        var disclosed = new DisclosedContract("cid-1", Template, new byte[] { 1 });

        var rewrite = () => disclosed with { ContractId = " " };

        rewrite.Should().Throw<ArgumentException>().WithParameterName("ContractId");
    }

    public static TheoryData<string> BlankElements => new() { "", "   ", "\t" };

    [Theory]
    [MemberData(nameof(BlankElements))]
    public void TransactionResult_rejects_a_blank_archived_contract_id_element(string blank)
    {
        var construct = () => NewTransactionResult(["cid-1", blank]);

        construct.Should().Throw<ArgumentException>()
            .WithParameterName("ArchivedContractIds")
            .Which.Message.Should().StartWith("Element 1 is null, empty or whitespace;");
    }

    [Fact]
    public void TransactionResult_rejects_a_blank_archived_contract_id_element_through_with_expression()
    {
        var result = NewTransactionResult(["cid-1"]);

        var rewrite = () => result with { ArchivedContractIds = ["cid-1", " "] };

        rewrite.Should().Throw<ArgumentException>().WithParameterName("ArchivedContractIds");
    }

    [Fact]
    public void TransactionResult_accepts_no_archived_contract_ids()
    {
        var result = NewTransactionResult([]);

        result.ArchivedContractIds.Count.Should().Be(0);
    }

    [Fact]
    public void TransactionResult_keeps_its_json_property_order()
    {
        var result = NewTransactionResult(["cid-1"]);

        var json = System.Text.Json.JsonSerializer.Serialize(result);

        json.Should().Be(
            "{\"UpdateId\":\"u-1\",\"CompletionOffset\":" + System.Text.Json.JsonSerializer.Serialize(LedgerOffset.At(1))
            + ",\"CreatedContracts\":[],\"ArchivedContractIds\":[\"cid-1\"],\"CommandId\":null,\"ExercisedEvents\":[],\"PaidTrafficCost\":null}");
    }

    [Theory]
    [MemberData(nameof(BlankElements))]
    public void ExerciseOutcome_Many_rejects_a_blank_contract_id_element(string blank)
    {
        var construct = () => new ExerciseOutcome<string>.Many(["cid-1", blank]);

        construct.Should().Throw<ArgumentException>()
            .WithParameterName("ContractIds")
            .Which.Message.Should().StartWith("Element 1 is null, empty or whitespace;");
    }

    [Fact]
    public void ExerciseOutcome_Many_rejects_a_blank_contract_id_element_through_with_expression()
    {
        var outcome = new ExerciseOutcome<string>.Many(["cid-1", "cid-2"]);

        var rewrite = () => outcome with { ContractIds = ["cid-1", " "] };

        rewrite.Should().Throw<ArgumentException>().WithParameterName("ContractIds");
    }

    private static TransactionResult NewTransactionResult(EquatableArray<string> archivedContractIds) =>
        new("u-1", LedgerOffset.At(1), [], archivedContractIds, null);

    private static ExercisedEvent NewExercisedEvent(string contractId) => new(
        contractId, Template, null, new ChoiceName("DoThing"), DamlRecord.Create(), DamlRecord.Create(), true, [], []);

    private static CreatedContract NewCreatedContract(string contractId) => new(
        "event-1", contractId, Template, DamlRecord.Create(), [], [], []);

    private static TreeEvent.Created NewTreeCreated(string contractId) => new(
        "event-1", contractId, Template, DamlRecord.Create(), [], [], []);

    private static TreeEvent.Exercised NewTreeExercised(string contractId) => new(
        "event-1", contractId, Template, null, new ChoiceName("DoThing"), DamlRecord.Create(), DamlRecord.Create(), true, [], [], []);
}
