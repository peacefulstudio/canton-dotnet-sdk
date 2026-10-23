// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// End-to-end integration test for the C# codegen → NuGet packing path. It is
/// <b>generator-agnostic</b>: the thing that turns the DAR into a <c>.csproj</c>
/// is injected via the <c>CODEGEN_CS_CMD</c> environment variable, so the same
/// fixture, the same <c>dotnet pack</c>, and the same <c>.nupkg</c> assertions
/// (id, version, <c>Apache-2.0</c> license, <c>.dll</c> under <c>lib/</c>) cover
/// every gate, parameterised <i>only</i> on where codegen comes from:
///
/// <list type="bullet">
///   <item><b>Test B (always-on):</b> a branch-local bundle entrypoint that runs
///   this branch's JVM helper + <c>Daml.Codegen.CSharp.Cli</c> emitter — always
///   reflects the code under review, no GHCR pull, no version pin.</item>
///   <item><b>Test A (key moments):</b> a locally-built OCI bundle's entrypoint —
///   additionally exercises the dpm/OCI packaging layer on branch code.</item>
///   <item><b>Test C (follow-up):</b> the published <c>dpm codegen-cs</c> against a
///   freshly-pushed canary (set <c>CODEGEN_CS_CMD=dpm</c>,
///   <c>CODEGEN_CS_BASE_ARGS=codegen-cs</c>).</item>
/// </list>
///
/// The injected command must honour the shipped <c>dpm codegen-cs</c> interface:
/// <c>&lt;cmd&gt; [base-args] --dar &lt;dar&gt; --out &lt;out&gt; -- &lt;emitter args&gt;</c>.
///
/// The fixture's generated project references its Splice dependency
/// (<c>splice-api-token-metadata-v1</c>, vendored under <c>Snapshots/</c> as its own
/// upstream DAR) as a <c>PackageReference</c>. That dependency DAR is emitted and packed
/// by the same injected generator into a workspace feed first, so the gate proves the
/// generator under test against bindings it produced itself — never against the bindings
/// a previously released emitter published to nuget.org.
///
/// Skipped unless <c>CODEGEN_CS_CMD</c> is set AND the local runtime packs exist
/// under <c>&lt;repo&gt;/output/nuget/</c> (run <c>dotnet pack -c Release</c> first).
/// </summary>
public class NuGetPackIntegrationTests
{
    private const string FixtureSnapshotName = "splice-api-token-holding-v1";
    private const string DependencyPackageName = "splice-api-token-metadata-v1";
    private const string GeneratedPackageIdRoot = "Splice.";
    private const string ExpectedPackageId = $"{GeneratedPackageIdRoot}Api.Token.Holding.V1";
    private const string DependencyPackageId = $"{GeneratedPackageIdRoot}Api.Token.Metadata.V1";
    private const string GeneratedPackageIdPattern = $"{GeneratedPackageIdRoot}*";

