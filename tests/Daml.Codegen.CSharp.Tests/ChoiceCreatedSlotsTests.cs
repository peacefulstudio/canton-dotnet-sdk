// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceCreatedSlotsTests
{
    private const string LocalPackageId = "pkg-id";

    private sealed class StubResolver(string resolvedName = "Resolved", Func<string, DamlPackage?>? lookupPackage = null) : ICrossPackageResolver
    {
        public string Resolve(DamlTypeRef typeRef, PackageEmitContext context) => resolvedName;

        public IReadOnlySet<string> DiscoveredExternalPackageIds => new HashSet<string>();

        public DamlPackage? LookupPackage(string packageId) => lookupPackage?.Invoke(packageId);
    }

    private static DamlPackage Package() =>
        new()
        {
            PackageId = LocalPackageId,
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [new DamlModule { Name = "Main", Templates = [], DataTypes = [], Interfaces = [] }],
            DependencyReferences = [],
        };

    private static PackageEmitContext Context() =>
        PackageEmitContext.ForPackage(Package(), new CodeGenOptions { NamespacePrefix = "Test.Package" }, isMainPackage: true).Single();

    private static IReadOnlyList<ChoiceCreatedSlot> Extract(DamlType returnType, StubResolver? resolver = null)
    {
        var context = Context();
        var actualResolver = resolver ?? new StubResolver();
        var mapper = new DamlTypeMapper(context, actualResolver);
        return ChoiceCreatedSlots.Extract(context, actualResolver, mapper, returnType);
    }

    private static DamlTypeRef Ref(string name) => new(LocalPackageId, "Main", name);

    private static DamlTypeApp ContractIdOf(DamlType arg) =>
        new(new DamlPrimitiveType(DamlPrimitive.ContractId), [arg]);

    private static DamlTypeApp OptionalOf(DamlType arg) =>
        new(new DamlPrimitiveType(DamlPrimitive.Optional), [arg]);

    private static DamlTypeApp ListOf(DamlType arg) =>
        new(new DamlPrimitiveType(DamlPrimitive.List), [arg]);

    private static DamlTypeApp Tuple(params DamlType[] components) =>
        new(new DamlTypeRef(LocalPackageId, "DA.Types", $"Tuple{components.Length}"), components);

    [Fact]
    public void ChoiceCreatedSlots_single_contract_id_yields_one_single_slot()
    {
        var slots = Extract(ContractIdOf(Ref("Agreement")));

        slots.Should().ContainSingle();
        slots[0].FieldName.Should().Be("Agreement");
        slots[0].Cardinality.Should().Be(CreatedCardinality.Single);
    }

    [Fact]
    public void ChoiceCreatedSlots_template_typed_contract_id_has_no_interface_matcher()
    {
        var slots = Extract(ContractIdOf(Ref("Agreement")));

        slots[0].Interface.Should().BeNull();
    }

    [Fact]
    public void ChoiceCreatedSlots_interface_typed_contract_id_yields_an_interface_matcher_with_the_interface_module_and_entity()
    {
        var module = new DamlModule
        {
            Name = "Main",
            Templates = [],
            DataTypes = [new DamlDataType { Name = "Holdable", Definition = new DamlRecordDefinition([]) }],
            Interfaces = [new DamlInterface { Name = "Holdable", Choices = [], ViewType = null }],
        };
        var package = new DamlPackage
        {
            PackageId = LocalPackageId,
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };
        var context = PackageEmitContext.ForPackage(package, new CodeGenOptions { NamespacePrefix = "Test.Package" }, isMainPackage: true).Single();
        var resolver = new StubResolver();
        var mapper = new DamlTypeMapper(context, resolver);

        var slots = ChoiceCreatedSlots.Extract(context, resolver, mapper, ContractIdOf(Ref("Holdable")));

        slots.Should().ContainSingle();
        slots[0].Interface.Should().Be(new InterfaceMatcher("Main", "Holdable"));
    }

    [Fact]
    public void ChoiceCreatedSlots_interface_typed_contract_id_from_a_foreign_package_yields_an_interface_matcher()
    {
        const string ForeignPackageId = "foreign-pkg-id";
        var foreignModule = new DamlModule
        {
            Name = "Foreign.Module",
            Templates = [],
            DataTypes = [new DamlDataType { Name = "Holdable", Definition = new DamlRecordDefinition([]) }],
            Interfaces = [new DamlInterface { Name = "Holdable", Choices = [], ViewType = null }],
        };
        var foreignPackage = new DamlPackage
        {
            PackageId = ForeignPackageId,
            Name = "foreign-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [foreignModule],
            DependencyReferences = [],
        };
        var resolver = new StubResolver(lookupPackage: id => id == ForeignPackageId ? foreignPackage : null);
        var context = Context();
        var mapper = new DamlTypeMapper(context, resolver);
        var foreignRef = new DamlTypeRef(ForeignPackageId, "Foreign.Module", "Holdable");

        var slots = ChoiceCreatedSlots.Extract(context, resolver, mapper, ContractIdOf(foreignRef));

        slots.Should().ContainSingle();
        slots[0].Interface.Should().Be(new InterfaceMatcher("Foreign.Module", "Holdable"));
    }

    [Fact]
    public void ChoiceCreatedSlots_optional_contract_id_yields_an_optional_slot()
    {
        var slots = Extract(OptionalOf(ContractIdOf(Ref("Agreement"))));

        slots.Should().ContainSingle();
        slots[0].Cardinality.Should().Be(CreatedCardinality.Optional);
    }

    [Fact]
    public void ChoiceCreatedSlots_list_of_contract_id_yields_a_list_slot()
    {
        var slots = Extract(ListOf(ContractIdOf(Ref("Agreement"))));

        slots.Should().ContainSingle();
        slots[0].Cardinality.Should().Be(CreatedCardinality.List);
    }

    [Fact]
    public void ChoiceCreatedSlots_tuple_is_flattened_across_components()
    {
        var slots = Extract(Tuple(ContractIdOf(Ref("Buyer")), ContractIdOf(Ref("Seller"))));

        slots.Should().HaveCount(2);
        slots[0].FieldName.Should().Be("Buyer");
        slots[1].FieldName.Should().Be("Seller");
    }

    [Fact]
    public void ChoiceCreatedSlots_same_template_twice_disambiguates_field_names()
    {
        var slots = Extract(Tuple(ContractIdOf(Ref("Half")), ContractIdOf(Ref("Half"))));

        slots.Should().HaveCount(2);
        slots[0].FieldName.Should().Be("Half");
        slots[1].FieldName.Should().Be("Half2");
    }

    [Fact]
    public void ChoiceCreatedSlots_base_name_ending_in_digit_is_left_untouched_when_unique()
    {
        var slots = Extract(Tuple(ContractIdOf(Ref("Half2")), ContractIdOf(Ref("Whole"))));

        slots.Should().HaveCount(2);
        slots[0].FieldName.Should().Be("Half2");
        slots[1].FieldName.Should().Be("Whole");
    }

    [Fact]
    public void ChoiceCreatedSlots_synthesized_suffix_does_not_steal_a_later_real_name()
    {
        var slots = Extract(Tuple(
            ContractIdOf(Ref("Half")),
            ContractIdOf(Ref("Half")),
            ContractIdOf(Ref("Half2"))));

        slots.Select(s => s.FieldName).Should().Equal("Half", "Half3", "Half2");
    }

    [Fact]
    public void ChoiceCreatedSlots_cascading_collisions_each_get_a_distinct_free_suffix()
    {
        var slots = Extract(Tuple(
            ContractIdOf(Ref("Half")),
            ContractIdOf(Ref("Half")),
            ContractIdOf(Ref("Half")),
            ContractIdOf(Ref("Half2")),
            ContractIdOf(Ref("Half3"))));

        slots.Select(s => s.FieldName).Should().Equal("Half", "Half4", "Half5", "Half2", "Half3");
    }

    [Fact]
    public void ChoiceCreatedSlots_bare_contract_id_has_an_empty_result_path()
    {
        var slots = Extract(ContractIdOf(Ref("Agreement")));

        slots[0].ResultPath.Should().BeEmpty();
    }

    [Fact]
    public void ChoiceCreatedSlots_tuple_components_record_their_zero_based_position()
    {
        var slots = Extract(Tuple(ContractIdOf(Ref("Buyer")), ContractIdOf(Ref("Seller"))));

        slots[0].ResultPath.Should().Equal(new ExerciseResultStep.TupleComponent(0));
        slots[1].ResultPath.Should().Equal(new ExerciseResultStep.TupleComponent(1));
    }

    [Fact]
    public void ChoiceCreatedSlots_optional_contract_id_steps_into_the_optional_value()
    {
        var slots = Extract(OptionalOf(ContractIdOf(Ref("Agreement"))));

        slots[0].ResultPath.Should().Equal(new ExerciseResultStep.OptionalValue());
    }

    [Fact]
    public void ChoiceCreatedSlots_list_of_tuples_steps_into_each_element_then_the_component_outermost_first()
    {
        var slots = Extract(ListOf(Tuple(new DamlPrimitiveType(DamlPrimitive.Int64), ContractIdOf(Ref("Agreement")))));

        slots.Should().ContainSingle();
        slots[0].ResultPath.Should().Equal(
            new ExerciseResultStep.ListElements(),
            new ExerciseResultStep.TupleComponent(1));
    }

    [Fact]
    public void ChoiceCreatedSlots_non_contract_return_yields_no_slots()
    {
        Extract(new DamlPrimitiveType(DamlPrimitive.Int64)).Should().BeEmpty();
        Extract(new DamlPrimitiveType(DamlPrimitive.Unit)).Should().BeEmpty();
        Extract(Ref("SomeRecord")).Should().BeEmpty();
    }
}
