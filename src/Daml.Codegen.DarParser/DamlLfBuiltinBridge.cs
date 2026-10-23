// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using LfBuiltin = Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.BuiltinType;
using PbBuiltin = Daml.Codegen.Intermediate.BuiltinType;

namespace Daml.Codegen.DarParser;

/// <summary>
/// The Daml-LF → Intermediate-proto <c>BuiltinType</c> bridge. The two enums spell the
/// same builtin set with different members and numbering — Daml-LF 2 has
/// <c>UNIT = 0</c>, no unspecified value, and <c>BIGNUMERIC = 1002</c> /
/// <c>ROUNDING_MODE = 1003</c> above a reserved field, while the intermediate proto has
/// <c>BUILTIN_TYPE_UNSPECIFIED = 0</c> through <c>BUILTIN_TYPE_FAILURE_CATEGORY = 22</c>
/// with <c>BUILTIN_TYPE_SCENARIO = 20</c> in the middle — so
/// <see cref="PackageContext"/>'s builtin conversion cannot consult
/// <see cref="DamlPrimitiveCatalog"/> on the Daml-LF enum directly. Every defined
/// Daml-LF value bridges to its identical intermediate-proto value; a value the bridge
/// does not know (a newer Daml-LF schema, or malformed wire data casting an undefined
/// number to the enum) fails loudly instead of guessing.
/// </summary>
internal static class DamlLfBuiltinBridge
{
    /// <summary>
    /// Returns the intermediate-proto <see cref="PbBuiltin"/> value naming the same
    /// builtin as <paramref name="builtin"/>.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// <paramref name="builtin"/> is not a defined Daml-LF <c>BuiltinType</c> value the
    /// bridge knows.
    /// </exception>
    public static PbBuiltin ToProtoBuiltin(LfBuiltin builtin) => builtin switch
    {
        LfBuiltin.Unit => PbBuiltin.Unit,
        LfBuiltin.Bool => PbBuiltin.Bool,
        LfBuiltin.Int64 => PbBuiltin.Int64,
        LfBuiltin.Date => PbBuiltin.Date,
        LfBuiltin.Timestamp => PbBuiltin.Timestamp,
        LfBuiltin.Numeric => PbBuiltin.Numeric,
        LfBuiltin.Party => PbBuiltin.Party,
        LfBuiltin.Text => PbBuiltin.Text,
        LfBuiltin.ContractId => PbBuiltin.ContractId,
        LfBuiltin.Optional => PbBuiltin.Optional,
        LfBuiltin.List => PbBuiltin.List,
        LfBuiltin.Genmap => PbBuiltin.GenMap,
        LfBuiltin.Any => PbBuiltin.Any,
        LfBuiltin.AnyException => PbBuiltin.AnyException,
        LfBuiltin.TypeRep => PbBuiltin.TypeRep,
        LfBuiltin.Arrow => PbBuiltin.Arrow,
        LfBuiltin.Update => PbBuiltin.Update,
        LfBuiltin.FailureCategory => PbBuiltin.FailureCategory,
        LfBuiltin.Textmap => PbBuiltin.TextMap,
        LfBuiltin.Bignumeric => PbBuiltin.Bignumeric,
        LfBuiltin.RoundingMode => PbBuiltin.RoundingMode,
        _ => throw new NotSupportedException(
            $"Daml-LF BuiltinType '{builtin}' is unknown and has no C# mapping. " +
            "Update DamlLfBuiltinBridge.ToProtoBuiltin (and the emitter, runtime, and JSON serializer) to add the mapping."),
    };
}
