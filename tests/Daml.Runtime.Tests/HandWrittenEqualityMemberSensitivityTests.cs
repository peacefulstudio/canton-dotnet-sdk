// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Daml.Runtime.Contracts;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// A hand-written <c>Equals</c> can silently miss a member added later, and the round-trip sweep
/// cannot see it: the member round-trips unchanged. For each public member, a copy that differs
/// in that member alone must be unequal, unless the member is <c>[JsonIgnore]</c> or a named
/// exclusion, where the copy must stay equal and hash equal.
/// </summary>
public class HandWrittenEqualityMemberSensitivityTests
{
    private static readonly MethodInfo ShallowCopy =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static TheoryData<string> GuardedTypes()
    {
        var data = new TheoryData<string>();
        foreach (var type in HandWrittenEqualityGuardTable.Entries.Keys)
        {
            data.Add(NameOf(type));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(GuardedTypes))]
    public void HandWrittenEquals_is_sensitive_to_every_member_except_JsonIgnore_and_named_exclusions(string typeName)
    {
        var guarded = HandWrittenEqualityGuardTable.Entries.Single(pair => NameOf(pair.Key) == typeName);

        var violations = PublicMembers(guarded.Key).SelectMany(member => Violations(guarded.Value, member));

        string.Join(Environment.NewLine, violations).Should().BeEmpty();
    }

    [Fact]
    public void HandWrittenEqualityGuard_names_only_members_the_types_declare()
    {
        var unknown = HandWrittenEqualityGuardTable.Entries.SelectMany(pair =>
        {
            var members = PublicMembers(pair.Key).Select(member => member.Name).ToHashSet();
            return pair.Value.Alternates.Keys.Concat(pair.Value.NamedExclusions.Keys)
                .Where(name => !members.Contains(name))
                .Select(name => $"{NameOf(pair.Key)}.{name}");
        });

        string.Join(Environment.NewLine, unknown).Should().BeEmpty();
    }

    [Fact]
    public void HandWrittenEqualityGuard_covers_every_runtime_type_with_a_hand_written_Equals()
    {
        var withHandWrittenEquals = typeof(LedgerOffset).Assembly.GetExportedTypes()
            .Where(HasHandWrittenEquals)
            .ToHashSet();
        var accountedFor = HandWrittenEqualityGuardTable.Entries.Keys.Select(Normalised)
            .Concat(HandWrittenEqualityGuardTable.NotGuarded.Keys)
            .ToHashSet();

        var unaccounted = withHandWrittenEquals.Except(accountedFor).Select(type => type.FullName);
        var stale = accountedFor.Except(withHandWrittenEquals).Select(type => type.FullName);

        string.Join(Environment.NewLine, unaccounted).Should().BeEmpty();
        string.Join(Environment.NewLine, stale).Should().BeEmpty();
    }

    [Fact]
    public void HandWrittenEqualityGuard_guards_no_type_it_also_lists_as_not_guarded()
    {
        var both = HandWrittenEqualityGuardTable.Entries.Keys.Select(Normalised)
            .Intersect(HandWrittenEqualityGuardTable.NotGuarded.Keys)
            .Select(type => type.FullName);

        string.Join(Environment.NewLine, both).Should().BeEmpty();
    }

    private static IEnumerable<string> Violations(HandWrittenEqualityGuardEntry entry, MemberInfo member)
    {
        if (!entry.Alternates.TryGetValue(member.Name, out var alternate))
        {
            yield return $"{member.Name}: the guard table has no alternate value for it";
            yield break;
        }

        var copy = ShallowCopy.Invoke(entry.Sample, null)!;
        if (!TrySet(copy, member, alternate))
        {
            yield return $"{member.Name}: it has no setter the guard can use to build a copy that differs in it";
            yield break;
        }

        var equal = entry.Sample.Equals(copy);
        var excluded = member.IsDefined(typeof(JsonIgnoreAttribute)) || entry.NamedExclusions.ContainsKey(member.Name);
        if (!excluded && equal)
        {
            yield return $"{member.Name}: Equals ignores it";
        }

        if (excluded && !equal)
        {
            yield return $"{member.Name}: Equals compares it, though it is excluded from equality";
        }

        if (excluded && equal && entry.Sample.GetHashCode() != copy.GetHashCode())
        {
            yield return $"{member.Name}: GetHashCode includes it, though it is excluded from equality";
        }
    }

    private static bool TrySet(object target, MemberInfo member, object? value)
    {
        switch (member)
        {
            case PropertyInfo { SetMethod: not null } property:
                property.SetValue(target, value);
                return true;
            case FieldInfo { IsInitOnly: false } field:
                field.SetValue(target, value);
                return true;
            default:
                return false;
        }
    }

    private static IEnumerable<MemberInfo> PublicMembers(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .Cast<MemberInfo>()
            .Concat(type.GetFields(BindingFlags.Public | BindingFlags.Instance));

    private static bool HasHandWrittenEquals(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Any(method => method.Name == "Equals"
                && !method.IsDefined(typeof(CompilerGeneratedAttribute))
                && method.GetParameters() is [var parameter]
                && Normalised(parameter.ParameterType) == type);

    private static Type Normalised(Type type) =>
        type.IsGenericType ? type.GetGenericTypeDefinition() : type;

    private static string NameOf(Type type)
    {
        var name = type.Name.Split('`')[0];
        return type.DeclaringType is null ? name : $"{NameOf(type.DeclaringType)}.{name}";
    }
}
