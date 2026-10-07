// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// The member names each emitted declaration kind carries, per declaration. A Daml type may not
/// share its C# name with one of these (CS0542: a member cannot be named like its enclosing
/// type), and a Daml field may not PascalCase to one either (CS0102 / CS8866: the property
/// redeclares the member). Each set is exactly what Roslyn rejects for that kind, pinned by
/// <c>EmittedCompilingTypeNamesStayUnchangedTests</c> and the <c>EmittedReserved*CompileTests</c>,
/// and applies only to the kind that emits the member: a record named <c>Tag</c> compiles, a variant named <c>Tag</c> does not.
/// </summary>
internal static class ReservedMemberNames
{
    private static readonly string[] SynthesizedRecordMembers =
        ["Equals", "GetHashCode", "ToString", "PrintMembers", "EqualityContract"];

    private static readonly string[] ObjectMembersRejectedAsPositionalProperty =
        ["GetType", "MemberwiseClone", "ReferenceEquals", "Clone"];

    private static readonly string[] TemplateIdentityMembers =
        ["TemplateId", "PackageId", "PackageName", "PackageVersion", "DamlTypeId"];

    private static readonly string[] RecordSerializationMembers = ["ToRecord", "FromRecord"];

    private static readonly string[] VariantMembers = ["Tag", "ToVariant", "FromVariant"];

    private const string DeconstructMember = "Deconstruct";

    private const string CloneMember = "Clone";

    private const string PayloadMember = "Value";

    private const string KeyWitnessMember = "Key";

    /// <summary>
    /// Members a record with <paramref name="fieldCount"/> fields declares. <c>Deconstruct</c> is
    /// synthesized only for a positional record, so a field-less record may still be named so.
    /// </summary>
    internal static IReadOnlySet<string> OfRecordType(int fieldCount) =>
        Set(SynthesizedRecordMembers, RecordSerializationMembers, fieldCount > 0 ? [DeconstructMember] : []);

    /// <summary>Members a variant's abstract base record declares.</summary>
    internal static IReadOnlySet<string> OfVariantType() =>
        Set(SynthesizedRecordMembers, VariantMembers);

    /// <summary>
    /// Members a template record with <paramref name="fieldCount"/> payload fields declares:
    /// everything a record does, the template identity statics, a <c>Choice&lt;C&gt;</c>
    /// descriptor per choice and, on a keyed template, a <c>&lt;C&gt;ByKeyCommand</c> builder per choice.
    /// </summary>
    internal static IReadOnlySet<string> OfTemplateType(DamlTemplate template, int fieldCount, bool hasUpgradedPackageId)
    {
        var choiceMembers = ChoiceMembers(template);
        return Set(OfRecordType(fieldCount), TemplateIdentityMembers, UpgradedPackageIdMember(hasUpgradedPackageId), choiceMembers);
    }

    /// <summary>
    /// Names a constructor's nested record may not take inside its variant's abstract base record:
    /// the members the base declares, <c>Clone</c> (no record may declare a member of that name),
    /// <c>Value</c> whenever some constructor of the variant carries a payload (the payload
    /// property of that constructor would then redeclare it), and <c>Deconstruct</c> when the
    /// constructor itself carries a payload (its positional record synthesizes that method).
    /// </summary>
    internal static IReadOnlySet<string> OfVariantConstructorType(bool constructorCarriesPayload, bool someConstructorCarriesPayload) =>
        Set(
            OfVariantType(),
            [CloneMember],
            someConstructorCarriesPayload ? [PayloadMember] : [],
            constructorCarriesPayload ? [DeconstructMember] : []);

    /// <summary>
    /// Names the record a choice takes as its argument may not take, as it is nested inside
    /// <paramref name="template"/>: every member of the template record, <c>Clone</c>, the
    /// <c>Key</c> witness of a keyed template and the members the argument record itself carries.
    /// </summary>
    internal static IReadOnlySet<string> OfNestedChoiceArgumentType(
        DamlTemplate template, int templateFieldCount, bool hasUpgradedPackageId, int argumentFieldCount) =>
        Set(
            OfTemplateType(template, templateFieldCount, hasUpgradedPackageId),
            OfRecordType(argumentFieldCount),
            [CloneMember],
            template.Key is null ? [] : [KeyWitnessMember]);

    /// <summary>Property names a record's positional parameters may not take.</summary>
    internal static IReadOnlySet<string> OfRecordField() =>
        Set(SynthesizedRecordMembers, RecordSerializationMembers, ObjectMembersRejectedAsPositionalProperty, [DeconstructMember]);

    /// <summary>
    /// Property names <paramref name="template"/>'s record positional parameters may not take:
    /// everything a record field may not, the template identity statics and the per-choice
    /// <c>Choice&lt;C&gt;</c> and, on a keyed template, <c>&lt;C&gt;ByKeyCommand</c> members, and the
    /// nested <c>&lt;C&gt;</c> record of a choice whose argument is a record.
    /// </summary>
    internal static IReadOnlySet<string> OfTemplateField(
        DamlTemplate template, bool hasUpgradedPackageId, PackageNameTable nameTable) =>
        Set(OfRecordField(), TemplateIdentityMembers, UpgradedPackageIdMember(hasUpgradedPackageId), ChoiceMembers(template), NestedChoiceArgumentTypes(template, nameTable));

    private static IEnumerable<string> UpgradedPackageIdMember(bool hasUpgradedPackageId) =>
        hasUpgradedPackageId ? ["UpgradedPackageId"] : [];

    private static IEnumerable<string> NestedChoiceArgumentTypes(DamlTemplate template, PackageNameTable nameTable) =>
        template.Choices
            .Where(choice => nameTable.NestedChoiceArgumentRecord(choice) is not null)
            .Select(choice => Identifiers.Sanitize(choice.Name));

    private static IEnumerable<string> ChoiceMembers(DamlTemplate template) =>
        template.Choices.SelectMany(choice =>
        {
            var choiceName = Identifiers.Sanitize(choice.Name);
            return template.Key is null
                ? new[] { $"Choice{choiceName}" }
                : [$"Choice{choiceName}", $"{choiceName}ByKeyCommand"];
        });

    private static IReadOnlySet<string> Set(params IEnumerable<string>[] names) =>
        names.SelectMany(group => group).ToHashSet(StringComparer.Ordinal);
}
