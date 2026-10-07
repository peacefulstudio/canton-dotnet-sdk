// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestCommandPayloadWriterTests : IDisposable
{
    private static readonly Identifier VariantId = new("pkg", "Module", "Shape");
    private static readonly Identifier EnumId = new("pkg", "Module", "Suit");

    private readonly SubmissionRecorder _recorder = new();

    public void Dispose() => _recorder.Dispose();

    public static TheoryData<string, string> NestedOptionalsAsOneArrayLevelPerOptional => new()
    {
        { "Some None", "[[]]" },
        { "Some (Some x)", """[["x"]]""" },
    };

    [Theory]
    [MemberData(nameof(NestedOptionalsAsOneArrayLevelPerOptional))]
    public async Task SubmitAndWaitAsync_writes_a_flat_nested_Optional_as_one_array_level_per_Optional(
        string state, string expectedChoiceArgument)
    {
        var flatNestedOptional = state == "Some None"
            ? new DamlOptional(new DamlOptional(null))
            : new DamlOptional(new DamlOptional(new DamlText("x")));

        var submitted = await _recorder.ChoiceArgumentOnTheWire(flatNestedOptional);

        submitted.Should().Be(expectedChoiceArgument);
    }

    [Fact]
    public async Task SubmitAndWaitAsync_writes_an_empty_TextMap_key()
    {
        var textMap = new DamlTextMap(new Dictionary<string, DamlValue> { [string.Empty] = new DamlInt64(1) });

        var submitted = await _recorder.ChoiceArgumentOnTheWire(textMap);

        submitted.Should().Be("""{"":"1"}""");
    }

    [Fact]
    public async Task SubmitAndWaitAsync_writes_an_empty_TextMap_key_inside_createArguments()
    {
        var textMap = new DamlTextMap(new Dictionary<string, DamlValue> { [string.Empty] = new DamlInt64(1) });

        var submitted = await _recorder.CreateArgumentsOnTheWire(DamlRecord.Create(DamlField.Create("m", textMap)));

        submitted.Should().Be("""{"m":{"":"1"}}""");
    }

    [Fact]
    public void SubmitAndWaitAsync_rejects_a_duplicate_field_label_with_a_JsonException_before_any_request()
    {
        var record = DamlRecord.Create(
            DamlField.Create("a", new DamlText("1")),
            DamlField.Create("a", new DamlText("2")));

        Action submit = () => _recorder.SubmitCreate(record);

        submit.Should().Throw<JsonException>()
            .WithMessage("Duplicate field label 'a' in Daml record; refusing to serialize last-wins");
        _recorder.Transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public void SubmitAndWaitAsync_rejects_an_empty_field_label_with_a_JsonException_before_any_request()
    {
        var record = DamlRecord.Create(DamlField.Create(string.Empty, new DamlText("1")));

        Action submit = () => _recorder.SubmitCreate(record);

        submit.Should().Throw<JsonException>().WithMessage("A Daml record field label must not be empty");
        _recorder.Transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public void SubmitAndWaitAsync_rejects_an_empty_variant_constructor_with_a_JsonException_before_any_request()
    {
        var variant = DamlVariant.Create(VariantId, string.Empty, DamlUnit.Instance);

        Action submit = () => _recorder.SubmitExercise(variant);

        submit.Should().Throw<JsonException>().WithMessage("A Daml variant constructor must not be empty");
        _recorder.Transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public void SubmitAndWaitAsync_rejects_an_empty_enum_constructor_with_a_JsonException_before_any_request()
    {
        var enumValue = new DamlEnum(EnumId, string.Empty);

        Action submit = () => _recorder.SubmitExercise(enumValue);

        submit.Should().Throw<JsonException>().WithMessage("A Daml enum constructor must not be empty");
        _recorder.Transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitAndWaitAsync_accepts_a_choice_argument_nested_64_levels_deep()
    {
        var submitted = await _recorder.ChoiceArgumentOnTheWire(NestedLists(levels: 64));

        submitted.Should().Be(new string('[', 64) + new string(']', 64));
    }

    [Fact]
    public async Task SubmitAndWaitAsync_accepts_createArguments_nested_64_levels_deep()
    {
        var record = DamlRecord.Create(DamlField.Create("deep", NestedLists(levels: 63)));

        var submitted = await _recorder.CreateArgumentsOnTheWire(record);

        submitted.Should().Be("""{"deep":""" + new string('[', 63) + new string(']', 63) + "}");
    }

    [Fact]
    public void SubmitAndWaitAsync_rejects_a_choice_argument_nested_65_levels_deep_with_a_JsonException_before_any_request()
    {
        Action submit = () => _recorder.SubmitExercise(NestedLists(levels: 65));

        submit.Should().Throw<JsonException>().WithMessage("*maximum configured depth of 64*");
        _recorder.Transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public void SubmitAndWaitAsync_rejects_createArguments_nested_65_levels_deep_with_a_JsonException_before_any_request()
    {
        var record = DamlRecord.Create(DamlField.Create("deep", NestedLists(levels: 64)));

        Action submit = () => _recorder.SubmitCreate(record);

        submit.Should().Throw<JsonException>().WithMessage("*maximum configured depth of 64*");
        _recorder.Transport.Requests.Should().BeEmpty();
    }

    private static DamlValue NestedLists(int levels)
    {
        DamlValue nested = new DamlList([]);
        for (var level = 1; level < levels; level++)
        {
            nested = new DamlList([nested]);
        }

        return nested;
    }
}
