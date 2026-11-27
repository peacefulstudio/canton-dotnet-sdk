// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Runtime.Versioning;

namespace Daml.Codegen.MSBuild.TestSupport;

/// <summary>
/// The target framework moniker the running test assembly was compiled for. A test assembly built
/// for two targets builds its throwaway consumers and fake tools for the same target it runs on,
/// so each target exercises its own build output rather than one shared literal.
/// </summary>
internal static class TestTargetFramework
{
    internal static string Moniker { get; } = ReadMoniker();

    private static string ReadMoniker()
    {
        var frameworkName = typeof(TestTargetFramework).Assembly
            .GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName;
        var version = frameworkName[(frameworkName.IndexOf("Version=v", StringComparison.Ordinal) + "Version=v".Length)..];
        return $"net{version}";
    }
}
