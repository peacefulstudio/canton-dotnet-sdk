// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedNamespacePrefixWithDependenciesCompilesTests
{
    private const string MainPackageId = "main-package-id";
    private const string DependencyPackageId = "dependency-package-id";

    private static DamlPackage DependencyPackage() =>
        new()
        {
            PackageId = DependencyPackageId,
            Name = "acme-dependency",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = "Dep.Types",
                    Templates =
                    [
                        new DamlTemplate { Name = "Holding", Choices = [] },
                    ],
                    DataTypes =
                    [
                        new DamlDataType { Name = "Currency", Definition = new DamlEnumDefinition(["USD", "EUR"]) },
                        new DamlDataType
                        {
                            Name = "Money",
                            Definition = new DamlRecordDefinition(
                            [
                                new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Numeric)),
                                new DamlFieldDefinition("currency", new DamlTypeRef(DependencyPackageId, "Dep.Types", "Currency")),
                            ]),
                        },
                        new DamlDataType
                        {
                            Name = "Holding",
                            Definition = new DamlRecordDefinition(
                                [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                        },
                    ],
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

    private static DamlPackage MainPackage() =>
        new()
        {
            PackageId = MainPackageId,
            Name = "acme-main",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = "Acme.Orders",
                    Templates =
                    [
                        new DamlTemplate { Name = "Order", Choices = [] },
                    ],
                    DataTypes =
                    [
                        new DamlDataType
                        {
                            Name = "Order",
                            Definition = new DamlRecordDefinition(
                            [
                                new DamlFieldDefinition("buyer", new DamlPrimitiveType(DamlPrimitive.Party)),
                                new DamlFieldDefinition("price", new DamlTypeRef(DependencyPackageId, "Dep.Types", "Money")),
                                new DamlFieldDefinition("currency", new DamlTypeRef(DependencyPackageId, "Dep.Types", "Currency")),
                                new DamlFieldDefinition(
                                    "holding",
                                    new DamlTypeApp(
                                        new DamlPrimitiveType(DamlPrimitive.ContractId),
                                        [new DamlTypeRef(DependencyPackageId, "Dep.Types", "Holding")])),
                            ]),
                        },
                    ],
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

    [Fact]
    public void Emitted_main_package_under_a_namespace_prefix_compiles_against_its_unprefixed_dependency()
    {
        var options = new CodeGenOptions { NamespacePrefix = "Consumer.Bindings", IncludeDependencies = true };
        var files = CreateGenerator(options).Generate(
            new DarModel { MainPackage = MainPackage(), Dependencies = [DependencyPackage()] });

        var order = files.Single(f => f.RelativePath.EndsWith("/Order.cs", StringComparison.Ordinal));
        order.RelativePath.Should().Be("Consumer/Bindings/Acme/Orders/Order.cs");
        order.Content.Should().Contain("namespace Consumer.Bindings.Acme.Orders;");
        order.Content.Should().Contain("global::Dep.Types.Money Price");
        order.Content.Should().Contain("global::Dep.Types.Currency Currency");
        order.Content.Should().Contain("ContractId<global::Dep.Types.Holding> Holding");

        var money = files.Single(f => f.RelativePath.EndsWith("/Money.cs", StringComparison.Ordinal));
        money.RelativePath.Should().Be("Dep/Types/Money.cs");
        money.Content.Should().Contain("namespace Dep.Types;");

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "the main package's references into the dependency must name the namespace the dependency is emitted under, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }
}
