// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedTypeNamedContractIdentifiersCompilesTests
{
    private static DamlModule ModuleDeclaringATemplateAndARecordNamedContractIdentifiers() =>
        new()
        {
            Name = "Acme.Registry",
            Templates = [new DamlTemplate { Name = "Deed", Choices = [] }],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Deed",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "ContractIdentifiers",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("registrar", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

    [Fact]
    public void A_Daml_record_named_ContractIdentifiers_beside_a_template_compiles()
    {
        var files = CreateGenerator().Generate(CreateTestDar([ModuleDeclaringATemplateAndARecordNamedContractIdentifiers()]));

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        errors.Should().BeEmpty(
            "the codegen reserves no top-level helper name, so a Daml type called ContractIdentifiers is emitted as itself");
        files.Single(f => f.RelativePath == "Acme/Registry/ContractIdentifiers.cs").Content
            .Should().Contain("public sealed record ContractIdentifiers(");
    }
}
