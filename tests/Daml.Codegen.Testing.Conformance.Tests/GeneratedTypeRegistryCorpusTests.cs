// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class GeneratedTypeRegistryCorpusTests
{
    private const string RichTypesPackageId = "e2f1475ac77ca53e35a5ac260f1cd3999b8262bd1b1846d81dd55530375bda68";

    private const string ContractKeysPackageId = "33f0e87d5b88878f27b92868bb308be19850408c8e8fb40f3fe753e04ace8fe5";

    private static RegistryLookup<TValue>.Resolved ResolvedOf<TValue>(RegistryLookup<TValue> lookup) =>
        lookup.Should().BeOfType<RegistryLookup<TValue>.Resolved>().Subject;

    private static IChoice ChoiceOf(Identifier identifier, string choiceName) =>
        ResolvedOf(GeneratedTypeReaders.FindChoice(identifier, new ChoiceName(choiceName))).Value;

    [Fact]
    public void FindRecordReader_resolves_the_asset_create_reader_once_a_generated_type_is_touched()
    {
        _ = Asset.TemplateId;

        var resolved = ResolvedOf(GeneratedTypeReaders.FindRecordReader(new Identifier(RichTypesPackageId, "RichTypes", "Asset")));

        resolved.DeclaringType.Should().Be<Asset>();
    }

    [Fact]
    public void FindChoice_resolves_the_archive_choice_of_the_asset_template()
    {
        _ = Asset.TemplateId;

        ChoiceOf(new Identifier(RichTypesPackageId, "RichTypes", "Asset"), "Archive").ResultType.Should().Be<DamlUnit>();
    }

    [Fact]
    public void FindChoice_resolves_the_choices_of_the_holding_interface()
    {
        _ = Asset.TemplateId;
        var holding = new Identifier(RichTypesPackageId, "RichTypes", "Holding");

        ChoiceOf(holding, "Describe").ResultType.Should().Be<string>();
        ChoiceOf(holding, "Archive").ResultType.Should().Be<DamlUnit>();
        ResolvedOf(GeneratedTypeReaders.FindChoice(holding, new ChoiceName("Split"))).DeclaringType.Should().Be<IHolding>();
    }

    [Fact]
    public void FindRecordReader_does_not_resolve_an_interface_or_a_plain_record()
    {
        _ = Asset.TemplateId;

        GeneratedTypeReaders.FindRecordReader(new Identifier(RichTypesPackageId, "RichTypes", "Holding"))
            .Should().BeOfType<RegistryLookup<DamlLfElementReader>.Missing>();
        GeneratedTypeReaders.FindRecordReader(new Identifier(RichTypesPackageId, "RichTypes", "Reissue"))
            .Should().BeOfType<RegistryLookup<DamlLfElementReader>.Missing>();
    }

    [Fact]
    public void FindKeyDescriptor_resolves_a_keyed_template_and_not_an_unkeyed_one()
    {
        _ = Account.TemplateId;
        _ = Asset.TemplateId;

        ResolvedOf(GeneratedTypeReaders.FindKeyDescriptor(new Identifier(ContractKeysPackageId, "ContractKeys", "Account")))
            .DeclaringType.Should().Be<Account>();
        GeneratedTypeReaders.FindKeyDescriptor(new Identifier(RichTypesPackageId, "RichTypes", "Asset"))
            .Should().BeOfType<RegistryLookup<IKeyDescriptor>.Missing>();
    }

    [Fact]
    public void FindChoice_resolves_by_module_and_entity_when_the_package_id_is_unknown()
    {
        _ = Asset.TemplateId;

        ChoiceOf(new Identifier("unknown-package-id", "RichTypes", "Asset"), "Archive").ResultType.Should().Be<DamlUnit>();
    }
}
