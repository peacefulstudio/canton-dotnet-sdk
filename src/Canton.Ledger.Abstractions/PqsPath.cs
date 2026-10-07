// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace Canton.Ledger.Abstractions;

internal abstract class PqsPath
{
    public abstract T Accept<T>(IPqsPathVisitor<T> visitor);
}

internal sealed class PqsScopeRoot(PqsScope scope) : PqsPath
{
    public PqsScope Scope { get; } = scope;

    public override T Accept<T>(IPqsPathVisitor<T> visitor) => visitor.Visit(this);
}

internal sealed partial class PqsField : PqsPath
{
    public PqsField(PqsPath parent, string name)
    {
        if (!SafeNamePattern().IsMatch(name))
            throw new ArgumentException($"Invalid field name: '{name}'");
        Parent = parent;
        Name = name;
    }

    public PqsPath Parent { get; }

    public string Name { get; }

    public override T Accept<T>(IPqsPathVisitor<T> visitor) => visitor.Visit(this);

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex SafeNamePattern();
}

internal sealed class PqsFirstElement(PqsPath optional) : PqsPath
{
    public PqsPath Optional { get; } = optional;

    public override T Accept<T>(IPqsPathVisitor<T> visitor) => visitor.Visit(this);
}

internal sealed class PqsSomeCast(PqsPath optional, bool listEncoded) : PqsPath
{
    public PqsPath Optional { get; } = optional;

    public bool ListEncoded { get; } = listEncoded;

    public override T Accept<T>(IPqsPathVisitor<T> visitor) => visitor.Visit(this);
}

internal sealed class PqsConstructorCast(PqsPath variant, string tag) : PqsPath
{
    public PqsPath Variant { get; } = variant;

    public string Tag { get; } = tag;

    public override T Accept<T>(IPqsPathVisitor<T> visitor) => visitor.Visit(this);
}

internal sealed class PqsMapEntry(PqsPath map, PqsOperand key, int aliasOrdinal) : PqsPath
{
    public PqsPath Map { get; } = map;

    public PqsOperand Key { get; } = key;

    public int AliasOrdinal { get; } = aliasOrdinal;

    public override T Accept<T>(IPqsPathVisitor<T> visitor) => visitor.Visit(this);
}

internal sealed class PqsDefaulted(PqsPath value, PqsOperand @default) : PqsPath
{
    public PqsPath Value { get; } = value;

    public PqsOperand Default { get; } = @default;

    public override T Accept<T>(IPqsPathVisitor<T> visitor) => visitor.Visit(this);
}

internal sealed class PqsScope
{
    private PqsScope(int? aliasOrdinal) => AliasOrdinal = aliasOrdinal;

    public static PqsScope Payload { get; } = new(null);

    public int? AliasOrdinal { get; }

    public static PqsScope Element(int aliasOrdinal) => new(aliasOrdinal);
}
