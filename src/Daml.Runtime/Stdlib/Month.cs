// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Stdlib;

/// <summary>
/// Daml stdlib enum DA.Date.Types.Month. Hand-coded into Daml.Runtime since
/// daml-stdlib is a frozen stdlib package (no NuGet equivalent) whose enums are
/// referenced by Daml Finance and Splice DARs. Mirrors the wire format emitted for
/// generated enums: each constructor round-trips through <see cref="DamlEnum"/> by name.
/// </summary>
public enum Month
{
    /// <summary>January.</summary>
    Jan,
    /// <summary>February.</summary>
    Feb,
    /// <summary>March.</summary>
    Mar,
    /// <summary>April.</summary>
    Apr,
    /// <summary>May.</summary>
    May,
    /// <summary>June.</summary>
    Jun,
    /// <summary>July.</summary>
    Jul,
    /// <summary>August.</summary>
    Aug,
    /// <summary>September.</summary>
    Sep,
    /// <summary>October.</summary>
    Oct,
    /// <summary>November.</summary>
    Nov,
    /// <summary>December.</summary>
    Dec,
}

/// <summary>Extension methods for <see cref="Month"/> serialization.</summary>
public static class MonthExtensions
{
    /// <summary>Converts to a DamlEnum value.</summary>
    public static DamlEnum ToDamlEnum(this Month value) => value switch
    {
        Month.Jan => DamlEnum.Create("Jan"),
        Month.Feb => DamlEnum.Create("Feb"),
        Month.Mar => DamlEnum.Create("Mar"),
        Month.Apr => DamlEnum.Create("Apr"),
        Month.May => DamlEnum.Create("May"),
        Month.Jun => DamlEnum.Create("Jun"),
        Month.Jul => DamlEnum.Create("Jul"),
        Month.Aug => DamlEnum.Create("Aug"),
        Month.Sep => DamlEnum.Create("Sep"),
        Month.Oct => DamlEnum.Create("Oct"),
        Month.Nov => DamlEnum.Create("Nov"),
        Month.Dec => DamlEnum.Create("Dec"),
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    /// <summary>Creates an instance from a DamlEnum value.</summary>
    public static Month FromDamlEnum(DamlEnum value) => value.Constructor switch
    {
        "Jan" => Month.Jan,
        "Feb" => Month.Feb,
        "Mar" => Month.Mar,
        "Apr" => Month.Apr,
        "May" => Month.May,
        "Jun" => Month.Jun,
        "Jul" => Month.Jul,
        "Aug" => Month.Aug,
        "Sep" => Month.Sep,
        "Oct" => Month.Oct,
        "Nov" => Month.Nov,
        "Dec" => Month.Dec,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value.Constructor, null)
    };

    /// <summary>
    /// Decodes this enum's Daml-LF JSON — a bare constructor string — into its wire value.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [SuppressMessage(
        "Naming", "CA1707:Identifiers should not contain underscores",
        Justification = "The double-underscore prefix is the emitted-plumbing naming convention shared "
            + "by every __ReadDamlLfJson member; it marks a compiler-dispatched member no hand-written "
            + "call site should name, the same intent EditorBrowsable(Never) signals.")]
    public static DamlEnum __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
        DamlLfJsonDecoders.ReadEnumConstructor(json, context, ExpectedConstructors);

    private static readonly string[] ExpectedConstructors =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
}
