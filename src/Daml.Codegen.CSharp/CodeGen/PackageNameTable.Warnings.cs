// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;

namespace Daml.Codegen.CSharp.CodeGen;

internal sealed partial class PackageNameTable
{
    /// <summary>
    /// Logs the warnings raised while the table was built: one 1201 for every type emitted under
    /// a suffixed name, one 1202 for every record or template field whose property is, then one
    /// 1200 for every choice-argument type two templates share.
    /// </summary>
    internal void LogWarnings(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        foreach (var rename in Renames)
        {
            LogRenamedType(logger, rename.Kind, _package.Name, rename.Module, rename.DamlName, rename.ClashingMember, rename.EmittedName);
        }

        foreach (var rename in _fieldRenames)
        {
            LogRenamedField(logger, rename.Kind, _package.Name, rename.Module, rename.DamlName, rename.FieldName, rename.ClashingMember, rename.EmittedName);
        }

        LogAmbiguities(logger);
    }

    /// <summary>
    /// Logs only the 1200 warnings: one for every choice-argument type two templates share. A
    /// package that is referenced but not emitted reports no renames, which its own Daml source
    /// owns, but a shared argument type decides which template a reference to it names.
    /// </summary>
    internal void LogAmbiguities(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        foreach (var ambiguity in _ambiguities)
        {
            LogAmbiguousChoiceArgument(logger, ambiguity.QualifiedName, _package.Name, ambiguity.KeptTemplate, ambiguity.IgnoredTemplate);
        }
    }

    [LoggerMessage(
        EventId = 1200,
        Level = LogLevel.Warning,
        Message = "Choice-argument type {ChoiceArgumentKey} in package {PackageName} is used by both templates {KeptTemplate} and {IgnoredTemplate} in the same package; keeping {KeptTemplate} and ignoring {IgnoredTemplate}. Rename one choice-argument type to disambiguate.")]
    private static partial void LogAmbiguousChoiceArgument(ILogger logger, string choiceArgumentKey, string packageName, string keptTemplate, string ignoredTemplate);

    [LoggerMessage(
        EventId = 1201,
        Level = LogLevel.Warning,
        Message = "Daml {Kind} {ModuleName}:{DamlName} in package {PackageName} is named like the member '{ClashingMember}' the generated {Kind} type carries; it is emitted as '{EmittedName}'. Its Daml name, the wire name, is unchanged.")]
    private static partial void LogRenamedType(ILogger logger, string kind, string packageName, string moduleName, string damlName, string clashingMember, string emittedName);

    [LoggerMessage(
        EventId = 1202,
        Level = LogLevel.Warning,
        Message = "Field {FieldName} of Daml {Kind} {ModuleName}:{DamlName} in package {PackageName} would be the property '{ClashingMember}', which the generated {Kind} type already carries; it is emitted as '{EmittedName}'. Its Daml name, the wire name, is unchanged.")]
    private static partial void LogRenamedField(ILogger logger, string kind, string packageName, string moduleName, string damlName, string fieldName, string clashingMember, string emittedName);
}
