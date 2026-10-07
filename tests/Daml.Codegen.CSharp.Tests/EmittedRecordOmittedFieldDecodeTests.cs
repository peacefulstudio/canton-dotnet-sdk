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
                    Name = "MidOptional",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("text", text),
                        new DamlFieldDefinition("mid", new DamlOptionalType(text)),
                        new DamlFieldDefinition("tail", text),
                    ]),
                },
                new DamlDataType
                {
                    Name = "Box",
                    TypeParams = ["a"],
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("item", new DamlTypeVar("a"))]),
                },
                new DamlDataType
                {
                    Name = "Hold",
                    TypeParams = ["a", "b"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("head", new DamlTypeVar("a")),
                        new DamlFieldDefinition(
                            "held",
                            new DamlTypeApp(
                                new DamlTypeRef("test-package-id", "Test.Module", "Box"),
                                [new DamlTypeVar("b")])),
                    ]),
                },
                new DamlDataType
                {
                    Name = "Pick",
                    TypeParams = ["a"],
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor(
                            "Picked",
                            new DamlTypeApp(
                                new DamlTypeRef("test-package-id", "Test.Module", "Box"),
                                [new DamlTypeVar("a")])),
                    ]),
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

    private static DamlRecord Decode(string typeName, string json, params object?[] readersAndAbsences) =>
        DecodeAt([typeof(string)], typeName, json, readersAndAbsences);

    private static DamlRecord DecodeAt(Type[] typeArguments, string typeName, string json, params object?[] readersAndAbsences)
    {
        using var document = JsonDocument.Parse(json);
        object?[] arguments = [document.RootElement, DamlLfJsonDecodeContext.Root(typeName), .. readersAndAbsences];
        return (DamlRecord)InvokeStatic(Close(typeName, typeArguments), "__ReadDamlLfJson", arguments);
    }

    private static object FromRecord(string typeName, DamlRecord record, params object?[] convertersAndAbsences) =>
        FromRecordAt([typeof(string)], typeName, record, convertersAndAbsences);

    private static object FromRecordAt(Type[] typeArguments, string typeName, DamlRecord record, params object?[] convertersAndAbsences) =>
        InvokeStatic(Close(typeName, typeArguments), "FromRecord", [record, .. convertersAndAbsences]);

    private static Type Close(string typeName, Type[] typeArguments)
    {
        var type = Emitted.GetTypes().Single(t => t.Name == typeName);
        return type.IsGenericTypeDefinition
            ? type.MakeGenericType(typeArguments.Length == 1
                ? [.. type.GetGenericArguments().Select(_ => typeArguments[0])]
                : typeArguments)
            : type;
    }

    private static object InvokeStatic(Type type, string methodName, object?[] arguments)
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
    public void ReadDamlLfJson_keeps_no_field_for_an_omitted_trailing_Optional_key()
    {
        var record = Decode("TrailingNote", """{"text":"inner"}""");

        record.Fields.Should().Equal(DamlField.Create("text", new DamlText("inner")));
        record.GetField("remark").Should().BeNull();
        record.GetOptionalField("remark").Should().Be(DamlOptional.None);
    }

    [Fact]
    public void ReadDamlLfJson_keeps_no_field_for_an_omitted_mid_record_Optional_key()
    {
        var record = Decode("MidOptional", """{"text":"head","tail":"end"}""");

        record.Fields.Should().Equal(
            DamlField.Create("text", new DamlText("head")),
            DamlField.Create("tail", new DamlText("end")));
        record.GetOptionalField("mid").Should().Be(DamlOptional.None);
    }

    [Fact]
    public void ReadDamlLfJson_keeps_declaration_order_when_an_Optional_key_is_present()
    {
        var record = Decode("MidOptional", """{"tail":"end","mid":"between","text":"head"}""");

        record.Fields.Should().Equal(
            DamlField.Create("text", new DamlText("head")),
            DamlField.Create("mid", DamlOptional.Some(new DamlText("between"))),
            DamlField.Create("tail", new DamlText("end")));
    }

    [Fact]
    public void ReadDamlLfJson_keeps_a_null_Optional_field_as_a_flat_None_field()
    {
        var record = Decode("TrailingNote", """{"text":"inner","remark":null}""");

        record.Fields.Should().Equal(
            DamlField.Create("text", new DamlText("inner")),
            DamlField.Create("remark", DamlOptional.None));
    }

    [Fact]
    public void ReadDamlLfJson_keeps_a_null_mid_record_Optional_field_as_a_flat_None_field()
    {
        var record = Decode("MidOptional", """{"text":"head","mid":null,"tail":"end"}""");

        record.Fields.Should().Equal(
            DamlField.Create("text", new DamlText("head")),
            DamlField.Create("mid", DamlOptional.None),
            DamlField.Create("tail", new DamlText("end")));
    }

    [Fact]
    public void ReadDamlLfJson_keeps_a_non_Optional_field_required()
    {
        var act = () => Decode("TrailingNote", """{"remark":"remark"}""");

        act.Should().Throw<JsonException>().WithMessage("Required Daml field 'TrailingNote.text' is missing from the JSON object");
    }

    [Fact]
    public void ReadDamlLfJson_keeps_no_field_for_an_omitted_trailing_nested_Optional_key()
    {
        var record = Decode("NestedTail", """{"text":"inner"}""");

        record.Fields.Should().Equal(DamlField.Create("text", new DamlText("inner")));
        record.GetField("tailMaybe").Should().BeNull();
        record.GetOptionalChainField("tailMaybe").Should().Be(DamlOptionalChain.None);
    }

    [Fact]
    public void ReadDamlLfJson_keeps_an_empty_array_nested_Optional_field_as_a_chain_None_field()
    {
        var record = Decode("NestedTail", """{"text":"inner","tailMaybe":[]}""");

        record.Fields.Should().Equal(
            DamlField.Create("text", new DamlText("inner")),
            DamlField.Create("tailMaybe", DamlOptionalChain.None));
    }

    [Fact]
    public void ReadDamlLfJson_still_rejects_null_for_a_nested_Optional_field()
    {
        var act = () => Decode("NestedTail", """{"text":"inner","tailMaybe":null}""");

        act.Should().Throw<JsonException>().WithMessage("Expected JSON Array at 'NestedTail.tailMaybe' but found Null");
    }

    private static readonly DamlLfElementReader ReadNestedOptionalText = (element, context) => DamlLfJsonDecoders.ReadOptionalChain(
        element,
        context,
        (inner, innerContext) => DamlLfJsonDecoders.ReadOptionalChain(inner, innerContext, DamlLfJsonDecoders.ReadText));

    private static readonly DamlLfElementReader ReadPlainText = DamlLfJsonDecoders.ReadText;

    [Fact]
    public void ReadDamlLfJson_decodes_a_present_type_parameter_field_with_the_instantiation_reader()
    {
        var record = Decode("Box`1", """{"item":"boxed"}""", ReadPlainText, null);

        record.Fields.Should().Equal(DamlField.Create("item", new DamlText("boxed")));
    }

    [Fact]
    public void ReadDamlLfJson_keeps_no_field_for_an_omitted_type_parameter_key_at_an_Optional_instantiation()
    {
        var record = Decode("Box`1", "{}", ReadOptionalText, DamlOptional.None);

        record.Fields.Should().BeEmpty();
        record.GetField("item").Should().BeNull();
    }

    [Fact]
    public void ReadDamlLfJson_keeps_no_field_for_an_omitted_type_parameter_key_at_a_nested_Optional_instantiation()
    {
        var record = DecodeAt([typeof(Optional<Optional<string>>)], "Box`1", "{}", ReadNestedOptionalText, DamlOptional.None);

        record.Fields.Should().BeEmpty();
    }

    [Fact]
    public void ReadDamlLfJson_reports_an_omitted_type_parameter_field_missing_at_a_non_Optional_instantiation()
    {
        var act = () => Decode("Box`1", "{}", ReadPlainText, null);

        act.Should().Throw<JsonException>().WithMessage("Required Daml field 'Box`1.item' is missing from the JSON object");
    }

    [Fact]
    public void ReadDamlLfJson_forwards_the_absence_of_its_own_type_parameter_to_a_generic_field_it_instantiates()
    {
        var record = DecodeAt(
            [typeof(string), typeof(string)],
            "Hold`2",
            """{"head":"h","held":{}}""",
            ReadPlainText,
            null,
            ReadOptionalText,
            DamlOptional.None);

        record.Fields.Select(field => field.Label).Should().Equal("head", "held");
        record.GetRequiredField("held").As<DamlRecord>().Fields.Should().BeEmpty();
    }

    [Fact]
    public void ReadDamlLfJson_reports_the_nested_generic_field_missing_when_its_forwarded_absence_is_required()
    {
        var act = () => DecodeAt(
            [typeof(string), typeof(string)],
            "Hold`2",
            """{"held":{}}""",
            ReadOptionalText,
            DamlOptional.None,
            ReadPlainText,
            null);

        act.Should().Throw<JsonException>().WithMessage("Required Daml field 'Hold`2.held.item' is missing from the JSON object");
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
    public void FromRecord_converts_a_present_type_parameter_field()
    {
        var decoded = FromRecord(
            "Box`1",
            DamlRecord.Create(DamlField.Create("item", new DamlText("boxed"))),
            (Func<DamlValue, string>)(value => value.As<DamlText>().Value),
            null);

        Property(decoded, "Item").Should().Be("boxed");
    }

    [Fact]
    public void FromRecord_reads_an_omitted_type_parameter_field_as_None_at_an_Optional_instantiation()
    {
        Func<DamlValue, string> convertOptionalText = value => value.As<DamlOptional>().Value?.As<DamlText>().Value!;

        var decoded = FromRecord("Box`1", DamlRecord.Create(), convertOptionalText, DamlOptional.None);

        Property(decoded, "Item").Should().BeNull();
    }

    [Fact]
    public void FromRecord_reads_an_omitted_type_parameter_field_as_None_at_a_nested_Optional_instantiation()
    {
        Func<DamlValue, Optional<Optional<string>>> convertNestedOptionalText = value => Optional<Optional<string>>.FromChainValue(
            value,
            inner => Optional<string>.FromChainValue(inner, text => text.As<DamlText>().Value));

        var decoded = FromRecordAt(
            [typeof(Optional<Optional<string>>)], "Box`1", DamlRecord.Create(), convertNestedOptionalText, DamlOptional.None);

        Property(decoded, "Item").Should().Be(new Optional<Optional<string>>.None());
    }

    [Fact]
    public void FromRecord_reports_an_omitted_type_parameter_field_missing_at_a_non_Optional_instantiation()
    {
        Func<DamlValue, string> convertText = value => value.As<DamlText>().Value;

        var act = () => FromRecord("Box`1", DamlRecord.Create(), convertText, null);

        act.Should().Throw<InvalidOperationException>().WithMessage("Required field 'item' not found in record.");
    }

    [Fact]
    public void FromRecord_surfaces_the_InvalidCastException_a_type_parameter_converter_throws()
    {
        Func<DamlValue, string> convertBroken = _ => throw new InvalidCastException("converter bug");

        var act = () => FromRecord("Box`1", DamlRecord.Create(), convertBroken, DamlOptional.None);

        act.Should().Throw<InvalidCastException>().WithMessage("converter bug");
    }

    [Fact]
    public void FromRecord_forwards_the_absence_of_its_own_type_parameter_to_a_generic_field_it_instantiates()
    {
        Func<DamlValue, string> convertText = value => value.As<DamlText>().Value;
        Func<DamlValue, string> convertOptionalText = value => value.As<DamlOptional>().Value?.As<DamlText>().Value!;
        var record = DamlRecord.Create(
            DamlField.Create("head", new DamlText("h")),
            DamlField.Create("held", DamlRecord.Create()));

        var decoded = FromRecordAt(
            [typeof(string), typeof(string)], "Hold`2", record, convertText, null, convertOptionalText, DamlOptional.None);

        Property(Property(decoded, "Held")!, "Item").Should().BeNull();
    }

    [Fact]
    public void FromRecord_reports_the_nested_generic_field_missing_when_its_forwarded_absence_is_required()
    {
        Func<DamlValue, string> convertText = value => value.As<DamlText>().Value;
        Func<DamlValue, string> convertOptionalText = value => value.As<DamlOptional>().Value?.As<DamlText>().Value!;
        var record = DamlRecord.Create(DamlField.Create("held", DamlRecord.Create()));

        var act = () => FromRecordAt(
            [typeof(string), typeof(string)], "Hold`2", record, convertOptionalText, DamlOptional.None, convertText, null);

        act.Should().Throw<InvalidOperationException>().WithMessage("Required field 'item' not found in record.");
    }

    private static DamlVariant DecodeVariantAt(Type[] typeArguments, string typeName, string json, params object?[] readersAndAbsences)
    {
        using var document = JsonDocument.Parse(json);
        object?[] arguments = [document.RootElement, DamlLfJsonDecodeContext.Root(typeName), .. readersAndAbsences];
        return (DamlVariant)InvokeStatic(Close(typeName, typeArguments), "__ReadDamlLfJson", arguments);
    }

    private static object FromVariantAt(Type[] typeArguments, string typeName, DamlVariant variant, params object?[] convertersAndAbsences) =>
        InvokeStatic(Close(typeName, typeArguments), "FromVariant", [variant, .. convertersAndAbsences]);

    [Fact]
    public void Variant_ReadDamlLfJson_forwards_the_absence_of_its_own_type_parameter_to_a_generic_record_it_carries()
    {
        var variant = DecodeVariantAt([typeof(string)], "Pick`1", """{"tag":"Picked","value":{}}""", ReadOptionalText, DamlOptional.None);

        variant.Value.As<DamlRecord>().Fields.Should().BeEmpty();
    }

    [Fact]
    public void Variant_ReadDamlLfJson_reports_the_carried_generic_record_field_missing_when_the_absence_is_required()
    {
        var act = () => DecodeVariantAt([typeof(string)], "Pick`1", """{"tag":"Picked","value":{}}""", ReadPlainText, null);

        act.Should().Throw<JsonException>().WithMessage("Required Daml field 'Pick`1.value.item' is missing from the JSON object");
    }

    [Fact]
    public void Variant_FromVariant_forwards_the_absence_of_its_own_type_parameter_to_a_generic_record_it_carries()
    {
        Func<DamlValue, string> convertOptionalText = value => value.As<DamlOptional>().Value?.As<DamlText>().Value!;
        var variant = DamlVariant.Create("Picked", DamlRecord.Create());

        var decoded = FromVariantAt([typeof(string)], "Pick`1", variant, convertOptionalText, DamlOptional.None);

        Property(Property(decoded, "Value")!, "Item").Should().BeNull();
    }

    [Fact]
    public void Variant_FromVariant_reports_the_carried_generic_record_field_missing_when_the_absence_is_required()
    {
        Func<DamlValue, string> convertText = value => value.As<DamlText>().Value;
        var variant = DamlVariant.Create("Picked", DamlRecord.Create());

        var act = () => FromVariantAt([typeof(string)], "Pick`1", variant, convertText, null);

        act.Should().Throw<InvalidOperationException>().WithMessage("Required field 'item' not found in record.");
    }
}
