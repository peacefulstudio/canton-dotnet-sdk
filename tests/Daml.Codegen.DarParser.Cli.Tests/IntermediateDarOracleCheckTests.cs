// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using AwesomeAssertions;
using Daml.Codegen.DarParser;
using Google.Protobuf;
using Xunit;

namespace Daml.Codegen.DarParser.Cli.Tests;

/// <summary>
/// The Intermediate DAR oracle check: the C# <see cref="DarArchive"/> and
/// <see cref="IntermediateDarWriter"/> (the path <c>dar2intermediate</c> runs) must write, byte for
/// byte, the Intermediate DAR the JVM helper recorded as the oracle fixture for the same <c>.dar</c>.
/// </summary>
public class IntermediateDarOracleCheckTests
{
    private const int LeastOracleFixturesWorthChecking = 12;

    public static TheoryData<string> OracleFixtureNames() => ParitySnapshots.Names();

    [Fact]
    public void IntermediateDarOracleCheck_covers_every_oracle_fixture_under_Snapshots()
    {
        ParitySnapshots.Discovered().Should().HaveCountGreaterThanOrEqualTo(LeastOracleFixturesWorthChecking,
            "the csproj must copy every Snapshots/<name> .dar and intermediate.binpb pair, " +
            "otherwise the oracle check quietly narrows back to a couple of fixtures");
    }

    [Theory]
    [MemberData(nameof(OracleFixtureNames))]
    public async Task IntermediateDarOracleCheck_dar_parser_bytes_equal_the_oracle_fixture(string fixtureName)
    {
        var oracleBytes = await File.ReadAllBytesAsync(
            ParitySnapshots.ProtoPath(fixtureName), TestContext.Current.CancellationToken);
        var dar = await DarArchive.ReadAsync(
            ParitySnapshots.DarPath(fixtureName), TestContext.Current.CancellationToken);

        var darParserBytes = IntermediateDarWriter.Write(dar).ToByteArray();

        var firstDifference = IntermediateDarByteComparison.DescribeFirstDifference(oracleBytes, darParserBytes);
        if (firstDifference is not null)
            throw new Xunit.Sdk.XunitException(
                $"The C# DarParser's Intermediate DAR for '{fixtureName}' is not byte-equal to the oracle fixture. " +
                $"First difference: {firstDifference}");
    }

    [Fact]
    public async Task IntermediateDarOracleCheck_dar_parser_ignores_a_dalf_the_manifest_does_not_list_as_the_oracle_does()
    {
        var oracleBytes = await File.ReadAllBytesAsync(
            ParitySnapshots.ProtoPath("contractkeys"), TestContext.Current.CancellationToken);
        var unlistedDalf = DalfEntryBytes(ParitySnapshots.DarPath("richtypes"), entryNameFragment: "/richtypes-");
        var darWithUnlistedDalf = CopyOfDarPlusEntry(
            ParitySnapshots.DarPath("contractkeys"), "extra/unlisted.dalf", unlistedDalf);

        try
        {
            var dar = await DarArchive.ReadAsync(darWithUnlistedDalf, TestContext.Current.CancellationToken);

            var darParserBytes = IntermediateDarWriter.Write(dar).ToByteArray();

            var firstDifference = IntermediateDarByteComparison.DescribeFirstDifference(oracleBytes, darParserBytes);
            if (firstDifference is not null)
                throw new Xunit.Sdk.XunitException(
                    "The oracle writes the clean contractkeys Intermediate DAR for a DAR carrying a .dalf its manifest " +
                    $"does not list; the C# DarParser's differs. First difference: {firstDifference}");
        }
        finally
        {
            File.Delete(darWithUnlistedDalf);
        }
    }

    private static byte[] DalfEntryBytes(string darPath, string entryNameFragment)
    {
        using var archive = ZipFile.OpenRead(darPath);
        var entry = archive.Entries.First(candidate =>
            candidate.FullName.EndsWith(".dalf", StringComparison.Ordinal)
            && candidate.FullName.Contains(entryNameFragment, StringComparison.Ordinal));
        using var content = new MemoryStream();
        using var stream = entry.Open();
        stream.CopyTo(content);
        return content.ToArray();
    }

    private static string CopyOfDarPlusEntry(string darPath, string extraEntryName, byte[] extraEntryBytes)
    {
        var copyPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".dar");
        File.Copy(darPath, copyPath);
        using var archive = ZipFile.Open(copyPath, ZipArchiveMode.Update);
        using var extraEntryStream = archive.CreateEntry(extraEntryName).Open();
        extraEntryStream.Write(extraEntryBytes);
        return copyPath;
    }
}