    [Fact]
    public async Task NuGetPackIntegration_generated_csproj_packs_into_nupkg_for_splice_holding_v1_fixture()
    {
        var darPath = RequireSnapshotDar(FixtureSnapshotName);
        var dependencyDar = RequireSnapshotDar(DependencyPackageName);

        var generatorCmd = Environment.GetEnvironmentVariable("CODEGEN_CS_CMD");
        Assert.SkipUnless(
            !string.IsNullOrWhiteSpace(generatorCmd),
            "Integration test requires CODEGEN_CS_CMD — the codegen-cs generator to exercise (a branch-local " +
            "bundle entrypoint, a locally-built OCI bundle entrypoint, or `dpm`). CI sets it per gate.");

        var localNuGetSource = LocateLocalNuGetSource();
        Assert.SkipUnless(
            localNuGetSource is not null,
            "Integration test requires local NuGet packs of Daml.Runtime and Daml.Ledger.Abstractions under <repo>/output/nuget/. " +
            "Produce them with `dotnet pack src/Daml.Runtime -c Release && dotnet pack src/Daml.Ledger.Abstractions -c Release`.");

        var runtimeVersion = ReadRepoVersion();
        runtimeVersion.Should().NotBeNullOrEmpty(
            "the generated csproj needs a Daml.Runtime version, read from Directory.Build.props <Version>");

        var ct = TestContext.Current.CancellationToken;
        var workspace = CreateTempWorkspace();
        try
        {
            var codegen = new CodegenCommand(generatorCmd!, LeadingGeneratorArgs(), runtimeVersion!, workspace);

            var dependencyFeed = Path.Combine(workspace, "dependency-feed");
            Directory.CreateDirectory(dependencyFeed);
            WriteLocalNuGetConfig(workspace, localNuGetSource!, dependencyFeed, Path.Combine(workspace, "nuget-packages"));

            var dependencyCsproj = await codegen.GenerateProjectAsync(
                dependencyDar, Path.Combine(workspace, "dependency-out"), DependencyPackageId, ct);
            await PackAsync(dependencyCsproj, dependencyFeed, ct);
            RequireNupkg(dependencyFeed, DependencyPackageId);

            var csprojPath = await codegen.GenerateProjectAsync(
                darPath, Path.Combine(workspace, "out"), ExpectedPackageId, ct);
            var packOutput = Path.Combine(workspace, "nupkg-out");
            await PackAsync(csprojPath, packOutput, ct);

            var nupkg = RequireNupkg(packOutput, ExpectedPackageId);

            var nuspecVersion = ReadNuspecVersion(nupkg);
            nuspecVersion.Should().MatchRegex(@"^\d+\.\d+\.\d+(\.\d+)?$",
                "the .nuspec must carry an M.m.p[.r] version (4th segment is normalized away by NuGet when r=0)");
            nuspecVersion.Split('.').Length.Should().BeGreaterThanOrEqualTo(3,
                "the M.m.p.g versioning scheme requires at least the 3-part DAR-intrinsic version in the manifest");

            var nuspecLicense = ReadNuspecLicense(nupkg);
            nuspecLicense.Should().Be("Apache-2.0",
                "OSS-published NuGets must declare Apache-2.0 in the manifest");

            var contents = ListNupkgEntries(nupkg);
            contents.Should().Contain(e => e.EndsWith(".dll", StringComparison.OrdinalIgnoreCase),
                "the pack must include the compiled assembly under lib/");

            var nuspecDependencyIds = ReadNuspecDependencyIds(nupkg);
            nuspecDependencyIds.Should().Contain(DependencyPackageId,
                $"the fixture's bindings must reach {DependencyPackageName} through a PackageReference — that is the " +
                "cross-package spelling this gate exists to prove, and an emitter that inlined the referent types or " +
                "stopped discovering the dependency would still compile, pack and restore, leaving the dependency " +
                $"feed written and never read — but the manifest declares {FormatList(nuspecDependencyIds)}");
        }
        finally
        {
            TryCleanup(workspace);
        }
    }

    private static string RequireSnapshotDar(string snapshotName)
    {
        var darPath = Path.Combine(AppContext.BaseDirectory, "Snapshots", snapshotName, $"{snapshotName}.dar");
        File.Exists(darPath).Should().BeTrue(
            $"the integration test requires the DAR fixture at {darPath}");
        return darPath;
    }

