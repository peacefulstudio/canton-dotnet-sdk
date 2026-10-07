// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Xunit;
using static Daml.Codegen.CSharp.Tests.ReservedTypeNameFixtures;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedCrossTypeNameClashTests
{
    private static DamlType ContractIdOf(string name) =>
        new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.ContractId), [Ref(name)]);

    private static DamlTemplate AssetWithChoice(string choiceName, DamlType returnType, DamlType? argumentType = null) =>
        Template("Asset", null, Choice(choiceName, returnType, argumentType));

    [Theory]
    [InlineData("AssetExtensions")]
    [InlineData("AssetSubmissionExtensions")]
    public void Sibling_type_named_like_a_template_companion_class_is_a_codegen_error(string siblingName)
    {
        var dar = DarOf(
            [AssetWithChoice("Spawn", ContractIdOf("Asset"))],
            Record("Asset", Field("owner", PartyType)),
            Record(siblingName, Field("x", IntType)));

        var failure = () => CreateGenerator().Generate(dar);

        failure.Should().Throw<CodegenException>().Which.Message.Should().Contain($"The C# type '{siblingName}' is declared twice")
            .And.Contain("Daml template 'Test.Module:Asset'").And.Contain($"Daml record 'Test.Module:{siblingName}'");
    }

    [Fact]
    public void Template_named_like_the_name_a_choice_result_record_used_to_take_compiles()
    {
        var dar = DarOf(
            [
                AssetWithChoice("Spawn", ContractIdOf("Asset")),
                Template("SpawnResult", null),
            ],
            Record("Asset", Field("owner", PartyType)),
            Record("SpawnResult", Field("owner", PartyType)));

        CompileErrors(dar).Should().BeEmpty();
    }

    [Fact]
    public void Generic_sibling_named_like_a_template_companion_class_compiles_because_its_arity_differs()
    {
        var dar = DarOf(
            [AssetWithChoice("Spawn", ContractIdOf("Asset"))],
            Record("Asset", Field("owner", PartyType)),
            new DamlDataType { Name = "AssetExtensions", TypeParams = ["a"], Definition = new DamlRecordDefinition([Field("x", new DamlTypeVar("a"))]) });

        CompileErrors(dar).Should().BeEmpty();
    }

    [Fact]
    public void Sibling_named_like_a_companion_class_the_template_does_not_emit_compiles()
    {
        var dar = DarOf(
            [Template("Asset", null)],
            Record("Asset", Field("owner", PartyType)),
            Record("AssetNonContractExtensions", Field("x", IntType)));

        CompileErrors(dar).Should().BeEmpty();
    }

    [Fact]
    public void Record_named_like_the_extensions_class_of_a_template_is_a_codegen_error_naming_both_daml_names()
    {
        var dar = DarOf(
            [AssetWithChoice("Reissue", IntType)],
            Record("Asset", Field("owner", PartyType)),
            Record("AssetNonContractExtensions", Field("x", IntType)));

        var failure = () => CreateGenerator().Generate(dar);

        failure.Should().Throw<CodegenException>().Which.Message.Should().Be(
            "The C# type 'AssetNonContractExtensions' is declared twice in namespace 'Test.Module' of package 'test-package': " +
            "as a type generated for Daml template 'Test.Module:Asset' and as the type of Daml record 'Test.Module:AssetNonContractExtensions'. " +
            "Rename 'Asset' or 'AssetNonContractExtensions' in Daml; a type generated for a template can also be avoided by renaming one of its choices.");
    }

    public static TheoryData<string> ConstructorNamesAlwaysRejected =>
        ["Tag", "ToVariant", "FromVariant", "Equals", "GetHashCode", "ToString", "PrintMembers", "EqualityContract", "Clone"];

    [Theory]
    [MemberData(nameof(ConstructorNamesAlwaysRejected))]
    public void Variant_constructor_named_like_a_member_of_the_variant_is_a_codegen_error(string constructorName)
    {
        var dar = DarOf([], Variant("Shape", new DamlVariantConstructor(constructorName, null), new DamlVariantConstructor("Other", null)));

        var failure = () => CreateGenerator().Generate(dar);

        failure.Should().Throw<CodegenException>().Which.Message.Should().Be(
            $"Constructor '{constructorName}' of Daml variant 'Test.Module:Shape' in package 'test-package' would be emitted as a nested C# type named '{constructorName}', " +
            "which the generated variant type already declares as a member. " +
            $"Rename the constructor '{constructorName}' in Daml.");
    }

    [Fact]
    public void Variant_constructor_named_value_is_a_codegen_error_only_when_some_constructor_carries_a_payload()
    {
        var withPayload = DarOf([], Variant("Shape", new DamlVariantConstructor("Value", null), new DamlVariantConstructor("Other", IntType)));
        var withoutPayload = DarOf([], Variant("Shape", new DamlVariantConstructor("Value", null), new DamlVariantConstructor("Other", null)));

        var failure = () => CreateGenerator().Generate(withPayload);

        failure.Should().Throw<CodegenException>().Which.Message.Should().Contain("Constructor 'Value' of Daml variant 'Test.Module:Shape'");
        CompileErrors(withoutPayload).Should().BeEmpty();
    }

    [Fact]
    public void Variant_constructor_named_deconstruct_is_a_codegen_error_only_when_it_carries_a_payload()
    {
        var withPayload = DarOf([], Variant("Shape", new DamlVariantConstructor("Deconstruct", IntType)));
        var withoutPayload = DarOf([], Variant("Shape", new DamlVariantConstructor("Deconstruct", null), new DamlVariantConstructor("Other", IntType)));

        var failure = () => CreateGenerator().Generate(withPayload);

        failure.Should().Throw<CodegenException>().Which.Message.Should().Contain("Constructor 'Deconstruct' of Daml variant 'Test.Module:Shape'");
        CompileErrors(withoutPayload).Should().BeEmpty();
    }

    [Fact]
    public void Variant_constructor_named_like_the_variant_compiles_unchanged()
    {
        var dar = DarOf([], Variant("Shape", new DamlVariantConstructor("Shape", IntType), new DamlVariantConstructor("Other", null)));

        CompileErrors(dar).Should().BeEmpty();
    }

    public static TheoryData<string, bool> ChoiceNamesRejectedBesideTheirTemplate =>
        new()
        {
            { "Equals", false }, { "GetHashCode", false }, { "ToString", false }, { "PrintMembers", false }, { "EqualityContract", false },
            { "Deconstruct", false }, { "Clone", false }, { "ToRecord", false }, { "FromRecord", false },
            { "TemplateId", false }, { "PackageId", false }, { "PackageName", false }, { "PackageVersion", false }, { "DamlTypeId", false },
            { "ChoiceSpawn", false }, { "Key", true }, { "SpawnByKeyCommand", true },
        };

    private static DarModel TemplateWithRecordArgumentChoice(string choiceName, bool keyed) =>
        DarOf(
            [Template("Asset", keyed ? new DamlPrimitiveType(DamlPrimitive.Party) : null, Choice("Spawn", IntType, Ref("Spawn")), Choice(choiceName, IntType, Ref(choiceName)))],
            Record("Asset", Field("owner", PartyType)),
            Record("Spawn", Field("n", IntType)),
            Record(choiceName, Field("n", IntType)));

    [Theory]
    [MemberData(nameof(ChoiceNamesRejectedBesideTheirTemplate))]
    public void Choice_with_a_record_argument_named_like_a_template_member_is_a_codegen_error(string choiceName, bool needsKeyedTemplate)
    {
        var failure = () => CreateGenerator().Generate(TemplateWithRecordArgumentChoice(choiceName, keyed: needsKeyedTemplate));

        failure.Should().Throw<CodegenException>().Which.Message.Should().Be(
            $"Choice '{choiceName}' of Daml template 'Test.Module:Asset' in package 'test-package' takes a record argument that would be emitted as a nested C# type named '{choiceName}', " +
            "which the generated template type already declares as a member. " +
            $"Rename the choice '{choiceName}' in Daml.");
    }

    [Fact]
    public void Choice_named_key_with_a_record_argument_compiles_on_an_unkeyed_template()
    {
        CompileErrors(TemplateWithRecordArgumentChoice("Key", keyed: false)).Should().BeEmpty();
    }

    [Fact]
    public void Choice_named_like_a_template_member_but_taking_a_non_record_argument_compiles()
    {
        var dar = DarOf(
            [Template("Asset", null, Choice("Equals", IntType))],
            Record("Asset", Field("owner", PartyType)));

        CompileErrors(dar).Should().BeEmpty();
    }

    [Fact]
    public void Choice_with_a_record_argument_named_like_its_template_is_a_codegen_error()
    {
        var dar = DarOf(
            [Template("Asset", null, Choice("Asset", IntType, Ref("Asset_Arg")))],
            Record("Asset", Field("owner", PartyType)),
            Record("Asset_Arg", Field("n", IntType)));

        var failure = () => CreateGenerator().Generate(dar);

        failure.Should().Throw<CodegenException>().Which.Message.Should().Be(
            "Choice 'Asset' of Daml template 'Test.Module:Asset' in package 'test-package' takes a record argument that would be emitted as a nested C# type named 'Asset', " +
            "which the generated template type already declares as a member. " +
            "Rename the choice 'Asset' in Daml.");
    }

    [Fact]
    public void Two_templates_of_a_module_with_a_same_named_choice_returning_contract_ids_compile()
    {
        var dar = DarOf(
            [
                Template("Asset", null, Choice("Spawn", ContractIdOf("Asset"))),
                Template("Voucher", null, Choice("Spawn", ContractIdOf("Voucher"))),
            ],
            Record("Asset", Field("owner", PartyType)),
            Record("Voucher", Field("owner", PartyType)));

        CompileErrors(dar).Should().BeEmpty();
    }

    [Fact]
    public void Variant_constructors_emitted_under_the_same_nested_name_are_a_codegen_error_naming_both()
    {
        var dar = DarOf([], Variant("Shape", new DamlVariantConstructor("Shape", null), new DamlVariantConstructor("Shape_", null)));

        var failure = () => CreateGenerator().Generate(dar);

        failure.Should().Throw<CodegenException>().Which.Message.Should().Be(
            "Constructors 'Shape' and 'Shape_' of Daml variant 'Test.Module:Shape' in package 'test-package' would both be emitted as the nested C# type 'Shape_'. " +
            "Rename one of them in Daml.");
    }

    [Fact]
    public void Sibling_named_like_a_template_companion_class_is_a_codegen_error_with_block_scoped_namespaces()
    {
        var dar = DarOf(
            [AssetWithChoice("Spawn", ContractIdOf("Asset"))],
            Record("Asset", Field("owner", PartyType)),
            Record("AssetExtensions", Field("x", IntType)));
        var generator = CreateGenerator(new CodeGenOptions { UseFileScopedNamespaces = false });

        var failure = () => generator.Generate(dar);

        failure.Should().Throw<CodegenException>().Which.Message.Should().Contain("The C# type 'AssetExtensions' is declared twice");
    }

    [Fact]
    public void Template_with_a_contract_id_choice_named_after_itself_compiles()
    {
        var dar = DarOf(
            [Template("SpawnResult", null, Choice("Spawn", ContractIdOf("SpawnResult")))],
            Record("SpawnResult", Field("owner", PartyType)));

        CompileErrors(dar).Should().BeEmpty();
    }

    [Fact]
    public void Generic_variant_constructor_named_like_an_emitted_type_parameter_is_a_codegen_error()
    {
        var variant = new DamlDataType
        {
            Name = "Shape",
            TypeParams = ["foo"],
            Definition = new DamlVariantDefinition([new DamlVariantConstructor("TFoo", new DamlTypeVar("foo"))]),
        };
        var dar = DarOf([], variant);

        var failure = () => CreateGenerator().Generate(dar);

        failure.Should().Throw<CodegenException>().Which.Message.Should().Contain("Constructor 'TFoo' of Daml variant 'Test.Module:Shape'");
    }

    [Fact]
    public void Module_whose_namespace_is_spelled_like_a_template_companion_class_of_another_module_is_a_codegen_error()
    {
        var dar = DarOfModules(
            ModuleOf("Test.Module", [AssetWithChoice("Spawn", ContractIdOf("Asset"))], Record("Asset", Field("owner", PartyType))),
            ModuleOf("Test.Module.AssetExtensions", [], Record("Other", Field("x", IntType))));

        var failure = () => CreateGenerator().Generate(dar);

        failure.Should().Throw<CodegenException>().Which.Message.Should().Contain("CS0101");
    }

    [Fact]
    public void Module_whose_namespace_is_spelled_like_a_template_the_root_filter_excludes_is_still_a_codegen_error()
    {
        var dar = DarOfModules(
            ModuleOf("Test.Module.Asset", [], Record("Other", Field("x", IntType))),
            ModuleOf("Test.Module", [AssetWithChoice("Spawn", ContractIdOf("Asset"))], Record("Asset", Field("owner", PartyType))));
        var generator = CreateGenerator(new CodeGenOptions { RootFilter = "^Test\\.Module:Nothing$" });

        var failure = () => generator.Generate(dar);

        failure.Should().Throw<CodegenException>().Which.Message.Should().Contain("CS0101");
    }
}
