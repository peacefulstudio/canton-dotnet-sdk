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
    private readonly Func<object, object> toParameter;
    private readonly Func<string, object> parse;

    private PqsLeafType(
        PqsLeafKind kind,
        bool isOrdered,
        Func<object, object> toParameter,
        Func<string, object> parse,
        bool hasDefault = true)
    {
        Kind = kind;
        IsOrdered = isOrdered;
        this.toParameter = toParameter;
        this.parse = parse;
        HasDefault = hasDefault;
    }

    public PqsLeafKind Kind { get; }

    public string DamlTypeName => Kind.ToString();

    public bool IsOrdered { get; }

    public bool HasDefault { get; }

    public PqsOperand Operand(object value) => new(Kind, toParameter(value));

    public PqsOperand DefaultOperand(Type clrType) =>
        Operand(Activator.CreateInstance(Nullable.GetUnderlyingType(clrType) ?? clrType)!);

    public PqsOperand ParseOperand(string value, string paramName)
    {
        try
        {
            return new(Kind, parse(value));
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

    private static readonly PqsLeafType Text = new(PqsLeafKind.Text, false, value => value, value => value);

    private static readonly PqsLeafType Int64 = new(
        PqsLeafKind.Int64,
        true,
        value => value,
        value => long.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));

    private static readonly PqsLeafType Numeric = new(PqsLeafKind.Numeric, true, value => value, NumericLiteral);

    private static readonly PqsLeafType Bool = new(PqsLeafKind.Bool, false, value => value, value => bool.Parse(value));

    private static readonly PqsLeafType Date = new(
        PqsLeafKind.Date,
        true,
        value => value,
        value => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture));

    private static readonly PqsLeafType Time = new(
        PqsLeafKind.Time,
        true,
        value => ((DateTimeOffset)value).ToUniversalTime(),
        value => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime());

    private static readonly PqsLeafType PartyLeaf = new(
        PqsLeafKind.Party, false, value => ((Party)value).Value, value => value, hasDefault: false);

    private static readonly PqsLeafType ContractIdLeaf = new(
        PqsLeafKind.ContractId, false, value => ((ContractId)value).Value, value => value);

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
            PqsLeafKind.Enum,
            false,
            value => ((DamlEnum)toDamlEnum.Invoke(null, [Enum.ToObject(enumType, value)])!).Constructor,
            value => value);
    }

    private static string NumericLiteral(string value) =>
        NumericLiteralPattern().IsMatch(value)
            ? value
            : throw new FormatException($"'{value}' is not a numeric literal.");

    [GeneratedRegex(@"^[+-]?(\d+\.?\d*|\.\d+)$")]
    private static partial Regex NumericLiteralPattern();
}
