// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.DarParser.Cli.Tests;

/// <summary>
/// Keeps Daml-LF 2.2 read-proven: the Daml SDK 3.6 compiler defaults to 2.3, so the
/// <c>conformance/nonserializable</c> corpus is the one fixture that requests 2.2 explicitly.
/// </summary>
public sealed class NonSerializableDarCharacterizationTests
{
    [Fact]
    public async Task NonSerializableDarCharacterization_main_package_is_built_at_daml_lf_2_2()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "nonserializable.dar");

        var archive = await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        archive.MainPackage.Name.Should().Be("nonserializable");
        archive.MainPackage.LfVersion.Should().Be(
            "2.2",
            "conformance/nonserializable/daml.yaml pins --target=2.2, the only fixture that keeps Daml-LF 2.2 "
            + "covered now that the compiler default is 2.3");
    }
}
