// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Canton.Ledger.Abstractions;

internal sealed partial class PqsLeafType
{
    private readonly Func<SqlFragment, SqlFragment> typedText;
    private readonly Func<object, object> toParameter;
    private readonly Func<string, SqlFragment> parse;

    private PqsLeafType(
        string damlTypeName,
        bool isOrdered,
        Func<SqlFragment, SqlFragment> typedText,
        Func<object, object> toParameter,
        Func<string, SqlFragment> parse,
        bool hasDefault = true)
    {
        DamlTypeName = damlTypeName;
        IsOrdered = isOrdered;
        this.typedText = typedText;
        this.toParameter = toParameter;
        this.parse = parse;
        HasDefault = hasDefault;
    }

    public string DamlTypeName { get; }

    public bool IsOrdered { get; }

    public bool HasDefault { get; }

    public SqlFragment Typed(SqlFragment text) => typedText(text);

    public SqlFragment Parameter(object value) => SqlFragment.Parameter(toParameter(value));

    public SqlFragment DefaultParameter(Type clrType) =>
        Parameter(Activator.CreateInstance(Nullable.GetUnderlyingType(clrType) ?? clrType)!);

    public SqlFragment ParseParameter(string value, string paramName)
    {
        try
        {
            return parse(value);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException($"'{value}' is not a valid Daml {DamlTypeName} value.", paramName, ex);
        }
        catch (OverflowException ex)
        {
            throw new ArgumentException($"'{value}' is out of range for a Daml {DamlTypeName} value.", paramName, ex);
        }
    }

    public static PqsLeafType? For(Type clrType)
    {
        var type = Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (type == typeof(string)) return Text;
        if (type == typeof(long)) return Int64;
        if (type == typeof(decimal)) return Numeric;
        if (type == typeof(bool)) return Bool;
        if (type == typeof(DateOnly)) return Date;
        if (type == typeof(DateTimeOffset)) return Time;
        if (type == typeof(Party)) return PartyLeaf;
        if (typeof(ContractId).IsAssignableFrom(type)) return ContractIdLeaf;
        if (type.IsEnum) return EnumLeaf(type);
        return null;
    }

    private static readonly PqsLeafType Text = new(
        "Text", false, text => text, value => value, value => SqlFragment.Parameter(value));

    private static readonly PqsLeafType Int64 = new(
        "Int64",
        true,
        text => SqlFragment.Of($"({text})::bigint"),
        value => value,
        value => SqlFragment.Parameter(long.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)));

    private static readonly PqsLeafType Numeric = new(
        "Numeric",
        true,
        text => SqlFragment.Of($"({text})::numeric"),
        value => value,
        NumericLiteral);

    private static readonly PqsLeafType Bool = new(
        "Bool",
        false,
        text => SqlFragment.Of($"({text})::boolean"),
        value => value,
        value => SqlFragment.Parameter(bool.Parse(value)));

    private static readonly PqsLeafType Date = new(
        "Date",
        true,
        text => SqlFragment.Of($"({text})::date"),
        value => value,
        value => SqlFragment.Parameter(DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture)));

    private static readonly PqsLeafType Time = new(
        "Time",
        true,
        text => SqlFragment.Of($"({text})::timestamptz"),
        value => ((DateTimeOffset)value).ToUniversalTime(),
        value => SqlFragment.Parameter(
            DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime()));

    private static readonly PqsLeafType PartyLeaf = new(
        "Party", false, text => text, value => ((Party)value).Value, value => SqlFragment.Parameter(value), hasDefault: false);

    private static readonly PqsLeafType ContractIdLeaf = new(
        "ContractId", false, text => text, value => ((ContractId)value).Value, value => SqlFragment.Parameter(value));

    private static PqsLeafType EnumLeaf(Type enumType)
    {
        var toDamlEnum = enumType.Assembly
            .GetType($"{enumType.Namespace}.{enumType.Name}Extensions")
            ?.GetMethod("ToDamlEnum", BindingFlags.Public | BindingFlags.Static, [enumType]);
        if (toDamlEnum is null || toDamlEnum.ReturnType != typeof(DamlEnum))
            throw new InvalidOperationException(
                $"Enum '{enumType.Name}' has no generated '{enumType.Name}Extensions.ToDamlEnum' mapping, so its " +
                $"Daml constructor names cannot be resolved. Regenerate the Daml bindings; the typed filter DSL " +
                $"never guesses a Daml constructor name from a C# enum member name.");

        return new(
            "Enum",
            false,
            text => text,
            value => ((DamlEnum)toDamlEnum.Invoke(null, [Enum.ToObject(enumType, value)])!).Constructor,
            value => SqlFragment.Parameter(value));
    }

    private static SqlFragment NumericLiteral(string value)
    {
        if (!NumericLiteralPattern().IsMatch(value))
            throw new FormatException($"'{value}' is not a numeric literal.");
        return SqlFragment.Of($"{SqlFragment.Parameter(value)}::numeric");
    }

    [GeneratedRegex(@"^[+-]?(\d+\.?\d*|\.\d+)$")]
    private static partial Regex NumericLiteralPattern();
}
