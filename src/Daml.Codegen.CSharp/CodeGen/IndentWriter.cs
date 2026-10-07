// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Helper for writing indented code.
/// </summary>
internal sealed class IndentWriter(StringBuilder sb)
{
    private int _indentLevel;
    private const string IndentString = "    ";

    private readonly SortedSet<string> _requiredUsings = new(StringComparer.Ordinal);

    public string CurrentTypeName { get; set; } = "";

    public IReadOnlySet<string> CurrentReservedMemberNames { get; set; } = new HashSet<string>();

    /// <summary>Records that the given namespace is referenced in this file.</summary>
    public void Require(string ns) => _requiredUsings.Add(ns);

    /// <summary>Returns the sorted set of namespaces required by this file.</summary>
    public IReadOnlyCollection<string> RequiredUsings => _requiredUsings;

    private bool _atLineStart = true;

    public void Indent() => _indentLevel++;
    public void Dedent() => _indentLevel = Math.Max(0, _indentLevel - 1);

    public void Append(string text)
    {
        WriteIndentIfAtLineStart();
        sb.Append(text);
    }

    private void WriteIndentIfAtLineStart()
    {
        if (!_atLineStart)
        {
            return;
        }
        for (int i = 0; i < _indentLevel; i++)
        {
            sb.Append(IndentString);
        }
        _atLineStart = false;
    }

    /// <remarks>
    /// Line endings are written explicitly rather than through <c>StringBuilder.AppendLine</c>,
    /// which uses <see cref="Environment.NewLine"/>. Generated source ships in NuGet packages and
    /// is compared byte for byte by the drift tests, so an OS-dependent newline would make a
    /// Windows codegen run differ from a macOS or Linux one for no change in behaviour. LF is the
    /// conventional choice for cross-platform source distribution and matches what
    /// <c>.editorconfig</c> pins repo-wide.
    /// </remarks>
    private const char Newline = '\n';

    public void AppendLine(string? line = null)
    {
        if (line is not null)
        {
            WriteIndentIfAtLineStart();
            sb.Append(line);
        }
        sb.Append(Newline);
        _atLineStart = true;
    }
}