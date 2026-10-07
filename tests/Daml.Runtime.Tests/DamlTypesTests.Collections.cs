// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;
using Daml.Runtime.Stdlib;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public partial class DamlTypesTests
{
    [Fact]
    public void DamlOptional_None_should_have_no_value()
    {
        // Act
        var optional = DamlOptional.None;

        // Assert
        optional.HasValue.Should().BeFalse();
        optional.Value.Should().BeNull();
    }

    [Fact]
    public void DamlOptional_Some_should_have_value()
    {
        // Arrange
        var innerValue = new DamlText("test");

        // Act
        var optional = DamlOptional.Some(innerValue);

        // Assert
        optional.HasValue.Should().BeTrue();
        optional.Value.Should().Be(innerValue);
    }

    [Fact]
    public void DamlOptional_GetValueOrDefault_should_return_value_when_present()
    {
        // Arrange
        var optional = DamlOptional.Some(new DamlInt64(42));

        // Act
        var result = optional.GetValueOrDefault<DamlInt64>();

        // Assert
        result.Should().NotBeNull();
        result!.Value.Should().Be(42);
    }

    [Fact]
    public void DamlOptional_GetValueOrDefault_should_return_null_when_None()
    {
        // Arrange
        var optional = DamlOptional.None;

        // Act
        var result = optional.GetValueOrDefault<DamlInt64>();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void DamlOptional_GetValueOrThrow_should_return_value_when_present()
    {
        // Arrange
        var innerValue = new DamlText("present");
        var optional = DamlOptional.Some(innerValue);

        // Act
        var result = optional.GetValueOrThrow();

        // Assert
        result.Should().Be(innerValue);
    }

    [Fact]
    public void DamlOptional_GetValueOrThrow_should_throw_when_None()
    {
        // Arrange
        var optional = DamlOptional.None;

        // Act
        var act = () => optional.GetValueOrThrow();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Optional value is None.");
    }

    [Fact]
    public void DamlList_should_contain_values()
    {
        // Arrange
        var values = new DamlValue[]
        {
            new DamlInt64(1),
            new DamlInt64(2),
            new DamlInt64(3)
        };

        // Act
        var list = DamlList.Create(values);

        // Assert
        list.Count.Should().Be(3);
        list[0].As<DamlInt64>().Value.Should().Be(1);
        list[1].As<DamlInt64>().Value.Should().Be(2);
        list[2].As<DamlInt64>().Value.Should().Be(3);
    }

    [Fact]
    public void DamlList_AsEnumerable_should_cast_correctly()
    {
        // Arrange
        var list = DamlList.Create(
            new DamlInt64(1),
            new DamlInt64(2),
            new DamlInt64(3));

        // Act
        var result = list.AsEnumerable<DamlInt64>().ToList();

        // Assert
        result.Should().HaveCount(3);
        result[0].Value.Should().Be(1);
        result[1].Value.Should().Be(2);
        result[2].Value.Should().Be(3);
    }

    [Fact]
    public void DamlList_Create_should_work_with_enumerable()
    {
        // Arrange
        var values = new List<DamlValue>
        {
            new DamlText("a"),
            new DamlText("b")
        };

        // Act
        var list = DamlList.Create(values);

        // Assert
        list.Count.Should().Be(2);
        list[0].As<DamlText>().Value.Should().Be("a");
        list[1].As<DamlText>().Value.Should().Be("b");
    }

    [Fact]
    public void DamlList_constructor_should_defensively_copy_values()
    {
        var values = new List<DamlValue> { new DamlInt64(1) };

        var list = new DamlList(values);
        values.Add(new DamlInt64(2));

        list.Values.Should().ContainSingle();
    }

    [Fact]
    public void DamlTextMap_should_store_and_retrieve_values()
    {
        // Arrange & Act
        var map = DamlTextMap.Create(
            ("key1", new DamlInt64(100)),
            ("key2", new DamlText("value2")));

        // Assert
        map.Count.Should().Be(2);
        map["key1"].As<DamlInt64>().Value.Should().Be(100);
        map["key2"].As<DamlText>().Value.Should().Be("value2");
    }

    [Fact]
    public void DamlTextMap_TryGetValue_should_return_correct_result()
    {
        // Arrange
        var map = DamlTextMap.Create(("existing", new DamlInt64(42)));

        // Act & Assert
        map.TryGetValue("existing", out var existingValue).Should().BeTrue();
        existingValue!.As<DamlInt64>().Value.Should().Be(42);

        map.TryGetValue("nonexistent", out var missingValue).Should().BeFalse();
        missingValue.Should().BeNull();
    }

    [Fact]
    public void DamlTextMap_constructor_should_defensively_copy_values()
    {
        var values = new Dictionary<string, DamlValue> { ["a"] = new DamlInt64(1) };

        var map = new DamlTextMap(values);
        values["b"] = new DamlInt64(2);

        map.Values.Should().ContainSingle();
        map.Values.Should().ContainKey("a");
    }

    [Fact]
    public void DamlGenMap_should_store_key_value_pairs()
    {
        // Arrange & Act
        var map = DamlGenMap.Create(
            (new DamlInt64(1), new DamlText("one")),
            (new DamlInt64(2), new DamlText("two")));

        // Assert
        map.Count.Should().Be(2);
        map.Entries[0].Key.As<DamlInt64>().Value.Should().Be(1);
        map.Entries[0].Value.As<DamlText>().Value.Should().Be("one");
        map.Entries[1].Key.As<DamlInt64>().Value.Should().Be(2);
        map.Entries[1].Value.As<DamlText>().Value.Should().Be("two");
    }

    [Fact]
    public void DamlGenMap_Create_should_throw_on_structurally_equal_duplicate_keys()
    {
        var act = () => DamlGenMap.Create(
            (new DamlText("k"), new DamlInt64(1)),
            (new DamlText("k"), new DamlInt64(2)));

        act.Should().Throw<ArgumentException>().WithMessage("*duplicate*",
            "DamlTextMap.Create rejects duplicate keys, and GenMap keys are unique on the ledger too");
    }

    [Fact]
    public void DamlGenMap_Create_should_accept_distinct_keys_of_equal_shape()
    {
        var act = () => DamlGenMap.Create(
            (new DamlText("k1"), new DamlInt64(1)),
            (new DamlText("k2"), new DamlInt64(2)));

        act.Should().NotThrow();
    }

    [Fact]
    public void DamlGenMap_constructor_should_defensively_copy_entries()
    {
        var entries = new List<(DamlValue Key, DamlValue Value)>
        {
            (new DamlText("k1"), new DamlInt64(1))
        };

        var map = new DamlGenMap(entries);
        entries.Add((new DamlText("k2"), new DamlInt64(2)));

        map.Entries.Should().ContainSingle();
    }

    [Fact]
    public void DamlGenMap_constructor_should_reject_duplicate_keys()
    {
        var entries = new List<(DamlValue Key, DamlValue Value)>
        {
            (new DamlText("k"), new DamlInt64(1)),
            (new DamlText("k"), new DamlInt64(2))
        };

        var act = () => new DamlGenMap(entries);

        act.Should().Throw<ArgumentException>().WithMessage("*duplicate*",
            "the constructor enforces the same invariant as Create");
    }

    [Fact]
    public void DamlRecord_should_allow_field_access()
    {
        // Arrange
        var record = DamlRecord.Create(
            DamlField.Create("name", new DamlText("Alice")),
            DamlField.Create("amount", new DamlNumeric(100.50m))
        );

        // Act
        var name = record.GetField("name");
        var amount = record.GetField("amount");

        // Assert
        name.Should().NotBeNull();
        name!.As<DamlText>().Value.Should().Be("Alice");
        amount.Should().NotBeNull();
        amount!.As<DamlNumeric>().Value.Should().Be(100.50m);
    }

    [Fact]
    public void DamlRecord_GetRequiredField_should_return_value_when_exists()
    {
        // Arrange
        var record = DamlRecord.Create(
            DamlField.Create("name", new DamlText("Alice")));

        // Act
        var result = record.GetRequiredField("name");

        // Assert
        result.As<DamlText>().Value.Should().Be("Alice");
    }

    [Fact]
    public void DamlRecord_GetRequiredField_should_throw_when_missing()
    {
        // Arrange
        var record = DamlRecord.Create(
            DamlField.Create("name", new DamlText("Alice")));

        // Act
        var act = () => record.GetRequiredField("missing");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Required field 'missing' not found in record.");
    }

    [Fact]
    public void DamlRecord_GetOptionalField_should_return_the_present_value()
    {
        var record = DamlRecord.Create(DamlField.Create("remark", DamlOptional.Some(new DamlText("noted"))));

        record.GetOptionalField("remark").Should().Be(DamlOptional.Some(new DamlText("noted")));
    }

    [Fact]
    public void DamlRecord_GetOptionalField_should_read_an_omitted_field_as_None()
    {
        var record = DamlRecord.Create(DamlField.Create("text", new DamlText("inner")));

        record.GetOptionalField("remark").Should().Be(new DamlOptional(null));
    }

    [Fact]
    public void DamlRecord_GetOptionalChainField_should_return_the_present_value()
    {
        var record = DamlRecord.Create(DamlField.Create("tailMaybe", DamlOptionalChain.Some(DamlOptionalChain.None)));

        record.GetOptionalChainField("tailMaybe").Should().Be(DamlOptionalChain.Some(DamlOptionalChain.None));
    }

    [Fact]
    public void DamlRecord_GetOptionalChainField_should_read_an_omitted_field_as_a_chain_None()
    {
        var record = DamlRecord.Create(DamlField.Create("text", new DamlText("inner")));

        record.GetOptionalChainField("tailMaybe").Should().Be(new DamlOptionalChain(null));
    }

    [Fact]
    public void DamlRecord_GetTypeParameterField_should_convert_the_present_value()
    {
        var record = DamlRecord.Create(DamlField.Create("item", new DamlText("boxed")));

        record.GetTypeParameterField("item", value => value.As<DamlText>().Value, null).Should().Be("boxed");
    }

    [Fact]
    public void DamlRecord_GetTypeParameterField_should_convert_the_present_value_when_the_instantiation_is_an_Optional()
    {
        var record = DamlRecord.Create(DamlField.Create("item", DamlOptional.Some(new DamlText("boxed"))));

        var item = record.GetTypeParameterField(
            "item", value => value.As<DamlOptional>().Value!.As<DamlText>().Value, DamlOptional.None);

        item.Should().Be("boxed");
    }

    [Fact]
    public void DamlRecord_GetTypeParameterField_should_hand_an_omitted_field_to_the_converter_as_the_given_None_at_an_Optional_instantiation()
    {
        var record = DamlRecord.Create();

        var item = record.GetTypeParameterField("item", value => value.As<DamlOptional>().Value, DamlOptional.None);

        item.Should().BeNull();
    }

    [Fact]
    public void DamlRecord_GetTypeParameterField_should_read_an_omitted_field_as_None_at_a_nested_Optional_instantiation()
    {
        var record = DamlRecord.Create();

        var item = record.GetTypeParameterField(
            "item",
            value => Optional<Optional<string>>.FromChainValue(
                value, inner => Optional<string>.FromChainValue(inner, text => text.As<DamlText>().Value)),
            DamlOptional.None);

        item.Should().Be(new Optional<Optional<string>>.None());
    }

    [Fact]
    public void DamlRecord_GetTypeParameterField_should_report_an_omitted_field_missing_at_a_required_instantiation_without_calling_the_converter()
    {
        var record = DamlRecord.Create();

        var act = () => record.GetTypeParameterField<string>(
            "item", _ => throw new Xunit.Sdk.XunitException("the converter must not be called"), null);

        act.Should().Throw<InvalidOperationException>().WithMessage("Required field 'item' not found in record.");
    }

    [Fact]
    public void DamlRecord_GetTypeParameterField_should_surface_the_InvalidCastException_a_converter_throws_at_an_Optional_instantiation()
    {
        var record = DamlRecord.Create();

        var act = () => record.GetTypeParameterField<string>(
            "item", _ => throw new InvalidCastException("converter bug"), DamlOptional.None);

        act.Should().Throw<InvalidCastException>().WithMessage("converter bug");
    }

    [Fact]
    public void DamlRecord_Create_should_store_record_id()
    {
        // Arrange
        var recordId = new Identifier("pkg123", "MyModule", "MyRecord");

        // Act
        var record = DamlRecord.Create(recordId,
            DamlField.Create("field1", new DamlInt64(10)));

        // Assert
        record.RecordId.Should().Be(recordId);
        record.Fields.Should().HaveCount(1);
    }

    [Fact]
    public void DamlVariant_should_store_constructor_and_value()
    {
        // Arrange
        var value = new DamlText("error message");

        // Act
        var variant = DamlVariant.Create("Error", value);

        // Assert
        variant.Constructor.Should().Be("Error");
        variant.Is("Error").Should().BeTrue();
        variant.Is("Success").Should().BeFalse();
        variant.GetValue<DamlText>().Value.Should().Be("error message");
    }

    [Fact]
    public void DamlVariant_Create_should_store_variant_id()
    {
        // Arrange
        var variantId = new Identifier("pkg123", "Either", "Either");

        // Act
        var variant = DamlVariant.Create(variantId, "Left", new DamlText("error"));

        // Assert
        variant.VariantId.Should().Be(variantId);
        variant.Constructor.Should().Be("Left");
        variant.Value.As<DamlText>().Value.Should().Be("error");
    }

    [Fact]
    public void DamlEnum_should_store_constructor()
    {
        // Arrange & Act
        var enumValue = DamlEnum.Create("Red");

        // Assert
        enumValue.Constructor.Should().Be("Red");
        enumValue.EnumId.Should().BeNull();
        enumValue.Is("Red").Should().BeTrue();
        enumValue.Is("Blue").Should().BeFalse();
    }

    [Fact]
    public void DamlEnum_Create_should_store_enum_id()
    {
        // Arrange
        var enumId = new Identifier("pkg123", "Colors", "Color");

        // Act
        var enumValue = DamlEnum.Create(enumId, "Green");

        // Assert
        enumValue.EnumId.Should().Be(enumId);
        enumValue.Constructor.Should().Be("Green");
    }

    [Fact]
    public void DamlValue_As_should_throw_on_invalid_cast()
    {
        // Arrange
        DamlValue value = new DamlInt64(42);

        // Act
        var act = () => value.As<DamlText>();

        // Assert
        act.Should().Throw<InvalidCastException>()
            .WithMessage("Cannot cast DamlInt64 to DamlText");
    }

    [Fact]
    public void DamlValue_TryGet_should_return_true_for_valid_cast()
    {
        // Arrange
        DamlValue value = new DamlInt64(42);

        // Act
        var success = value.TryGet<DamlInt64>(out var result);

        // Assert
        success.Should().BeTrue();
        result!.Value.Should().Be(42);
    }

    [Fact]
    public void DamlValue_TryGet_should_return_false_for_invalid_cast()
    {
        // Arrange
        DamlValue value = new DamlInt64(42);

        // Act
        var success = value.TryGet<DamlText>(out var result);

        // Assert
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void Identifier_Parse_should_parse_three_part_format()
    {
        // Arrange
        var input = "abc123:Module.Name:Entity";

        // Act
        var id = Identifier.Parse(input);

        // Assert
        id.PackageId.Should().Be("abc123");
        id.ModuleName.Should().Be("Module.Name");
        id.EntityName.Should().Be("Entity");
        id.FullyQualifiedName.Should().Be("Module.Name:Entity");
    }

    [Fact]
    public void Identifier_Parse_should_parse_two_part_format()
    {
        // Arrange
        var input = "Module.Name:Entity";

        // Act
        var id = Identifier.Parse(input);

        // Assert
        id.PackageId.Should().BeEmpty();
        id.ModuleName.Should().Be("Module.Name");
        id.EntityName.Should().Be("Entity");
    }

    [Fact]
    public void Identifier_Parse_should_throw_on_invalid_format()
    {
        // Arrange
        var input = "InvalidFormat";

        // Act
        var act = () => Identifier.Parse(input);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage("Invalid identifier format: InvalidFormat");
    }

    [Fact]
    public void Identifier_ToString_should_return_full_format()
    {
        // Arrange
        var id = new Identifier("pkg123", "Module.Name", "Entity");

        // Act
        var result = id.ToString();

        // Assert
        result.Should().Be("pkg123:Module.Name:Entity");
    }
}
