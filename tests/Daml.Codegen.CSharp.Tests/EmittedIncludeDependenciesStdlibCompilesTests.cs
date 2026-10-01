// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedIncludeDependenciesStdlibCompilesTests
{
    private static DamlPackage Package(string id, string name, params DamlModule[] modules) =>
        new()
        {
            PackageId = id,
            Name = name,
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = modules,
            DependencyReferences = [],
        };

    private static DamlModule EmptyModule(string name) =>
        new() { Name = name, Templates = [], DataTypes = [], Interfaces = [] };

    private static DamlModule ModuleWithRecord(string moduleName, string recordName) =>
        new()
        {
            Name = moduleName,
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = recordName,
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

    private static DarModel DarWithBothStdlibPackagesAndOneLibrary() =>
        new()
        {
            MainPackage = Package("main-id", "acme-main", ModuleWithRecord("Acme.Orders", "Order")),
            Dependencies =
            [
                Package("prim-id", "daml-prim", EmptyModule("LibraryModules")),
                Package("stdlib-id", "daml-stdlib", EmptyModule("LibraryModules"), ModuleWithRecord("DA.Types", "Tuple2")),
                Package("lib-id", "acme-lib", ModuleWithRecord("Acme.Lib", "Widget")),
            ],
        };

    [Fact]
    public void Generate_with_dependencies_included_skips_daml_prim_and_daml_stdlib_which_daml_runtime_provides()
    {
        var options = new CodeGenOptions { IncludeDependencies = true };

        var files = CreateGenerator(options).Generate(DarWithBothStdlibPackagesAndOneLibrary());

        files.Select(f => f.RelativePath).Should().Contain("Acme/Orders/Order.cs");
        files.Select(f => f.RelativePath).Should().Contain("Acme/Lib/Widget.cs");
        files.Should().NotContain(f => f.Content.Contains("namespace LibraryModules", StringComparison.Ordinal));
        files.Should().NotContain(f => f.Content.Contains("namespace DA.Types", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_with_dependencies_included_compiles_when_both_stdlib_packages_declare_LibraryModules()
    {
        var options = new CodeGenOptions { IncludeDependencies = true };

        var files = CreateGenerator(options).Generate(DarWithBothStdlibPackagesAndOneLibrary());

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "the daml-prim and daml-stdlib LibraryModules collision must not abort or corrupt the emit, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Generate_with_dependencies_included_still_rejects_two_library_packages_sharing_a_module_name()
    {
        var options = new CodeGenOptions { IncludeDependencies = true };
        var dar = new DarModel
        {
            MainPackage = Package("main-id", "acme-main", ModuleWithRecord("Acme.Orders", "Order")),
            Dependencies =
            [
                Package("a-id", "acme-a", ModuleWithRecord("Shared.Module", "A")),
                Package("b-id", "acme-b", ModuleWithRecord("Shared.Module", "B")),
            ],
        };

        var emit = () => CreateGenerator(options).Generate(dar);

        emit.Should().Throw<CodegenException>()
            .WithMessage("*acme-a:Shared.Module and acme-b:Shared.Module map to the same C# namespace 'Shared.Module'*");
    }

    [Fact]
    public async Task Generate_with_dependencies_included_succeeds_on_the_real_quickstart_dar()
    {
        var protoPath = Path.Combine(AppContext.BaseDirectory, "QuickstartSample", "intermediate.binpb");
        IntermediateDar proto;
        await using (var stream = File.OpenRead(protoPath))
        {
            proto = IntermediateDar.Parser.ParseFrom(stream);
        }
        var dar = IntermediateDarReader.Read(proto);
        dar.Dependencies.Count(dep => dep.Name is "daml-prim" or "daml-stdlib").Should().Be(2);

        var files = CreateGenerator(new CodeGenOptions { IncludeDependencies = true }).Generate(dar);

        files.Should().NotBeEmpty();
        files.Should().NotContain(f => f.Content.Contains("namespace LibraryModules", StringComparison.Ordinal));
    }
}
