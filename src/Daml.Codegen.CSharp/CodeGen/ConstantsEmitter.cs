// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Numerics;
using System.Text;
using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>A literal constant ready to be written: the C# type and the C# literal that spells its value.</summary>
/// <param name="DamlName">The Daml value name.</param>
/// <param name="CSharpType">The C# keyword type of the <c>const</c>: <c>string</c>, <c>long</c>, <c>bool</c> or <c>decimal</c>.</param>
/// <param name="Literal">The C# literal, ASCII only.</param>
internal sealed record RenderedConstant(string DamlName, string CSharpType, string Literal);

/// <summary>
/// Emits the top-level Daml values whose body is a single literal as <c>public const</c> fields of a
/// static class in the module's namespace. Text, Int and Bool always have a faithful C# constant; a
/// Numeric has one only when <see cref="decimal"/> holds it exactly, and is otherwise left out.
/// </summary>
internal static class ConstantsEmitter
{
    internal const string ClassNameStem = "Constants";

    private const int MaxDecimalFractionDigits = 28;

    private static readonly BigInteger MaxDecimalUnscaled = (BigInteger.One << 96) - 1;

    /// <summary>
    /// The class name: <c>Constants</c> with trailing <c>_</c>s until it is not in
    /// <paramref name="takenNames"/>.
    /// </summary>
    internal static string ClassName(IReadOnlySet<string> takenNames)
    {
        var name = ClassNameStem;
        while (takenNames.Contains(name))
        {
            name += "_";
        }
        return name;
    }

    /// <summary>
    /// Renders <paramref name="constant"/>, or returns <c>null</c> for a Numeric that
    /// <see cref="decimal"/> cannot hold exactly: more than 28 fraction digits or an unscaled value
    /// above 2^96 - 1, both measured after trailing fraction zeros are dropped.
    /// </summary>
    internal static RenderedConstant? Render(DamlConstant constant)
    {
        ArgumentNullException.ThrowIfNull(constant);

        return constant.Value switch
        {
            DamlTextConstantValue text => new RenderedConstant(constant.Name, "string", TextLiteral(text.Value)),
            DamlInt64ConstantValue int64 => new RenderedConstant(constant.Name, "long", int64.Value.ToString(CultureInfo.InvariantCulture)),
            DamlBoolConstantValue boolean => new RenderedConstant(constant.Name, "bool", boolean.Value ? "true" : "false"),
            DamlNumericConstantValue numeric => TryDecimalLiteral(numeric.Value, out var literal)
                ? new RenderedConstant(constant.Name, "decimal", literal)
                : null,
            _ => throw new CodegenException($"Constant '{constant.Name}' carries the unsupported literal kind {constant.Value.GetType().Name}."),
        };
    }

    /// <summary>
    /// Writes the static class <paramref name="className"/> declaring one <c>const</c> per
    /// <paramref name="constants"/> entry, named by <see cref="Identifiers.MemberName(string, string, IReadOnlySet{string})"/>
    /// with a trailing <c>_</c> on a name an earlier constant already took.
    /// </summary>
    internal static void WriteClass(
        IndentWriter indent,
        CodeGenOptions options,
        string moduleName,
        string className,
        IReadOnlyList<RenderedConstant> constants)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine($"/// <summary>Top-level literal values of Daml module {EmitterHelpers.EscapeXmlText(moduleName)}.</summary>");
        }
        indent.AppendLine($"public static class {className}");
        indent.AppendLine("{");
        indent.Indent();

        var reserved = ReservedMemberNames.OfConstantsClass();
        var takenMembers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var constant in constants)
        {
            var member = Identifiers.MemberName(constant.DamlName, className, reserved);
            while (!takenMembers.Add(member))
            {
                member += "_";
            }

            if (options.GenerateXmlDocs)
            {
                indent.AppendLine($"/// <summary>Daml value {EmitterHelpers.EscapeXmlText(constant.DamlName)} of module {EmitterHelpers.EscapeXmlText(moduleName)}.</summary>");
            }
            indent.AppendLine($"public const {constant.CSharpType} {member} = {constant.Literal};");
        }

        indent.Dedent();
        indent.AppendLine("}");
    }

    private static string TextLiteral(string value)
    {
        var literal = new StringBuilder("\"");
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    literal.Append("\\\"");
                    break;
                case '\\':
                    literal.Append("\\\\");
                    break;
                case '\n':
                    literal.Append("\\n");
                    break;
                case '\r':
                    literal.Append("\\r");
                    break;
                case '\t':
                    literal.Append("\\t");
                    break;
                case >= ' ' and <= '~':
                    literal.Append(c);
                    break;
                default:
                    literal.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}");
                    break;
            }
        }
        return literal.Append('"').ToString();
    }

    private static bool TryDecimalLiteral(string plainDecimal, out string literal)
    {
        var negative = plainDecimal.StartsWith('-');
        var magnitude = negative ? plainDecimal[1..] : plainDecimal;
        var separator = magnitude.IndexOf('.', StringComparison.Ordinal);
        var integerDigits = (separator < 0 ? magnitude : magnitude[..separator]).TrimStart('0');
        var fractionDigits = (separator < 0 ? string.Empty : magnitude[(separator + 1)..]).TrimEnd('0');

        var unscaled = BigInteger.Parse(integerDigits + fractionDigits + "0", NumberStyles.None, CultureInfo.InvariantCulture) / 10;
        if (fractionDigits.Length > MaxDecimalFractionDigits || unscaled > MaxDecimalUnscaled)
        {
            literal = string.Empty;
            return false;
        }

        var integerText = integerDigits.Length == 0 ? "0" : integerDigits;
        var digits = fractionDigits.Length == 0 ? integerText : $"{integerText}.{fractionDigits}";
        literal = (negative && !unscaled.IsZero ? "-" : string.Empty) + digits + "m";
        return true;
    }
}
