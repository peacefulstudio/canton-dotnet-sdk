// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using Daml.Codegen.MSBuild.TestSupport;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.MSBuild.Tests;

[Collection("MSBuild")]
public sealed class SharedToolsPublishTests : IDisposable
{
    private readonly MsBuildIntegrationFixture _fx;
    private readonly string _tempDir;

    public SharedToolsPublishTests(MsBuildIntegrationFixture fx)
    {
        _fx = fx;
        _tempDir = Path.Combine(Path.GetTempPath(), $"tools-publish-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException ex) { Console.Error.WriteLine($"[tools-publish test cleanup] Could not delete temp dir {_tempDir}: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { Console.Error.WriteLine($"[tools-publish test cleanup] Could not delete temp dir {_tempDir}: {ex.Message}"); }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void SharedToolsPublish_second_cli_publish_into_shared_tools_dir_preserves_first_publish_files_and_shared_checksums()
    {
        var toolDir = Path.Combine(_tempDir, "net10.0");

        var firstPublish = MsBuildProcessRunner.RunDotnet(
            $"publish \"{_fx.DarParserCliProject}\" -c Debug -o \"{toolDir}\" --no-self-contained", _fx.RepoRoot);
        firstPublish.ExitCode.Should().Be(0,
            $"the first CLI (dar2intermediate) must publish into the shared tools dir.\nSTDOUT:\n{firstPublish.StdOut}\nSTDERR:\n{firstPublish.StdErr}");

        var afterFirstPublish = HashTreeByRelativePath(toolDir);
        afterFirstPublish.Should().NotBeEmpty(
            "the first publish must place assemblies in the shared tools dir before the second publish runs");

        var secondPublish = MsBuildProcessRunner.RunDotnet(
            $"publish \"{_fx.EmitterCliProject}\" -c Debug -o \"{toolDir}\" --no-self-contained", _fx.RepoRoot);
        secondPublish.ExitCode.Should().Be(0,
            $"the second CLI (emitter) must publish into the same shared tools dir.\nSTDOUT:\n{secondPublish.StdOut}\nSTDERR:\n{secondPublish.StdErr}");

        var afterSecondPublish = HashTreeByRelativePath(toolDir);

        foreach (var (relativePath, hashAfterFirstPublish) in afterFirstPublish)
        {
            afterSecondPublish.Should().ContainKey(relativePath,
                $"the additive second publish must not remove {relativePath} that the first publish placed in the shared tools dir");
            afterSecondPublish[relativePath].Should().Be(hashAfterFirstPublish,
                $"{relativePath} must be byte-identical after the second publish " +
                "(if shared by both CLIs a divergent version would split the tool-dir invariant before it reaches a consumer; " +
                "if exclusive to the first CLI it must not have been silently overwritten)");
        }
    }

    private static IReadOnlyDictionary<string, string> HashTreeByRelativePath(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(root, path),
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
}
