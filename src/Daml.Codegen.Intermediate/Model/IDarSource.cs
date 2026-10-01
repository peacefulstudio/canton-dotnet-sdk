// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.Intermediate.Model;

/// <summary>
/// Emitter input contract — a main package plus dependency packages, with
/// per-package-id lookup. Two production adapters implement it: the
/// proto-direct <see cref="DarModel"/>, produced by
/// <c>IntermediateDarReader</c> from an <c>IntermediateDar</c> message, and a
/// DAR-direct adapter, produced by reading a <c>.dar</c> file. Keeps the emitter library decoupled from how the
/// DAR is decoded.
/// </summary>
public interface IDarSource
{
    /// <summary>The main package being generated.</summary>
    DamlPackage MainPackage { get; }

    /// <summary>The dependency packages (used for cross-package type resolution).</summary>
    IReadOnlyList<DamlPackage> Dependencies { get; }

    /// <summary>All packages — main first, then dependencies.</summary>
    IEnumerable<DamlPackage> AllPackages => Dependencies.Prepend(MainPackage);

    /// <summary>Returns the package with the given id, or null if absent.</summary>
    DamlPackage? GetPackageById(string packageId) => FindPackageById(this, packageId);

    internal static DamlPackage? FindPackageById(IDarSource source, string packageId) =>
        source.AllPackages.FirstOrDefault(package => package.PackageId == packageId);
}
