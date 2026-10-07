// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using RuntimeNamespaces = Daml.Runtime.RuntimeNamespaces;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Which of the two collection shapes a generated member carries, if either: a Daml
/// <c>List</c> emitted as <c>IReadOnlyList&lt;T&gt;</c>, or a Daml <c>TextMap</c> or
/// <c>GenMap</c> emitted as <c>IReadOnlyDictionary&lt;TKey,TValue&gt;</c>. An <c>Optional</c>
/// around either keeps the shape and adds the nullable annotation.
/// </summary>
internal enum CollectionShape
{
    /// <summary>Not a collection; record-synthesized equality already reads it by value.</summary>
    None,

    /// <summary>An <c>IReadOnlyList&lt;T&gt;</c> member, compared element by element in order.</summary>
    List,

    /// <summary>An <c>IReadOnlyDictionary&lt;TKey,TValue&gt;</c> member, compared key by key.</summary>
    Map,
}

/// <summary>
/// One member of a generated record, as the value-semantics emitter needs to see it.
/// </summary>
/// <param name="Name">The C# member name.</param>
/// <param name="CSharpType">The emitted C# type of the member.</param>
/// <param name="Shape">The member's collection shape.</param>
/// <param name="Summary">
/// The XML-doc summary the redeclared property carries. Redeclaring loses the exemption a
/// primary-constructor-synthesized property has from CS1591, so this is not optional in a
/// consumer project that generates a documentation file.
/// </param>
/// <param name="DamlFieldName">
/// The Daml field name the member projects, carried so the redeclared property keeps its
/// <c>DamlFieldAttribute</c>; <c>null</c> for a member that carries no such attribute.
/// </param>
internal sealed record ValueMember(
    string Name,
    string CSharpType,
    CollectionShape Shape,
    string Summary,
    string? DamlFieldName);

/// <summary>
/// Emits the value semantics a generated record needs once any of its members is a
/// collection: a defensive copy at construction and at <c>init</c>, an <c>Equals</c> that
/// reads list members element by element and map members key by key independently of
/// insertion order, and a matching <c>GetHashCode</c>. Record-synthesized equality compares
/// an <c>IReadOnlyList</c> or <c>IReadOnlyDictionary</c> member by reference, so without this
/// two values decoded from the same ledger payload are unequal and neither is findable by
/// content in a set or dictionary. The loops live in
/// <see cref="Daml.Runtime.Data.DamlFieldCollections"/>; this emitter writes the calls.
/// Constructed per package over the module's <see cref="PackageEmitContext"/> and the shared
/// <see cref="CodeGenOptions"/>.
/// </summary>
/// <remarks>
/// A copying member has to be declared in the record body, which puts it after every property
/// the primary constructor still synthesizes — the C# compiler emits the synthesized ones
/// first, then the body in source order. <c>Daml.Runtime.Serialization.DamlLfJsonReader</c>
/// reads a record's Daml field order off that metadata order, so declaring only the copying
/// members would reorder the fields it decodes into. Every member is therefore redeclared, in
/// Daml field order, and the parameter list carries no <c>DamlFieldAttribute</c>.
/// </remarks>
internal sealed class CollectionValueSemanticsEmitter(CodeGenOptions options)
{
    /// <summary>
    /// Whether <paramref name="members"/> carries a collection, and so whether
    /// <see cref="Write"/> produces anything. Callers consult this to leave the emitted
    /// <c>[property: DamlFieldAttribute]</c> on the primary-constructor parameter of a record
    /// that keeps its synthesized properties.
    /// </summary>
    internal static bool NeedsValueSemantics(IReadOnlyList<ValueMember> members) =>
        members.Any(member => member.Shape != CollectionShape.None);

    /// <summary>
    /// Writes the copying properties, <c>Equals</c> and <c>GetHashCode</c> for
    /// <paramref name="members"/> into <paramref name="indent"/>, positioned inside the body of
    /// <paramref name="selfType"/>. Writes nothing when no member is a collection.
    /// </summary>
    /// <param name="indent">Writer positioned at the top of the record body.</param>
    /// <param name="selfType">The record's own type, including type parameters.</param>
    /// <param name="members">Every member of the record, in declaration order.</param>
    /// <param name="derivesFromRecord">
    /// Whether the record has a record base whose equality and hash code the emitted overrides
    /// must fold in — true for a variant constructor, false for a standalone record.
    /// </param>
    internal void Write(
        IndentWriter indent,
        string selfType,
        IReadOnlyList<ValueMember> members,
        bool derivesFromRecord)
    {
        if (!NeedsValueSemantics(members))
        {
            return;
        }

        indent.Require("System");
        indent.Require("System.Collections.Generic");
        indent.Require(RuntimeNamespaces.Data);

        foreach (var member in members)
        {
            WriteRedeclaredProperty(indent, member);
        }

        WriteEquals(indent, selfType, members, derivesFromRecord);
        WriteGetHashCode(indent, members, derivesFromRecord);
    }

