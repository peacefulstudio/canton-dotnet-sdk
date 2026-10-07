// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.Intermediate.Model;
using Xunit;
using static Daml.Codegen.CSharp.Tests.ReservedTypeNameFixtures;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Pins that every Daml type name which compiled before the reserved-name renaming is still
/// emitted under exactly that name. The name lists are the names a probe over the previous
/// emitter compiled cleanly, per declaration kind.
/// </summary>
public class EmittedCompilingTypeNamesStayUnchangedTests
{
    public static TheoryData<string> RecordNamesThatCompiledBefore =>
    [
        "Zebra", "Alpha", "Beta", "Bump", "BumpByKeyCommand", "BumpCommand", "By", "ChoiceBump", "ChoicePeek",
        "ChoicePoke", "ChoiceUse", "Choices", "CompareTo", "DamlTypeId", "DecodeBumpResult", "ExpectedConstructors",
        "Finalize", "FromCreatedContracts", "FromDamlEnum", "FromVariant", "GetType", "GetTypeCode", "Green",
        "HasFlag", "Holds", "InterfaceId", "Item", "Items", "Key", "Maybe", "MemberwiseClone", "N", "Owner",
        "PackageId", "PackageName", "PackageVersion", "Peek", "PeekByKeyCommand", "PeekCommand", "PokeCommand",
        "ProjectBumpResult", "ProjectPeekResult", "ProjectPokeResult", "ProjectUseResult", "Red", "ReferenceEquals",
        "Tag", "TemplateId", "ToDamlEnum", "ToVariant", "TryBumpAsync", "TryCreateAsync", "TryPeekAsync",
        "TryPokeAsync", "TryUseAsync", "UseByKeyCommand", "UseCommand", "Value", "View", "X", "_items"
    ];

    public static TheoryData<string> VariantNamesThatCompiledBefore =>
    [
        "Zebra", "Alpha", "Beta", "Bump", "BumpByKeyCommand", "BumpCommand", "By", "ChoiceBump", "ChoicePeek",
        "ChoicePoke", "ChoiceUse", "Choices", "CompareTo", "DamlTypeId", "DecodeBumpResult", "Deconstruct",
        "ExpectedConstructors", "Finalize", "FromCreatedContracts", "FromDamlEnum", "FromRecord", "GetType",
        "GetTypeCode", "Green", "HasFlag", "Holds", "InterfaceId", "Item", "Items", "Key", "Maybe",
        "MemberwiseClone", "N", "Owner", "PackageId", "PackageName", "PackageVersion", "Peek", "PeekByKeyCommand",
        "PeekCommand", "PokeCommand", "ProjectBumpResult", "ProjectPeekResult", "ProjectPokeResult",
        "ProjectUseResult", "Red", "ReferenceEquals", "TemplateId", "ToDamlEnum", "ToRecord", "TryBumpAsync",
        "TryCreateAsync", "TryPeekAsync", "TryPokeAsync", "TryUseAsync", "UseByKeyCommand", "UseCommand", "Value",
        "View", "X", "_items"
    ];

    public static TheoryData<string> EnumNamesThatCompiledBefore =>
    [
        "Zebra", "Alpha", "Beta", "Bump", "BumpByKeyCommand", "BumpCommand", "By", "ChoiceBump", "ChoicePeek",
        "ChoicePoke", "ChoiceUse", "Choices", "CompareTo", "DamlTypeId", "DecodeBumpResult", "Deconstruct",
        "EqualityContract", "FromCreatedContracts", "FromRecord", "FromVariant", "GetTypeCode", "Green", "HasFlag",
        "Holds", "InterfaceId", "Item", "Items", "Key", "Maybe", "N", "Owner", "PackageId", "PackageName",
        "PackageVersion", "Peek", "PeekByKeyCommand", "PeekCommand", "PokeCommand", "PrintMembers",
        "ProjectBumpResult", "ProjectPeekResult", "ProjectPokeResult", "ProjectUseResult", "Red", "Tag",
        "TemplateId", "ToRecord", "ToVariant", "TryBumpAsync", "TryCreateAsync", "TryPeekAsync", "TryPokeAsync",
        "TryUseAsync", "UseByKeyCommand", "UseCommand", "Value", "View", "X", "_items"
    ];

    public static TheoryData<string> TemplateNamesThatCompiledBefore =>
    [
        "Zebra", "Alpha", "Beta", "By", "ChoicePoke", "ChoiceUse", "Choices", "CompareTo", "ExpectedConstructors",
        "FromDamlEnum", "FromVariant", "GetTypeCode", "Green", "HasFlag", "Holds", "InterfaceId", "Item", "Items",
        "Key", "Maybe", "N", "Owner", "PokeCommand", "ProjectPokeResult", "ProjectUseResult", "Red", "Tag",
        "ToDamlEnum", "ToVariant", "TryCreateAsync", "TryPokeAsync", "TryUseAsync", "UseByKeyCommand", "UseCommand",
        "Value", "View", "X", "_items"
    ];

    private static DamlType ContractIdOf(string name) =>
        new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.ContractId), [Ref(name)]);

    private static DarModel Surrounding(DamlDataType subject, params DamlTemplate[] subjectTemplates) =>
        DarOf(
            [.. subjectTemplates, Template("Note", null, Choice("Take", IntType, Ref("Take")))],
            subject,
            Record("Holder", Field("inner", Ref(subject.Name))),
            Record("Note", Field("owner", PartyType), Field("item", Ref(subject.Name))),
            Record("Take", Field("x", Ref(subject.Name))));

    private static void AssertEmittedUnderItsOwnName(DarModel dar, string name)
    {
        CompileErrors(dar).Should().BeEmpty();
        DeclaresType(dar, name).Should().BeTrue();
        DeclaresType(dar, name + "_").Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(RecordNamesThatCompiledBefore))]
    public void Record_name_that_compiled_before_is_emitted_unchanged(string name) =>
        AssertEmittedUnderItsOwnName(Surrounding(Record(name, Field("owner", PartyType), Field("n", IntType))), name);

    [Theory]
    [MemberData(nameof(VariantNamesThatCompiledBefore))]
    public void Variant_name_that_compiled_before_is_emitted_unchanged(string name) =>
        AssertEmittedUnderItsOwnName(
            Surrounding(Variant(name, new DamlVariantConstructor("Holds", IntType), new DamlVariantConstructor("Empty", null))),
            name);

    [Theory]
    [MemberData(nameof(EnumNamesThatCompiledBefore))]
    public void Enum_name_that_compiled_before_is_emitted_unchanged(string name) =>
        AssertEmittedUnderItsOwnName(Surrounding(Enum(name, "Red", "Green")), name);

    [Theory]
    [MemberData(nameof(TemplateNamesThatCompiledBefore))]
    public void Template_name_that_compiled_before_is_emitted_unchanged(string name) =>
        AssertEmittedUnderItsOwnName(
            DarOf(
                [
                    Template(name, new DamlPrimitiveType(DamlPrimitive.Party), Choice("Bump", ContractIdOf(name), Ref("Bump")), Choice("Peek", IntType, Ref("Peek"))),
                    Template("Note", null, Choice("Take", IntType, Ref("Take"))),
                ],
                Record(name, Field("owner", PartyType), Field("n", IntType)),
                Record("Bump", Field("by", IntType)),
                Record("Peek", Field("w", IntType)),
                Record("Holder", Field("inner", Ref(name))),
                Record("Note", Field("owner", PartyType), Field("item", Ref(name))),
                Record("Take", Field("x", Ref(name)))),
            name);
}
