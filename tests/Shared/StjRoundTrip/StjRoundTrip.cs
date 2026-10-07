// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using AwesomeAssertions.Equivalency;
using Daml.Runtime.Serialization;

namespace Daml.Testing.StjRoundTrip;

/// <summary>
/// The round trip every sweep holds a sample to: serialize it declared as the case's type, read
/// it back, and require the value read to be equivalent to the value written, <c>Equals</c> it,
/// and hash equal to it. Members marked <c>[JsonIgnore]</c> do not travel by definition, so the
/// equivalence step leaves them out; <c>Equals</c> and the hash code still see the whole value.
/// </summary>
internal static class StjRoundTrip
{
    /// <summary>
    /// Asserts the sample reads back equivalent by runtime member types, with byte memory
    /// compared by its contents, then equal, then with an equal hash code. A sample with no public
    /// members skips the equivalence step, which would have nothing to compare. A failure names
    /// the case, the leg and the step that broke.
    /// </summary>
    public static void AssertReadsBackEquivalentEqualAndHashEqual(StjRoundTripCase roundTripCase, StjOptionsLeg leg)
    {
        var options = OptionsFor(leg);
        var sample = roundTripCase.Sample;

        var json = Step(roundTripCase, leg, "serialize", () =>
            JsonSerializer.Serialize(sample, roundTripCase.DeclaredType, options));
        var restored = Step(roundTripCase, leg, "deserialize", () =>
            JsonSerializer.Deserialize(json, roundTripCase.DeclaredType, options));

        if (HasMembersToCompare(sample))
        {
            Step(roundTripCase, leg, "equivalence", () =>
                restored.Should().BeEquivalentTo(
                    sample,
                    config => config
                        .PreferringRuntimeMemberTypes()
                        .Excluding(member => IsJsonIgnored(member))
                        .Using<ReadOnlyMemory<byte>>(
                            context => context.Subject.ToArray().Should().Equal(context.Expectation.ToArray()))
                        .WhenTypeIs<ReadOnlyMemory<byte>>()));
        }

        Step(roundTripCase, leg, "equality", () => restored.Should().Be(sample));
        Step(roundTripCase, leg, "hash code", () =>
            restored!.GetHashCode().Should().Be(sample.GetHashCode()));
    }

    /// <summary>
    /// Passes only when <see cref="AssertReadsBackEquivalentEqualAndHashEqual"/> throws. A case
    /// that reads back as itself fails, naming the root cause that no longer holds.
    /// </summary>
    public static void AssertStillFails(StjRoundTripCase roundTripCase, StjOptionsLeg leg, string knownRootCause)
    {
        if (!Fails(roundTripCase, leg))
        {
            throw new StjRoundTripFailure(
                $"{roundTripCase.Id} [{leg}] now reads back as itself, so its known-broken root cause no longer holds: "
                + $"{knownRootCause}. Delete its known-broken entry.");
        }

        // Run the serialize stage independently so a new serialization regression is not masked
        // by the known-broken entry: no documented root cause is a serialization failure, so
        // serialize must always succeed.
        Step(roundTripCase, leg, "serialize", () =>
            JsonSerializer.Serialize(roundTripCase.Sample, roundTripCase.DeclaredType, OptionsFor(leg)));
    }

    private static bool Fails(StjRoundTripCase roundTripCase, StjOptionsLeg leg)
    {
        try
        {
            AssertReadsBackEquivalentEqualAndHashEqual(roundTripCase, leg);
            return false;
        }
        catch (StjRoundTripFailure)
        {
            return true;
        }
    }

    private static bool IsJsonIgnored(IMemberInfo member) =>
        member.DeclaringType.GetProperty(member.Name)?.IsDefined(typeof(JsonIgnoreAttribute)) == true;

    private static bool HasMembersToCompare(object sample) =>
        sample.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Length > 0
        || sample.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).Length > 0;

    private static JsonSerializerOptions? OptionsFor(StjOptionsLeg leg) =>
        leg switch
        {
            StjOptionsLeg.Default => null,
            StjOptionsLeg.AddDamlConverters => new JsonSerializerOptions().AddDamlConverters(),
            _ => throw new ArgumentOutOfRangeException(nameof(leg), leg, "Unknown options leg."),
        };

    private static T Step<T>(StjRoundTripCase roundTripCase, StjOptionsLeg leg, string step, Func<T> run)
    {
        try
        {
            return run();
        }
#pragma warning disable CA1031
        catch (Exception exception)
#pragma warning restore CA1031
        {
            throw new StjRoundTripFailure(
                $"{roundTripCase.Id} [{leg}] {step}: {exception.GetType().Name}: {exception.Message}",
                exception);
        }
    }
}
