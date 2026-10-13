// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Data;

/// <summary>
/// Represents a Daml type identifier (package ID + module + entity name).
/// </summary>
/// <param name="PackageId">The package ID (hash).</param>
/// <param name="ModuleName">The fully qualified module name.</param>
/// <param name="EntityName">The entity name within the module.</param>
public sealed record Identifier(
    string PackageId,
    string ModuleName,
    string EntityName)
{
    /// <summary>
    /// Gets the fully qualified name (module.entity).
    /// </summary>
    public string FullyQualifiedName => $"{ModuleName}:{EntityName}";

    /// <summary>Returns the canonical <c>packageId:module:entity</c> form accepted by <see cref="Parse"/>.</summary>
    public override string ToString() => $"{PackageId}:{ModuleName}:{EntityName}";

    internal Identifier AddressedByPackageName(string packageName) =>
        PackageNames.IsPlaceholder(packageName) ? this : this with { PackageId = $"#{packageName}" };

    /// <summary>
    /// Parses an identifier from its string representation.
    /// </summary>
    public static Identifier Parse(string value)
    {
        var parts = value.Split(':');
        return parts.Length switch
        {
            3 => new Identifier(parts[0], parts[1], parts[2]),
            2 => new Identifier(string.Empty, parts[0], parts[1]),
            _ => throw new FormatException($"Invalid identifier format: {value}")
        };
    }
}
