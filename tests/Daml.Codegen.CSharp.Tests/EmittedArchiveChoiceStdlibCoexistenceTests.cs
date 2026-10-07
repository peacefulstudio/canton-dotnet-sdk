// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedArchiveChoiceStdlibCoexistenceTests
{
    private static readonly DamlPackage StdlibStub = new()
    {
        PackageId = "daml-prim-pkg-id",
        Name = "daml-prim",
        Version = new Version(1, 0, 0),
        LfVersion = "2.1",
        Modules = [],
        DependencyReferences = [],
    };

    private static IReadOnlyList<GeneratedFile> GenerateTemplateWithArchiveChoiceBesideUserArchiveRecord()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Archive",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef(StdlibStub.PackageId, "DA.Internal.Template", "Archive"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "Archive",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("reason", new DamlPrimitiveType(DamlPrimitive.Text))]),
                },
            ],
            Interfaces = [],
        };

        var dar = new DarModel
        {
            MainPackage = new DamlPackage
            {
                PackageId = "test-pkg",
                Name = "test-package",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules = [module],
                DependencyReferences = [],
            },
            Dependencies = [StdlibStub],
        };

        return CreateGenerator(new CodeGenOptions { EnableNullableReferenceTypes = true, UseFileScopedNamespaces = true }).Generate(dar);
    }

    [Fact]
    public void Template_archive_choice_compiles_beside_a_user_record_named_archive_and_the_runtime_stdlib_archive()
    {
        var files = GenerateTemplateWithArchiveChoiceBesideUserArchiveRecord();

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        errors.Should().BeEmpty(string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Template_archive_choice_stays_the_argument_less_unit_shape_and_never_binds_the_runtime_stdlib_archive()
    {
        var files = GenerateTemplateWithArchiveChoiceBesideUserArchiveRecord();
        var assembly = EmitToAssembly(files);
        var choice = (IChoice)assembly.GetTypes().Single(t => t.Name == "Asset")
            .GetProperty("ChoiceArchive", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;
        using var document = JsonDocument.Parse("{}");

        var decoded = choice.DecodeArgumentJson(document.RootElement, DamlLfJsonDecodeContext.Root("Archive"));

        decoded.Should().Be(DamlUnit.Instance);
        files.Single(f => f.RelativePath == "Test/Module/Asset.cs").Content
            .Should().NotContain("Stdlib.Archive");
    }
}
