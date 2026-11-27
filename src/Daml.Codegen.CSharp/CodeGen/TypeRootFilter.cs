// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// The <see cref="CodeGenOptions.RootFilter"/> rule: which <c>Module:Type</c> names the emitter
/// writes files for. An absent pattern admits everything. Beside the names the pattern matches,
/// the filter admits the <c>Module:Type</c> names in <paramref name="closure"/>: the templates and
/// interfaces <see cref="RootFilterClosure"/> found the emitted code refers to.
/// </summary>
internal sealed class TypeRootFilter(string? pattern, IReadOnlySet<string> closure)
{
    internal TypeRootFilter(string? pattern)
        : this(pattern, new HashSet<string>(StringComparer.Ordinal))
    {
    }

    internal static TypeRootFilter IncludeAll { get; } = new(null);

    /// <summary>The regular expression this filter matches <c>Module:Type</c> names against, or <c>null</c> when it admits everything.</summary>
    internal string? Pattern { get; } = pattern;

    /// <summary>What two filters admit the same names under: the pattern and the closure names, ordinally sorted.</summary>
    internal string? CacheKey { get; } = pattern is null
        ? null
        : string.Join('\n', closure.Order(StringComparer.Ordinal).Prepend(pattern));

    /// <summary>This filter with <paramref name="closureNames"/> admitted as well.</summary>
    internal TypeRootFilter Admitting(IEnumerable<string> closureNames) =>
        new(Pattern, closure.Concat(closureNames).ToHashSet(StringComparer.Ordinal));

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private readonly Regex? _regex = pattern is null ? null : Compile(pattern);

    internal bool Includes(string moduleName, string typeName)
    {
        if (_regex is null || closure.Contains($"{moduleName}:{typeName}"))
        {
            return true;
        }
        try
        {
            return _regex.IsMatch($"{moduleName}:{typeName}");
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw new CodegenException(
                $"The RootFilter pattern '{Pattern}' timed out after {MatchTimeout.TotalSeconds:0} second(s) matching "
                + $"'{moduleName}:{typeName}'. Simplify the pattern to avoid catastrophic backtracking.",
                ex);
        }
    }

    private static Regex Compile(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new CodegenException($"The RootFilter pattern '{pattern}' is not a valid regular expression: {ex.Message}", ex);
        }
    }
}
