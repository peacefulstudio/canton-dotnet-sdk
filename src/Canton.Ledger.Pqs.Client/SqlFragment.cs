// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Canton.Ledger.Pqs.Client;

internal sealed partial class SqlFragment
{
    private SqlFragment(ImmutableArray<object> parts) => Parts = parts;

    internal ImmutableArray<object> Parts { get; }

    public static SqlFragment Of(SqlFragmentBuilder builder) => new(builder.ToParts());

    public static SqlFragment Parameter(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new([new BoundValue(value)]);
    }

    public static SqlFragment JsonKey(string fieldName)
    {
        if (!SafeFieldNamePattern().IsMatch(fieldName))
            throw new ArgumentException($"Invalid field name: '{fieldName}'");
        return new([$"'{fieldName}'"]);
    }

    public static SqlFragment Join(SqlFragment separator, IEnumerable<SqlFragment> fragments)
    {
        var parts = ImmutableArray.CreateBuilder<object>();
        var first = true;
        foreach (var fragment in fragments)
        {
            if (!first)
                parts.AddRange(separator.Parts);
            parts.AddRange(fragment.Parts);
            first = false;
        }

        return new(parts.ToImmutable());
    }

    public static SqlFragment Alias(char prefix, int index) =>
        new([string.Create(CultureInfo.InvariantCulture, $"{prefix}{index}")]);

    public string Render(ICollection<(string Name, object Value)> parameters, ref int paramIndex)
    {
        var sql = new StringBuilder();
        foreach (var part in Parts)
        {
            if (part is BoundValue bound)
            {
                var name = string.Create(CultureInfo.InvariantCulture, $"@p{paramIndex++}");
                parameters.Add((name, bound.Value));
                sql.Append(name);
            }
            else
            {
                sql.Append((string)part);
            }
        }

        return sql.ToString();
    }

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex SafeFieldNamePattern();

    private sealed record BoundValue(object Value);
}

[InterpolatedStringHandler]
internal readonly ref struct SqlFragmentBuilder
{
    private readonly ImmutableArray<object>.Builder parts;

    public SqlFragmentBuilder(int literalLength, int formattedCount) =>
        parts = ImmutableArray.CreateBuilder<object>((2 * formattedCount) + 1);

    public void AppendLiteral(string literal) => parts.Add(literal);

    public void AppendFormatted(SqlFragment fragment) => parts.AddRange(fragment.Parts);

    internal ImmutableArray<object> ToParts() => parts.ToImmutable();
}
