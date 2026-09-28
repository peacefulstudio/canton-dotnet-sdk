// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;

namespace Daml.Codegen.CSharp.Tests.TestHelpers;

/// <summary>
/// Shared factory for the <see cref="CSharpCodeGenerator"/> used across the
/// codegen test suite. Centralizes the default <see cref="CodeGenOptions"/>
/// (nullable reference types, file-scoped namespaces, XML docs) so individual
/// test classes import this via <c>using static</c> and call
/// <see cref="CreateGenerator"/> directly. The generator's logger is
/// left at its <c>NullLogger</c> default, so the suite stays silent.
/// </summary>
public static class GeneratorFactory
{
    /// <summary>
    /// Creates a silent <see cref="CSharpCodeGenerator"/>. When
    /// <paramref name="options"/> is <c>null</c>, the suite-wide default options
    /// are used.
    /// </summary>
    public static CSharpCodeGenerator CreateGenerator(CodeGenOptions? options = null) =>
        new(options ?? DefaultOptions());

    private static CodeGenOptions DefaultOptions() =>
        new()
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            GenerateXmlDocs = true,
        };
}
