// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedRecordOmittedFieldDecodeTests
{
    private static readonly Assembly Emitted = EmitToAssembly(Generate());

    private static IReadOnlyList<GeneratedFile> Generate()
    {
        var text = new DamlPrimitiveType(DamlPrimitive.Text);
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "TrailingNote",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("text", text),
                        new DamlFieldDefinition("remark", new DamlOptionalType(text)),
                    ]),
                },
                new DamlDataType
                {
                    Name = "NestedTail",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("text", text),
                        new DamlFieldDefinition("tailMaybe", new DamlOptionalType(new DamlOptionalType(text))),
                    ]),
                },
                new DamlDataType
                {
                    Name = "Box",
                    TypeParams = ["a"],
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("item", new DamlTypeVar("a"))]),
                },
            ],
            Interfaces = [],
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

    private static DamlRecord Decode(string typeName, string json, params object[] readers) =>
        DecodeAt(typeof(string), typeName, json, readers);

    private static DamlRecord DecodeAt(Type typeArgument, string typeName, string json, params object[] readers)
    {
        using var document = JsonDocument.Parse(json);
        object[] arguments = [document.RootElement, DamlLfJsonDecodeContext.Root(typeName), .. readers];
        return (DamlRecord)InvokeStatic(Close(typeName, typeArgument), "__ReadDamlLfJson", arguments);
    }

    private static object FromRecord(string typeName, DamlRecord record, params object[] converters) =>
        FromRecordAt(typeof(string), typeName, record, converters);

    private static object FromRecordAt(Type typeArgument, string typeName, DamlRecord record, params object[] converters) =>
        InvokeStatic(Close(typeName, typeArgument), "FromRecord", [record, .. converters]);

    private static Type Close(string typeName, Type typeArgument)
    {
        var type = Emitted.GetTypes().Single(t => t.Name == typeName);
        return type.IsGenericTypeDefinition
            ? type.MakeGenericType([.. type.GetGenericArguments().Select(_ => typeArgument)])
            : type;
    }

    private static object InvokeStatic(Type type, string methodName, object[] arguments)
    {
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!;
        try
        {
            return method.Invoke(null, arguments)!;
        }
        catch (TargetInvocationException invocation) when (invocation.InnerException is not null)
        {
            throw invocation.InnerException;
        }
    }

    private static object? Property(object instance, string name) =>
        instance.GetType().GetProperty(name)!.GetValue(instance);

    private static readonly DamlLfElementReader ReadOptionalText =
        (element, context) => DamlLfJsonDecoders.ReadOptional(element, context, DamlLfJsonDecoders.ReadText);

    [Fact]
    public void ReadDamlLfJson_decodes_an_omitted_trailing_Optional_field_as_None()
    {
        var record = Decode("TrailingNote", """{"text":"inner"}""");

        record.GetRequiredField("text").Should().Be(new DamlText("inner"));
        record.GetRequiredField("remark").Should().Be(DamlOptional.None);
    }

    [Fact]
    public void ReadDamlLfJson_decodes_an_explicit_null_Optional_field_as_None()
    {
        var record = Decode("TrailingNote", """{"text":"inner","remark":null}""");

        record.GetRequiredField("remark").Should().Be(DamlOptional.None);
    }

    [Fact]
    public void ReadDamlLfJson_keeps_a_non_Optional_field_required()
    {
        var act = () => Decode("TrailingNote", """{"remark":"remark"}""");

        act.Should().Throw<JsonException>().WithMessage("Required Daml field 'TrailingNote.text' is missing from the JSON object");
    }

    [Fact]
    public void ReadDamlLfJson_decodes_an_omitted_trailing_nested_Optional_field_as_None()
    {
        var record = Decode("NestedTail", """{"text":"inner"}""");

        record.GetRequiredField("tailMaybe").Should().Be(DamlOptionalChain.None);
    }

    [Fact]
    public void ReadDamlLfJson_still_rejects_null_for_a_nested_Optional_field()
    {
        var act = () => Decode("NestedTail", """{"text":"inner","tailMaybe":null}""");

        act.Should().Throw<JsonException>().WithMessage("Expected JSON Array at 'NestedTail.tailMaybe' but found Null");
    }

    [Fact]
    public void ReadDamlLfJson_decodes_an_omitted_type_parameter_field_as_None_at_an_Optional_instantiation()
    {
        var record = Decode("Box`1", "{}", ReadOptionalText);

        record.GetRequiredField("item").Should().Be(DamlOptional.None);
    }

    [Fact]
    public void ReadDamlLfJson_decodes_an_omitted_type_parameter_field_as_None_at_a_nested_Optional_instantiation()
    {
        DamlLfElementReader readNestedOptionalText = (element, context) => DamlLfJsonDecoders.ReadOptionalChain(
            element,
            context,
            (inner, innerContext) => DamlLfJsonDecoders.ReadOptionalChain(inner, innerContext, DamlLfJsonDecoders.ReadText));

        var record = DecodeAt(typeof(Optional<Optional<string>>), "Box`1", "{}", readNestedOptionalText);

        record.GetRequiredField("item").Should().Be(new DamlOptionalChain(null));
    }

    [Fact]
    public void ReadDamlLfJson_reports_an_omitted_type_parameter_field_missing_at_a_non_Optional_instantiation()
    {
        var act = () => Decode("Box`1", "{}", (DamlLfElementReader)DamlLfJsonDecoders.ReadText);

        act.Should().Throw<JsonException>().WithMessage("Required Daml field 'Box`1.item' is missing from the JSON object");
    }

    [Fact]
    public void FromRecord_reads_an_omitted_trailing_Optional_field_as_None()
    {
        var decoded = FromRecord("TrailingNote", DamlRecord.Create(DamlField.Create("text", new DamlText("inner"))));

        Property(decoded, "Text").Should().Be("inner");
        Property(decoded, "Remark").Should().BeNull();
    }

    [Fact]
    public void FromRecord_keeps_a_non_Optional_field_required()
    {
        var act = () => FromRecord("TrailingNote", DamlRecord.Create(DamlField.Create("remark", DamlOptional.None)));

        act.Should().Throw<InvalidOperationException>().WithMessage("Required field 'text' not found in record.");
    }

    [Fact]
    public void FromRecord_reads_an_omitted_trailing_nested_Optional_field_as_None()
    {
        var decoded = FromRecord("NestedTail", DamlRecord.Create(DamlField.Create("text", new DamlText("inner"))));

        Property(decoded, "TailMaybe").Should().Be(new Optional<Optional<string>>.None());
    }

    [Fact]
    public void FromRecord_reads_an_omitted_type_parameter_field_as_None_at_an_Optional_instantiation()
    {
        Func<DamlValue, string> convertOptionalText = value => value.As<DamlOptional>().Value?.As<DamlText>().Value!;

        var decoded = FromRecord("Box`1", DamlRecord.Create(), convertOptionalText);

        Property(decoded, "Item").Should().BeNull();
    }

    [Fact]
    public void FromRecord_reads_an_omitted_type_parameter_field_as_None_at_a_nested_Optional_instantiation()
    {
        Func<DamlValue, Optional<Optional<string>>> convertNestedOptionalText = value => Optional<Optional<string>>.FromChainValue(
            value,
            inner => Optional<string>.FromChainValue(inner, text => text.As<DamlText>().Value));

        var decoded = FromRecordAt(typeof(Optional<Optional<string>>), "Box`1", DamlRecord.Create(), convertNestedOptionalText);

        Property(decoded, "Item").Should().Be(new Optional<Optional<string>>.None());
    }

    [Fact]
    public void FromRecord_reports_an_omitted_type_parameter_field_missing_at_a_non_Optional_instantiation()
    {
        Func<DamlValue, string> convertText = value => value.As<DamlText>().Value;

        var act = () => FromRecord("Box`1", DamlRecord.Create(), convertText);

        act.Should().Throw<InvalidOperationException>().WithMessage("Required field 'item' not found in record.");
    }
}
