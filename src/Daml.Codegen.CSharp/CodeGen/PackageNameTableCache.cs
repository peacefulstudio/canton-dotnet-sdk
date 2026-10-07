// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// The package name tables of one DAR, each built once: the emit context of every emitted package
/// and the cross-package resolver read the same table of a package, so the main package and each
/// emitted dependency are scanned a single time between them. A table is keyed by the package id
/// and the root filter it was built under. The main package is built under
/// <see cref="CodeGenOptions.RootFilter"/>; a dependency is built unfiltered unless
/// <see cref="CodeGenOptions.IncludeDependencies"/> emits it, in which case the same filter applies.
/// The warnings of a table are logged when it is built: all of them for a package that is emitted,
/// only the choice-argument ambiguities for a dependency that is merely referenced.
/// </summary>
internal sealed class PackageNameTableCache
{
    private readonly CodeGenOptions _options;
    private readonly string? _mainPackageId;
    private readonly ILogger _logger;
    private readonly TypeRootFilter _mainRootFilter;
    private readonly TypeRootFilter _dependencyRootFilter;
    private readonly Dictionary<(string PackageId, string? RootFilter), PackageNameTable> _tables = [];

    /// <summary>Creates the cache of one DAR.</summary>
    /// <param name="options">Read for the namespace prefix, the root filter and whether dependencies are emitted.</param>
    /// <param name="mainPackageId">The package id of the DAR's main package, the only one the namespace prefix applies to; <c>null</c> when the cache serves a dependency alone.</param>
    /// <param name="logger">Where table warnings go; omit it and the cache stays silent.</param>
    public PackageNameTableCache(CodeGenOptions options, string? mainPackageId, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _mainPackageId = mainPackageId;
        _logger = logger ?? NullLogger.Instance;
        _mainRootFilter = new TypeRootFilter(options.RootFilter);
        _dependencyRootFilter = options.IncludeDependencies ? _mainRootFilter : TypeRootFilter.IncludeAll;
    }

    /// <summary>
    /// The table of <paramref name="package"/> under the root filter its role in the DAR calls for.
    /// </summary>
    /// <exception cref="CodegenException">The package cannot be named; see <see cref="PackageNameTable.For"/>.</exception>
    public PackageNameTable For(DamlPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        return For(package, IsMainPackage(package) ? _mainRootFilter : _dependencyRootFilter);
    }

    /// <summary>The table of <paramref name="package"/> built under <paramref name="rootFilter"/>.</summary>
    /// <exception cref="CodegenException">The package cannot be named; see <see cref="PackageNameTable.For"/>.</exception>
    public PackageNameTable For(DamlPackage package, TypeRootFilter rootFilter)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(rootFilter);

        var key = (package.PackageId, rootFilter.Pattern);
        if (!_tables.TryGetValue(key, out var table))
        {
            table = PackageNameTable.For(package, _options, IsMainPackage(package), rootFilter);
            LogWarningsOf(table, package);
            _tables[key] = table;
        }
        return table;
    }

    private bool IsMainPackage(DamlPackage package) => package.PackageId == _mainPackageId;

    private void LogWarningsOf(PackageNameTable table, DamlPackage package)
    {
        if (IsMainPackage(package) || _options.IncludeDependencies)
        {
            table.LogWarnings(_logger);
        }
        else
        {
            table.LogAmbiguities(_logger);
        }
    }
}
