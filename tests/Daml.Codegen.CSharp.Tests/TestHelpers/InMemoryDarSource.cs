// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.Tests.TestHelpers;

/// <summary>An <see cref="IDarSource"/> over packages built in memory, so a test reaches the real cross-package resolver without a DAR on disk.</summary>
/// <param name="main">The package being generated.</param>
/// <param name="dependencies">The packages type refs are resolved against.</param>
public sealed class InMemoryDarSource(DamlPackage main, params DamlPackage[] dependencies) : IDarSource
{
    /// <inheritdoc />
    public DamlPackage MainPackage => main;

    /// <inheritdoc />
    public IReadOnlyList<DamlPackage> Dependencies => dependencies;
}
