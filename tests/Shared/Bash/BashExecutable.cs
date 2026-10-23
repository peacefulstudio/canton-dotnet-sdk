// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.Tests.Shared;

/// <summary>
/// The <c>bash</c> every bash-invoking test launches. On Windows, <c>CreateProcess</c> resolves
/// a bare <c>bash</c> through the system directory before <c>PATH</c>, which finds the WSL
/// launcher (<c>System32\bash.exe</c>) ahead of Git Bash, so tests start the Git Bash found on
/// <c>PATH</c> by its full path.
/// </summary>
internal static class BashExecutable
{
    private const string WindowsAppsSegment = @"\WindowsApps";

    /// <summary>The executable to hand to <see cref="System.Diagnostics.ProcessStartInfo"/>.</summary>
    public static string Path { get; } = Resolve();

    internal static string? LocateOnWindowsPath(
        string pathVariable,
        string windowsDirectory,
        Func<string, bool> fileExists)
    {
        foreach (var rawEntry in pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var entry = rawEntry.TrimEnd('\\');
            if (IsWslLauncherDirectory(entry, windowsDirectory))
            {
                continue;
            }
            var candidate = entry + @"\bash.exe";
            if (fileExists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private static bool IsWslLauncherDirectory(string directory, string windowsDirectory)
        => (directory + '\\').StartsWith(windowsDirectory + '\\', StringComparison.OrdinalIgnoreCase)
           || directory.Contains(WindowsAppsSegment, StringComparison.OrdinalIgnoreCase);

    private static string Resolve()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "bash";
        }
        return LocateOnWindowsPath(
                   Environment.GetEnvironmentVariable("PATH") ?? string.Empty,
                   Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                   File.Exists)
               ?? throw new InvalidOperationException(
                   "Git Bash was not found on PATH outside the Windows directory; install Git for Windows.");
    }
}
