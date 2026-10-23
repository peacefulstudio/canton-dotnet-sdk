// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.MSBuild.TestSupport;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.MSBuild.Tests;

[Collection("MSBuild")]
public class DamlProjectTests : DamlProjectStepContractTests
{
    private readonly MsBuildIntegrationFixture _fx;

    public DamlProjectTests(MsBuildIntegrationFixture fx) : base(fx)
    {
        _fx = fx;
    }

    protected override void SeedFakeTools(string toolsDirectory) => _fx.SeedFakeToolsDir(toolsDirectory);

    private protected override void WriteConsumerProject(string projectDirectory, IReadOnlyList<DamlProjectItem> damlProjects) =>
        ConsumerProjectWriter.Write(projectDirectory, _fx.PropsFile, _fx.TargetsFile, damlProjects: damlProjects, damlCodegenMode: "Dpm",
            damlCodegenBundleDigest: "sha256:9d056e899e4d1ff071d2b438785650a1e81bdd6c26f613e42d3afd1081211432");

    [Fact]
    public void DamlProject_full_dotnet_build_compiles_the_generated_bindings_into_the_consumer_assembly()
    {
        var damlDirectory = Path.Combine(ConsumerDirectory, "daml");
        Directory.CreateDirectory(damlDirectory);
        File.WriteAllText(Path.Combine(damlDirectory, "daml.yaml"), "name: fake\n");
        File.WriteAllText(Path.Combine(damlDirectory, "Main.daml"), "module Main where\n");
        WriteConsumerProject(ConsumerDirectory, [new DamlProjectItem("daml")]);
        var toolsDirectory = Path.Combine(RootDirectory, "full-build-tools");
        _fx.SeedFakeToolsDir(toolsDirectory);
        var environment = new Dictionary<string, string?>
        {
            ["PATH"] = MsBuildProcessRunner.BuildPath(toolsDirectory),
            ["FAKE_DPM_BUILD_DAR"] = _fx.FixtureDar,
        };

        var (exit, stdout, stderr) = MsBuildProcessRunner.RunDotnetBuild(ConsumerDirectory, "--verbosity minimal", environment);

        exit.Should().Be(0, $"a plain dotnet build must build the project, generate and compile.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        var assembly = Path.Combine(ConsumerDirectory, "bin", "Debug", "net10.0", "TestConsumer.dll");
        File.ReadAllText(assembly, System.Text.Encoding.Latin1).Should().Contain("FakeDpmGenerated",
            "the marker class the fake codegen-cs emits must be compiled into the consumer assembly");
    }
}
