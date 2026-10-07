// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.Intermediate.Model;
using Xunit;
using static Daml.Codegen.CSharp.Tests.ReservedTypeNameFixtures;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedReservedRecordNameCompileTests
{
    public static TheoryData<string> RecordMemberNames =>
        ["ToRecord", "FromRecord", "Equals", "GetHashCode", "ToString", "PrintMembers", "EqualityContract", "Deconstruct"];

    private static DarModel RecordNamedAndReferenced(string name) =>
        DarOf(
            [],
            Record(name, Field("x", IntType)),
            Record("Holder", Field("inner", Ref(name))));

    [Theory]
    [MemberData(nameof(RecordMemberNames))]
    public void Record_named_like_a_generated_member_compiles(string name)
    {
        CompileErrors(RecordNamedAndReferenced(name)).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(RecordMemberNames))]
    public void Record_named_like_a_generated_member_is_emitted_with_one_trailing_underscore(string name)
    {
        var dar = RecordNamedAndReferenced(name);

        DeclaresType(dar, name + "_").Should().BeTrue();
        DeclaresType(dar, name).Should().BeFalse();
    }
}
