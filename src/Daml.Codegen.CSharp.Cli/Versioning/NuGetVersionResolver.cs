// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Versioning;

namespace Daml.Codegen.CSharp.Cli.Versioning;

/// <summary>
/// Entry point for the 4-part <c>M.m.p.g</c> NuGet versioning scheme. Composes
/// a DAR-intrinsic <see cref="Version"/> (segments 1–3, from the package metadata)
/// with the codegen-generation ordinal (segment 4) resolved from the supplied
/// <see cref="JsonReleaseCounterStore"/>. Intended to be called by the NuGet packing
/// step once per package being packed; every package packed under one codegen
/// version resolves to the same ordinal.
/// </summary>
internal static class NuGetVersionResolver
{
    /// <summary>
    /// Computes the 4-part NuGet version for one package being packed. The
    /// <paramref name="counterStore"/> is mutated and persisted in-place the first
    /// time <paramref name="codegenVersion"/> is seen, per the semantics in
    /// <see cref="JsonReleaseCounterStore.ResolveGeneration"/>.
    /// </summary>
    public static FourPartPackageVersion Compute(
        Version intrinsicVersion,
        string codegenVersion,
        JsonReleaseCounterStore counterStore)
    {
        ArgumentNullException.ThrowIfNull(counterStore);

        var ordinal = counterStore.ResolveGeneration(codegenVersion);
        return FourPartPackageVersion.FromIntrinsic(intrinsicVersion, ordinal);
    }
}
