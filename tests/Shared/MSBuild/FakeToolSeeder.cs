// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.MSBuild.TestSupport;

/// <summary>
/// Copies a built fake-tool project (<c>tests/Fixtures/Fake*</c>) into a tools directory
/// under the file name a bare command resolves to on this platform.
/// </summary>
internal static class FakeToolSeeder
{
    internal static string FakeDpmProject(string repoRoot) => Path.Combine(repoRoot, "tests", "Fixtures", "FakeDpm");

    internal static string SeededToolPath(string toolsDir, string tool) =>
        Path.Combine(toolsDir, MsBuildProcessRunner.PlatformExecutableName(tool));

    internal static void Seed(string fakeToolProject, string toolsDir, string tool)
    {
        Directory.CreateDirectory(toolsDir);
        var buildOutput = Path.Combine(fakeToolProject, "bin", "Debug", "net10.0");
        foreach (var file in Directory.GetFiles(buildOutput))
        {
            File.Copy(file, Path.Combine(toolsDir, Path.GetFileName(file)), overwrite: true);
        }
        MsBuildProcessRunner.EnsureExecutable(SeededToolPath(toolsDir, tool));
    }
}
