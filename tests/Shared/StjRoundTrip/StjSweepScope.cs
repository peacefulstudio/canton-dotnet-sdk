// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;
using Daml.Runtime.Stdlib;

namespace Daml.Testing.StjRoundTrip;

/// <summary>
/// Decides which exported types a sweep must sample, turns its hand-written sample table into
/// cases, and reports what the table leaves uncovered or unpopulated.
/// </summary>
internal static class StjSweepScope
{
    private const string AsSelfSuffix = "/as-self";

    /// <summary>
    /// The exported types that are value types: everything but static classes, interfaces,
    /// converters, exceptions, attributes, delegates, <c>*Extensions</c> classes and the types the
    /// sweep names explicitly.
    /// </summary>
    public static IReadOnlyList<Type> ValueTypes(IEnumerable<Type> exported, IReadOnlySet<Type> namedNotAValueType) =>
        [.. exported.Where(type => !IsNotAValueType(type) && !namedNotAValueType.Contains(type))];

    /// <summary>
    /// The named exclusions that name no exported type.
    /// </summary>
    public static IReadOnlyList<Type> StaleExclusions(IEnumerable<Type> exported, IReadOnlySet<Type> namedNotAValueType)
    {
        var exportedTypes = exported.ToHashSet();
        return [.. namedNotAValueType.Where(type => !exportedTypes.Contains(type))];
    }

