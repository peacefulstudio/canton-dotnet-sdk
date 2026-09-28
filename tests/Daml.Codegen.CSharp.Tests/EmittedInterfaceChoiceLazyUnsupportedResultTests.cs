// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedInterfaceChoiceLazyUnsupportedResultTests
{
    private static readonly Assembly Emitted = EmitToAssembly(Generate());

    private static IReadOnlyList<GeneratedFile> Generate()
    {
        var iface = new DamlInterface
        {
            Name = "Holding",
            Choices =
            [
                new DamlChoice
                {
                    Name = "Split",
                    Consuming = true,
                    ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                    ReturnType = new DamlTypeApp(new DamlTypeVar("f"), [new DamlTypeVar("a")]),
                },
            ],
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes = [],
            Interfaces = [iface],
        };

        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var options = new CodeGenOptions { EnableNullableReferenceTypes = true, UseFileScopedNamespaces = true };
        return CreateGenerator(options).Generate(dar);
    }

    private static IChoice SplitChoice =>
        (IChoice)Emitted.GetTypes().Single(t => t.Name == "IHolding")
            .GetProperty("ChoiceSplit", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

    [Fact]
    public void SplitChoiceDescriptor_constructs_and_its_ArgumentJsonReader_still_works()
    {
        using var document = JsonDocument.Parse("{}");

        var read = SplitChoice.ReadArgumentJson(document.RootElement, DamlLfJsonDecodeContext.Root("Split"));

        read.Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void SplitChoiceResultJsonReader_throws_NotSupportedException_naming_the_type_and_path_only_when_invoked()
    {
        var resultJsonReaderProperty = SplitChoice.GetType().GetProperty("ResultJsonReader")!;
        var resultJsonReader = resultJsonReaderProperty.GetValue(SplitChoice)!;
        var invoke = resultJsonReader.GetType().GetMethod("Invoke")!;

        using var document = JsonDocument.Parse("""{"amount":"1.0"}""");
        var context = DamlLfJsonDecodeContext.Root("GenericResults.split");

        var act = () =>
        {
            try
            {
                invoke.Invoke(resultJsonReader, [document.RootElement, context]);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }
        };

        act.Should().Throw<NotSupportedException>()
            .WithMessage(
                "Daml type 'DamlTypeApp*Base = DamlTypeVar { Name = f }*' " +
                "at 'GenericResults.split' lies outside the emitted Daml-LF JSON decoders");
    }
}
