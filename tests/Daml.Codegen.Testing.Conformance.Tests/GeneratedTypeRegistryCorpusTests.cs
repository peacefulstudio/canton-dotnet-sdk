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
    private const string RichTypesPackageId = "628ba2f8b1c316e4e7df3db05ea1e840c22e0276787292aaf207631594a8ae2a";

    private const string ContractKeysPackageId = "2bf1bcfec97eb3d0466d5fc85bc58d65c7854a5d4e9d5b88401d9c33833d86e2";

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
    public void FindChoice_resolves_by_package_name_when_the_package_id_is_unknown()
    {
        _ = Asset.TemplateId;

        var lookup = GeneratedTypeReaders.FindChoice(
            new Identifier("unknown-package-id", "RichTypes", "Asset"),
            new ChoiceName("Archive"),
            "richtypes-ha8d30776aa45");

        ResolvedOf(lookup).Value.ResultType.Should().Be<DamlUnit>();
    }

    [Fact]
    public void FindChoice_does_not_resolve_an_unknown_package_id_without_a_package_name()
    {
        _ = Asset.TemplateId;

        GeneratedTypeReaders.FindChoice(new Identifier("unknown-package-id", "RichTypes", "Asset"), new ChoiceName("Archive"))
            .Should().BeOfType<RegistryLookup<IChoice>.Missing>();
    }
}
