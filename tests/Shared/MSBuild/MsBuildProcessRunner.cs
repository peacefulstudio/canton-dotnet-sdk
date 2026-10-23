// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Xunit;

namespace Daml.Codegen.MSBuild.TestSupport;

/// <summary>
/// Process, PATH and repo-root plumbing shared by the MSBuild adapter integration
/// fixtures. Every child process is killed with its tree and surfaced as a
/// <see cref="TimeoutException"/> if it outlives the five-minute cap.
/// </summary>
internal static class MsBuildProcessRunner
{
    private const string ExecShellName = "sh";

    internal static (int ExitCode, string StdOut, string StdErr) RunDotnetBuild(
        string projectDir,
        string extraArgs = "",
        IReadOnlyDictionary<string, string?>? environment = null) =>
        RunDotnet($"build {extraArgs}", projectDir, environment);

    internal static (int ExitCode, string StdOut, string StdErr) RunDotnet(
        string args,
        string workingDir,
        IReadOnlyDictionary<string, string?>? environment = null) =>
        RunProcessAsync("dotnet", args, workingDir, environment).GetAwaiter().GetResult();

    /// <summary>
    /// The real system PATH with a fake-tool directory prepended, and — for tools listed in
    /// <paramref name="hide"/> — every ambient PATH entry that would otherwise resolve one of
    /// them stripped out first. Lets a test guarantee "dpm not found" or "java not found"
    /// deterministically regardless of what happens to be installed on the host running it.
    /// Hiding a tool must not also hide the command interpreter, so on POSIX the returned PATH
    /// carries <see cref="ShellShimDirectory"/>: a stripped entry is regularly the one the shell
    /// lives in too — on a merged-/usr Linux, <c>java</c> and <c>sh</c> are both <c>/usr/bin</c>
    /// entries — and MSBuild's Exec task cannot then start any command at all.
    /// </summary>
    internal static string BuildPath(string? fakeToolDir, params string[] hide)
    {
        var ambient = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var entries = ambient.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(dir => !hide.Any(tool => ResolvesFrom(dir, tool)))
            .ToList();
        if (hide.Length > 0 && !OperatingSystem.IsWindows())
        {
            entries.Insert(0, ShellShimDirectory.Value);
        }
        if (fakeToolDir is not null)
        {
            entries.Insert(0, fakeToolDir);
        }
        return string.Join(Path.PathSeparator, entries);
    }

    /// <summary>
    /// A directory holding nothing but a link to the <c>sh</c> that MSBuild's <c>Exec</c> task
    /// starts by bare name, and so resolves from PATH, on every non-Windows platform (its
    /// GenerateFullPathToTool returns the bare name there, and the full path to the system
    /// cmd.exe on Windows — which is why Windows needs no counterpart to this).
    /// </summary>
    private static readonly Lazy<string> ShellShimDirectory = new(() =>
    {
        var directory = Path.Combine(Path.GetTempPath(), $"daml-msbuild-shell-shim-{Environment.ProcessId}");
        Directory.CreateDirectory(directory);
        var shim = Path.Combine(directory, ExecShellName);
        File.Delete(shim);
        File.CreateSymbolicLink(shim, AmbientShellPath());
        return directory;
    });

    private static string AmbientShellPath() =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(dir => ResolvesFrom(dir, ExecShellName))
            .Select(dir => Path.Combine(dir, ExecShellName))
            .FirstOrDefault() ?? $"/bin/{ExecShellName}";

    /// <summary>
    /// Whether a bare <paramref name="tool"/> name would resolve to an executable in
    /// <paramref name="directory"/> the way the platform's command interpreter resolves it:
    /// the file itself on POSIX, and — because Windows resolves an extensionless command by
    /// appending each PATHEXT suffix in turn — <c>tool.exe</c>, <c>tool.cmd</c> and the rest
    /// of PATHEXT there.
    /// </summary>
    internal static bool ResolvesFrom(string directory, string tool)
    {
        if (!OperatingSystem.IsWindows())
        {
            return File.Exists(Path.Combine(directory, tool));
        }
        return WindowsPathExtensions()
            .Any(extension => File.Exists(Path.Combine(directory, tool + extension)));
    }

    /// <summary>
    /// The file name a bare <paramref name="tool"/> command must carry on this platform to be
    /// both executable and PATH-resolvable — <c>tool.exe</c> on Windows, <c>tool</c> elsewhere.
    /// </summary>
    internal static string PlatformExecutableName(string tool) =>
        OperatingSystem.IsWindows() ? tool + ".exe" : tool;

    private static IEnumerable<string> WindowsPathExtensions() =>
        (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Runs <paramref name="command"/> through the same command interpreter the MSBuild
    /// <c>Exec</c> task uses — <c>cmd.exe</c> on Windows, <c>/bin/sh</c> elsewhere — so a test
    /// exercises the tool resolution the adapter's <c>Exec Command="dpm --version"</c> actually
    /// gets rather than .NET's own process-start resolution.
    /// </summary>
    internal static Task<(int ExitCode, string StdOut, string StdErr)> RunThroughPlatformShellAsync(
        string command,
        string workingDir,
        IReadOnlyDictionary<string, string?>? environment = null) =>
        OperatingSystem.IsWindows()
            ? RunProcessAsync("cmd.exe", $"/c {command}", workingDir, environment)
            : RunProcessAsync("/bin/sh", $"-c \"{command}\"", workingDir, environment);

    internal static void EnsureExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        var mode = File.GetUnixFileMode(path);
        const UnixFileMode executeBits = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        if ((mode & executeBits) != executeBits)
        {
            File.SetUnixFileMode(path, mode | executeBits);
        }
    }

    internal static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
        string exe,
        string args,
        string workingDir,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                if (value is null)
                {
                    psi.EnvironmentVariables.Remove(key);
                }
                else
                {
                    psi.EnvironmentVariables[key] = value;
                }
            }
        }
        using var proc = Process.Start(psi)!;
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken, timeoutCts.Token);
        try
        {
            await proc.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); }
            catch (Exception ex) { Console.Error.WriteLine($"[MSBuild test] failed to kill `{exe} {args}`: {ex.Message}"); }
            await ObserveWithoutRethrowing(stdoutTask, stderrTask);
            if (timeoutCts.IsCancellationRequested)
                throw new TimeoutException($"`{exe} {args}` did not exit within 5 minutes.");
            throw;
        }
        return (proc.ExitCode, await stdoutTask, await stderrTask);
    }

    private static Task ObserveWithoutRethrowing(params Task[] tasks) =>
        Task.WhenAll(tasks.Select(task => task.ContinueWith(_ => { }, TaskContinuationOptions.None)));

    /// <summary>
    /// Walks up from the test binary until it finds the repo root, identified by a
    /// <c>Directory.Build.props</c> sitting next to
    /// <c>src/<paramref name="adapterProjectDirectoryName"/></c>.
    /// </summary>
    internal static string LocateRepoRoot(string adapterProjectDirectoryName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props"))
                && Directory.Exists(Path.Combine(current.FullName, "src", adapterProjectDirectoryName)))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException(
            $"Cannot locate repo root from {AppContext.BaseDirectory}. " +
            $"Expected a Directory.Build.props alongside src/{adapterProjectDirectoryName}/.");
    }
}
