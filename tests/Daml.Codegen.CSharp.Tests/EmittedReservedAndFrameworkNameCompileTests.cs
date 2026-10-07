// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedReservedAndFrameworkNameCompileTests
{
    private const string Module = "App.Main";

    private static DamlType Int64 => new DamlPrimitiveType(DamlPrimitive.Int64);

    private static DamlType Party => new DamlPrimitiveType(DamlPrimitive.Party);

    private static DamlType Unit => new DamlPrimitiveType(DamlPrimitive.Unit);

    private static DamlTypeRef Ref(string name, string module = Module) => new("", module, name);

    private static DamlDataType Record(string name, params DamlFieldDefinition[] fields) =>
        new() { Name = name, Definition = new DamlRecordDefinition(fields) };

    private static DamlDataType NamedType(string name, IReadOnlyList<string> typeParameters) =>
        new()
        {
            Name = name,
            TypeParams = typeParameters,
            Definition = new DamlRecordDefinition(
                [Field("n", Int64), .. typeParameters.Select(parameter => Field(parameter, new DamlTypeVar(parameter)))]),
        };

    private static DamlFieldDefinition Field(string name, DamlType type) => new(name, type);

    private static DamlChoice Choice(string name, DamlType argument, DamlType result, bool consuming = true) =>
        new() { Name = name, Consuming = consuming, ArgumentType = argument, ReturnType = result };

    private static DamlModule Mod(
        string name,
        DamlTemplate[]? templates = null,
        DamlDataType[]? dataTypes = null,
        DamlInterface[]? interfaces = null) =>
        new()
        {
            Name = name,
            Templates = templates ?? [],
            DataTypes = dataTypes ?? [],
            Interfaces = interfaces ?? [],
        };

    private static DarModel DarOf(params DamlModule[] modules) =>
        new()
        {
            MainPackage = new DamlPackage
            {
                PackageId = "test-package-id",
                Name = "test-package",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules = modules,
                DependencyReferences = [],
            },
            Dependencies = [],
        };

    private static List<string> CompileErrors(DarModel dar) =>
        CompileEmittedFiles(CreateGenerator().Generate(dar))
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Id + " " + d.GetMessage(CultureInfo.InvariantCulture) + " @ " + d.Location.GetLineSpan())
            .Distinct()
            .Take(6)
            .ToList();

    private static DarModel EveryEmitterSurfaceBesideATypeNamed(string name, int arity = 0)
    {
        var namedReference = arity == 0 ? Ref(name) : Int64;
        var namedTypeParameters = Enumerable.Range(0, arity).Select(index => $"p{index}").ToList();
        var interfaceView = Record("AssetView", Field("owner", Party), Field("amount", Int64));
        var asset = new DamlInterface
        {
            Name = "Asset",
            Choices =
            [
                Choice("Poke", Unit, Int64, consuming: false),
                Choice("Burn", Ref("Wrapper"), Ref("Wrapper")),
            ],
            Methods = [new DamlInterfaceMethod("getOwner", Party)],
            ViewType = Ref("AssetView"),
        };
        var note = new DamlTemplate
        {
            Name = "Note",
            Key = Party,
            Implements = [Ref("Asset")],
            Choices =
            [
                Choice("Close", Unit, Unit),
                Choice("Peek", Unit, Int64, consuming: false),
                Choice("Rename", Ref("Wrapper"), Ref("Wrapper")),
                Choice("Fetch", Unit, new DamlListType(Ref("Wrapper")), consuming: false),
                Choice("Maybe", Unit, new DamlOptionalType(Ref("Wrapper")), consuming: false),
                Choice("Spawn", Unit, new DamlContractIdType(Ref("Note"))),
            ],
        };
        var wrapper = Record(
            "Wrapper",
            Field("owner", Party),
            Field("optional", new DamlOptionalType(Int64)),
            Field("items", new DamlListType(namedReference)),
            Field("textMap", new DamlTextMapType(Int64)),
            Field("genMap", new DamlGenMapType(Int64, Int64)),
            Field("timestamp", new DamlPrimitiveType(DamlPrimitive.Timestamp)),
            Field("date", new DamlPrimitiveType(DamlPrimitive.Date)),
            Field("numeric", new DamlPrimitiveType(DamlPrimitive.Numeric)),
            Field("contract", new DamlContractIdType(Ref("Note"))));
        var box = new DamlDataType
        {
            Name = "Box",
            TypeParams = ["a"],
            Definition = new DamlRecordDefinition([Field("item", new DamlTypeVar("a"))]),
        };
        var pick = new DamlDataType
        {
            Name = "Pick",
            TypeParams = ["a"],
            Definition = new DamlVariantDefinition(
            [
                new DamlVariantConstructor("Left", new DamlTypeVar("a")),
                new DamlVariantConstructor("Right", namedReference),
                new DamlVariantConstructor("Neither", null),
            ]),
        };
        var color = new DamlDataType { Name = "Color", Definition = new DamlEnumDefinition(["Red", "Green"]) };
        var notePayload = Record("Note", Field("owner", Party));
        return DarOf(Mod(
            Module,
            [note],
            [NamedType(name, namedTypeParameters), Record("Asset"), notePayload, wrapper, box, pick, color, interfaceView],
            [asset]));
    }

    public static TheoryData<string> FrameworkTypeNames => new()
    {
        "Zebra",
        "CancellationToken",
        "Version",
        "TimeSpan",
        "StringComparison",
        "ArgumentNullException",
        "DateTimeOffset",
        "IEnumerable",
        "Task",
        "ValueTask",
        "Func",
        "Action",
        "Exception",
        "InvalidOperationException",
        "OperationCanceledException",
        "IReadOnlyList",
        "IReadOnlyDictionary",
        "HashSet",
        "EqualityComparer",
        "HashCode",
        "Nullable",
        "Guid",
        "Type",
        "Enum",
        "String",
        "Object",
        "Convert",
        "Math",
        "Array",
        "Attribute",
        "FormatException",
        "ArgumentException",
        "KeyNotFoundException",
        "NotSupportedException",
        "Lazy",
        "Uri",
        "DateOnly",
        "StringBuilder",
        "StringComparer",
        "IEquatable",
        "IDisposable",
        "List",
        "Dictionary",
    };

    [Theory]
    [MemberData(nameof(FrameworkTypeNames))]
    public void Every_emitter_surface_compiles_beside_a_type_named_like_a_framework_type(string frameworkTypeName)
    {
        CompileErrors(EveryEmitterSurfaceBesideATypeNamed(frameworkTypeName)).Should().BeEmpty(frameworkTypeName);
    }

    public static TheoryData<string, int> GenericFrameworkTypeNames => new()
    {
        { "Task", 1 },
        { "ValueTask", 1 },
        { "Func", 1 },
        { "Func", 2 },
        { "Func", 3 },
        { "IEnumerable", 1 },
        { "IReadOnlyList", 1 },
        { "IReadOnlyDictionary", 2 },
        { "HashSet", 1 },
        { "EqualityComparer", 1 },
        { "Nullable", 1 },
        { "Lazy", 1 },
        { "IEquatable", 1 },
        { "List", 1 },
        { "Dictionary", 2 },
        { "Action", 1 },
        { "Action", 2 },
    };

    [Theory]
    [MemberData(nameof(GenericFrameworkTypeNames))]
    public void Every_emitter_surface_compiles_beside_a_generic_type_named_like_a_generic_framework_type(
        string frameworkTypeName,
        int arity)
    {
        CompileErrors(EveryEmitterSurfaceBesideATypeNamed(frameworkTypeName, arity))
            .Should().BeEmpty($"{frameworkTypeName}`{arity}");
    }

    private static readonly IReadOnlySet<string> FrameworkSimpleTypeNames = FrameworkTypes()
        .Select(type => type.Name.Split('`')[0])
        .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<Type> FrameworkTypes()
    {
        var frameworkNamespaces = new HashSet<string>(StringComparer.Ordinal)
        {
            "System",
            "System.Collections.Generic",
            "System.Threading",
            "System.Threading.Tasks",
            "System.Linq",
            "System.Text",
            "System.Text.Json",
            "System.ComponentModel",
            "System.Globalization",
        };
        var assemblies = new[]
        {
            typeof(object).Assembly,
            typeof(HashSet<>).Assembly,
            typeof(Enumerable).Assembly,
            typeof(System.Text.Json.JsonElement).Assembly,
        };
        return assemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.Namespace is not null && frameworkNamespaces.Contains(type.Namespace) && !type.IsNested);
    }

    private static bool IsRootedAtGlobalOrMemberOfAnotherType(IdentifierNameSyntax name) =>
        name.Parent switch
        {
            QualifiedNameSyntax qualified => qualified.Right == name,
            MemberAccessExpressionSyntax access => access.Name == name,
            AliasQualifiedNameSyntax => true,
            _ => false,
        };

    private static IEnumerable<string> UnqualifiedFrameworkReferences(GeneratedFile file) =>
        CSharpSyntaxTree.ParseText(file.Content).GetRoot().DescendantNodes()
            .Where(node => node is IdentifierNameSyntax or GenericNameSyntax)
            .Select(node => node switch
            {
                IdentifierNameSyntax name when !IsRootedAtGlobalOrMemberOfAnotherType(name) => name.Identifier.Text,
                GenericNameSyntax generic when generic.Parent is not (QualifiedNameSyntax or MemberAccessExpressionSyntax) => generic.Identifier.Text,
                _ => string.Empty,
            })
            .Where(FrameworkSimpleTypeNames.Contains)
            .Select(name => $"{file.RelativePath}: {name}");

    [Fact]
    public void Emitted_code_never_references_a_framework_type_by_its_simple_name()
    {
        var files = CreateGenerator().Generate(EveryEmitterSurfaceBesideATypeNamed("Zebra"));

        string.Join("\n", files.SelectMany(UnqualifiedFrameworkReferences).Distinct()).Should().BeEmpty();
    }

    [Fact]
    public void View_record_in_another_module_than_its_interface_compiles()
    {
        var dar = DarOf(
            Mod("App.Views", dataTypes: [Record("AssetView", Field("owner", Party))]),
            Mod(
                "App.Ifaces",
                dataTypes: [Record("Asset")],
                interfaces: [new DamlInterface { Name = "Asset", Choices = [], ViewType = Ref("AssetView", "App.Views") }]));

        CompileErrors(dar).Should().BeEmpty();
    }
}
