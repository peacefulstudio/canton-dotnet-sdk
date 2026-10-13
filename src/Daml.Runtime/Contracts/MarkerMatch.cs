// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Daml.Runtime.Contracts;

internal static class MarkerMatch
{
    internal static bool Template(DamlTypeDescriptor marker, string? packageName, Identifier candidate) =>
        Template(marker, packageName, candidate.ModuleName, candidate.EntityName);

    internal static bool Template(DamlTypeDescriptor marker, string? packageName, string moduleName, string entityName) =>
        !string.IsNullOrEmpty(packageName)
        && string.Equals(packageName, marker.PackageName, StringComparison.Ordinal)
        && SameModuleAndEntity(marker.Identifier, moduleName, entityName);

    internal static bool Interface(DamlTypeDescriptor marker, Identifier candidate) =>
        Interface(marker, candidate.PackageId, candidate.ModuleName, candidate.EntityName);

    internal static bool Interface(DamlTypeDescriptor marker, string packageId, string moduleName, string entityName) =>
        string.Equals(packageId, marker.Identifier.PackageId, StringComparison.Ordinal)
        && SameModuleAndEntity(marker.Identifier, moduleName, entityName);

    private static bool SameModuleAndEntity(Identifier expected, string moduleName, string entityName) =>
        string.Equals(moduleName, expected.ModuleName, StringComparison.Ordinal)
        && string.Equals(entityName, expected.EntityName, StringComparison.Ordinal);
}
