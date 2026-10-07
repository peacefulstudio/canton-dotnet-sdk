// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// One Daml declaration's emitted file together with the declaration it was emitted for.
/// </summary>
/// <param name="DamlKind">The declaration kind: <c>template</c>, <c>record</c>, <c>variant</c>, <c>enum</c> or <c>interface</c>.</param>
/// <param name="DamlName">The Daml declaration name.</param>
/// <param name="File">The emitted C# source file.</param>
internal sealed record EmittedDeclarationFile(string DamlKind, string DamlName, GeneratedFile File);

/// <summary>
/// Fails an emit that would declare one C# type twice in a module's namespace. The names are read
/// off the emitted source itself, so every companion type an emitter writes beside a declaration
/// (<c>&lt;T&gt;Extensions</c>, <c>&lt;T&gt;SubmissionExtensions</c>, <c>&lt;C&gt;Result</c>, ...) is
/// covered without a second list of them that could drift from the emitters. A type is identified
/// by its name and its generic arity, because <c>Box</c> and <c>Box&lt;a&gt;</c> coexist.
/// </summary>
internal static partial class EmittedTypeNameClashes
{
    [GeneratedRegex(@"^(?<indent>[ \t]*)public (?<modifiers>(?:[a-z]+ )*?)(?:record struct|record|class|struct|enum|interface) (?<name>[A-Za-z_][A-Za-z0-9_]*)(?<typeParams><[^>\r\n]*>)?", RegexOptions.Multiline)]
    private static partial Regex TopLevelDeclaration();

    private sealed record Declaration(string TypeKey, string TypeName, EmittedDeclarationFile Origin, bool IsOwnType, bool IsPartial);

    /// <summary>
    /// Throws when two different Daml declarations of one module emit a C# type of the same name
    /// and arity into its namespace.
    /// </summary>
    /// <exception cref="CodegenException">A C# type would be declared twice.</exception>
    internal static void Require(string packageName, string moduleName, string moduleNamespace, IEnumerable<EmittedDeclarationFile> files)
    {
        var firstDeclarationByType = new Dictionary<string, Declaration>(StringComparer.Ordinal);
        foreach (var declaration in files.SelectMany(DeclarationsOf))
        {
            if (!firstDeclarationByType.TryGetValue(declaration.TypeKey, out var first) || IsSharedPartialShell(first, declaration))
            {
                firstDeclarationByType.TryAdd(declaration.TypeKey, declaration);
                continue;
            }

            throw new CodegenException(
                $"The C# type '{declaration.TypeName}' is declared twice in namespace '{moduleNamespace}' of package '{packageName}': " +
                $"{Describe(first, moduleName)} and {Describe(declaration, moduleName)}. " +
                $"Rename '{first.Origin.DamlName}' or '{declaration.Origin.DamlName}' in Daml; a type generated for a template can also be avoided by renaming one of its choices.");
        }
    }

    /// <summary>
    /// The simple names of the top-level types declared by <paramref name="files"/>, companion
    /// types included.
    /// </summary>
    internal static IReadOnlySet<string> TopLevelTypeNamesOf(IEnumerable<EmittedDeclarationFile> files) =>
        files.SelectMany(DeclarationsOf).Select(declaration => declaration.TypeName).ToHashSet(StringComparer.Ordinal);

    private static bool IsSharedPartialShell(Declaration left, Declaration right) =>
        left.IsPartial && right.IsPartial
        && left.Origin.DamlKind == right.Origin.DamlKind && left.Origin.DamlName == right.Origin.DamlName;

    private static IEnumerable<Declaration> DeclarationsOf(EmittedDeclarationFile file)
    {
        var matches = TopLevelDeclaration().Matches(file.File.Content);
        var topLevelIndent = matches.Count == 0 ? 0 : matches.Min(match => match.Groups["indent"].Length);
        return matches
            .Where(match => match.Groups["indent"].Length == topLevelIndent)
            .Select((match, index) =>
            {
                var name = match.Groups["name"].Value;
                var arity = match.Groups["typeParams"].Success ? match.Groups["typeParams"].Value.Count(c => c == ',') + 1 : 0;
                var isPartial = match.Groups["modifiers"].Value.Split(' ').Contains("partial");
                return new Declaration($"{name}`{arity}", name, file, IsOwnType: index == 0, isPartial);
            });
    }

    private static string Describe(Declaration declaration, string moduleName) =>
        declaration.IsOwnType
            ? $"as the type of Daml {declaration.Origin.DamlKind} '{moduleName}:{declaration.Origin.DamlName}'"
            : $"as a type generated for Daml {declaration.Origin.DamlKind} '{moduleName}:{declaration.Origin.DamlName}'";
}
