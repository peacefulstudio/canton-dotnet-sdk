// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Fails an emit that would declare one C# property twice in a generated record. Casing a Daml
/// field name drops its underscores and capitalises its first letter, so distinct Daml fields
/// (<c>my_field</c> and <c>myField</c>, or <c>period</c> and <c>period_</c> in a record named
/// <c>Period</c>) can emit the same property while each keeps its own wire name. The member names
/// are derived the way the emitters derive them, from the Daml model, so the check runs once per
/// module and needs no knowledge of how each emitter writes its source.
/// </summary>
internal static class EmittedMemberNameClashes
{
    /// <summary>
    /// Throws when two different Daml fields of one type emit the same C# member.
    /// </summary>
    /// <param name="packageName">The Daml package the type belongs to.</param>
    /// <param name="moduleName">The Daml module declaring the type.</param>
    /// <param name="damlKind">What the fields belong to, for the message: <c>record</c>, <c>template</c> or <c>choice argument</c>.</param>
    /// <param name="damlTypeName">The Daml name of the type, for the message.</param>
    /// <param name="damlFieldNames">The Daml field names of the type.</param>
    /// <param name="memberNameOf">Maps a Daml field name to the C# member the emitters give it.</param>
    /// <exception cref="CodegenException">Two fields would emit the same C# member.</exception>
    internal static void Require(
        string packageName,
        string moduleName,
        string damlKind,
        string damlTypeName,
        IEnumerable<string> damlFieldNames,
        Func<string, string> memberNameOf)
    {
        var fieldByMember = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var damlFieldName in damlFieldNames)
        {
            var member = memberNameOf(damlFieldName);
            if (fieldByMember.TryAdd(member, damlFieldName))
            {
                continue;
            }

            var first = fieldByMember[member];
            throw new CodegenException(
                $"The Daml fields '{first}' and '{damlFieldName}' of {damlKind} '{moduleName}:{damlTypeName}' in package '{packageName}' " +
                $"would both be emitted as the C# member '{member}'. Rename '{first}' or '{damlFieldName}' in Daml.");
        }
    }

    /// <summary>
    /// Throws when any record, template payload or choice argument of the module in
    /// <paramref name="context"/> would emit two fields as the same C# member.
    /// </summary>
    /// <param name="context">The module to check.</param>
    /// <param name="isIncludedByRootFilter">Whether a template of the module is emitted at all.</param>
    /// <exception cref="CodegenException">Two fields would emit the same C# member.</exception>
    internal static void RequireForModule(PackageEmitContext context, Func<string, string, bool> isIncludedByRootFilter)
    {
        var module = context.Module;
        var templatesByName = module.Templates.ToDictionary(template => template.Name, StringComparer.Ordinal);

        foreach (var dataType in module.DataTypes)
        {
            if (dataType.Definition is not DamlRecordDefinition record
                || context.NameTable.IsInterface(module.Name, dataType.Name)
                || context.NameTable.NestedChoiceArgumentHome(module.Name, dataType.Name) is not null)
            {
                continue;
            }

            var isTemplatePayload = templatesByName.TryGetValue(dataType.Name, out var template);
            if (isTemplatePayload && !isIncludedByRootFilter(module.Name, dataType.Name))
            {
                continue;
            }

            var emittedTypeName = context.EmittedTypeName(module.Name, dataType.Name);
            var reservedMemberNames = isTemplatePayload
                ? context.TemplateFieldReservedNames(template!)
                : ReservedMemberNames.OfRecordField();
            RequireDistinctMembers(context, isTemplatePayload ? "template" : "record", dataType.Name, emittedTypeName, reservedMemberNames, record);
        }

        foreach (var template in module.Templates.Where(template => isIncludedByRootFilter(module.Name, template.Name)))
        {
            foreach (var choice in template.Choices)
            {
                if (context.NameTable.NestedChoiceArgumentRecord(choice) is { } argumentRecord)
                {
                    RequireDistinctMembers(
                        context,
                        "choice argument",
                        $"{template.Name}.{choice.Name}",
                        Identifiers.Sanitize(choice.Name),
                        ReservedMemberNames.OfRecordField(),
                        argumentRecord);
                }
            }
        }
    }

    private static void RequireDistinctMembers(
        PackageEmitContext context,
        string damlKind,
        string damlTypeName,
        string emittedTypeName,
        IReadOnlySet<string> reservedMemberNames,
        DamlRecordDefinition record) =>
        Require(
            context.Package.Name,
            context.Module.Name,
            damlKind,
            damlTypeName,
            record.Fields.Select(field => field.Name),
            damlFieldName => Identifiers.MemberName(damlFieldName, emittedTypeName, reservedMemberNames));
}
