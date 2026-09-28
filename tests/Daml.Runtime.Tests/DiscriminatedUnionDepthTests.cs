// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;
using Daml.Runtime.Stdlib;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins <see cref="Daml.Runtime.Serialization.DiscriminatedUnionJson"/>'s depth behaviour on both
/// sides of the wire — see the type's remarks for why <see cref="DiscriminatedUnionJson.Write{TUnion}"/>
/// needs its own re-entrant guard while <see cref="DiscriminatedUnionJson.Read{TUnion}"/> does not.
/// </summary>
public class DiscriminatedUnionDepthTests
{
    private const int NestingLevelsPastDefaultMaxDepth = 70;
    private const int ExplicitMaxDepth = 8;

    private static readonly JsonSerializerOptions ExplicitSmallMaxDepthOptions = new() { MaxDepth = ExplicitMaxDepth };
    private static readonly JsonSerializerOptions MaxDepthAboveTheSafetyCeilingOptions = new() { MaxDepth = 40 };
    private static readonly JsonSerializerOptions MaxDepthWellAboveTheSafetyCeilingOptions = new() { MaxDepth = 200 };

    private static (object Value, Type DeclaredType) BuildNestedOptional(int levels)
    {
        object value = new Optional<int>.Some(1);
        var declaredType = typeof(Optional<int>);
        var openSome = typeof(Optional<>).GetNestedType("Some")!;
        for (var i = 0; i < levels; i++)
        {
            var closedSome = openSome.MakeGenericType(declaredType);
            value = closedSome.GetConstructors()[0].Invoke(new[] { value });
            declaredType = typeof(Optional<>).MakeGenericType(declaredType);
        }

        return (value, declaredType);
    }

    private static string BuildNestedOptionalJson(int levels)
    {
        var json = new StringBuilder();
        for (var i = 0; i < levels; i++)
        {
            json.Append("{\"$case\":\"Some\",\"Value\":");
        }

        json.Append('1');
        for (var i = 0; i < levels; i++)
        {
            json.Append('}');
        }

        return json.ToString();
    }

    private sealed record Layer<T>(T Next);

    private static (object Value, Type DeclaredType) BuildOptionalHopsSeparatedByPlainLayers(int hops, int layersPerHop)
    {
        object value = new Optional<int>.Some(1);
        var declaredType = typeof(Optional<int>);
        var openSome = typeof(Optional<>).GetNestedType("Some")!;

        for (var hop = 0; hop < hops; hop++)
        {
            for (var layer = 0; layer < layersPerHop; layer++)
            {
                var layerType = typeof(Layer<>).MakeGenericType(declaredType);
                value = layerType.GetConstructors()[0].Invoke(new[] { value });
                declaredType = layerType;
            }

            var closedSome = openSome.MakeGenericType(declaredType);
            value = closedSome.GetConstructors()[0].Invoke(new[] { value });
            declaredType = typeof(Optional<>).MakeGenericType(declaredType);
        }

        return (value, declaredType);
    }

    [Fact]
    public void Optional_write_counts_ordinary_layers_between_nested_unions_toward_MaxDepth()
    {
        var (value, declaredType) = BuildOptionalHopsSeparatedByPlainLayers(hops: 2, layersPerHop: 3);

        var act = () => JsonSerializer.Serialize(value, declaredType, ExplicitSmallMaxDepthOptions);

        act.Should().Throw<JsonException>().WithMessage("*exceeded the maximum union nesting depth of 8*", because:
            "three ordinary Layer<T> records written between each pair of unions still consume real "
            + "call-stack depth through System.Text.Json's own recursive object writer even though "
            + "none of those hops is itself a re-entrant Write<TUnion> call a hop-only counter could "
            + "see; with only 3 union hops total the old counter stayed at 3, comfortably under an "
            + "explicit MaxDepth of 8, while the real cumulative nesting depth had already reached 8, "
            + "so a caller relying on MaxDepth to bound worst-case stack usage was not actually "
            + "protected until every level — union or not — counted toward the guard");
    }

    [Fact]
    public void Optional_write_refuses_to_exceed_its_hardcoded_safety_ceiling_well_short_of_the_documented_default_MaxDepth()
    {
        var (value, declaredType) = BuildNestedOptional(NestingLevelsPastDefaultMaxDepth);

        var act = () => JsonSerializer.Serialize(value, declaredType);

        act.Should().Throw<JsonException>().WithMessage("*exceeded the maximum union nesting depth of 20*", because:
            "a macos-amd64 CI run of this exact path (daml-codegen-csharp workflow run 35363419884, "
            + "job 105660005803) crashed the test host with an uncaught native stack overflow before "
            + "the counted depth ever reached System.Text.Json's own documented MaxDepth default of "
            + "64, so Write now also enforces a fixed, platform-independent safety ceiling of 20 - "
            + "chosen with real margin below 64 - well short of where that crash was observed");
    }

