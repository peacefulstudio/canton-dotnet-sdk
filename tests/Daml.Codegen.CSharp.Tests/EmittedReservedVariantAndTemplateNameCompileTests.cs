// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.Intermediate.Model;
using Xunit;
using static Daml.Codegen.CSharp.Tests.ReservedTypeNameFixtures;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedReservedVariantAndTemplateNameCompileTests
{
    public static TheoryData<string> VariantMemberNames =>
        ["Tag", "ToVariant", "FromVariant", "Equals", "GetHashCode", "ToString", "PrintMembers", "EqualityContract"];

    public static TheoryData<string> TemplateMemberNames =>
        [
            "ToRecord", "FromRecord", "Deconstruct", "Equals", "GetHashCode", "ToString", "PrintMembers", "EqualityContract",
            "TemplateId", "PackageId", "PackageName", "PackageVersion", "DamlTypeId", "ChoiceReissue", "ReissueByKeyCommand",
        ];

    private static DamlType ContractIdOf(string name) =>
        new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.ContractId), [Ref(name)]);

    private static DarModel VariantNamedAndReferenced(string name) =>
        DarOf(
            [],
            Variant(name, new DamlVariantConstructor("First", IntType), new DamlVariantConstructor("Second", null)),
            Record("Holder", Field("inner", Ref(name))));

    private static DarModel KeyedTemplateNamedAndReferenced(string name) =>
        DarOf(
            [Template(name, new DamlPrimitiveType(DamlPrimitive.Text), Choice("Reissue", ContractIdOf(name)))],
            Record(name, Field("owner", PartyType)),
            Record("Holder", Field("inner", Ref(name)), Field("handle", ContractIdOf(name))));

    [Theory]
    [MemberData(nameof(VariantMemberNames))]
    public void Variant_named_like_a_generated_member_compiles_and_is_emitted_with_one_trailing_underscore(string name)
    {
        var dar = VariantNamedAndReferenced(name);

        CompileErrors(dar).Should().BeEmpty();
        DeclaresType(dar, name + "_").Should().BeTrue();
        DeclaresType(dar, name).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(TemplateMemberNames))]
    public void Template_named_like_a_generated_member_compiles_and_is_emitted_with_one_trailing_underscore(string name)
    {
        var dar = KeyedTemplateNamedAndReferenced(name);

        CompileErrors(dar).Should().BeEmpty();
        DeclaresType(dar, name + "_").Should().BeTrue();
        DeclaresType(dar, name).Should().BeFalse();
    }
}
