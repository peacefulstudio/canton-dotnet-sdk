// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.DarParser;
using Daml.Codegen.CSharp;
using Daml.Codegen.Intermediate;
using Google.Protobuf;
using Xunit;

namespace Daml.Codegen.DarParser.Cli.Tests;

/// <summary>
/// The LocalNet parity suite compiles the Splice bindings the emitter generates from the
/// vendored <c>splice-wallet-0.1.25.binpb</c>, so building that suite needs only the emitter, not
/// the DAR decoder. These tests keep that proto a faithful, current decode of the vendored DAR.
/// </summary>
public sealed class ParitySpliceBindingsFixtureFreshnessTests
{
    private static string FixturePath(string extension) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", $"splice-wallet-0.1.25.{extension}");

    [Fact]
    public async Task ParitySpliceBindingsFixture_binpb_equals_the_dar2intermediate_output_of_the_vendored_dar()
    {
        var dar = await DarArchive.ReadAsync(FixturePath("dar"), TestContext.Current.CancellationToken);

        var decoded = IntermediateDarWriter.Write(dar).ToByteArray();

        var vendored = await File.ReadAllBytesAsync(FixturePath("binpb"), TestContext.Current.CancellationToken);
        decoded.Should().Equal(
            vendored,
            "the vendored proto is stale; regenerate it with scripts/regenerate.sh write --only parity-splice-bindings");
    }

    [Fact]
    public async Task ParitySpliceBindingsFixture_binpb_carries_the_package_ids_that_the_LocalNet_Splice_deploys()
    {
        var vendored = await File.ReadAllBytesAsync(FixturePath("binpb"), TestContext.Current.CancellationToken);

        var model = IntermediateDarReader.Read(IntermediateDar.Parser.ParseFrom(vendored));

        model.MainPackage.Name.Should().Be("splice-wallet");
        model.MainPackage.Version.Should().Be(new Version(0, 1, 25));
        model.MainPackage.PackageId.Should().Be("916f16ea9413ba72d9ae660c9d0741f1078fc27b82b61e4b9db6a991dc47a848");
        model.Dependencies
            .Where(dependency => dependency.Name == "splice-amulet")
            .Select(dependency => dependency.PackageId)
            .Should().Equal("17b6f7c18efec9022dde39a19039783c68490a722e8a5088b22ee034678171a2");
    }
}