    private void WriteRedeclaredProperty(IndentWriter indent, ValueMember member)
    {
        if (member.Shape == CollectionShape.None)
        {
            WriteSummary(indent, member.Summary);
            WriteDamlFieldAttribute(indent, member);
            indent.AppendLine($"public {member.CSharpType} {member.Name} {{ get; init; }} = {member.Name};");
            indent.AppendLine();
            return;
        }

        var backingField = BackingFieldName(member.Name);
        indent.AppendLine($"private readonly {member.CSharpType} {backingField} = {Copy(member.Name)};");
        indent.AppendLine();

        WriteSummary(
            indent,
            $"{member.Summary} Copied when this value is constructed and on <c>init</c>, so a later "
            + "change to the caller's collection cannot alter this value's equality or hash code.");
        WriteDamlFieldAttribute(indent, member);
        indent.AppendLine($"public {member.CSharpType} {member.Name}");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"get => {backingField};");
        indent.AppendLine($"init => {backingField} = {Copy("value")};");
        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();
    }

    private void WriteSummary(IndentWriter indent, string summary)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine($"/// <summary>{summary}</summary>");
        }
    }

    private static void WriteDamlFieldAttribute(IndentWriter indent, ValueMember member)
    {
        if (member.DamlFieldName is { } damlFieldName)
        {
            indent.AppendLine($"[{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlFieldAttribute)}(\"{damlFieldName}\")]");
        }
    }

    private void WriteEquals(
        IndentWriter indent,
        string selfType,
        IReadOnlyList<ValueMember> members,
        bool derivesFromRecord)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine(
                "/// <summary>Compares by content, reading list members element by element and map members "
                + "key by key independently of insertion order.</summary>");
            indent.AppendLine("/// <param name=\"other\">The value to compare against.</param>");
            indent.AppendLine("/// <returns><c>true</c> when every member is equal.</returns>");
        }

        var clauses = new List<string> { "other is not null" };
        if (derivesFromRecord)
        {
            clauses.Add("base.Equals(other)");
        }
        clauses.AddRange(members.Select(MemberEqualityClause));

        indent.AppendLine($"public bool Equals({selfType}? other) =>");
        indent.Indent();
        for (var index = 0; index < clauses.Count; index++)
        {
            var terminator = index == clauses.Count - 1 ? ";" : "";
            var connector = index == 0 ? "" : "&& ";
            indent.AppendLine($"{connector}{clauses[index]}{terminator}");
        }
        indent.Dedent();
        indent.AppendLine();
    }

    private void WriteGetHashCode(
        IndentWriter indent,
        IReadOnlyList<ValueMember> members,
        bool derivesFromRecord)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <inheritdoc />");
        }

        indent.AppendLine("public override int GetHashCode()");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"var hash = new {TypeReferenceQualifier.Qualify("HashCode")}();");
        if (derivesFromRecord)
        {
            indent.AppendLine("hash.Add(base.GetHashCode());");
        }
        foreach (var member in members)
        {
            indent.AppendLine($"hash.Add({MemberHashOperand(member)});");
        }
        indent.AppendLine("return hash.ToHashCode();");
        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();
    }

    private string MemberEqualityClause(ValueMember member) => member.Shape switch
    {
        CollectionShape.None =>
            $"{TypeReferenceQualifier.Qualify("EqualityComparer")}<{member.CSharpType}>.Default.Equals({member.Name}, other.{member.Name})",
        _ => $"{Collections}.Equal({member.Name}, other.{member.Name})",
    };

    private static string MemberHashOperand(ValueMember member) => member.Shape switch
    {
        CollectionShape.None => member.Name,
        _ => $"{Collections}.Hash({member.Name})",
    };

    private static string Copy(string expression) => $"{Collections}.Copy({expression})";

    private static string Collections => TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlFieldCollections);

    private static string BackingFieldName(string memberName)
    {
        // Daml field names are non-empty identifiers, so unescaped is never empty here.
        var unescaped = memberName.StartsWith('@') ? memberName[1..] : memberName;
        return $"_{char.ToLowerInvariant(unescaped[0])}{unescaped[1..]}";
    }
}
