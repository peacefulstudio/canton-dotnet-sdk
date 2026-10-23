// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.DarParser;
using Daml.Codegen.Intermediate;
using AwesomeAssertions;
using Google.Protobuf;
using Xunit;

namespace Daml.Codegen.DarParser.Cli.Tests;

/// <summary>
/// Proves the pure-.NET <c>dar2intermediate</c> producer
/// (<see cref="IntermediateDarWriter"/>) emits an <c>intermediate.binpb</c> that
/// the consumer (<c>IntermediateDarReader.Read</c>, the same surface as the
/// public CLI's <c>--intermediate</c>) reads back, and that codegen from that
/// round-trip equals codegen taken straight from the parser-direct path.
/// </summary>
public class Dar2IntermediateRoundTripTests
{
    private const string SnapshotName = "splice-api-token-holding-v1";

    private static string DarPath =>
        Path.Combine(AppContext.BaseDirectory, "Snapshots", SnapshotName, $"{SnapshotName}.dar");

    [Fact]
    public async Task Dar2IntermediateRoundTrip_written_binpb_round_trips_through_reader_unchanged()
    {
        var dar = await DarArchive.ReadAsync(DarPath, TestContext.Current.CancellationToken);

        var written = IntermediateDarWriter.Write(dar);
        var bytes = written.ToByteArray();
        var reparsed = IntermediateDar.Parser.ParseFrom(bytes);

        reparsed.Should().Be(written,
            "the serialized .binpb must parse back into the exact same IntermediateDar message");

        var model = IntermediateDarReader.Read(reparsed);

        model.MainPackage.PackageId.Should().Be(dar.MainPackage.PackageId);
        model.MainPackage.Name.Should().Be(dar.MainPackage.Name);
        model.MainPackage.Version.Should().Be(dar.MainPackage.Version);
    }

    [Fact]
    public async Task Dar2IntermediateRoundTrip_writes_the_stdlib_dependency_version_untruncated_and_the_reader_keeps_it()
    {
        var dar = await DarArchive.ReadAsync(DarPath, TestContext.Current.CancellationToken);

        var written = IntermediateDarWriter.Write(dar);

        written.Dependencies.Single(p => p.PackageName == "daml-stdlib").PackageVersion
            .Should().Be("3.3.0.20250502.13767.0");
        var model = IntermediateDarReader.Read(written);
        model.Dependencies.Single(p => p.Name == "daml-stdlib").RawVersion
            .Should().Be("3.3.0.20250502.13767.0");
        model.Dependencies.Single(p => p.Name == "daml-stdlib").Version
            .Should().Be(new Version(3, 3, 0));
    }

    [Fact]
    public async Task Dar2IntermediateRoundTrip_codegen_from_round_tripped_binpb_equals_parser_direct_codegen()
    {
        var dar = await DarArchive.ReadAsync(DarPath, TestContext.Current.CancellationToken);

        var parserDirectFiles = new CSharpCodeGenerator(new CodeGenOptions()).Generate(dar);

        var proto = IntermediateDar.Parser.ParseFrom(IntermediateDarWriter.Write(dar).ToByteArray());
        var roundTripped = IntermediateDarReader.Read(proto);
        var roundTrippedFiles = new CSharpCodeGenerator(new CodeGenOptions()).Generate(roundTripped);

        var parserByPath = parserDirectFiles.ToDictionary(f => f.RelativePath.Replace('\\', '/'), f => f.Content);
        var roundTrippedByPath = roundTrippedFiles.ToDictionary(f => f.RelativePath.Replace('\\', '/'), f => f.Content);

        roundTrippedByPath.Keys.Should().BeEquivalentTo(parserByPath.Keys,
            "the pure-.NET proto round-trip must emit the same set of files as the parser-direct path");

        foreach (var (relPath, parserContent) in parserByPath)
        {
            roundTrippedByPath[relPath].Should().Be(parserContent,
                $"file `{relPath}` must be byte-identical between the parser-direct path and the dar2intermediate round-trip");
        }
    }

    [Fact]
    public async Task Dar2IntermediateRoundTrip_written_binpb_is_deterministic_across_runs()
    {
        var dar = await DarArchive.ReadAsync(DarPath, TestContext.Current.CancellationToken);

        var first = IntermediateDarWriter.Write(dar).ToByteArray();
        var second = IntermediateDarWriter.Write(dar).ToByteArray();

        second.Should().Equal(first, "the producer must emit byte-stable output for the same DAR");
    }
}
