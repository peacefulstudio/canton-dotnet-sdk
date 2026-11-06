// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions.Testing.Conformance;

namespace Canton.Ledger.Testing.LiveConformance;

/// <summary>
/// The two DARs a live participant needs before the conformance kit can run against it: the
/// kit's interface DAR and the probe DAR that declares an <c>interface instance</c> of it. Both are
/// materialized once per test process beside the test assembly so path-based uploads can reach them.
/// </summary>
internal static class LiveConformanceDars
{
    private const string KitFileName = "conformancekit.dar";
    private const string ProbeFileName = "conformanceprobe.dar";

    private static readonly Lazy<IReadOnlyList<string>> MaterializedPaths =
        new(Materialize, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Gets the paths to upload, interface DAR first.</summary>
    internal static IReadOnlyList<string> Paths => MaterializedPaths.Value;

    private static IReadOnlyList<string> Materialize()
    {
        using var kit = ConformanceKitDar.Open();
        using var probe = typeof(LiveConformanceDars).Assembly.GetManifestResourceStream(ProbeFileName)
            ?? throw new InvalidOperationException($"The assembly embeds no '{ProbeFileName}' resource.");

        return [CopyBesideAssembly(kit, KitFileName), CopyBesideAssembly(probe, ProbeFileName)];
    }

    private static string CopyBesideAssembly(Stream dar, string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        using var file = File.Create(path);
        dar.CopyTo(file);
        return path;
    }
}
