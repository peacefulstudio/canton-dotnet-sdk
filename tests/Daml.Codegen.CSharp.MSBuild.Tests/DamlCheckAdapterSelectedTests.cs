// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.MSBuild.TestSupport;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.MSBuild.Tests;

[Collection("MSBuild")]
public class DamlCheckAdapterSelectedTests : IDisposable
{
    private readonly MsBuildIntegrationFixture _fx;
    private readonly string _tempDir;

    public DamlCheckAdapterSelectedTests(MsBuildIntegrationFixture fx)
    {
        _fx = fx;
        _tempDir = Path.Combine(Path.GetTempPath(), $"msbuild-adapter-selected-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException ex) { Console.Error.WriteLine($"[MSBuild test cleanup] Could not delete temp dir {_tempDir}: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { Console.Error.WriteLine($"[MSBuild test cleanup] Could not delete temp dir {_tempDir}: {ex.Message}"); }
        GC.SuppressFinalize(this);
    }

    private void WriteConsumer(string mode) =>
        ConsumerProjectWriter.Write(_tempDir, _fx.PropsFile, _fx.TargetsFile,
            Path.Combine(_tempDir, "DamlGenerated") + Path.DirectorySeparatorChar,
            [_fx.FixtureDar],
            damlCodegenMode: mode);

    [Theory]
    [InlineData("Dpmm")]
    [InlineData("Standalon")]
    [InlineData("None")]
    public void DamlCheckAdapterSelected_unknown_mode_fails_with_DAML0005_naming_the_mode_and_the_valid_ones(string mode)
    {
        WriteConsumer(mode);

        var (exit, stdout, stderr) = MsBuildProcessRunner.RunDotnetBuild(_tempDir, "--verbosity minimal");

        exit.Should().NotBe(0, $"an unknown DamlCodegenMode selects no tool.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        var output = stdout + stderr;
        output.Should().Contain("DAML0005");
        output.Should().Contain($"DamlCodegenMode '{mode}' is not supported");
        output.Should().Contain("Dpm").And.Contain("Standalone");
        output.Should().NotContain("MSB6", "the failure must be the adapter's own diagnostic, not an Exec that ran an empty tool");
    }

    [Theory]
    [InlineData("Dpm")]
    [InlineData("Standalone")]
    public void DamlCheckAdapterSelected_known_mode_passes(string mode)
    {
        WriteConsumer(mode);

        var (exit, stdout, stderr) = MsBuildProcessRunner.RunDotnetBuild(
            _tempDir, "--target DamlCheckAdapterSelected --verbosity minimal");

        exit.Should().Be(0, $"{mode} is a shipped adapter.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }
}
