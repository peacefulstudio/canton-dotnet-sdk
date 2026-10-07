// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceReturnsContractIdsTests
{
    private const string LocalPackageId = "pkg-id";

    private static DamlTypeRef Ref(string name) => new(LocalPackageId, "Main", name);

    private static DamlPrimitiveType Prim(DamlPrimitive primitive) => new(primitive);

    private static DamlTypeApp ContractIdOf(DamlType arg) => new(Prim(DamlPrimitive.ContractId), [arg]);

    private static DamlTypeApp OptionalOf(DamlType arg) => new(Prim(DamlPrimitive.Optional), [arg]);

    private static DamlTypeApp ListOf(DamlType arg) => new(Prim(DamlPrimitive.List), [arg]);

    private static DamlTypeApp Tuple(params DamlType[] components) =>
        new(new DamlTypeRef(LocalPackageId, "DA.Types", $"Tuple{components.Length}"), components);

    [Fact]
    public void ReturnsContractIds_is_true_for_a_bare_contract_id()
    {
        ChoiceEmitter.ReturnsContractIds(ContractIdOf(Ref("Coin"))).Should().BeTrue();
    }

    [Fact]
    public void ReturnsContractIds_is_true_for_a_contract_id_in_its_dedicated_model_form()
    {
        ChoiceEmitter.ReturnsContractIds(new DamlContractIdType(Ref("Coin"))).Should().BeTrue();
    }

    [Fact]
    public void ReturnsContractIds_is_true_for_an_optional_contract_id_in_either_model_form()
    {
        ChoiceEmitter.ReturnsContractIds(OptionalOf(ContractIdOf(Ref("Coin")))).Should().BeTrue();
        ChoiceEmitter.ReturnsContractIds(new DamlOptionalType(ContractIdOf(Ref("Coin")))).Should().BeTrue();
    }

    [Fact]
    public void ReturnsContractIds_is_true_for_a_list_of_contract_ids_in_either_model_form()
    {
        ChoiceEmitter.ReturnsContractIds(ListOf(ContractIdOf(Ref("Coin")))).Should().BeTrue();
        ChoiceEmitter.ReturnsContractIds(new DamlListType(ContractIdOf(Ref("Coin")))).Should().BeTrue();
    }

    [Fact]
    public void ReturnsContractIds_is_true_for_a_tuple_with_any_contract_id_component()
    {
        ChoiceEmitter.ReturnsContractIds(Tuple(Prim(DamlPrimitive.Int64), ContractIdOf(Ref("Coin")))).Should().BeTrue();
    }

    [Fact]
    public void ReturnsContractIds_is_true_for_a_contract_id_nested_through_list_tuple_and_optional()
    {
        var nested = ListOf(Tuple(Prim(DamlPrimitive.Text), OptionalOf(ContractIdOf(Ref("Coin")))));

        ChoiceEmitter.ReturnsContractIds(nested).Should().BeTrue();
    }

    [Fact]
    public void ReturnsContractIds_is_false_for_a_tuple_without_a_contract_id()
    {
        ChoiceEmitter.ReturnsContractIds(Tuple(Prim(DamlPrimitive.Int64), Prim(DamlPrimitive.Text))).Should().BeFalse();
    }

    [Fact]
    public void ReturnsContractIds_is_false_for_primitives_and_unit()
    {
        ChoiceEmitter.ReturnsContractIds(Prim(DamlPrimitive.Unit)).Should().BeFalse();
        ChoiceEmitter.ReturnsContractIds(Prim(DamlPrimitive.Int64)).Should().BeFalse();
        ChoiceEmitter.ReturnsContractIds(OptionalOf(Prim(DamlPrimitive.Text))).Should().BeFalse();
        ChoiceEmitter.ReturnsContractIds(ListOf(Prim(DamlPrimitive.Party))).Should().BeFalse();
    }

    [Fact]
    public void ReturnsContractIds_is_false_for_a_record_even_when_it_has_a_contract_id_field()
    {
        ChoiceEmitter.ReturnsContractIds(Ref("Bundle")).Should().BeFalse();
    }

    [Fact]
    public void ReturnsContractIds_is_false_for_a_tuple_named_type_outside_DA_Types()
    {
        var lookalike = new DamlTypeApp(new DamlTypeRef(LocalPackageId, "Main", "Tuple2"), [ContractIdOf(Ref("Coin")), Prim(DamlPrimitive.Int64)]);

        ChoiceEmitter.ReturnsContractIds(lookalike).Should().BeFalse();
    }
}
