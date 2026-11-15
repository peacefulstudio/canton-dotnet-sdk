// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Compiles a generic record and a generic variant in memory and instantiates each at a list type
/// argument, so a member whose type is a type parameter holds an <c>IReadOnlyList</c>. Two values
/// built from equal content must be equal and hash equal; record-synthesized equality reads such a
/// member by reference.
/// </summary>
public class EmittedTypeParameterFieldEqualityTests
{
    private const string ModuleName = "Test.Generic";

    private static readonly Lazy<Type[]> EmittedTypes = new(Compile);

    private static Type[] Compile()
    {
        var module = new DamlModule
        {
            Name = ModuleName,
            Templates = [],
            Interfaces = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Pair",
                    TypeParams = ["a", "b"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("label", new DamlPrimitiveType(DamlPrimitive.Text)),
                        new DamlFieldDefinition("first", new DamlTypeVar("a")),
                        new DamlFieldDefinition("second", new DamlTypeVar("b")),
                    ]),
                },
                new DamlDataType
                {
                    Name = "Slot",
                    TypeParams = ["a"],
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Filled", new DamlTypeVar("a")),
                        new DamlVariantConstructor("Empty", null),
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

        var files = new CSharpCodeGenerator(new CodeGenOptions { EnableNullableReferenceTypes = true })
            .Generate(new DarModel { MainPackage = package, Dependencies = [] });

        return EmittedCodeCompilesTestHelpers.EmitToAssembly(files).GetTypes();
    }

    private static Type EmittedType(string name) => EmittedTypes.Value.Single(type => type.Name == name);

    private static IReadOnlyList<long> Longs(params long[] values) => new List<long>(values);

    private static IReadOnlyDictionary<string, long> Dictionary(params (string Key, long Value)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => entry.Value);

    private static object Pair(string label, IReadOnlyList<long> first, IReadOnlyDictionary<string, long> second) =>
        Activator.CreateInstance(
            EmittedType("Pair`2").MakeGenericType(typeof(IReadOnlyList<long>), typeof(IReadOnlyDictionary<string, long>)),
            label,
            first,
            second)!;

    private static object Filled(IReadOnlyList<long> value) =>
        Activator.CreateInstance(EmittedType("Slot`1").GetNestedType("Filled")!.MakeGenericType(typeof(IReadOnlyList<long>)), value)!;

    [Fact]
    public void Record_with_type_parameter_fields_equals_another_value_of_the_same_content()
    {
        var left = Pair("p", Longs(1, 2), Dictionary(("a", 1), ("b", 2)));
        var right = Pair("p", Longs(1, 2), Dictionary(("b", 2), ("a", 1)));

        left.Equals(right).Should().BeTrue();
        right.Equals(left).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void Record_with_type_parameter_fields_differs_when_one_field_differs()
    {
        var sample = Pair("p", Longs(1, 2), Dictionary(("a", 1)));

        sample.Equals(Pair("q", Longs(1, 2), Dictionary(("a", 1)))).Should().BeFalse();
        sample.Equals(Pair("p", Longs(2, 1), Dictionary(("a", 1)))).Should().BeFalse();
        sample.Equals(Pair("p", Longs(1, 2), Dictionary(("a", 2)))).Should().BeFalse();
    }

    [Fact]
    public void Variant_constructor_with_a_type_parameter_payload_equals_another_value_of_the_same_content()
    {
        var left = Filled(Longs(1, 2));
        var right = Filled(Longs(1, 2));

        left.Equals(right).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
        left.Equals(Filled(Longs(1, 3))).Should().BeFalse();
    }
}
