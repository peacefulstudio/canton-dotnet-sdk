// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Reflection;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Compiles and runs consumer code that is generic over the <c>IArchivable&lt;TSelf&gt;</c>
/// facet against one emitted template and one emitted interface, so the implicit
/// <c>Archive</c> choice is reachable through a type parameter on both kinds of owner.
/// </summary>
public class EmittedArchivableFacetCompilesTests
{
    private const string ConsumerSource = """
        using Daml.Runtime;
        using Daml.Runtime.Commands;
        using Daml.Runtime.Contracts;
        using Daml.Runtime.Data;

        namespace Consumer;

        public static class Archiver
        {
            public static ExerciseCommand CommandFor<T>(ContractId<T> contractId)
                where T : IDamlType, IArchivable<T> =>
                ExerciseCommand.For(contractId, T.ChoiceArchive, DamlUnit.Instance);
        }
        """;

    private static readonly DamlPackage StdlibStub = new()
    {
        PackageId = "daml-prim-pkg-id",
        Name = "daml-prim",
        Version = new Version(1, 0, 0),
        LfVersion = "2.1",
        Modules = [],
        DependencyReferences = [],
    };

    private static readonly IReadOnlyList<GeneratedFile> Files = [.. GenerateOfferAndCustody(), GeneratedFile.Text("Consumer/Archiver.cs", ConsumerSource)];

    [Fact]
    public void Generic_consumer_code_constrained_on_the_facet_compiles_against_a_template_and_an_interface()
    {
        var errors = CompileEmittedFiles(Files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        errors.Should().BeEmpty(string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_interface_declaring_the_facet_does_not_hide_the_inherited_descriptor()
    {
        var hiding = CompileEmittedFiles(Files).Where(d => d.Id == "CS0108").ToList();

        hiding.Should().BeEmpty(string.Join("\n", hiding.Select(d => d.GetMessage(CultureInfo.InvariantCulture) + " @ " + d.Location)));
    }

    [Theory]
    [InlineData("Offer")]
    [InlineData("ICustody")]
    public void Generic_archive_builds_the_archive_command_for_the_owner(string ownerTypeName)
    {
        var assembly = EmitToAssembly(Files);
        var owner = assembly.GetTypes().Single(t => t.Name == ownerTypeName);
        var contractId = Activator.CreateInstance(typeof(Daml.Runtime.Contracts.ContractId<>).MakeGenericType(owner), "cid-1");
        var commandFor = assembly.GetType("Consumer.Archiver")!
            .GetMethod("CommandFor", BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod(owner);

        var command = (ExerciseCommand)commandFor.Invoke(null, [contractId])!;

        command.Choice.Value.Should().Be("Archive");
        command.ContractId.Value.Should().Be("cid-1");
        command.TemplateId.EntityName.Should().Be(ownerTypeName.TrimStart('I'));
        command.ChoiceArgument.Should().BeOfType<DamlRecord>().Which.Fields.Should().BeEmpty();
    }

    private static DamlChoice ArchiveChoice() =>
        new()
        {
            Name = "Archive",
            Consuming = true,
            ArgumentType = new DamlTypeRef(StdlibStub.PackageId, "DA.Internal.Template", "Archive"),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
            Observers = DamlPartyAnalysis.Static([]),
        };

    private static IReadOnlyList<GeneratedFile> GenerateOfferAndCustody()
    {
        var module = new DamlModule
        {
            Name = "Acme.Offers",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Offer",
                    Choices = [ArchiveChoice()],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Observers = DamlPartyAnalysis.Static([]),
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Offer",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Custody",
                    Choices = [ArchiveChoice()],
                },
            ],
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
}
