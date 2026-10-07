// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.Tests.TestHelpers;

/// <summary>A module list that counts how many times a consumer walks it, so a test can tell a rebuild from a cache hit.</summary>
public sealed class CountingModules(IReadOnlyList<DamlModule> inner) : IReadOnlyList<DamlModule>
{
    /// <summary>How many times the list has been enumerated.</summary>
    public int EnumerationCount { get; private set; }

    /// <inheritdoc />
    public DamlModule this[int index] => inner[index];

    /// <inheritdoc />
    public int Count => inner.Count;

    /// <inheritdoc />
    public IEnumerator<DamlModule> GetEnumerator()
    {
        EnumerationCount++;
        return inner.GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
