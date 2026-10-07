// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Pins that the registration files of several packages compile into one assembly. The MSBuild
/// path compiles every generated directory into one consumer assembly, so two packages — two
/// versions of one package included — must never declare the same registration class in one
/// namespace (CS0101), and a registration class must not clash with a type of the package it
/// registers.
/// </summary>
public class EmittedPackageRegistrationCompilesTests
{
    private static DamlChoice Choice(string name) =>
        new()
        {
            Name = name,
            Consuming = false,
            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
        };

    private static DamlDataType Record(string name) =>
        new()
        {
            Name = name,
            Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
        };

    private static DamlPackage PackageOf(string packageId, string packageName, params string[] templateNames) =>
        new()
        {
            PackageId = packageId,
            Name = packageName,
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = "Shared.Module",
                    Templates = templateNames
                        .Select(name => new DamlTemplate { Name = name, Choices = [Choice("Ping")], Key = new DamlPrimitiveType(DamlPrimitive.Text) })
                        .ToList(),
                    DataTypes = templateNames.Select(Record).ToList(),
                    Interfaces = [new DamlInterface { Name = $"I{templateNames[0]}Facade", Choices = [Choice("Look")] }],
                },
            ],
            DependencyReferences = [],
        };

    private static IReadOnlyList<GeneratedFile> GenerateAlone(DamlPackage package) =>
        CreateGenerator().Generate(new DarModel { MainPackage = package, Dependencies = [] });

    private static IReadOnlyList<string> Errors(IReadOnlyList<GeneratedFile> files) =>
        CompileEmittedFiles(files)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToList();

    [Fact]
    public void Two_versions_of_one_package_registered_into_one_namespace_declare_distinct_registration_classes()
    {
        var versionOne = GenerateAlone(PackageOf("1111", "same-name", "Asset"));
        var versionTwo = GenerateAlone(PackageOf("2222", "same-name", "Loan"));

        Errors([.. versionOne, .. versionTwo]).Should().BeEmpty();
    }

    [Fact]
    public void A_main_package_and_its_dependency_compile_with_their_registration_classes()
    {
        var dar = new DarModel
        {
            MainPackage = PackageOf("1111", "main-name", "Asset"),
            Dependencies = [PackageOf("2222", "dep-name", "Loan")],
        };

        var files = CreateGenerator(new CodeGenOptions { IncludeDependencies = true, NamespacePrefix = "Acme" }).Generate(dar);

        Errors(files).Should().BeEmpty();
    }

    [Fact]
    public void A_type_named_like_the_registration_class_makes_it_take_a_trailing_underscore_and_still_compile()
    {
        var package = PackageOf("1111", "main-name", "Asset");
        var withClashingRecord = new DamlPackage
        {
            PackageId = package.PackageId,
            Name = package.Name,
            Version = package.Version,
            LfVersion = package.LfVersion,
            DependencyReferences = [],
            Modules =
            [
                new DamlModule
                {
                    Name = "Shared.Module",
                    Templates = package.Modules[0].Templates,
                    DataTypes = [.. package.Modules[0].DataTypes, Record("PackageRegistration_1111")],
                    Interfaces = package.Modules[0].Interfaces,
                },
            ],
        };

        var files = GenerateAlone(withClashingRecord);

        files.Should().Contain(file => file.RelativePath == "Shared/Module/PackageRegistration_1111_.cs");
        Errors(files).Should().BeEmpty();
    }
}
