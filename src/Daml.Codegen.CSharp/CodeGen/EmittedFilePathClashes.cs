// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Fails an emit whose files would overwrite one another on a case-insensitive file system. Two
/// Daml declarations whose names differ only by case (<c>Foo</c> and <c>FOO</c>, or a module
/// <c>A.Foo</c> beside <c>A.FOO</c>) are distinct in Daml and emit distinct C# types, yet on macOS
/// and Windows their files are one path and the second write replaces the first. Paths are compared
/// whole, directory segments included, across every package of one generation.
/// </summary>
internal sealed class EmittedFilePathClashes
{
    private sealed record Origin(string PackageName, string ModuleName, EmittedDeclarationFile Declaration);

    private readonly Dictionary<string, Origin> _firstOriginByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Records the files of one module and throws when any of them has the path of a file recorded
    /// earlier, ignoring case.
    /// </summary>
    /// <exception cref="CodegenException">Two files would share a path on a case-insensitive file system.</exception>
    internal void Require(string packageName, string moduleName, IEnumerable<EmittedDeclarationFile> files)
    {
        foreach (var declaration in files)
        {
            var origin = new Origin(packageName, moduleName, declaration);
            if (_firstOriginByPath.TryAdd(declaration.File.RelativePath, origin))
            {
                continue;
            }

            var first = _firstOriginByPath[declaration.File.RelativePath];
            throw new CodegenException(
                $"The generated files for {Describe(first)} and {Describe(origin)} would collide on a case-insensitive file system: " +
                $"'{first.Declaration.File.RelativePath}' and '{declaration.File.RelativePath}' are one file on macOS and Windows. " +
                "Rename one of them in Daml.");
        }
    }

    private static string Describe(Origin origin) =>
        $"Daml {origin.Declaration.DamlKind} '{origin.PackageName}:{origin.ModuleName}:{origin.Declaration.DamlName}'";
}
