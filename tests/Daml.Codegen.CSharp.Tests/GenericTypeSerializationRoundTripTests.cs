// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Reflection;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Runtime.Data;
using Daml.Testing.Roslyn;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Runtime proof that generic records and variants round-trip through the
/// emitted converter-delegate serialization surface, driven straight from the
/// emitter's model rather than from a compiled DAR: this compiles a synthetic
/// package containing a generic record, a generic variant, and a non-generic
/// record that embeds both instantiated over <see cref="Party"/>,
/// Roslyn-compiles it in memory, then reflection-invokes the emitted
/// <c>ToRecord</c>/<c>FromRecord</c>/<c>ToVariant</c>/<c>FromVariant</c> with
/// real converters and asserts a serialize-then-deserialize round trip
/// preserves the object graph — including the embedding record whose fields
/// previously deserialized to a <c>TODO</c> stub. The conformance corpus covers
/// the same shapes end to end through a real DAR (<c>Box</c>, <c>Slot</c>); the
/// synthetic package is what lets a type-parameter regression fail here without
/// waiting on a corpus rebuild.
/// </summary>
public class GenericTypeSerializationRoundTripTests
{
    private const string ModuleName = "Test.Generics";
    private const string RecordName = "Box";
    private const string VariantName = "Wrapper";
    private const string HolderName = "Holder";

    private static readonly DamlTypeRef BoxRef = new("", ModuleName, RecordName);
    private static readonly DamlTypeRef WrapperRef = new("", ModuleName, VariantName);

    private static DamlTypeApp AppliedTo(DamlTypeRef typeRef, DamlType argument) => new(typeRef, [argument]);

    private static Assembly CompileAssembly()
    {
        var party = new DamlPrimitiveType(DamlPrimitive.Party);

        var module = new DamlModule
        {
            Name = ModuleName,
            Templates = [],
            Interfaces = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = RecordName,
                    TypeParams = ["a"],
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("value", new DamlTypeVar("a"))]),
                },
                new DamlDataType
                {
                    Name = VariantName,
                    TypeParams = ["a"],
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Wrapped", new DamlTypeVar("a")),
                        new DamlVariantConstructor("Blank", null),
                    ]),
                },
                new DamlDataType
                {
                    Name = HolderName,
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("boxOfParty", AppliedTo(BoxRef, party)),
                        new DamlFieldDefinition("wrapperOfParty", AppliedTo(WrapperRef, party)),
                    ]),
                },
            ],
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

        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
        };
        var files = new CSharpCodeGenerator(options)
            .Generate(new DarModel { MainPackage = package, Dependencies = [] });

        return EmitAssembly(files);
    }

    private static Assembly EmitAssembly(IReadOnlyList<GeneratedFile> files)
    {
        var parseOptions = new CSharpParseOptions(documentationMode: DocumentationMode.Parse);
        var trees = files
            .Where(f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal))
            .Select(f => CSharpSyntaxTree.ParseText(f.Content, parseOptions, path: f.RelativePath))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName: "GenericTypeSerializationRoundTripTests-emit",
            syntaxTrees: trees,
            references: ConsumerReferenceSet.Assemblies,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.Should().BeTrue(
            "the generated generic types must compile before they can round-trip, but got: {0}",
            string.Join(
                "\n",
                emit.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.GetMessage(CultureInfo.InvariantCulture) + " @ " + d.Location)));

        stream.Seek(0, SeekOrigin.Begin);
        return Assembly.Load(stream.ToArray());
    }

    private static readonly Assembly Emitted = CompileAssembly();

    private static Type EmittedType(string simpleOrGenericName) =>
        Emitted.GetTypes().Single(t => t.Name == simpleOrGenericName);

    private static readonly Func<Party, DamlValue> PartyToValue = p => p.ToDamlValue();
    private static readonly Func<DamlValue, Party> PartyFromValue = v => Party.FromDamlValue(v.As<DamlParty>());

    [Fact]
    public void GenericTypeSerializationRoundTrip_generic_record_round_trips_through_its_converter_delegates()
    {
        var boxOfParty = EmittedType("Box`1").MakeGenericType(typeof(Party));
        var box = Activator.CreateInstance(boxOfParty, new Party("alice"))!;

        var record = boxOfParty.GetMethod("ToRecord")!.Invoke(box, [PartyToValue])!;
        var roundTripped = boxOfParty.GetMethod("FromRecord", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [record, PartyFromValue, null]);

        roundTripped.Should().Be(box);
    }

    [Fact]
    public void GenericTypeSerializationRoundTrip_generic_variant_round_trips_through_its_converter_delegates()
    {
        var wrapperOfParty = EmittedType("Wrapper`1").MakeGenericType(typeof(Party));
        var fromVariant = wrapperOfParty.GetMethod("FromVariant", BindingFlags.Public | BindingFlags.Static)!;

        var initial = DamlVariant.Create("Wrapped", new Party("bob").ToDamlValue());
        var wrapper = fromVariant.Invoke(null, [initial, PartyFromValue, null])!;

        var serialized = wrapperOfParty.GetMethod("ToVariant")!.Invoke(wrapper, [PartyToValue]);
        var roundTripped = fromVariant.Invoke(null, [serialized, PartyFromValue, null]);

        roundTripped.Should().Be(wrapper);
    }

    [Fact]
    public void GenericTypeSerializationRoundTrip_record_embedding_instantiated_generics_round_trips_without_a_stub()
    {
        var boxOfParty = EmittedType("Box`1").MakeGenericType(typeof(Party));
        var wrapperOfParty = EmittedType("Wrapper`1").MakeGenericType(typeof(Party));
        var holderType = EmittedType(HolderName);

        var box = Activator.CreateInstance(boxOfParty, new Party("carol"))!;
        var wrapper = wrapperOfParty.GetMethod("FromVariant", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [DamlVariant.Create("Wrapped", new Party("dave").ToDamlValue()), PartyFromValue, null])!;
        var holder = Activator.CreateInstance(holderType, box, wrapper)!;

        var record = holderType.GetMethod("ToRecord")!.Invoke(holder, []);
        var roundTripped = holderType.GetMethod("FromRecord", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [record]);

        roundTripped.Should().Be(holder);
    }

    [Fact]
    public void GenericTypeSerializationRoundTrip_nullary_variant_constructor_round_trips_through_the_converter_delegate()
    {
        var wrapperOfParty = EmittedType("Wrapper`1").MakeGenericType(typeof(Party));
        var fromVariant = wrapperOfParty.GetMethod("FromVariant", BindingFlags.Public | BindingFlags.Static)!;

        var blank = fromVariant.Invoke(null, [DamlVariant.Create("Blank", DamlUnit.Instance), PartyFromValue, null])!;

        var serialized = wrapperOfParty.GetMethod("ToVariant")!.Invoke(blank, [PartyToValue]);
        var roundTripped = fromVariant.Invoke(null, [serialized, PartyFromValue, null]);

        roundTripped.Should().Be(blank);
    }
}
