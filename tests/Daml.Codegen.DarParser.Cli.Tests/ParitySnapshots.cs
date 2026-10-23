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

    public static IReadOnlyList<string> Discovered()
    {
        var snapshotsRoot = Path.Combine(AppContext.BaseDirectory, "Snapshots");

        if (!Directory.Exists(snapshotsRoot))
            throw new DirectoryNotFoundException(
                $"Snapshots root not found at '{snapshotsRoot}'. " +
                "Check that the snapshot fixtures are copied to the output directory in the .csproj.");

        var names = Directory.EnumerateDirectories(snapshotsRoot)
            .Select(directory => Path.GetFileName(directory)!)
            .Where(name => File.Exists(ProtoPath(name)) && File.Exists(DarPath(name)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (names.Count == 0)
            throw new InvalidOperationException(
                $"No snapshot directory under '{snapshotsRoot}' carries both a .dar and an intermediate.binpb. " +
                "A zero-case theory would silently skip the parity comparison.");

        return names;
    }

    public static string DarPath(string snapshotName) =>
        Path.Combine(AppContext.BaseDirectory, "Snapshots", snapshotName, $"{snapshotName}.dar");

    public static string ProtoPath(string snapshotName) =>
        Path.Combine(AppContext.BaseDirectory, "Snapshots", snapshotName, "intermediate.binpb");
}
