// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text.Json;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Runtime.Serialization;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class GenericChoiceResultJsonDecoderCorpusTests
{
    private const string ModuleName = "Test.Module";
    private const string BoxName = "Box";
    private const string WrapperName = "Wrapper";
    private const string VaultName = "Vault";
    private const string PairName = "Pair";

    private static readonly DamlTypeRef BoxRef = new("", ModuleName, BoxName);
    private static readonly DamlTypeRef WrapperRef = new("", ModuleName, WrapperName);
    private static readonly DamlTypeRef PairRef = new("", ModuleName, PairName);
    private static readonly DamlType TextType = new DamlPrimitiveType(DamlPrimitive.Text);
    private static readonly DamlType BoolType = new DamlPrimitiveType(DamlPrimitive.Bool);

    private static DamlModule BuildModule(DamlType peekReturnType, DamlType peekVariantReturnType)
    {
        var owner = new DamlPartyPayloadField("owner");

        return new DamlModule
        {
            Name = ModuleName,
            Templates =
            [
                new DamlTemplate
                {
                    Name = VaultName,
                    Signatories = DamlPartyAnalysis.Static([owner]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Peek",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = peekReturnType,
                            Controllers = DamlPartyAnalysis.Static([owner]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                        new DamlChoice
                        {
                            Name = "PeekVariant",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = peekVariantReturnType,
                            Controllers = DamlPartyAnalysis.Static([owner]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                        new DamlChoice
                        {
                            Name = "PeekPair",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(PairRef, [TextType, BoolType]),
                            Controllers = DamlPartyAnalysis.Static([owner]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = VaultName,
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = BoxName,
                    TypeParams = ["a"],
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("value", new DamlTypeVar("a"))]),
                },
                new DamlDataType
                {
                    Name = WrapperName,
                    TypeParams = ["a"],
                    Definition = new DamlVariantDefinition([new DamlVariantConstructor("Wrapped", new DamlTypeVar("a"))]),
                },
                new DamlDataType
                {
                    Name = PairName,
                    TypeParams = ["a", "b"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("first", new DamlTypeVar("a")),
                        new DamlFieldDefinition("second", new DamlTypeVar("b")),
                    ]),
                },
            ],
            Interfaces = [],
        };
    }

    private static IReadOnlyList<GeneratedFile> Emit() =>
        CreateGenerator(new CodeGenOptions { EnableNullableReferenceTypes = true, UseFileScopedNamespaces = true })
            .Generate(CreateTestDar(BuildModule(
                peekReturnType: new DamlTypeApp(BoxRef, [TextType]),
                peekVariantReturnType: new DamlTypeApp(WrapperRef, [TextType]))));

    private static string SourceOf(IReadOnlyList<GeneratedFile> files, string fileName) =>
        files.Single(f => f.RelativePath.EndsWith(fileName, StringComparison.Ordinal)).Content;

    [Fact]
    public void FromJson_for_an_instantiated_generic_choice_return_type_emits_a_call_to_the_record_own_injected_reader_overload()
    {
        var files = Emit();
        var vaultSource = SourceOf(files, "Vault.cs");
        var boxSource = SourceOf(files, $"{BoxName}.cs");

        vaultSource.Should().NotContain(
            $"{DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.ReadRecord(json, typeof({BoxName}<string>), context)",
            "the unconstrained ReadRecord(Type) overload still requires IDamlRecord at runtime, which a generic record never implements — it throws NotSupportedException for Box<string> even though it compiles, so FromJson must not emit a call to it for an instantiated generic record");
        vaultSource.Should().Contain(
            $"{BoxName}<string>.__ReadDamlLfJson(json, context, (__json0, __ctx0) => {DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.ReadText(__json0, __ctx0), null)",
            "a choice whose return type is an instantiated generic record must call the record's own generic __ReadDamlLfJson overload with one injected reader per type argument, not a DamlLfJsonDecoders.ReadRecord call");
        boxSource.Should().Contain(
            "global::Daml.Runtime.Serialization.DamlLfJsonDecoders.AddTypeParameterField(fields, json, context, \"value\", readTA, absentTA);",
            "Box's own __ReadDamlLfJson carries the field-by-field decode Vault.cs now only calls into, driven by its injected readTA reader");
    }

    [Fact]
    public void FromJson_for_an_instantiated_generic_choice_return_type_emits_a_call_to_the_variant_own_injected_reader_overload()
    {
        var files = Emit();
        var vaultSource = SourceOf(files, "Vault.cs");
        var wrapperSource = SourceOf(files, $"{WrapperName}.cs");

        vaultSource.Should().NotContain(
            $"{DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.ReadVariant(json, typeof({WrapperName}<string>), context)",
            "the unconstrained ReadVariant(Type) overload still requires IDamlVariant at runtime, which a generic variant never implements — it throws NotSupportedException for Wrapper<string> even though it compiles, so FromJson must not emit a call to it for an instantiated generic variant");
        vaultSource.Should().Contain(
            $"{WrapperName}<string>.__ReadDamlLfJson(json, context, (__json0, __ctx0) => {DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.ReadText(__json0, __ctx0), null)",
            "a choice whose return type is an instantiated generic variant must call the variant's own generic __ReadDamlLfJson overload with one injected reader per type argument, not a DamlLfJsonDecoders.ReadVariant call");
        wrapperSource.Should().Contain(
            $"\"Wrapped\" => global::Daml.Runtime.Data.DamlVariant.Create(\"Wrapped\", readTA({DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.RequireVariantValue(json, context), context.Field(\"value\"))),",
            "Wrapper's own __ReadDamlLfJson carries the arm-by-arm decode Vault.cs now only calls into, driven by its injected readTA reader");
    }

    [Fact]
    public void Pair_gives_its_two_type_parameters_distinguishable_type_arguments_to_detect_a_reader_order_swap()
    {
        var files = Emit();
        var vaultSource = SourceOf(files, "Vault.cs");
        var pairSource = SourceOf(files, $"{PairName}.cs");

        vaultSource.Should().Contain(
            $"{PairName}<string, bool>.__ReadDamlLfJson(json, context, "
            + $"(__json0, __ctx0) => {DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.ReadText(__json0, __ctx0), null, "
            + $"(__json0, __ctx0) => {DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.ReadBool(__json0, __ctx0), null)",
            "the call site must inject the Text reader for the first type argument and the Bool reader for the second, in declaration order — a transposed call would inject the Bool reader where Text is expected; each lambda is independently scoped, so both siblings reuse __json0/__ctx0 rather than being indexed by argument position");
        pairSource.Should().Contain(
            "global::Daml.Runtime.Serialization.DamlLfJsonDecoders.AddTypeParameterField(fields, json, context, \"first\", readTA, absentTA);",
            "Pair's own __ReadDamlLfJson must decode the 'first' field with readTA, the reader for its first type parameter");
        pairSource.Should().Contain(
            "global::Daml.Runtime.Serialization.DamlLfJsonDecoders.AddTypeParameterField(fields, json, context, \"second\", readTB, absentTB);",
            "Pair's own __ReadDamlLfJson must decode the 'second' field with readTB, the reader for its second type parameter — swapping readTA/readTB here would still compile but decode both fields with the wrong reader");
    }

    [Fact]
    public void ChoicePeekPair_ResultJsonDecoder_decodes_first_and_second_through_their_own_type_specific_readers()
    {
        var assembly = EmitToAssembly(Emit());
        var vaultType = assembly.GetTypes().Single(t => t.Name == VaultName);
        var choice = vaultType.GetProperty("ChoicePeekPair", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var resultJsonDecoder = (Delegate)choice.GetType().GetProperty("ResultJsonDecoder")!.GetValue(choice)!;

        var json = JsonDocument.Parse("""{"first": "hello", "second": true}""").RootElement;
        var decoded = resultJsonDecoder.DynamicInvoke(json, DamlLfJsonDecodeContext.Root("$"))!;

        var pairOfStringBool = assembly.GetTypes().Single(t => t.Name == "Pair`2").MakeGenericType(typeof(string), typeof(bool));
        decoded.Should().BeOfType(pairOfStringBool);
        pairOfStringBool.GetProperty("First")!.GetValue(decoded).Should().Be("hello");
        pairOfStringBool.GetProperty("Second")!.GetValue(decoded).Should().Be(true);
    }

    [Fact]
    public void Emitted_template_with_an_instantiated_generic_choice_return_type_compiles()
    {
        var errors = CompileEmittedFiles(Emit())
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToList();

        errors.Should().BeEmpty(
            "a choice returning an instantiated generic record or variant must compile; got: {0}",
            string.Join("; ", errors.Select(d => d.ToString())));
    }

    [Fact]
    public void ChoicePeek_ResultJsonDecoder_round_trips_an_instantiated_generic_record_through_the_emitted_decoder()
    {
        var assembly = EmitToAssembly(Emit());
        var vaultType = assembly.GetTypes().Single(t => t.Name == VaultName);
        var choice = vaultType.GetProperty("ChoicePeek", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var resultJsonDecoder = (Delegate)choice.GetType().GetProperty("ResultJsonDecoder")!.GetValue(choice)!;

        var json = JsonDocument.Parse("""{"value": "hello"}""").RootElement;
        var decoded = resultJsonDecoder.DynamicInvoke(json, DamlLfJsonDecodeContext.Root("$"))!;

        var boxOfString = assembly.GetTypes().Single(t => t.Name == "Box`1").MakeGenericType(typeof(string));
        decoded.Should().BeOfType(boxOfString);
        boxOfString.GetProperty("Value")!.GetValue(decoded).Should().Be("hello");
    }

    [Fact]
    public void ChoicePeekVariant_ResultJsonDecoder_round_trips_an_instantiated_generic_variant_through_the_emitted_decoder()
    {
        var assembly = EmitToAssembly(Emit());
        var vaultType = assembly.GetTypes().Single(t => t.Name == VaultName);
        var choice = vaultType.GetProperty("ChoicePeekVariant", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var resultJsonDecoder = (Delegate)choice.GetType().GetProperty("ResultJsonDecoder")!.GetValue(choice)!;

        var json = JsonDocument.Parse("""{"tag": "Wrapped", "value": "hello"}""").RootElement;
        var decoded = resultJsonDecoder.DynamicInvoke(json, DamlLfJsonDecodeContext.Root("$"))!;

        var wrapperOfString = assembly.GetTypes().Single(t => t.Name == "Wrapper`1").MakeGenericType(typeof(string));
        var wrappedOfString = assembly.GetTypes().Single(t => t.Name == "Wrapped").MakeGenericType(typeof(string));
        decoded.Should().BeOfType(wrappedOfString);
        decoded.Should().BeAssignableTo(wrapperOfString);
        wrappedOfString.GetProperty("Value")!.GetValue(decoded).Should().Be("hello");
    }
}
