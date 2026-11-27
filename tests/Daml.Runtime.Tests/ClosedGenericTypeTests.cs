// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class ClosedGenericTypeTests
{
    private static readonly Type HalfOpenMap =
        typeof(Map<,>).MakeGenericType(typeof(string), typeof(Map<,>).GetGenericArguments()[1]);

    public static TheoryData<string, Type, Type, bool> Cases => new()
    {
        { "closed over the definition", typeof(Set<string>), typeof(Set<>), true },
        { "closed over two arguments", typeof(Map<string, long>), typeof(Map<,>), true },
        { "the open definition itself", typeof(Set<>), typeof(Set<>), false },
        { "half open", HalfOpenMap, typeof(Map<,>), false },
        { "closed over another definition", typeof(Map<string, long>), typeof(Set<>), false },
        { "not generic", typeof(string), typeof(Set<>), false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ClosedGenericType_IsOf_answers_whether_the_type_is_closed_over_the_definition(
        string caseName, Type type, Type definition, bool expected)
    {
        _ = caseName;

        ClosedGenericType.IsOf(type, definition).Should().Be(expected);
    }
}
