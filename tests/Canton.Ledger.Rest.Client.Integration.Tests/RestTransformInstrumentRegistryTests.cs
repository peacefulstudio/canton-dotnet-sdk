// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// Holds every adaptation transform <see cref="RestRefitSettings"/> registers to the pairing
/// convention that makes its retirement instrument a query rather than an inference: a LocalNet test
/// declares the transform it retires with <c>[Trait("Retires", nameof(TheTransform))]</c>. A transform
/// with no such test must appear in <see cref="ExemptTransforms"/> with the reason a live instrument is
/// impossible, so a missing instrument is a recorded decision and never silence.
/// <para>
/// The registered set is read out of the settings themselves — the converters, the type-info modifiers
/// and the URL parameter formatter — so a transform added to the registry without an instrument fails
/// here without anyone remembering to list it.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public class RestTransformInstrumentRegistryTests
{
    private const string RetiresTrait = "Retires";

    private static readonly FrozenDictionary<string, string> ExemptTransforms =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
        }.ToFrozenDictionary(StringComparer.Ordinal);

    [Fact]
    public void RegisteredTransforms_are_the_eight_the_settings_install()
    {
        RegisteredTransformNames().Should().BeEquivalentTo(
            [
                "WireIdentifierJsonConverter",
                "WireValueJsonConverter",
                "WireRecordJsonConverter",
                "DeduplicationPeriodJsonConverter",
                "WrappedOneOfJsonConverterFactory",
                "WireInt64Sites",
                "WireDurationSites",
                "JsonUrlParameterFormatter",
            ],
            "the enumeration is what every other assertion here rests on, so a registration the reader "
            + "stops finding would otherwise empty the registry and leave them trivially satisfied");
    }

    [Fact]
    public void RegisteredTransforms_each_have_a_LocalNet_instrument_or_a_stated_exemption()
    {
        var instrumented = InstrumentedTransformNames();

        var unaccountedFor = RegisteredTransformNames()
            .Where(transform => !instrumented.Contains(transform) && !ExemptTransforms.ContainsKey(transform))
            .ToList();

        unaccountedFor.Should().BeEmpty(
            "a transform that is retired by observing the participant needs a LocalNet test carrying "
            + $"[Trait(\"{RetiresTrait}\", nameof(<transform>))]; where a live instrument is impossible, "
            + "add the transform to ExemptTransforms with the reason");
    }

    [Fact]
    public void ExemptTransforms_name_only_registered_transforms_that_have_no_instrument()
    {
        var registered = RegisteredTransformNames();
        var instrumented = InstrumentedTransformNames();

        ExemptTransforms.Keys.Should().OnlyContain(
            transform => registered.Contains(transform) && !instrumented.Contains(transform),
            "an exemption for a transform that is gone, or that gained an instrument, is a stale "
            + "record that would hide the next transform to lose its instrument");
    }

    private static HashSet<string> RegisteredTransformNames()
    {
        var options = RestRefitSettings.SerializerOptions;
        var modifiers = ((DefaultJsonTypeInfoResolver)options.TypeInfoResolver!).Modifiers;

        return options.Converters.Select(converter => converter.GetType().Name)
            .Concat(modifiers.Select(modifier => modifier.Method.DeclaringType!.Name))
            .Append(RestRefitSettings.Create().UrlParameterFormatter.GetType().Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> InstrumentedTransformNames() =>
        typeof(RestTransformInstrumentRegistryTests).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(method => method.IsDefined(typeof(FactAttribute), inherit: true))
            .SelectMany(method => RetiredTransformsOf(method))
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<string> RetiredTransformsOf(MethodInfo method) =>
        method.GetCustomAttributes<TraitAttribute>(inherit: true)
            .Concat(method.DeclaringType!.GetCustomAttributes<TraitAttribute>(inherit: true))
            .Where(trait => trait.Name == RetiresTrait)
            .Select(trait => trait.Value);
}
