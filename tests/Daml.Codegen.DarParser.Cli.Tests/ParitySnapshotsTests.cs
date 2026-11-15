// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.DarParser.Cli.Tests;

public sealed class ParitySnapshotsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"parity-snapshots-{Guid.NewGuid():N}");

    public ParitySnapshotsTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void Discovered_lists_snapshots_that_have_both_a_dar_and_a_proto()
    {
        WriteSnapshot("beta", dar: true, proto: true);
        WriteSnapshot("alpha", dar: true, proto: true);

        ParitySnapshots.Discovered(root).Should().Equal("alpha", "beta");
    }

    [Fact]
    public void Discovered_fails_naming_the_snapshot_and_the_fetch_command_when_a_proto_has_no_dar()
    {
        WriteSnapshot("alpha", dar: true, proto: true);
        WriteSnapshot("splice-missing-dar", dar: false, proto: true);

        var discover = () => ParitySnapshots.Discovered(root);

        discover.Should().Throw<InvalidOperationException>()
            .Where(e => e.Message.Contains("splice-missing-dar", StringComparison.Ordinal)
                && e.Message.Contains("scripts/fetch-splice-snapshot-dars.sh", StringComparison.Ordinal));
    }

    [Fact]
    public void Discovered_fails_when_a_dar_has_no_proto()
    {
        WriteSnapshot("splice-missing-proto", dar: true, proto: false);

        var discover = () => ParitySnapshots.Discovered(root);

        discover.Should().Throw<InvalidOperationException>()
            .Where(e => e.Message.Contains("splice-missing-proto", StringComparison.Ordinal));
    }

    private void WriteSnapshot(string name, bool dar, bool proto)
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
        if (dar)
            File.WriteAllText(Path.Combine(directory, $"{name}.dar"), "dar");
        if (proto)
            File.WriteAllText(Path.Combine(directory, "intermediate.binpb"), "proto");
    }
}
