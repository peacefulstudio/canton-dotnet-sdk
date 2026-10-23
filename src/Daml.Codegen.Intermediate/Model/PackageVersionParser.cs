// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.RegularExpressions;

namespace Daml.Codegen.Intermediate.Model;

/// <summary>
/// Parses a Daml package version string into a <see cref="Version"/>. The
/// DAR-intrinsic version is a 3-part <c>Major.Minor.Patch</c> shape;
/// the optional 4th segment of the generated package's M.m.p.g NuGet version
/// is added downstream at pack time. Used by both the proto-direct
/// <c>IntermediateDarReader</c> and the parser-direct <c>DalfReader</c>
/// so the two paths report identical versions for the same DAR.
/// </summary>
public static class PackageVersionParser
{
    private static readonly Regex LeadingSegments =
        new(@"^(\d+)(?:\.(\d+))?(?:\.(\d+))?", RegexOptions.Compiled);

    /// <summary>
    /// Parses a Daml package version. Accepts one or more dot-separated numeric
    /// segments: a missing minor or patch segment reads as <c>0</c> (<c>2</c> is
    /// <c>2.0.0</c>, <c>1.0</c> is <c>1.0.0</c>), segments beyond the third and any
    /// trailing characters are ignored. Returns
    /// <see cref="Version">Version(0, 0, 0)</see> when the input does not start with
    /// a number or a segment overflows <see cref="int"/>.
    /// </summary>
    public static Version Parse(string? raw)
    {
        if (raw is null) return new Version(0, 0, 0);
        var match = LeadingSegments.Match(raw);
        if (!match.Success) return new Version(0, 0, 0);
        if (!TryParseSegment(match.Groups[1], out var major)
            || !TryParseSegment(match.Groups[2], out var minor)
            || !TryParseSegment(match.Groups[3], out var patch))
        {
            return new Version(0, 0, 0);
        }
        return new Version(major, minor, patch);
    }

    private static bool TryParseSegment(Group segment, out int value)
    {
        value = 0;
        return !segment.Success
            || int.TryParse(segment.Value, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
