// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Daml.Codegen.MSBuild.TestSupport;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.MSBuild.Tests;

/// <summary>
/// Pins how <c>Canton.Ledger.Grpc/DownloadProtos.targets</c> behaves when two builds of
/// the same checkout run it at once: both must finish with the full proto
/// set in place, instead of one deleting the JAR the other is about to hash (MSB3954) or
/// one moving over a file the other just published (MSB3677).
/// A local server stands in for Maven Central and holds the first request for each JAR
/// until a second one arrives, so the two builds are inside the download together.
/// </summary>
public sealed class DownloadProtosTargetsTests : IDisposable
{
    private const string CantonVersion = "1.0.0";
    private const string GoogleCommonProtosVersion = "9.9.9";

    private static readonly string[] JarRelativePaths =
    [
        $"com/daml/ledger-api-proto/{CantonVersion}/ledger-api-proto-{CantonVersion}.jar",
        $"com/daml/ledger-api-value-proto/{CantonVersion}/ledger-api-value-proto-{CantonVersion}.jar",
        $"com/google/api/grpc/proto-google-common-protos/{GoogleCommonProtosVersion}/proto-google-common-protos-{GoogleCommonProtosVersion}.jar",
    ];

    private readonly string _projectDir;

    public DownloadProtosTargetsTests()
    {
        _projectDir = Path.Combine(Path.GetTempPath(), $"download-protos-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_projectDir, recursive: true); }
        catch (IOException ex) { Console.Error.WriteLine($"[download-protos test cleanup] Could not delete {_projectDir}: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { Console.Error.WriteLine($"[download-protos test cleanup] Could not delete {_projectDir}: {ex.Message}"); }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task DownloadProtos_two_concurrent_builds_both_succeed_and_leave_the_full_proto_set()
    {
        var jars = JarRelativePaths.Select((path, index) => (Path: path, Bytes: JarContaining($"pkg{index}/file{index}.proto"))).ToArray();
        using var server = new SlowMavenServer(jars.ToDictionary(jar => "/" + jar.Path, jar => jar.Bytes));
        WriteProbeProject();
        var arguments = BuildArguments(server.BaseUrl, jars.Select(jar => HashOf(jar.Bytes)).ToArray());

        var builds = await Task.WhenAll(
            Task.Run(() => MsBuildProcessRunner.RunDotnet(arguments, _projectDir), TestContext.Current.CancellationToken),
            Task.Run(() => MsBuildProcessRunner.RunDotnet(arguments, _projectDir), TestContext.Current.CancellationToken));

        foreach (var build in builds)
        {
            build.ExitCode.Should().Be(0,
                $"each concurrent build must finish the proto download.\nSTDOUT:\n{build.StdOut}\nSTDERR:\n{build.StdErr}");
        }

        var publishedDir = PublishedDir();
        for (var index = 0; index < jars.Length; index++)
        {
            File.Exists(Path.Combine(publishedDir, "protos", $"pkg{index}", $"file{index}.proto")).Should().BeTrue(
                $"the contents of jar {index} must be extracted under the published protos directory");
            File.Exists(Path.Combine(publishedDir, Path.GetFileName(JarRelativePaths[index]))).Should().BeTrue(
                $"jar {index} must stay beside the extracted protos so later builds can re-verify its checksum");
        }
        Directory.GetFileSystemEntries(Path.Combine(_projectDir, "obj", "proto-cache")).Should().Equal(
            [publishedDir],
            "the published directory must be the only entry left: the losing build discards its staging directory");
    }

    [Fact]
    public async Task DownloadProtos_jar_failing_its_pinned_hash_fails_the_build_and_publishes_nothing()
    {
        var jars = JarRelativePaths.Select((path, index) => (Path: path, Bytes: JarContaining($"pkg{index}/file{index}.proto"))).ToArray();
        using var server = new SlowMavenServer(jars.ToDictionary(jar => "/" + jar.Path, jar => jar.Bytes), holdUntilSecondRequest: false);
        WriteProbeProject();
        var hashes = jars.Select(jar => HashOf(jar.Bytes)).ToArray();
        hashes[1] = new string('0', 64);

        var build = await Task.Run(() => MsBuildProcessRunner.RunDotnet(BuildArguments(server.BaseUrl, hashes), _projectDir), TestContext.Current.CancellationToken);

        build.ExitCode.Should().NotBe(0, "a JAR whose SHA-256 differs from the pinned one must fail the build");
        build.StdOut.Should().Contain("SHA-256 mismatch for ledger-api-value-proto-1.0.0.jar");
        Directory.GetFileSystemEntries(Path.Combine(_projectDir, "obj", "proto-cache")).Should().BeEmpty(
            "nothing may be published until every JAR has verified, and the staging directory must be removed");
    }

    [Fact]
    public async Task DownloadProtos_build_finishing_its_download_after_another_published_leaves_published_files_untouched()
    {
        var jars = JarRelativePaths.Select((path, index) => (Path: path, Bytes: JarContaining($"pkg{index}/file{index}.proto"))).ToArray();
        using var server = new SlowMavenServer(
            jars.ToDictionary(jar => "/" + jar.Path, jar => jar.Bytes),
            holdUntilSecondRequest: false,
            gatedPath: "/" + jars[0].Path);
        WriteProbeProject();
        var arguments = BuildArguments(server.BaseUrl, jars.Select(jar => HashOf(jar.Bytes)).ToArray());
        var late = Task.Run(() => MsBuildProcessRunner.RunDotnet(arguments, _projectDir), TestContext.Current.CancellationToken);
        await server.GatedRequestHeld.WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        var first = await Task.Run(() => MsBuildProcessRunner.RunDotnet(arguments, _projectDir), TestContext.Current.CancellationToken);
        first.ExitCode.Should().Be(0, $"the build that downloads without delay must publish.\nSTDOUT:\n{first.StdOut}\nSTDERR:\n{first.StdErr}");
        var published = SnapshotPublishedFiles();
        server.ReleaseGatedRequest();
        var second = await late;

        second.ExitCode.Should().Be(0, $"the late build must finish cleanly.\nSTDOUT:\n{second.StdOut}\nSTDERR:\n{second.StdErr}");
        SnapshotPublishedFiles().Should().Equal(published,
            "a build whose marker check passed before another build published must discard its staging directory, " +
            "because moving over files the other build is already compiling makes that build fail with MSB3954");
    }

    private string PublishedDir() =>
        Path.Combine(_projectDir, "obj", "proto-cache", $"{CantonVersion}-{GoogleCommonProtosVersion}");

    private List<(string RelativePath, long CreatedTicks, long ModifiedTicks)> SnapshotPublishedFiles() =>
        Directory.GetFiles(Path.Combine(_projectDir, "obj", "proto-cache"), "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(file => (Path.GetRelativePath(_projectDir, file), File.GetCreationTimeUtc(file).Ticks, File.GetLastWriteTimeUtc(file).Ticks))
            .ToList();

    private void WriteProbeProject()
    {
        File.Copy(TargetsFile(), Path.Combine(_projectDir, "DownloadProtos.targets"));
        File.WriteAllText(Path.Combine(_projectDir, "Probe.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <Import Project="DownloadProtos.targets" />
            </Project>
            """);
    }

    private static string TargetsFile() =>
        Path.Combine(MsBuildProcessRunner.LocateRepoRoot("Canton.Ledger.Grpc"),
            "src", "Canton.Ledger.Grpc", "DownloadProtos.targets");

    private static string BuildArguments(string baseUrl, string[] hashes) =>
        "build Probe.csproj --no-restore --target:DownloadProtos -nodeReuse:false " +
        $"-p:CantonVersion={CantonVersion} -p:GoogleCommonProtosVersion={GoogleCommonProtosVersion} " +
        $"-p:MavenCentralBase={baseUrl} " +
        $"-p:LedgerApiProtoHash={hashes[0]} -p:LedgerApiValueProtoHash={hashes[1]} -p:GoogleCommonProtosHash={hashes[2]}";

    private static byte[] JarContaining(string entryName)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("syntax = \"proto3\";\n");
        }
        return stream.ToArray();
    }

    private static string HashOf(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class SlowMavenServer : IDisposable
    {
        private const int ChunkCount = 5;
        private static readonly TimeSpan ChunkDelay = TimeSpan.FromMilliseconds(200);
        private static readonly TimeSpan SecondRequestPatience = TimeSpan.FromSeconds(20);

        private readonly HttpListener _listener = new();
        private readonly Dictionary<string, byte[]> _jarsByPath;
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _secondRequestArrived = new();
        private readonly ConcurrentDictionary<string, int> _requestCounts = new();
        private readonly CancellationTokenSource _stopping = new();

        private readonly bool _holdUntilSecondRequest;
        private readonly string? _gatedPath;
        private readonly TaskCompletionSource _gatedRequestHeld = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _gatedRequestReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _gatedRequestCount;

        internal SlowMavenServer(Dictionary<string, byte[]> jarsByPath, bool holdUntilSecondRequest = true, string? gatedPath = null)
        {
            _jarsByPath = jarsByPath;
            _holdUntilSecondRequest = holdUntilSecondRequest;
            _gatedPath = gatedPath;
            BaseUrl = $"http://127.0.0.1:{FreePort()}";
            _listener.Prefixes.Add(BaseUrl + "/");
            _listener.Start();
            _ = Task.Run(AcceptLoopAsync);
        }

        internal string BaseUrl { get; }

        internal Task GatedRequestHeld => _gatedRequestHeld.Task;

        internal void ReleaseGatedRequest() => _gatedRequestReleased.TrySetResult();

        public void Dispose()
        {
            _stopping.Cancel();
            _listener.Close();
            _stopping.Dispose();
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private async Task AcceptLoopAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }
                _ = Task.Run(() => ServeAsync(context));
            }
        }

        private async Task ServeAsync(HttpListenerContext context)
        {
            var path = context.Request.Url!.AbsolutePath;
            try
            {
                if (!_jarsByPath.TryGetValue(path, out var bytes))
                {
                    context.Response.StatusCode = 404;
                    return;
                }

                if (_holdUntilSecondRequest)
                {
                    await HoldFirstRequestUntilSecondArrivesAsync(path);
                }
                if (path == _gatedPath && Interlocked.Increment(ref _gatedRequestCount) == 1)
                {
                    _gatedRequestHeld.TrySetResult();
                    await _gatedRequestReleased.Task.WaitAsync(TimeSpan.FromMinutes(2), _stopping.Token);
                }
                context.Response.ContentLength64 = bytes.Length;
                var chunkSize = bytes.Length / ChunkCount + 1;
                for (var offset = 0; offset < bytes.Length; offset += chunkSize)
                {
                    var length = Math.Min(chunkSize, bytes.Length - offset);
                    await context.Response.OutputStream.WriteAsync(bytes.AsMemory(offset, length), _stopping.Token);
                    await context.Response.OutputStream.FlushAsync(_stopping.Token);
                    await Task.Delay(ChunkDelay, _stopping.Token);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or IOException or ObjectDisposedException)
            {
            }
            finally
            {
                try { context.Response.Close(); }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException) { }
            }
        }

        private async Task HoldFirstRequestUntilSecondArrivesAsync(string path)
        {
            var arrived = _secondRequestArrived.GetOrAdd(path, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            if (_requestCounts.AddOrUpdate(path, 1, (_, count) => count + 1) >= 2)
            {
                arrived.TrySetResult();
                return;
            }
            await Task.WhenAny(arrived.Task, Task.Delay(SecondRequestPatience, _stopping.Token));
        }
    }
}
