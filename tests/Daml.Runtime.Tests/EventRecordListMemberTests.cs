// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Daml.Runtime.Contracts;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Every list member of the public records in <c>Daml.Runtime.Contracts</c> and
/// <c>Daml.Runtime.Streams</c> is declared as <see cref="EquatableArray{T}"/>. A member that
/// regresses to <see cref="IReadOnlyList{T}"/> or an array still compiles — the struct converts
/// to the interface and a collection expression targets both — so the declared types are swept
/// here rather than trusted.
/// </summary>
public class EventRecordListMemberTests
{
    private static readonly Type[] ListShapes =
    [
        typeof(IEnumerable<>),
        typeof(IReadOnlyCollection<>),
        typeof(IReadOnlyList<>),
        typeof(ICollection<>),
        typeof(IList<>),
        typeof(List<>),
        typeof(EquatableArray<>),
    ];

    [Fact]
    public void EventRecords_declare_every_list_member_as_an_EquatableArray()
    {
        var offenders = ListMembers()
            .Where(member => !IsEquatableArray(member.PropertyType))
            .Select(member => $"{member.DeclaringType!.Name}.{member.Name}: {member.PropertyType.Name}");

        offenders.Should().BeEmpty();
    }

    /// <summary>
    /// Proves the sweep is not vacuous by pinning the exact member count: a discovery bug that
    /// dropped every record would otherwise pass the assertion above by finding nothing.
    /// </summary>
    [Fact]
    public void Sweep_finds_exactly_the_thirty_three_list_members()
    {
        ListMembers().Should().HaveCount(33);
    }

    private static IEnumerable<PropertyInfo> ListMembers() =>
        typeof(EquatableArray<>).Assembly.GetTypes()
            .Where(type => type.Namespace is "Daml.Runtime.Contracts" or "Daml.Runtime.Streams")
            .Where(type => type.IsClass && (type.IsPublic || type.IsNestedPublic))
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(property => IsListShaped(property.PropertyType));

    private static bool IsListShaped(Type type) =>
        type.IsArray || (type.IsGenericType && ListShapes.Contains(type.GetGenericTypeDefinition()));

    private static bool IsEquatableArray(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(EquatableArray<>);
}