    [Fact]
    public void Optional_write_enforces_an_explicit_MaxDepth_smaller_than_its_own_default()
    {
        var (value, declaredType) = BuildNestedOptional(ExplicitMaxDepth + 2);

        var act = () => JsonSerializer.Serialize(value, declaredType, ExplicitSmallMaxDepthOptions);

        act.Should().Throw<JsonException>().WithMessage("*exceeded the maximum union nesting depth of 8*", because:
            "Write must honour a caller-supplied JsonSerializerOptions.MaxDepth exactly as it honours "
            + "System.Text.Json's own default, not only the default itself, whenever that configured "
            + "value is still below the hardcoded safety ceiling");
    }

    [Fact]
    public void Optional_write_caps_at_its_hardcoded_safety_ceiling_even_when_the_caller_configures_a_MaxDepth_between_the_ceiling_and_the_documented_default()
    {
        var (value, declaredType) = BuildNestedOptional(30);

        var act = () => JsonSerializer.Serialize(value, declaredType, MaxDepthAboveTheSafetyCeilingOptions);

        act.Should().Throw<JsonException>().WithMessage("*exceeded the maximum union nesting depth of 20*", because:
            "MaxDepthAboveTheSafetyCeilingOptions configures MaxDepth = 40, above the hardcoded safety "
            + "ceiling of 20 but below the documented default of 64; the depth actually enforced is "
            + "Math.Min(configuredMaxDepth, 20), so this still refuses at 20, not 40 or 64 - the real "
            + "per-level native stack cost that motivates the ceiling does not shrink just because the "
            + "caller configured a larger MaxDepth");
    }

    [Fact]
    public void Optional_write_still_caps_at_its_hardcoded_safety_ceiling_even_when_the_caller_configures_a_much_larger_MaxDepth()
    {
        var (value, declaredType) = BuildNestedOptional(100);

        var act = () => JsonSerializer.Serialize(value, declaredType, MaxDepthWellAboveTheSafetyCeilingOptions);

        act.Should().Throw<JsonException>().WithMessage("*exceeded the maximum union nesting depth of 20*", because:
            "a caller who raises JsonSerializerOptions.MaxDepth no longer opts into deeper recursion "
            + "on this specific write path once the configured value is above the hardcoded safety "
            + "ceiling of 20: unlike System.Text.Json's own MaxDepth, this ceiling exists because of a "
            + "proven, platform-specific native stack-overflow failure mode that a larger "
            + "counted-depth budget does not fix");
    }

    [Fact]
    public void Optional_read_relies_on_System_Text_Json_own_reader_depth_guard_past_the_default_MaxDepth()
    {
        var (_, declaredType) = BuildNestedOptional(NestingLevelsPastDefaultMaxDepth - 1);
        var json = BuildNestedOptionalJson(NestingLevelsPastDefaultMaxDepth);

        var act = () => JsonSerializer.Deserialize(json, declaredType);

        act.Should().Throw<JsonException>().WithMessage("*maximum configured depth of 64*", because:
            "DiscriminatedUnionJson.Read has no depth guard of its own — JsonDocument.ParseValue(ref "
            + "reader) continues the ambient Utf8JsonReader's own token-depth tracking, so System.Text."
            + "Json's own reader depth guard (threaded from JsonSerializerOptions.MaxDepth, default 64) "
            + "already throws before any of this type's code runs; the JSON text here is built "
            + "directly rather than through DiscriminatedUnionJson.Write, since Write's own hardcoded "
            + "safety ceiling of 20 would otherwise refuse to produce JSON nested this deep in the "
            + "first place");
    }

    [Fact]
    public void Optional_read_enforces_an_explicit_MaxDepth_smaller_than_the_default_via_System_Text_Json_itself()
    {
        var (value, declaredType) = BuildNestedOptional(ExplicitMaxDepth + 2);
        var json = JsonSerializer.Serialize(value, declaredType);

        var act = () => JsonSerializer.Deserialize(json, declaredType, ExplicitSmallMaxDepthOptions);

        act.Should().Throw<JsonException>().WithMessage("*maximum configured depth of 8*", because:
            "System.Text.Json's reader depth guard must honour an explicit MaxDepth exactly as it "
            + "would for any other type, with no help needed from DiscriminatedUnionJson");
    }
}
