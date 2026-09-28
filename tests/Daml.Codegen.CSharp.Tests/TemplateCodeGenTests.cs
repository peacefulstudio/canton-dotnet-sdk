// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// End-to-end pipeline tests for template emission through the public
/// <see cref="CSharpCodeGenerator.Generate"/> surface: the project-file
/// companions, file placement, and the delegation seam onto
/// <see cref="TemplateEmitter"/>. The template body is asserted directly in
/// <see cref="TemplateEmitterTests"/>.
/// </summary>
public class TemplateCodeGenTests
{
    private static DamlModule SingleTemplateModule(string moduleName, string templateName) =>
        new()
        {
            Name = moduleName,
            Templates =
            [
                new DamlTemplate
                {
                    Name = templateName,
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = templateName,
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
                }
            ],
            Interfaces = []
        };

    [Fact]
    public void Generate_should_emit_a_README_when_project_file_generation_is_enabled()
    {
        var generator = CreateGenerator(new CodeGenOptions { GenerateProjectFile = true });

        var files = generator.Generate(CreateTestDar(SingleTemplateModule("Test.Module", "SimpleTemplate")));

        files.Should().ContainSingle(f => f.RelativePath == "README.md",
            "the csproj declares <PackageReadmeFile>README.md</PackageReadmeFile>, so dropping the README would fail dotnet pack with NU5039");
    }

    [Fact]
    public void Generate_should_emit_an_icon_when_project_file_generation_is_enabled()
    {
        var generator = CreateGenerator(new CodeGenOptions { GenerateProjectFile = true });

        var files = generator.Generate(CreateTestDar(SingleTemplateModule("Test.Module", "SimpleTemplate")));

        files.Should().ContainSingle(f => f.RelativePath == "icon.png",
            "the csproj declares <PackageIcon>icon.png</PackageIcon>, so dropping the icon would fail dotnet pack with NU5046")
            .Which.BinaryContent.Should().NotBeNullOrEmpty("the packed icon must carry its PNG bytes");
    }

    [Fact]
    public void Generate_should_not_emit_an_icon_when_project_file_generation_is_disabled()
    {
        var generator = CreateGenerator(new CodeGenOptions { GenerateProjectFile = false });

        var files = generator.Generate(CreateTestDar(SingleTemplateModule("Test.Module", "SimpleTemplate")));

        files.Should().NotContain(f => f.RelativePath == "icon.png");
    }

    [Fact]
    public void Generate_should_place_the_template_file_under_the_module_namespace_directory()
    {
        var generator = CreateGenerator();

        var files = generator.Generate(CreateTestDar(SingleTemplateModule("Deeply.Nested.Module", "MyTemplate")));
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("MyTemplate.cs", StringComparison.Ordinal));

        templateFile.Should().NotBeNull();
        templateFile!.RelativePath.Should().Be("Deeply/Nested/Module/MyTemplate.cs");
    }

    private static DamlModule ModuleWithTemplateButNoDataTypes(string moduleName, string templateName) =>
        new()
        {
            Name = moduleName,
            Templates =
            [
                new DamlTemplate
                {
                    Name = templateName,
                    Choices = []
                }
            ],
            DataTypes = [],
            Interfaces = []
        };

    [Fact]
    public void Generate_should_fail_loudly_when_a_template_has_no_same_named_record_definition()
    {
        var generator = CreateGenerator();

        var act = () => generator.Generate(CreateTestDar(ModuleWithTemplateButNoDataTypes("Test.Module", "Orphan"))).ToList();

        act.Should().Throw<CodegenException>()
            .WithMessage(
                "*Test.Module:Orphan*",
                "an LF template payload is always a same-named record, so a template without one is a malformed "
                + "model that must not be papered over with an empty payload — and the diagnostic has to name the "
                + "module too, since template names are only unique within one");
    }

    [Fact]
    public void Generate_should_not_validate_a_recordless_template_the_root_filter_excludes()
    {
        var generator = CreateGenerator(new CodeGenOptions { RootFilter = "Test\\.Module:Keep.*" });

        var act = () => generator.Generate(
            CreateTestDar(ModuleWithTemplateButNoDataTypes("Test.Module", "Orphan"))).ToList();

        act.Should().NotThrow(
            "the root filter skips a template before the payload lookup runs, so excluding a template also "
            + "excludes it from validation — the boundary between what this generator fails loudly on and what "
            + "it never looks at");
    }

    private static DamlModule ModuleWithTemplateAndSameNamedVariant(string moduleName, string templateName) =>
        new()
        {
            Name = moduleName,
            Templates =
            [
                new DamlTemplate
                {
                    Name = templateName,
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = templateName,
                    Definition = new DamlVariantDefinition([new DamlVariantConstructor("Only", null)])
                }
            ],
            Interfaces = []
        };

    [Fact]
    public void Generate_should_name_the_kind_when_the_same_named_definition_is_not_a_record()
    {
        var generator = CreateGenerator();

        var act = () => generator.Generate(
            CreateTestDar(ModuleWithTemplateAndSameNamedVariant("Test.Module", "Orphan"))).ToList();

        act.Should().Throw<CodegenException>()
            .WithMessage(
                "*DamlVariantDefinition*",
                "a same-named definition of the wrong kind is a different malformation from an absent one, and "
                + "reporting only that no record was found leaves the reader staring at a definition that is right there");
    }

    [Fact]
    public void Generate_should_delegate_template_emission_to_the_template_emitter()
    {
        var generator = CreateGenerator();

        var files = generator.Generate(CreateTestDar(SingleTemplateModule("Test.Module", "SimpleTemplate")));
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("SimpleTemplate.cs", StringComparison.Ordinal));

        templateFile.Should().NotBeNull();
        var code = templateFile!.Content;

        code.Should().Contain("public sealed partial record SimpleTemplate");
        code.Should().Contain(": ITemplate");
    }
}
