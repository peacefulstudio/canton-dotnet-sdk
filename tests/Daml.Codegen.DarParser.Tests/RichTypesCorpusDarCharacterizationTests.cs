// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.DarParser;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.DarParser.Tests;

/// <summary>
/// Reads the shipped conformance corpus DAR and asserts, construct by construct,
/// that the type-system corners the corpus claims to cover are really present in
/// the compiled archive. Codegen output alone cannot make that claim: the emitter
/// happily produces a file for a fixture that dropped a field, so the assertions
/// here read the DAR, not the generated tree.
/// </summary>
public sealed class RichTypesCorpusDarCharacterizationTests
{
    private const string CorpusLfVersion = "2.1";

    private static async Task<DamlPackage> LoadMainPackageAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "richtypes.dar");
        var archive = await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);
        return archive.MainPackage;
    }

    private static async Task<IReadOnlyList<DamlFieldDefinition>> TypeCornersFieldsAsync()
    {
        var package = await LoadMainPackageAsync();
        return package.Modules.Single(m => m.Name == "RichTypes")
            .DataTypes.Single(d => d.Name == "TypeCorners")
            .Definition.Should().BeOfType<DamlRecordDefinition>().Subject.Fields;
    }

    private static DamlType FieldType(IReadOnlyList<DamlFieldDefinition> fields, string name) =>
        fields.Single(f => f.Name == name).Type;

    private static async Task<IReadOnlyList<DamlFieldDefinition>> RecordFieldsAsync(string moduleName, string typeName)
    {
        var package = await LoadMainPackageAsync();
        return package.Modules.Single(m => m.Name == moduleName)
            .DataTypes.Single(d => d.Name == typeName)
            .Definition.Should().BeOfType<DamlRecordDefinition>().Subject.Fields;
    }

    private static IEnumerable<DamlType> DefinedTypes(DamlDataTypeDefinition definition) => definition switch
    {
        DamlRecordDefinition record => record.Fields.Select(field => field.Type),
        DamlVariantDefinition variant => variant.Constructors.Select(c => c.ArgumentType).OfType<DamlType>(),
        _ => []
    };

    private static IEnumerable<string> ImportedPackageIds(DamlType type) => type switch
    {
        DamlTypeRef reference when reference.PackageId.Length > 0 => [reference.PackageId],
        DamlTypeApp application => application.Arguments.Prepend(application.Base).SelectMany(ImportedPackageIds),
        DamlListType list => ImportedPackageIds(list.Element),
        DamlOptionalType optional => ImportedPackageIds(optional.Value),
        DamlTextMapType textMap => ImportedPackageIds(textMap.Value),
        DamlGenMapType genMap => ImportedPackageIds(genMap.Key).Concat(ImportedPackageIds(genMap.Value)),
        DamlContractIdType contractId => ImportedPackageIds(contractId.Payload),
        _ => []
    };

    [Fact]
    public async Task RichTypesCorpus_declares_every_imported_package_its_own_type_model_names()
    {
        var package = await LoadMainPackageAsync();

        var namedByTheTypeModel = package.Modules
            .SelectMany(module => module.DataTypes)
            .SelectMany(dataType => DefinedTypes(dataType.Definition))
            .SelectMany(ImportedPackageIds)
            .Where(packageId => packageId != package.PackageId)
            .ToHashSet();

        namedByTheTypeModel.Should().NotBeEmpty(
            "the corpus builds on the standard library, so its converted type model must name a foreign package");
        namedByTheTypeModel.Should()
            .BeSubsetOf(package.DependencyReferences.Select(reference => reference.PackageId));
    }

    [Fact]
    public async Task RichTypesCorpus_is_pinned_to_the_daml_lf_version_its_build_options_request()
    {
        var package = await LoadMainPackageAsync();

        package.Name.Should().Be("richtypes-h25b56c547fd1");
        package.LfVersion.Should().Be(
            CorpusLfVersion,
            "conformance/richtypes/daml.yaml pins --target={0}; damlc picks a different version when nothing asks, "
            + "so a mismatch means the pin stopped taking effect",
            CorpusLfVersion);
    }

    [Fact]
    public async Task RichTypesCorpus_declares_a_parameterized_record_and_a_parameterized_variant()
    {
        var package = await LoadMainPackageAsync();
        var dataTypes = package.Modules.Single(m => m.Name == "RichTypes").DataTypes;

        var box = dataTypes.Single(d => d.Name == "Box");
        box.TypeParams.Should().Equal("a");
        box.Definition.Should().BeOfType<DamlRecordDefinition>()
            .Subject.Fields.Single(f => f.Name == "item").Type.Should().Be(new DamlTypeVar("a"));

        var slot = dataTypes.Single(d => d.Name == "Slot");
        slot.TypeParams.Should().Equal("a");
        slot.Definition.Should().BeOfType<DamlVariantDefinition>()
            .Subject.Constructors.Select(c => c.Name).Should().Equal("Filled", "Vacant");
    }

    [Fact]
    public async Task RichTypesCorpus_instantiates_the_parameterized_types_in_a_template_payload()
    {
        var fields = await TypeCornersFieldsAsync();

        FieldType(fields, "boxedText").Should().BeOfType<DamlTypeApp>()
            .Subject.Base.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("Box");
        FieldType(fields, "boxedProfile").Should().BeOfType<DamlTypeApp>()
            .Subject.Arguments.Single().Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("Profile");
        FieldType(fields, "slot").Should().BeOfType<DamlTypeApp>()
            .Subject.Base.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("Slot");
    }

    [Fact]
    public async Task RichTypesCorpus_declares_gen_map_fields_keyed_by_something_other_than_text()
    {
        var fields = await TypeCornersFieldsAsync();

        var quotaByParty = FieldType(fields, "quotaByParty").Should().BeOfType<DamlGenMapType>().Subject;
        quotaByParty.Key.Should().Be(new DamlPrimitiveType(DamlPrimitive.Party));

        var labelByRank = FieldType(fields, "labelByRank").Should().BeOfType<DamlGenMapType>().Subject;
        labelByRank.Key.Should().Be(new DamlPrimitiveType(DamlPrimitive.Int64));
    }

    [Fact]
    public async Task RichTypesCorpus_nests_an_optional_inside_an_optional_through_a_wrapper_record()
    {
        var fields = await TypeCornersFieldsAsync();

        var outer = FieldType(fields, "nestedNote").Should().BeOfType<DamlOptionalType>().Subject;
        var wrapper = outer.Value.Should().BeOfType<DamlTypeApp>().Subject;
        wrapper.Base.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("Box");
        wrapper.Arguments.Single().Should().BeOfType<DamlOptionalType>();
    }

    [Fact]
    public async Task RichTypesCorpus_nests_an_optional_directly_inside_an_optional()
    {
        var fields = await TypeCornersFieldsAsync();

        var outer = FieldType(fields, "maybeMaybeNote").Should().BeOfType<DamlOptionalType>().Subject;
        var inner = outer.Value.Should().BeOfType<DamlOptionalType>().Subject;
        inner.Value.Should().Be(new DamlPrimitiveType(DamlPrimitive.Text));
    }

    [Fact]
    public async Task RichTypesCorpus_nests_an_optional_three_deep()
    {
        var fields = await RecordFieldsAsync("RichTypes", "ThreeDeepOptionals");

        foreach (var name in new[] { "deepNote", "tailDeepNote" })
        {
            var outer = FieldType(fields, name).Should().BeOfType<DamlOptionalType>().Subject;
            var middle = outer.Value.Should().BeOfType<DamlOptionalType>().Subject;
            var inner = middle.Value.Should().BeOfType<DamlOptionalType>().Subject;
            inner.Value.Should().Be(new DamlPrimitiveType(DamlPrimitive.Text));
        }
    }

    [Fact]
    public async Task RichTypesCorpus_hides_an_optional_behind_a_parameterized_wrapper_record()
    {
        var package = await LoadMainPackageAsync();

        var crate = package.Modules.Single(m => m.Name == "RichTypes").DataTypes.Single(d => d.Name == "Crate");
        crate.TypeParams.Should().Equal("a");
        var item = crate.Definition.Should().BeOfType<DamlRecordDefinition>()
            .Subject.Fields.Single(f => f.Name == "item").Type.Should().BeOfType<DamlOptionalType>().Subject;
        item.Value.Should().Be(new DamlTypeVar("a"));

        var fields = await TypeCornersFieldsAsync();
        var crateField = FieldType(fields, "crate").Should().BeOfType<DamlTypeApp>().Subject;
        crateField.Base.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("Crate");
        crateField.Arguments.Single().Should().Be(new DamlPrimitiveType(DamlPrimitive.Text));
    }

    [Fact]
    public async Task RichTypesCorpus_declares_either_and_tuple_fields()
    {
        var fields = await TypeCornersFieldsAsync();

        var rankOrLabel = FieldType(fields, "rankOrLabel").Should().BeOfType<DamlTypeApp>().Subject;
        rankOrLabel.Base.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("Either");
        rankOrLabel.Arguments.Should().Equal(
            new DamlPrimitiveType(DamlPrimitive.Int64),
            new DamlPrimitiveType(DamlPrimitive.Text));

        FieldType(fields, "pair").Should().BeOfType<DamlTypeApp>()
            .Subject.Base.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("Tuple2");
        FieldType(fields, "triple").Should().BeOfType<DamlTypeApp>()
            .Subject.Base.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("Tuple3");
    }

    [Fact]
    public async Task RichTypesCorpus_declares_a_recursive_record()
    {
        var package = await LoadMainPackageAsync();

        var branch = package.Modules.Single(m => m.Name == "RichTypes").DataTypes.Single(d => d.Name == "Branch");

        var children = branch.Definition.Should().BeOfType<DamlRecordDefinition>()
            .Subject.Fields.Single(f => f.Name == "children").Type;
        var list = children.Should().BeOfType<DamlListType>().Subject;
        list.Element.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("Branch");
    }

    [Fact]
    public async Task RichTypesCorpus_recursive_record_references_itself_by_the_main_package_id()
    {
        var package = await LoadMainPackageAsync();

        var children = package.Modules.Single(m => m.Name == "RichTypes").DataTypes.Single(d => d.Name == "Branch")
            .Definition.Should().BeOfType<DamlRecordDefinition>()
            .Subject.Fields.Single(f => f.Name == "children").Type;

        package.PackageId.Should().Be("2f0d55f302337c8a8a0497a00bc255f8e7a30aa52c783719dd5a3bdd486904d4");
        children.Should().BeOfType<DamlListType>().Subject.Element.Should().Be(
            new DamlTypeRef("2f0d55f302337c8a8a0497a00bc255f8e7a30aa52c783719dd5a3bdd486904d4", "RichTypes", "Branch"));
    }

    [Fact]
    public async Task RichTypesCorpus_interface_declares_choices_beyond_the_synthetic_archive()
    {
        var package = await LoadMainPackageAsync();

        var holding = package.Modules.Single(m => m.Name == "RichTypes").Interfaces.Single(i => i.Name == "Holding");

        holding.Choices.Should().Contain(c => c.Name == "Describe" && !c.Consuming);
        holding.Choices.Should().Contain(c => c.Name == "Reissue" && c.Consuming);
        holding.Choices.Should().Contain(c => c.Name == "Split" && !c.Consuming);
    }

    [Fact]
    public async Task RichTypesCorpus_interface_choice_returns_a_list_of_contract_ids_of_the_interface_itself()
    {
        var package = await LoadMainPackageAsync();

        var holding = package.Modules.Single(m => m.Name == "RichTypes").Interfaces.Single(i => i.Name == "Holding");
        var split = holding.Choices.Single(c => c.Name == "Split");

        split.Consuming.Should().BeFalse();
        var returnType = split.ReturnType.Should().BeOfType<DamlListType>().Subject;
        var element = returnType.Element.Should().BeOfType<DamlContractIdType>().Subject;
        element.Payload.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("Holding");
    }

    [Fact]
    public async Task RichTypesCorpus_template_choice_returns_a_list_of_contract_ids_of_its_own_template()
    {
        var package = await LoadMainPackageAsync();

        var genericResults = package.Modules.Single(m => m.Name == "RichTypes").Templates.Single(t => t.Name == "GenericResults");
        var returnContractIds = genericResults.Choices.Single(c => c.Name == "ReturnContractIds");

        returnContractIds.Consuming.Should().BeFalse();
        var returnType = returnContractIds.ReturnType.Should().BeOfType<DamlListType>().Subject;
        var element = returnType.Element.Should().BeOfType<DamlContractIdType>().Subject;
        element.Payload.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("GenericResults");
    }

    [Fact]
    public async Task RichTypesCorpus_template_choice_returns_an_optional_driven_by_its_own_argument()
    {
        var package = await LoadMainPackageAsync();

        var genericResults = package.Modules.Single(m => m.Name == "RichTypes").Templates.Single(t => t.Name == "GenericResults");
        var returnOptionalText = genericResults.Choices.Single(c => c.Name == "ReturnOptionalText");

        returnOptionalText.Consuming.Should().BeFalse();
        var returnType = returnOptionalText.ReturnType.Should().BeOfType<DamlOptionalType>().Subject;
        returnType.Value.Should().Be(new DamlPrimitiveType(DamlPrimitive.Text));

        var argumentType = returnOptionalText.ArgumentType.Should().BeOfType<DamlTypeRef>().Subject;
        argumentType.Name.Should().Be("ReturnOptionalText");
        var argumentFields = await RecordFieldsAsync("RichTypes", "ReturnOptionalText");
        argumentFields.Single(f => f.Name == "wantSome").Type.Should().Be(new DamlPrimitiveType(DamlPrimitive.Bool));
    }

    [Fact]
    public async Task RichTypesCorpus_template_choice_returns_a_text_keyed_map()
    {
        var package = await LoadMainPackageAsync();

        var genericResults = package.Modules.Single(m => m.Name == "RichTypes").Templates.Single(t => t.Name == "GenericResults");
        var returnTextMap = genericResults.Choices.Single(c => c.Name == "ReturnTextMap");

        returnTextMap.Consuming.Should().BeFalse();
        var returnType = returnTextMap.ReturnType.Should().BeOfType<DamlTextMapType>().Subject;
        returnType.Value.Should().Be(new DamlPrimitiveType(DamlPrimitive.Int64));
    }

    [Fact]
    public async Task RichTypesCorpus_template_choice_returns_a_gen_map_that_da_map_also_compiles_to()
    {
        var package = await LoadMainPackageAsync();

        var genericResults = package.Modules.Single(m => m.Name == "RichTypes").Templates.Single(t => t.Name == "GenericResults");
        var returnGenMap = genericResults.Choices.Single(c => c.Name == "ReturnGenMap");

        returnGenMap.Consuming.Should().BeFalse();
        var returnType = returnGenMap.ReturnType.Should().BeOfType<DamlGenMapType>().Subject;
        returnType.Key.Should().Be(new DamlPrimitiveType(DamlPrimitive.Text),
            "the choice result is written as DA.Map's Map Text Int, but Map is a type synonym for GenMap — "
            + "TypeCorners.quotaByParty/labelByRank pin the same erasure for Map-typed fields above — so this "
            + "one choice proves both the GenMap and the Map family");
        returnType.Value.Should().Be(new DamlPrimitiveType(DamlPrimitive.Int64));
    }

    [Fact]
    public async Task RichTypesCorpus_template_choice_returns_a_two_tuple()
    {
        var package = await LoadMainPackageAsync();

        var genericResults = package.Modules.Single(m => m.Name == "RichTypes").Templates.Single(t => t.Name == "GenericResults");
        var returnTuple = genericResults.Choices.Single(c => c.Name == "ReturnTuple");

        returnTuple.Consuming.Should().BeFalse();
        var returnType = returnTuple.ReturnType.Should().BeOfType<DamlTypeApp>().Subject;
        returnType.Base.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("Tuple2");
        returnType.Arguments.Should().Equal(
            new DamlPrimitiveType(DamlPrimitive.Text),
            new DamlPrimitiveType(DamlPrimitive.Int64));
    }

    [Fact]
    public async Task RichTypesCorpus_template_choice_returns_an_either_driven_by_its_own_argument()
    {
        var package = await LoadMainPackageAsync();

        var genericResults = package.Modules.Single(m => m.Name == "RichTypes").Templates.Single(t => t.Name == "GenericResults");
        var returnEither = genericResults.Choices.Single(c => c.Name == "ReturnEither");

        returnEither.Consuming.Should().BeFalse();
        var returnType = returnEither.ReturnType.Should().BeOfType<DamlTypeApp>().Subject;
        returnType.Base.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("Either");
        returnType.Arguments.Should().Equal(
            new DamlPrimitiveType(DamlPrimitive.Text),
            new DamlPrimitiveType(DamlPrimitive.Int64));

        var argumentFields = await RecordFieldsAsync("RichTypes", "ReturnEither");
        argumentFields.Single(f => f.Name == "wantRight").Type.Should().Be(new DamlPrimitiveType(DamlPrimitive.Bool));
    }

    [Fact]
    public async Task RichTypesCorpus_template_choice_returns_a_set()
    {
        var package = await LoadMainPackageAsync();

        var genericResults = package.Modules.Single(m => m.Name == "RichTypes").Templates.Single(t => t.Name == "GenericResults");
        var returnSet = genericResults.Choices.Single(c => c.Name == "ReturnSet");

        returnSet.Consuming.Should().BeFalse();
        var returnType = returnSet.ReturnType.Should().BeOfType<DamlTypeApp>().Subject;
        returnType.Base.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("Set");
        returnType.Arguments.Single().Should().Be(new DamlPrimitiveType(DamlPrimitive.Int64));
    }

    [Fact]
    public async Task RichTypesCorpus_template_choice_returns_a_non_empty_list()
    {
        var package = await LoadMainPackageAsync();

        var genericResults = package.Modules.Single(m => m.Name == "RichTypes").Templates.Single(t => t.Name == "GenericResults");
        var returnNonEmpty = genericResults.Choices.Single(c => c.Name == "ReturnNonEmpty");

        returnNonEmpty.Consuming.Should().BeFalse();
        var returnType = returnNonEmpty.ReturnType.Should().BeOfType<DamlTypeApp>().Subject;
        returnType.Base.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("NonEmpty");
        returnType.Arguments.Single().Should().Be(new DamlPrimitiveType(DamlPrimitive.Int64));
    }

    [Fact]
    public async Task RichTypesCorpus_template_choice_returns_an_optional_nested_directly_inside_an_optional()
    {
        var package = await LoadMainPackageAsync();

        var genericResults = package.Modules.Single(m => m.Name == "RichTypes").Templates.Single(t => t.Name == "GenericResults");
        var returnNestedOptional = genericResults.Choices.Single(c => c.Name == "ReturnNestedOptional");

        returnNestedOptional.Consuming.Should().BeFalse();
        var outer = returnNestedOptional.ReturnType.Should().BeOfType<DamlOptionalType>().Subject;
        var inner = outer.Value.Should().BeOfType<DamlOptionalType>().Subject;
        inner.Value.Should().Be(new DamlPrimitiveType(DamlPrimitive.Text));

        var argumentFields = await RecordFieldsAsync("RichTypes", "ReturnNestedOptional");
        argumentFields.Single(f => f.Name == "outer").Type.Should().Be(new DamlPrimitiveType(DamlPrimitive.Bool));
        argumentFields.Single(f => f.Name == "inner").Type.Should().Be(new DamlPrimitiveType(DamlPrimitive.Bool));
    }

    [Fact]
    public async Task RichTypesCorpus_interface_declares_its_holder_method_on_the_parser_direct_path()
    {
        var package = await LoadMainPackageAsync();

        var holding = package.Modules.Single(m => m.Name == "RichTypes").Interfaces.Single(i => i.Name == "Holding");

        holding.Methods.Should().Equal(new DamlInterfaceMethod("holder", new DamlPrimitiveType(DamlPrimitive.Party)));
    }

    [Fact]
    public async Task RichTypesCorpus_carries_no_contract_keys()
    {
        var package = await LoadMainPackageAsync();

        var keyed = package.Modules.SelectMany(m => m.Templates).Where(t => t.Key is not null).Select(t => t.Name);

        keyed.Should().BeEmpty(
            "contract keys require Daml-LF 2.3 or later and damlc rejects a key declaration while lowering "
            + "Core to Daml-LF at 2.1 and 2.2, so a keyed template cannot build at the {0} pin; the keys of "
            + "the Daml-LF 1.x line are a separate feature and this codegen rejects 1.x archives outright; "
            + "keyed coverage lives in the contractkeys corpus, which pins 2.3, and moving it here would "
            + "cost the 2.1 coverage this corpus exists to ship",
            CorpusLfVersion);
    }
}
