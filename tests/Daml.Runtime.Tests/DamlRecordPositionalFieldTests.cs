// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlRecordPositionalFieldTests
{
    private static DamlField Unlabelled(DamlValue value) => DamlField.Create("", value);

    private static DamlRecord UnlabelledRecord(params DamlValue[] values) =>
        DamlRecord.Create(values.Select(Unlabelled).ToArray());

    [Fact]
    public void GetRequiredField_reads_an_unlabelled_record_by_position()
    {
        var record = UnlabelledRecord(new DamlParty("alice"), new DamlInt64(42));

        record.GetRequiredField("owner", 0).Should().Be(new DamlParty("alice"));
        record.GetRequiredField("count", 1).Should().Be(new DamlInt64(42));
    }

    [Fact]
    public void GetRequiredField_reads_a_nested_unlabelled_record_by_position()
    {
        var record = UnlabelledRecord(new DamlText("outer"), UnlabelledRecord(new DamlInt64(7), new DamlText("inner")));

        var inner = record.GetRequiredField("nested", 1).As<DamlRecord>();

        inner.GetRequiredField("a", 0).Should().Be(new DamlInt64(7));
        inner.GetRequiredField("b", 1).Should().Be(new DamlText("inner"));
    }

    [Fact]
    public void GetRequiredField_reads_a_labelled_record_by_name_whatever_the_position()
    {
        var record = DamlRecord.Create(
            DamlField.Create("count", new DamlInt64(42)),
            DamlField.Create("owner", new DamlParty("alice")));

        record.GetRequiredField("owner", 0).Should().Be(new DamlParty("alice"));
    }

    [Fact]
    public void GetRequiredField_throws_for_a_name_missing_from_a_labelled_record_even_when_the_position_exists()
    {
        var record = DamlRecord.Create(DamlField.Create("owner", new DamlParty("alice")));

        var read = () => record.GetRequiredField("count", 0);

        read.Should().Throw<InvalidOperationException>().WithMessage("Required field 'count' not found in record.");
    }

    [Fact]
    public void GetRequiredField_throws_for_a_name_missing_from_a_partly_labelled_record()
    {
        var record = DamlRecord.Create(
            DamlField.Create("owner", new DamlParty("alice")),
            DamlField.Create("", new DamlInt64(42)));

        var read = () => record.GetRequiredField("count", 1);

        read.Should().Throw<InvalidOperationException>().WithMessage("Required field 'count' not found in record.");
    }

    [Fact]
    public void GetRequiredField_throws_when_an_unlabelled_record_ends_before_the_position()
    {
        var record = UnlabelledRecord(new DamlParty("alice"));

        var read = () => record.GetRequiredField("count", 1);

        read.Should().Throw<InvalidOperationException>().WithMessage("Required field 'count' not found in record.");
    }

    [Fact]
    public void GetOptionalField_reads_a_trailing_none_omitted_from_an_unlabelled_record_as_none()
    {
        var record = UnlabelledRecord(new DamlParty("alice"));

        record.GetOptionalField("note", 1).Should().Be(DamlOptional.None);
    }

    [Fact]
    public void GetOptionalField_reads_a_present_value_of_an_unlabelled_record_by_position()
    {
        var record = UnlabelledRecord(new DamlParty("alice"), DamlOptional.Some(new DamlText("hi")));

        record.GetOptionalField("note", 1).Should().Be(DamlOptional.Some(new DamlText("hi")));
    }

    [Fact]
    public void GetOptionalField_reads_a_name_missing_from_a_labelled_record_as_none()
    {
        var record = DamlRecord.Create(DamlField.Create("owner", new DamlParty("alice")));

        record.GetOptionalField("note", 1).Should().Be(DamlOptional.None);
    }

    [Fact]
    public void GetOptionalChainField_reads_a_trailing_none_omitted_from_an_unlabelled_record_as_none()
    {
        var record = UnlabelledRecord(new DamlParty("alice"));

        record.GetOptionalChainField("note", 1).Should().Be(DamlOptionalChain.None);
    }

    [Fact]
    public void GetTypeParameterField_converts_an_unlabelled_field_by_position()
    {
        var record = UnlabelledRecord(new DamlText("first"), new DamlInt64(9));

        var value = record.GetTypeParameterField("b", 1, v => v.As<DamlInt64>().Value, null);

        value.Should().Be(9);
    }

    [Fact]
    public void GetTypeParameterField_hands_the_absent_reading_to_the_converter_when_an_unlabelled_record_ends_early()
    {
        var record = UnlabelledRecord(new DamlText("first"));

        var value = record.GetTypeParameterField("b", 1, v => v == DamlOptional.None, DamlOptional.None);

        value.Should().BeTrue();
    }

    [Fact]
    public void GetTypeParameterField_throws_when_an_unlabelled_record_ends_early_and_the_field_is_required()
    {
        var record = UnlabelledRecord(new DamlText("first"));

        var read = () => record.GetTypeParameterField("b", 1, v => v, null);

        read.Should().Throw<InvalidOperationException>().WithMessage("Required field 'b' not found in record.");
    }
}
