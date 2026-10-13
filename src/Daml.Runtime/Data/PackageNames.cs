// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0


namespace Daml.Runtime.Data;

internal static class PackageNames
{
    internal static bool IsPlaceholder(string? packageName) =>
        string.IsNullOrEmpty(packageName)
        || packageName is "-no-package-metadata" or "UnknownPackage";
}
