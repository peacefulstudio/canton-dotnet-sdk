// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.DarParser;

/// <summary>
/// Reads the top-level values of a Daml-LF module whose whole body is one Text, Int64, Bool or
/// Numeric literal. Only a value a developer wrote qualifies: a name the compiler minted
/// (<c>$$cshow</c>, <c>$$maxtag_...</c>: damlc escapes the <c>$</c> of every compiler name as <c>$$</c>), a name with more than one segment, a generic value (its body is
/// a type abstraction), a computation, a reference to another value, and every other literal kind
/// (unit, date, timestamp) are left out without a trace.
/// </summary>
internal static partial class LiteralConstantReader
{
    [GeneratedRegex(@"\A(?:[A-Za-z0-9_]|\$u[0-9a-f]{4}|\$U[0-9a-f]{8})+\z")]
    private static partial Regex UserWrittenName();

    [GeneratedRegex(@"\A(?<sign>[+-]?)(?<integer>[0-9]+)(?:\.(?<fraction>[0-9]*))?\z")]
    private static partial Regex NumericLiteral();

    internal static IReadOnlyList<DamlConstant> Read(Module module, PackageContext ctx)
    {
        var constants = new List<DamlConstant>();
        foreach (var value in module.Values)
        {
            var segments = ctx.GetDottedNameSegments(value.NameWithType.NameInternedDname);
            if (segments.Count != 1 || !UserWrittenName().IsMatch(segments[0]))
            {
                continue;
            }

            if (ValueOf(value.Expr, segments[0], ctx) is { } literal)
            {
                constants.Add(new DamlConstant(segments[0], literal));
            }
        }
        return constants.OrderBy(constant => constant.Name, StringComparer.Ordinal).ToList();
    }

    private static DamlConstantValue? ValueOf(Expr? expr, string name, PackageContext ctx)
    {
        var current = expr;
        var followed = new HashSet<int>();
        while (current is { SumCase: Expr.SumOneofCase.InternedExpr })
        {
            if (!followed.Add(current.InternedExpr))
            {
                return null;
            }
            current = ctx.GetInternedExpr(current.InternedExpr);
        }

        return current?.SumCase switch
        {
            Expr.SumOneofCase.BuiltinCon => current.BuiltinCon switch
            {
                BuiltinCon.ConTrue => new DamlBoolConstantValue(true),
                BuiltinCon.ConFalse => new DamlBoolConstantValue(false),
                _ => null,
            },
            Expr.SumOneofCase.BuiltinLit => current.BuiltinLit.SumCase switch
            {
                BuiltinLit.SumOneofCase.Int64 => new DamlInt64ConstantValue(current.BuiltinLit.Int64),
                BuiltinLit.SumOneofCase.TextInternedStr => new DamlTextConstantValue(ctx.GetString(current.BuiltinLit.TextInternedStr)),
                BuiltinLit.SumOneofCase.NumericInternedStr => new DamlNumericConstantValue(
                    PlainDecimal(ctx.GetString(current.BuiltinLit.NumericInternedStr), name)),
                _ => null,
            },
            _ => null,
        };
    }

    private static string PlainDecimal(string archived, string name)
    {
        var match = NumericLiteral().Match(archived);
        if (!match.Success)
        {
            throw new InvalidDataException(
                $"Daml-LF value '{name}' holds the numeric literal '{archived}', which is not decimal notation " +
                "(optional sign, digits, optional '.' and digits). The archive is malformed.");
        }

        var sign = match.Groups["sign"].Value == "-" ? "-" : string.Empty;
        var fraction = match.Groups["fraction"].Value;
        return fraction.Length == 0
            ? $"{sign}{match.Groups["integer"].Value}"
            : $"{sign}{match.Groups["integer"].Value}.{fraction}";
    }
}
