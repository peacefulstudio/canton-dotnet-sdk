// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// The <see cref="CodeGenOptions.RootFilter"/> rule: which <c>Module:Type</c> names the emitter
/// writes files for. An absent pattern admits everything.
/// </summary>
internal sealed class TypeRootFilter(string? pattern)
{
    internal static TypeRootFilter IncludeAll { get; } = new(null);

    /// <summary>The regular expression this filter matches <c>Module:Type</c> names against, or <c>null</c> when it admits everything.</summary>
    internal string? Pattern { get; } = pattern;

    private readonly Regex? _regex = pattern is null ? null : new Regex(pattern, RegexOptions.Compiled);

    internal bool Includes(string moduleName, string typeName) =>
        _regex is null || _regex.IsMatch($"{moduleName}:{typeName}");
}
