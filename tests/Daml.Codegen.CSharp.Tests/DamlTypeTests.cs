// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class DamlTypeTests
{
    [Fact]
    public void DamlPrimitiveType_should_identify_primitives()
    {
        // Arrange
        var int64Type = new DamlPrimitiveType(DamlPrimitive.Int64);
        var textType = new DamlPrimitiveType(DamlPrimitive.Text);

        // Assert
        int64Type.Primitive.Should().Be(DamlPrimitive.Int64);
        textType.Primitive.Should().Be(DamlPrimitive.Text);
    }

    [Fact]
    public void DamlTypeApp_should_hash_structurally_identical_values_alike()
    {
        // Arrange
        var built = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.Optional),
            [new DamlPrimitiveType(DamlPrimitive.Text)]);
        var rebuilt = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.Optional),
            [new DamlPrimitiveType(DamlPrimitive.Text)]);

        // Assert
        rebuilt.GetHashCode().Should().Be(built.GetHashCode());
    }

    [Fact]
    public void DamlTypeRef_should_store_type_reference()
    {
        // Arrange
        var typeRef = new DamlTypeRef("pkg123", "Module.Name", "MyType");

        // Assert
        typeRef.PackageId.Should().Be("pkg123");
        typeRef.Module.Should().Be("Module.Name");
        typeRef.Name.Should().Be("MyType");
    }

    private static DamlPrimitiveType Prim(DamlPrimitive primitive) => new(primitive);

    private static DamlTypeApp App(DamlPrimitive constructor, params DamlType[] arguments) =>
        new(Prim(constructor), arguments);

    private static DamlTypeApp Box(DamlType argument) =>
        new(new DamlTypeRef("pkg123", "Module.Name", "Box"), [argument]);

    private static DamlTypeApp NumericScale(int scale) =>
        new(Prim(DamlPrimitive.Numeric), [new DamlTypeVar(scale.ToString(System.Globalization.CultureInfo.InvariantCulture))]);

    public static TheoryData<DamlType, DamlType, bool> TypedNodeEqualityPairs() => new()
    {
        { new DamlListType(Prim(DamlPrimitive.Int64)), new DamlListType(Prim(DamlPrimitive.Int64)), true },
        { new DamlListType(Prim(DamlPrimitive.Int64)), new DamlListType(Prim(DamlPrimitive.Text)), false },
        {
            new DamlListType(new DamlOptionalType(Prim(DamlPrimitive.Int64))),
            new DamlListType(new DamlOptionalType(Prim(DamlPrimitive.Int64))),
            true
        },
        {
            new DamlListType(new DamlOptionalType(Prim(DamlPrimitive.Int64))),
            new DamlListType(new DamlOptionalType(Prim(DamlPrimitive.Text))),
            false
        },
        {
            new DamlListType(new DamlOptionalType(Prim(DamlPrimitive.Int64))),
            new DamlOptionalType(new DamlListType(Prim(DamlPrimitive.Int64))),
            false
        },
        {
            new DamlGenMapType(Prim(DamlPrimitive.Party), Prim(DamlPrimitive.Int64)),
            new DamlGenMapType(Prim(DamlPrimitive.Party), Prim(DamlPrimitive.Int64)),
            true
        },
        {
            new DamlGenMapType(Prim(DamlPrimitive.Party), Prim(DamlPrimitive.Int64)),
            new DamlGenMapType(Prim(DamlPrimitive.Int64), Prim(DamlPrimitive.Party)),
            false
        },
        {
            new DamlListType(Prim(DamlPrimitive.Int64)),
            App(DamlPrimitive.List, Prim(DamlPrimitive.Int64)),
            false
        },
        {
            new DamlContractIdType(Prim(DamlPrimitive.Text)),
            new DamlContractIdType(Prim(DamlPrimitive.Text)),
            true
        },
        {
            new DamlTextMapType(Prim(DamlPrimitive.Int64)),
            new DamlTextMapType(Prim(DamlPrimitive.Int64)),
            true
        },
    };

    [Theory]
    [MemberData(nameof(TypedNodeEqualityPairs))]
    public void DamlType_typed_nodes_compare_by_structure_not_by_identity(DamlType left, DamlType right, bool expectEqual)
    {
        left.Equals(right).Should().Be(expectEqual,
            "the typed nodes compare their DamlType parameters structurally, so equal type trees "
            + "built independently compare equal and differing structure anywhere in the tree "
            + "compares unequal");
        if (expectEqual)
        {
            right.GetHashCode().Should().Be(left.GetHashCode(),
                "structurally equal trees must hash alike to stay findable in hash-based collections");
        }
    }

    public static TheoryData<DamlTypeApp, DamlTypeApp, bool> ResidualApplicationEqualityPairs() => new()
    {
        { Box(Prim(DamlPrimitive.Text)), Box(Prim(DamlPrimitive.Text)), true },
        { NumericScale(10), NumericScale(10), true },
        { Box(Prim(DamlPrimitive.Text)), Box(Prim(DamlPrimitive.Int64)), false },
        { NumericScale(10), NumericScale(20), false },
        { App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)), App(DamlPrimitive.List, Prim(DamlPrimitive.Text)), false },
    };

    [Theory]
    [MemberData(nameof(ResidualApplicationEqualityPairs))]
    public void DamlTypeApp_residual_generic_applications_keep_element_wise_equality(DamlTypeApp left, DamlTypeApp right, bool expectEqual)
    {
        left.Equals(right).Should().Be(expectEqual,
            "the hand-rolled element-wise equality on DamlTypeApp stays for the residual generic "
            + "applications it was written for — user-defined constructors and the Numeric scale "
            + "pun — so two independently built but identical trees compare equal");
        if (expectEqual)
        {
            right.GetHashCode().Should().Be(left.GetHashCode(),
                "structurally equal applications must hash alike to stay findable in hash-based collections");
        }
    }
}

public class DamlDataTypeTests
{
    [Fact]
    public void DamlRecordDefinition_should_store_fields()
    {
        // Arrange
        var fields = new[]
        {
            new DamlFieldDefinition("name", new DamlPrimitiveType(DamlPrimitive.Text)),
            new DamlFieldDefinition("age", new DamlPrimitiveType(DamlPrimitive.Int64))
        };
        var record = new DamlRecordDefinition(fields);

        // Assert
        record.Fields.Should().HaveCount(2);
        record.Fields[0].Name.Should().Be("name");
        record.Fields[1].Name.Should().Be("age");
    }

    [Fact]
    public void DamlVariantDefinition_should_store_constructors()
    {
        // Arrange
        var constructors = new[]
        {
            new DamlVariantConstructor("None", null),
            new DamlVariantConstructor("Some", new DamlPrimitiveType(DamlPrimitive.Text))
        };
        var variant = new DamlVariantDefinition(constructors);

        // Assert
        variant.Constructors.Should().HaveCount(2);
        variant.Constructors[0].Name.Should().Be("None");
        variant.Constructors[0].ArgumentType.Should().BeNull();
        variant.Constructors[1].Name.Should().Be("Some");
        variant.Constructors[1].ArgumentType.Should().NotBeNull();
    }

    [Fact]
    public void DamlEnumDefinition_should_store_constructors()
    {
        // Arrange
        var enumDef = new DamlEnumDefinition(["Red", "Green", "Blue"]);

        // Assert
        enumDef.Constructors.Should().BeEquivalentTo(["Red", "Green", "Blue"]);
    }
}
