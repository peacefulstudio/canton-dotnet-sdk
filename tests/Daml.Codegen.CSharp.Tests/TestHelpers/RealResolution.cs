// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.Tests.TestHelpers;

/// <summary>
/// The wiring the generator builds for one DAR — an <see cref="InMemoryDarSource"/>, the name table
/// cache of that DAR, the production <see cref="DarCrossPackageResolver"/> over both, and the emit
/// contexts of the main package reading the same cache — so an emitter test names types the way
/// <see cref="CSharpCodeGenerator"/> does.
/// </summary>
internal sealed class RealResolution
{
    private RealResolution(DamlPackage main, CodeGenOptions options, DamlPackage[] dependencies)
    {
        Dar = new InMemoryDarSource(main, dependencies);
        NameTables = new PackageNameTableCache(options, main.PackageId);
        Resolver = new DarCrossPackageResolver(Dar, NameTables);
        Contexts = PackageEmitContext.ForPackage(main, NameTables);
    }

    /// <summary>The DAR the resolver reads.</summary>
    public InMemoryDarSource Dar { get; }

    /// <summary>The name tables the resolver and the contexts share.</summary>
    public PackageNameTableCache NameTables { get; }

    /// <summary>The production resolver over <see cref="Dar"/>.</summary>
    public DarCrossPackageResolver Resolver { get; }

    /// <summary>One emit context per module of the main package.</summary>
    public IReadOnlyList<PackageEmitContext> Contexts { get; }

    /// <summary>The emit context of a main package that has exactly one module.</summary>
    public PackageEmitContext Context => Contexts.Single();

    /// <summary>Wires <paramref name="main"/> as the DAR's main package, with <paramref name="dependencies"/> beside it.</summary>
    public static RealResolution Of(DamlPackage main, CodeGenOptions options, params DamlPackage[] dependencies) =>
        new(main, options, dependencies);
}
