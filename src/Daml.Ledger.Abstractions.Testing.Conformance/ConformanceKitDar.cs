// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Ledger.Abstractions.Testing.Conformance;

/// <summary>
/// Access to the DAR that declares the Daml interface behind <see cref="IConformanceProbe"/>
/// (<c>Conformance.Kit:ConformanceProbe</c>). A subclass that runs the kit against a live
/// participant uploads this DAR, then declares its probe template an <c>interface instance</c>
/// of that interface.
/// </summary>
public static class ConformanceKitDar
{
    private const string ResourceName = "conformancekit.dar";

    /// <summary>
    /// Opens a stream over the embedded DAR. The caller owns the returned stream and must
    /// dispose it.
    /// </summary>
    public static Stream Open() =>
        typeof(ConformanceKitDar).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded conformance kit DAR resource '{ResourceName}' was not found.");
}
