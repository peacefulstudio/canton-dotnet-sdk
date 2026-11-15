// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace Daml.Codegen.DarParser.Cli.Tests;

internal static class ParitySnapshots
{
    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var name in Discovered())
        {
            data.Add(name);
        }
        return data;
    }

    public static IReadOnlyList<string> Discovered() =>
        Discovered(Path.Combine(AppContext.BaseDirectory, "Snapshots"));

    public static IReadOnlyList<string> Discovered(string snapshotsRoot)
    {
        if (!Directory.Exists(snapshotsRoot))
            throw new DirectoryNotFoundException(
                $"Snapshots root not found at '{snapshotsRoot}'. " +
                "Check that the snapshot fixtures are copied to the output directory in the .csproj.");

        var names = Directory.EnumerateDirectories(snapshotsRoot)
            .Select(directory => Path.GetFileName(directory)!)
            .Where(name => File.Exists(ProtoPath(snapshotsRoot, name)) || File.Exists(DarPath(snapshotsRoot, name)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var incomplete = names
            .Where(name => !File.Exists(ProtoPath(snapshotsRoot, name)) || !File.Exists(DarPath(snapshotsRoot, name)))
            .ToList();
        if (incomplete.Count > 0)
            throw new InvalidOperationException(
                $"Snapshot(s) {string.Join(", ", incomplete)} under '{snapshotsRoot}' lack a .dar or an intermediate.binpb. " +
                "The Splice DARs are not vendored: run scripts/fetch-splice-snapshot-dars.sh, or build this project, " +
                "which runs it. A parity theory must not shrink silently.");

        if (names.Count == 0)
            throw new InvalidOperationException(
                $"No snapshot directory under '{snapshotsRoot}' carries both a .dar and an intermediate.binpb. " +
                "A zero-case theory would silently skip the parity comparison.");

        return names;
    }

    public static string DarPath(string snapshotName) =>
        DarPath(Path.Combine(AppContext.BaseDirectory, "Snapshots"), snapshotName);

    public static string ProtoPath(string snapshotName) =>
        ProtoPath(Path.Combine(AppContext.BaseDirectory, "Snapshots"), snapshotName);

    private static string DarPath(string snapshotsRoot, string snapshotName) =>
        Path.Combine(snapshotsRoot, snapshotName, $"{snapshotName}.dar");

    private static string ProtoPath(string snapshotsRoot, string snapshotName) =>
        Path.Combine(snapshotsRoot, snapshotName, "intermediate.binpb");
}
