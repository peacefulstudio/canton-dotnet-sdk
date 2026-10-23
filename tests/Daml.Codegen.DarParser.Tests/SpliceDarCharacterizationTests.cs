// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.DarParser;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.DarParser.Tests;

public sealed class SpliceDarCharacterizationTests
{
    private const string MetadataPackageId = "4ded6b668cb3b64f7a88a30874cd41c75829f5e064b3fbbadf41ec7e8363354f";

    private static async Task<DarArchive> LoadAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "splice-api-token-holding-v1.dar");
        return await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SpliceDarCharacterization_main_package_reports_name_version_and_lf_version()
    {
        var archive = await LoadAsync();

        archive.MainPackage.Name.Should().Be("splice-api-token-holding-v1");
        archive.MainPackage.Version.Should().Be(new Version(1, 0, 0));
        archive.MainPackage.LfVersion.Should().Be("2.1");
    }

    [Fact]
    public async Task SpliceDarCharacterization_main_package_id_is_the_archive_content_hash()
    {
        var archive = await LoadAsync();

        archive.MainPackage.PackageId
            .Should().Be("718a0f77e505a8de22f188bd4c87fe74101274e9d4cb1bfac7d09aec7158d35b");
    }

    [Fact]
    public async Task SpliceDarCharacterization_main_package_has_the_holding_module()
    {
        var archive = await LoadAsync();

        archive.MainPackage.Modules.Should().ContainSingle()
            .Which.Name.Should().Be("Splice.Api.Token.HoldingV1");
    }

    [Fact]
    public async Task SpliceDarCharacterization_holding_interface_is_parsed_with_its_view_type()
    {
        var archive = await LoadAsync();
        var module = archive.MainPackage.Modules.Single();

        var holding = module.Interfaces.Should().ContainSingle()
            .Which;
        holding.Name.Should().Be("Holding");
        holding.ViewType.Should().BeOfType<DamlTypeRef>()
            .Which.Name.Should().Be("HoldingView");
    }

    [Fact]
    public async Task SpliceDarCharacterization_holding_interface_archive_choice_is_consuming_and_returns_unit()
    {
        var archive = await LoadAsync();
        var holding = archive.MainPackage.Modules.Single().Interfaces.Single();

        var choice = holding.Choices.Should().ContainSingle().Which;
        choice.Name.Should().Be("Archive");
        choice.Consuming.Should().BeTrue();
        choice.ReturnType.Should().Be(new DamlPrimitiveType(DamlPrimitive.Unit));
        choice.ArgumentType.Should().BeOfType<DamlTypeRef>()
            .Which.Name.Should().Be("Archive");
    }

    [Fact]
    public async Task SpliceDarCharacterization_holding_view_record_maps_party_and_text_fields_to_primitives()
    {
        var archive = await LoadAsync();
        var module = archive.MainPackage.Modules.Single();

        var view = module.DataTypes.Single(d => d.Name == "HoldingView");
        var fields = view.Definition.Should().BeOfType<DamlRecordDefinition>().Subject.Fields;

        fields.Single(f => f.Name == "owner").Type
            .Should().Be(new DamlPrimitiveType(DamlPrimitive.Party));
    }

    [Fact]
    public async Task SpliceDarCharacterization_instrument_id_record_maps_admin_party_and_text_id()
    {
        var archive = await LoadAsync();
        var module = archive.MainPackage.Modules.Single();

        var instrumentId = module.DataTypes.Single(d => d.Name == "InstrumentId");
        var fields = instrumentId.Definition.Should().BeOfType<DamlRecordDefinition>().Subject.Fields;

        fields.Single(f => f.Name == "admin").Type
            .Should().Be(new DamlPrimitiveType(DamlPrimitive.Party));
        fields.Single(f => f.Name == "id").Type
            .Should().Be(new DamlPrimitiveType(DamlPrimitive.Text));
    }

    [Fact]
    public async Task SpliceDarCharacterization_holding_view_amount_field_is_numeric_type_application()
    {
        var archive = await LoadAsync();
        var module = archive.MainPackage.Modules.Single();

        var view = module.DataTypes.Single(d => d.Name == "HoldingView");
        var fields = view.Definition.Should().BeOfType<DamlRecordDefinition>().Subject.Fields;

        var amount = fields.Single(f => f.Name == "amount").Type
            .Should().BeOfType<DamlTypeApp>().Subject;
        amount.Base.Should().Be(new DamlPrimitiveType(DamlPrimitive.Numeric));
    }

    [Fact]
    public async Task SpliceDarCharacterization_holding_view_lock_field_is_optional_type_application()
    {
        var archive = await LoadAsync();
        var module = archive.MainPackage.Modules.Single();

        var view = module.DataTypes.Single(d => d.Name == "HoldingView");
        var fields = view.Definition.Should().BeOfType<DamlRecordDefinition>().Subject.Fields;

        var lockField = fields.Single(f => f.Name == "lock").Type
            .Should().BeOfType<DamlOptionalType>().Subject;
        lockField.Value.Should().BeOfType<DamlTypeRef>()
            .Which.Name.Should().Be("Lock");
    }

    [Fact]
    public async Task SpliceDarCharacterization_holding_view_meta_field_references_imported_metadata_package()
    {
        var archive = await LoadAsync();
        var module = archive.MainPackage.Modules.Single();

        var view = module.DataTypes.Single(d => d.Name == "HoldingView");
        var fields = view.Definition.Should().BeOfType<DamlRecordDefinition>().Subject.Fields;

        var meta = fields.Single(f => f.Name == "meta").Type
            .Should().BeOfType<DamlTypeRef>().Subject;
        meta.Name.Should().Be("Metadata");
        meta.Module.Should().Be("Splice.Api.Token.MetadataV1");
        meta.PackageId.Should().Be(MetadataPackageId);
    }

    [Fact]
    public async Task SpliceDarCharacterization_lock_record_holders_field_is_list_of_party()
    {
        var archive = await LoadAsync();
        var module = archive.MainPackage.Modules.Single();

        var lockType = module.DataTypes.Single(d => d.Name == "Lock");
        var fields = lockType.Definition.Should().BeOfType<DamlRecordDefinition>().Subject.Fields;

        var holders = fields.Single(f => f.Name == "holders").Type
            .Should().BeOfType<DamlListType>().Subject;
        holders.Element.Should().Be(new DamlPrimitiveType(DamlPrimitive.Party));
    }

    [Fact]
    public async Task SpliceDarCharacterization_dependencies_include_the_metadata_package_resolved_by_name_and_version()
    {
        var archive = await LoadAsync();

        var metadata = archive.Dependencies
            .Should().ContainSingle(p => p.PackageId == MetadataPackageId).Which;
        metadata.Name.Should().Be("splice-api-token-metadata-v1");
        metadata.Version.Should().Be(new Version(1, 0, 0));
    }

    [Fact]
    public async Task SpliceDarCharacterization_metadata_package_any_value_is_a_variant_with_typed_constructors()
    {
        var archive = await LoadAsync();
        var metadata = archive.Dependencies.Single(p => p.PackageId == MetadataPackageId);

        var anyValue = metadata.Modules
            .SelectMany(m => m.DataTypes)
            .Single(d => d.Name == "AnyValue");
        var variant = anyValue.Definition.Should().BeOfType<DamlVariantDefinition>().Subject;

        variant.Constructors.Select(c => c.Name).Should().Contain(["AV_Text", "AV_Int", "AV_Party", "AV_Map"]);
        variant.Constructors.Single(c => c.Name == "AV_Text").ArgumentType
            .Should().Be(new DamlPrimitiveType(DamlPrimitive.Text));
    }

    [Fact]
    public async Task SpliceDarCharacterization_daml_stdlib_rounding_mode_is_stripped_as_non_serializable()
    {
        var archive = await LoadAsync();

        var numericModule = archive.Dependencies
            .SelectMany(p => p.Modules)
            .Should().ContainSingle(m => m.Name == "DA.Numeric").Subject;

        numericModule.DataTypes.Should().NotContain(d => d.Name == "RoundingMode",
            "RoundingMode is a non-serializable enum, which the JVM helper's SignatureErasure strips too");
    }

    [Fact]
    public async Task SpliceDarCharacterization_all_packages_includes_main_plus_every_dependency()
    {
        var archive = await LoadAsync();

        var all = ((IDarSource)archive).AllPackages.ToList();

        all.Should().HaveCount(archive.Dependencies.Count + 1);
        all.Should().ContainSingle(p => p.PackageId == archive.MainPackage.PackageId);
    }

    [Fact]
    public async Task GetPackageById_resolves_the_imported_metadata_package()
    {
        IDarSource archive = await LoadAsync();

        archive.GetPackageById(MetadataPackageId)!.Name
            .Should().Be("splice-api-token-metadata-v1");
        archive.GetPackageById("does-not-exist").Should().BeNull();
    }
}
