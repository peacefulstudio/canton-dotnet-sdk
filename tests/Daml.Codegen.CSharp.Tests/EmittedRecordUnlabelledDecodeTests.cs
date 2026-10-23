// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Runtime.Data;
using Daml.Runtime.Stdlib;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedRecordUnlabelledDecodeTests
{
    private static readonly Assembly Emitted = EmitToAssembly(Generate());

    private static IReadOnlyList<GeneratedFile> Generate()
    {
        var text = new DamlPrimitiveType(DamlPrimitive.Text);
        var integer = new DamlPrimitiveType(DamlPrimitive.Int64);
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Inner",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("count", integer),
                        new DamlFieldDefinition("label", text),
                    ]),
                },
                new DamlDataType
                {
                    Name = "Outer",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", text),
                        new DamlFieldDefinition("inner", new DamlTypeRef("test-package-id", "Test.Module", "Inner")),
                        new DamlFieldDefinition("note", new DamlOptionalType(text)),
                        new DamlFieldDefinition("deep", new DamlOptionalType(new DamlOptionalType(text))),
                    ]),
                },
                new DamlDataType
                {
                    Name = "Box",
                    TypeParams = ["a"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("tag", text),
                        new DamlFieldDefinition("item", new DamlTypeVar("a")),
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

    private static DamlField Unlabelled(DamlValue value) => DamlField.Create("", value);

    private static object FromRecord(string typeName, DamlRecord record, params object?[] convertersAndAbsences)
    {
        var type = Emitted.GetTypes().Single(t => t.Name == typeName);
        var closed = type.IsGenericTypeDefinition ? type.MakeGenericType(typeof(string)) : type;
        var method = closed.GetMethod("FromRecord", BindingFlags.Public | BindingFlags.Static)!;
        try
        {
            return method.Invoke(null, [record, .. convertersAndAbsences])!;
        }
        catch (TargetInvocationException invocation) when (invocation.InnerException is not null)
        {
            throw invocation.InnerException;
        }
    }

    private static object? Property(object instance, string name) =>
        instance.GetType().GetProperty(name)!.GetValue(instance);

    [Fact]
    public void FromRecord_decodes_an_unlabelled_flat_record_by_position()
    {
        var decoded = FromRecord(
            "Inner",
            DamlRecord.Create(Unlabelled(new DamlInt64(42)), Unlabelled(new DamlText("forty-two"))));

        Property(decoded, "Count").Should().Be(42L);
        Property(decoded, "Label").Should().Be("forty-two");
    }

    [Fact]
    public void FromRecord_decodes_a_nested_unlabelled_record_by_position()
    {
        var inner = DamlRecord.Create(Unlabelled(new DamlInt64(7)), Unlabelled(new DamlText("seven")));
        var outer = DamlRecord.Create(
            Unlabelled(new DamlText("alice")),
            Unlabelled(inner),
            Unlabelled(DamlOptional.Some(new DamlText("hello"))),
            Unlabelled(DamlOptionalChain.Some(DamlOptional.Some(new DamlText("deeper")))));

        var decoded = FromRecord("Outer", outer);

        Property(decoded, "Owner").Should().Be("alice");
        var decodedInner = Property(decoded, "Inner")!;
        Property(decodedInner, "Count").Should().Be(7L);
        Property(decodedInner, "Label").Should().Be("seven");
        Property(decoded, "Note").Should().Be("hello");
        Property(decoded, "Deep").Should().Be(new Optional<Optional<string>>.Some(new Optional<string>.Some("deeper")));
    }

    [Fact]
    public void FromRecord_reads_the_trailing_Optional_fields_an_unlabelled_record_omits_as_None()
    {
        var inner = DamlRecord.Create(Unlabelled(new DamlInt64(7)), Unlabelled(new DamlText("seven")));
        var outer = DamlRecord.Create(Unlabelled(new DamlText("alice")), Unlabelled(inner));

        var decoded = FromRecord("Outer", outer);

        Property(decoded, "Owner").Should().Be("alice");
        Property(decoded, "Note").Should().BeNull();
    }

    [Fact]
    public void FromRecord_decodes_an_unlabelled_generic_record_by_position()
    {
        var record = DamlRecord.Create(Unlabelled(new DamlText("tagged")), Unlabelled(new DamlText("boxed")));
        Func<DamlValue, string> convert = value => value.As<DamlText>().Value;

        var decoded = FromRecord("Box`1", record, convert, null);

        Property(decoded, "Tag").Should().Be("tagged");
        Property(decoded, "Item").Should().Be("boxed");
    }

    [Fact]
    public void FromRecord_still_names_the_missing_field_of_a_labelled_record()
    {
        var labelled = DamlRecord.Create(DamlField.Create("count", new DamlInt64(42)));

        var act = () => FromRecord("Inner", labelled);

        act.Should().Throw<InvalidOperationException>().WithMessage("Required field 'label' not found in record.");
    }
}
