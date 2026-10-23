// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Versioning;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class VersioningApiSurfaceTests
{
    private static readonly System.Reflection.Assembly EmitterLibrary = typeof(FourPartPackageVersion).Assembly;

    public static TheoryData<Type> CliSupportTypes() =>
        [
            typeof(FourPartPackageVersion),
            typeof(VerbosityConsoleLoggerProvider),
        ];

    [Theory]
    [MemberData(nameof(CliSupportTypes))]
    public void Cli_support_types_are_internal_to_the_emitter(Type cliSupportType)
    {
        cliSupportType.IsPublic.Should().BeFalse(
            "the version stamp and the console logger serve this project's own CLIs and must not freeze into the published library's public API");
    }

    [Theory]
    [InlineData("Daml.Codegen.CSharp.Versioning.JsonReleaseCounterStore")]
    [InlineData("Daml.Codegen.CSharp.Versioning.NuGetVersionResolver")]
    public void Release_counter_persistence_lives_in_the_cli_not_the_library(string typeName)
    {
        EmitterLibrary.GetType(typeName).Should().BeNull(
            "the file-backed release counter and NuGet version resolver are CI release plumbing owned by the CLI, so a library consumer inherits no disk policy");
    }
}