    /// <summary>
    /// One case per instance, declared as its table key, and a second <c>/as-self</c> case
    /// declared as its runtime type when that differs from the key.
    /// </summary>
    /// <exception cref="ArgumentException">An instance is not assignable to its key, or two cases share an id.</exception>
    public static IReadOnlyList<StjRoundTripCase> CasesWithAsSelfArms(IReadOnlyDictionary<Type, object[]> samples)
    {
        var cases = new List<StjRoundTripCase>();
        foreach (var (key, instances) in samples)
        {
            foreach (var instance in instances)
            {
                if (!key.IsInstanceOfType(instance))
                {
                    throw new ArgumentException(
                        $"{instance.GetType().FullName} is not assignable to its table key {key.FullName}.");
                }
            }

            cases.AddRange(CasesFor(key, instances));
        }

        var duplicate = cases.GroupBy(roundTripCase => roundTripCase.Id).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"The sample table yields the duplicate case id '{duplicate.Key}'.");
        }

        return cases;
    }

    /// <summary>
    /// The value types no case covers, once closed generics and arm runtime types are normalised
    /// to their open definitions.
    /// </summary>
    public static IReadOnlyList<Type> Uncovered(IReadOnlyList<Type> valueTypes, IReadOnlyList<StjRoundTripCase> cases)
    {
        var covered = cases
            .SelectMany(roundTripCase => new[] { roundTripCase.DeclaredType, roundTripCase.Sample.GetType() })
            .Select(Normalised)
            .ToHashSet();
        return [.. valueTypes.Where(type => !covered.Contains(type))];
    }

    /// <summary>
    /// The types the cases are declared as that are not among the in-scope value types.
    /// </summary>
    public static IReadOnlyList<Type> OutOfScopeKeys(IReadOnlyList<Type> valueTypes, IReadOnlyList<StjRoundTripCase> cases) =>
        [.. cases.Select(roundTripCase => roundTripCase.DeclaredType).Distinct().Where(type => !valueTypes.Contains(Normalised(type)))];

    /// <summary>
    /// The arms none of whose samples has every public member populated, including the
    /// <see cref="JsonIgnoreAttribute"/> members and counting an <c>Optional</c> holding <c>None</c> as unfilled, minus the exempt arms.
    /// </summary>
    public static IReadOnlyList<string> ArmsWithNoFullyPopulatedSample(
        IReadOnlyList<StjRoundTripCase> cases,
        IReadOnlySet<string> exemptArms) =>
        [.. SamplesByArm(cases).Where(arm => !arm.Value.Any(IsFullyPopulated) && !exemptArms.Contains(arm.Key)).Select(arm => arm.Key)];

    /// <summary>
    /// The exempt arms that name no arm, or an arm that already has a fully populated sample.
    /// </summary>
    public static IReadOnlyList<string> StaleExemptions(
        IReadOnlyList<StjRoundTripCase> cases,
        IReadOnlySet<string> exemptArms)
    {
        var arms = SamplesByArm(cases);
        return [.. exemptArms.Where(exempt => !arms.TryGetValue(exempt, out var samples) || samples.Any(IsFullyPopulated))];
    }

    /// <summary>
    /// The known-broken keys that name no case, or name a leg that does not exist.
    /// </summary>
    public static IReadOnlyList<string> StaleKnownBrokenKeys(
        IReadOnlyList<StjRoundTripCase> cases,
        IEnumerable<string> knownBrokenKeys)
    {
        var caseIds = cases.Select(roundTripCase => roundTripCase.Id).ToHashSet(StringComparer.Ordinal);
        return [.. knownBrokenKeys.Where(key => !NamesAnExistingCase(key, caseIds))];
    }

    private static bool NamesAnExistingCase(string knownBrokenKey, HashSet<string> caseIds)
    {
        var (caseId, legName) = StjKnownBroken.Split(knownBrokenKey);
        return caseIds.Contains(caseId) && (legName is null || Enum.TryParse<StjOptionsLeg>(legName, out _));
    }

    private static IEnumerable<StjRoundTripCase> CasesFor(Type key, object[] instances)
    {
        var sameArmCount = instances.GroupBy(instance => instance.GetType()).ToDictionary(group => group.Key, group => group.Count());
        var sameArmSeen = new Dictionary<Type, int>();
        foreach (var instance in instances)
        {
            var runtimeType = instance.GetType();
            var armId = ArmId(key, runtimeType);
            if (sameArmCount[runtimeType] > 1)
            {
                sameArmSeen[runtimeType] = sameArmSeen.GetValueOrDefault(runtimeType) + 1;
                armId += $"~{sameArmSeen[runtimeType]}";
            }

            yield return new StjRoundTripCase(armId, key, instance);
            if (runtimeType != key)
            {
                yield return new StjRoundTripCase(armId + AsSelfSuffix, runtimeType, instance);
            }
        }
    }

    private static Dictionary<string, List<object>> SamplesByArm(IReadOnlyList<StjRoundTripCase> cases)
    {
        var samplesByArm = new Dictionary<string, List<object>>(StringComparer.Ordinal);
        foreach (var roundTripCase in cases.Where(roundTripCase => !roundTripCase.Id.EndsWith(AsSelfSuffix, StringComparison.Ordinal)))
        {
            var arm = ArmId(roundTripCase.DeclaredType, roundTripCase.Sample.GetType());
            if (!samplesByArm.TryGetValue(arm, out var samples))
            {
                samplesByArm[arm] = samples = [];
            }

            samples.Add(roundTripCase.Sample);
        }

        return samplesByArm;
    }

    private static bool IsFullyPopulated(object sample) =>
        sample.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .All(property => IsPopulatedAsRequired(property, sample));

    private static bool IsPopulatedAsRequired(PropertyInfo property, object sample) =>
        property.GetValue(sample) is { } value && !IsOptionalWithoutValue(value);

    private static bool IsOptionalWithoutValue(object value)
    {
        for (var type = value.GetType(); type is not null; type = type.BaseType)
        {
            if (type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof(Optional<>))
            {
                return !(bool)type.GetProperty(nameof(Optional<string>.HasValue))!.GetValue(value)!;
            }
        }

        return false;
    }

    private static string ArmId(Type key, Type runtimeType) =>
        runtimeType == key ? Display(key) : $"{Display(key)}/{Display(runtimeType)}";

    private static string Display(Type type)
    {
        var tick = type.Name.IndexOf('`', StringComparison.Ordinal);
        if (tick < 0)
        {
            return type.Name;
        }

        var arity = int.Parse(type.Name[(tick + 1)..], CultureInfo.InvariantCulture);
        var ownArguments = type.GetGenericArguments()[^arity..];
        return $"{type.Name[..tick]}<{string.Join(",", ownArguments.Select(Display))}>";
    }

    private static Type Normalised(Type type) =>
        type.IsConstructedGenericType ? type.GetGenericTypeDefinition() : type;

    private static bool IsNotAValueType(Type type) =>
        (type.IsAbstract && type.IsSealed)
        || type.IsInterface
        || typeof(JsonConverter).IsAssignableFrom(type)
        || typeof(Exception).IsAssignableFrom(type)
        || typeof(Attribute).IsAssignableFrom(type)
        || typeof(Delegate).IsAssignableFrom(type)
        || type.Name.EndsWith("Extensions", StringComparison.Ordinal);
}

/// <summary>
/// The known-broken list a sweep shares across both legs: an entry keyed by case id covers both
/// legs, and an entry keyed <c>&lt;case id&gt;@&lt;leg&gt;</c> covers one.
/// </summary>
internal static class StjKnownBroken
{
    /// <summary>
    /// The root cause listed for the case on the leg, preferring the leg's own entry, or null
    /// when the case is not known broken there.
    /// </summary>
    public static string? RootCause(IReadOnlyDictionary<string, string> knownBroken, string caseId, StjOptionsLeg leg) =>
        knownBroken.TryGetValue($"{caseId}@{leg}", out var legCause)
            ? legCause
            : knownBroken.GetValueOrDefault(caseId);

    internal static (string CaseId, string? LegName) Split(string knownBrokenKey)
    {
        var at = knownBrokenKey.LastIndexOf('@');
        return at < 0 ? (knownBrokenKey, null) : (knownBrokenKey[..at], knownBrokenKey[(at + 1)..]);
    }
}