    /// <summary>
    /// The arguments the injected command takes before <c>--dar</c>, from
    /// <c>CODEGEN_CS_BASE_ARGS</c> (<c>codegen-cs</c> for the dpm subcommand; the bundle
    /// entrypoints take none).
    /// </summary>
    private static IReadOnlyList<string> LeadingGeneratorArgs() =>
        (Environment.GetEnvironmentVariable("CODEGEN_CS_BASE_ARGS") ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// One invocation shape of the shipped <c>dpm codegen-cs</c> interface — <c>--dar</c>/<c>--out</c>
    /// before <c>--</c>, emitter flags after — identical for every DAR the gate generates, so the
    /// dependency and the fixture are produced by exactly the same command.
    /// </summary>
    private sealed class CodegenCommand(string command, IReadOnlyList<string> baseArgs, string runtimeVersion, string workspace)
    {
        public async Task<string> GenerateProjectAsync(string darPath, string outDir, string packageId, CancellationToken ct)
        {
            Directory.CreateDirectory(outDir);
            var generatorArgs = new List<string>(baseArgs)
            {
                "--dar", darPath,
                "--out", outDir,
                "--",
                "--generate-project",
                "--runtime-version", runtimeVersion,
                "--package-license", "Apache-2.0",
                "--target-framework", "net10.0",
            };

            var codegen = await RunProcessAsync(command, generatorArgs, workspace, TimeSpan.FromMinutes(10), ct);
            codegen.ExitCode.Should().Be(0,
                $"`{command}` codegen of {Path.GetFileName(darPath)} must succeed.\nSTDOUT:\n{codegen.StdOut}\nSTDERR:\n{codegen.StdErr}");

            return RequireCsproj(outDir, packageId);
        }
    }

    private static async Task PackAsync(string csprojPath, string packOutput, CancellationToken ct)
    {
        Directory.CreateDirectory(packOutput);
        var packResult = await RunProcessAsync(
            "dotnet",
            ["pack", csprojPath, "-c", "Release", "-o", packOutput, "--verbosity", "minimal"],
            Path.GetDirectoryName(csprojPath)!,
            TimeSpan.FromMinutes(5),
            ct);

        packResult.ExitCode.Should().Be(0,
            $"dotnet pack of {Path.GetFileName(csprojPath)} must succeed.\nSTDOUT:\n{packResult.StdOut}\nSTDERR:\n{packResult.StdErr}");
    }

    /// <summary>
    /// Returns the generated project of <paramref name="packageId"/>, failing the test when none
    /// carries that id — a project under any other name is a naming-convention regression, never a
    /// match. <c>--generate-project</c> may nest the csproj under a per-package subdirectory, so
    /// this searches recursively.
    /// </summary>
    private static string RequireCsproj(string outDir, string packageId)
    {
        var produced = Directory.GetFiles(outDir, "*.csproj", SearchOption.AllDirectories);
        var csproj = produced.FirstOrDefault(p => CarriesPackageId(p, packageId));
        csproj.Should().NotBeNull(
            $"`--generate-project` must emit '{packageId}.csproj' under {outDir} — the project name is the Daml " +
            "package name converted to PascalCase per the generated-package naming convention — " +
            $"but it emitted {FileNamesOf(produced)}");
        return csproj!;
    }

    private static string RequireNupkg(string packOutput, string packageId)
    {
        var produced = Directory.GetFiles(packOutput, "*.nupkg");
        var packs = produced.Where(p => CarriesPackageId(p, packageId)).ToList();
        packs.Should().ContainSingle(
            $"packing must put exactly one '{packageId}' .nupkg in {packOutput} — the gate restores the fixture's " +
            "bindings against the dependency bindings this generator produced itself, never against the pack a " +
            "previously released emitter published to nuget.org, and the id must be the Daml package name " +
            $"converted to PascalCase — but the folder holds {FileNamesOf(produced)}");
        return packs[0];
    }

    private static bool CarriesPackageId(string path, string packageId) =>
        Path.GetFileName(path).StartsWith($"{packageId}.", StringComparison.Ordinal);

    private static string FileNamesOf(IReadOnlyList<string> paths) =>
        FormatList(paths.Select(Path.GetFileName).ToList()!);

    private static string FormatList(IReadOnlyList<string> values) =>
        values.Count == 0 ? "nothing" : string.Join(", ", values);

    /// <summary>
    /// Writes the <c>NuGet.config</c> every generated project under <paramref name="workspace"/>
    /// restores through (<c>dotnet pack</c> walks up from the csproj): <c>Daml.*</c> from the
    /// repo's local runtime packs, the generated <c>Splice.*</c> dependency from the workspace
    /// feed this test packs, everything else from nuget.org.
    /// </summary>
    private static void WriteLocalNuGetConfig(string workspace, string localSource, string dependencyFeed, string packagesFolder)
    {
        var configPath = Path.Combine(workspace, "NuGet.config");
        var content =
$@"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <config>
    <add key=""globalPackagesFolder"" value=""{packagesFolder}"" />
  </config>
  <packageSources>
    <clear />
    <add key=""local-output"" value=""{localSource}"" />
    <add key=""dependency-feed"" value=""{dependencyFeed}"" />
    <add key=""nuget.org"" value=""https://api.nuget.org/v3/index.json"" protocolVersion=""3"" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key=""local-output"">
      <package pattern=""Daml.*"" />
    </packageSource>
    <packageSource key=""dependency-feed"">
      <package pattern=""{GeneratedPackageIdPattern}"" />
    </packageSource>
    <packageSource key=""nuget.org"">
      <package pattern=""*"" />
    </packageSource>
  </packageSourceMapping>
</configuration>";
        File.WriteAllText(configPath, content);
    }

    private static string CreateTempWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), $"daml-codegen-pack-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryCleanup(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string? LocateLocalNuGetSource()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "output", "nuget");
            if (Directory.Exists(candidate)
                && Directory.GetFiles(candidate, "Daml.Runtime.*.nupkg").Length > 0
                && Directory.GetFiles(candidate, "Daml.Ledger.Abstractions.*.nupkg").Length > 0)
            {
                return candidate;
            }
            current = current.Parent;
        }
        return null;
    }

    private static string? ReadRepoVersion()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var props = Path.Combine(current.FullName, "Directory.Build.props");
            if (File.Exists(props))
            {
                var content = File.ReadAllText(props);
                var match = System.Text.RegularExpressions.Regex.Match(
                    content,
                    @"<Version>([^<]+)</Version>");
                if (match.Success)
                {
                    return match.Groups[1].Value.Trim();
                }
            }
            current = current.Parent;
        }
        return null;
    }

    private static string ReadNuspecVersion(string nupkgPath)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(nupkgPath);
        var nuspecEntry = archive.Entries.FirstOrDefault(
            e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        nuspecEntry.Should().NotBeNull("a .nupkg must contain a .nuspec manifest");
        using var stream = nuspecEntry!.Open();
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        var match = System.Text.RegularExpressions.Regex.Match(
            content,
            @"<version>([^<]+)</version>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        match.Success.Should().BeTrue("the .nuspec must declare a <version> element");
        return match.Groups[1].Value.Trim();
    }

    private static string ReadNuspecLicense(string nupkgPath)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(nupkgPath);
        var nuspecEntry = archive.Entries.FirstOrDefault(
            e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        nuspecEntry.Should().NotBeNull("a .nupkg must contain a .nuspec manifest");
        using var stream = nuspecEntry!.Open();
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        var match = System.Text.RegularExpressions.Regex.Match(
            content,
            @"<license\s+type=""expression""[^>]*>([^<]+)</license>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static IReadOnlyList<string> ReadNuspecDependencyIds(string nupkgPath)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(nupkgPath);
        var nuspecEntry = archive.Entries.FirstOrDefault(
            e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        nuspecEntry.Should().NotBeNull("a .nupkg must contain a .nuspec manifest");
        using var stream = nuspecEntry!.Open();
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        return System.Text.RegularExpressions.Regex.Matches(
                content,
                @"<dependency\s+id=""([^""]+)""",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Select(match => match.Groups[1].Value)
            .ToList();
    }

    private static IReadOnlyList<string> ListNupkgEntries(string nupkgPath)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(nupkgPath);
        return archive.Entries.Select(e => e.FullName).ToList();
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDir,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var program = fileName;
        var argList = new List<string>(arguments);
        if (OperatingSystem.IsWindows() && IsBatchScriptRequiringCmdExe(fileName))
        {
            program = "cmd.exe";
            argList.Insert(0, fileName);
            argList.Insert(0, "/c");
        }

        var psi = new ProcessStartInfo(program)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in argList)
        {
            psi.ArgumentList.Add(arg);
        }

        using var proc = Process.Start(psi)!;
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        try
        {
            await proc.WaitForExitAsync(ct).WaitAsync(timeout, ct);
        }
        catch (TimeoutException)
        {
            KillProcessTreeIgnoringAlreadyExitedOrDenied(proc);
            throw new TimeoutException($"`{program} {string.Join(' ', argList)}` did not exit within {timeout}.");
        }
        catch (OperationCanceledException)
        {
            KillProcessTreeIgnoringAlreadyExitedOrDenied(proc);
            throw;
        }
        return (proc.ExitCode, await stdoutTask, await stderrTask);
    }

    private static bool IsBatchScriptRequiringCmdExe(string fileName) =>
        fileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

    private static void KillProcessTreeIgnoringAlreadyExitedOrDenied(Process proc)
    {
        try
        {
            proc.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
