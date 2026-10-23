// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Daml.Codegen.MSBuild.TestSupport;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.MSBuild.Tests;

[Collection("MSBuild")]
public sealed class StandaloneUnsupportedDamlLfTests : IDisposable
{
    private const string LegacyDalfName = "legacy.dalf";

    private static readonly byte[] DummyPackageBytes = [0xDE, 0xAD, 0xBE, 0xEF];

    private readonly MsBuildIntegrationFixture _fx;
    private readonly string _projectDir;

    public StandaloneUnsupportedDamlLfTests(MsBuildIntegrationFixture fx)
    {
        _fx = fx;
        _projectDir = Path.Combine(Path.GetTempPath(), $"msbuild-unsupported-lf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_projectDir, recursive: true); }
        catch (IOException ex) { Console.Error.WriteLine($"[MSBuild test cleanup] Could not delete temp dir {_projectDir}: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { Console.Error.WriteLine($"[MSBuild test cleanup] Could not delete temp dir {_projectDir}: {ex.Message}"); }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void DamlGenerateBindings_a_daml_lf_1_archive_is_rejected_with_a_pointer_to_the_dpm_codegen_mode()
    {
        var payload = Concat([0x12, (byte)DummyPackageBytes.Length], DummyPackageBytes, [0x1A, 0x01, (byte)'1']);

        var (exit, output) = BuildDarWithPayload(payload);

        exit.Should().NotBe(0, $"a Daml-LF 1.x archive cannot be decoded by Standalone.\n{output}");
        output.Should().Contain(
            "Daml-LF 1.x archives are not supported here; set <DamlCodegenMode>Dpm</DamlCodegenMode> in your project to decode them with `dpm codegen-cs`.");
    }

    [Fact]
    public void DamlGenerateBindings_an_unsupported_daml_lf_2_minor_is_rejected_with_a_pointer_to_the_dpm_codegen_mode()
    {
        var payload = Concat([0x1A, 0x02, (byte)'9', (byte)'9'], [0x22, (byte)DummyPackageBytes.Length], DummyPackageBytes);

        var (exit, output) = BuildDarWithPayload(payload);

        exit.Should().NotBe(0, $"a Daml-LF 2.99 archive cannot be decoded by Standalone.\n{output}");
        output.Should().Contain(
            "Daml-LF 2.99 archives are not supported here; the supported minors are 2.1, 2.2, 2.3, 2.4, 2.5-rc1; " +
            "set <DamlCodegenMode>Dpm</DamlCodegenMode> in your project to decode other versions with `dpm codegen-cs`.");
    }

    private (int ExitCode, string Output) BuildDarWithPayload(byte[] archivePayload)
    {
        var dar = Path.Combine(_projectDir, "legacy.dar");
        WriteDar(dar, ArchiveWithVerifiedHash(archivePayload));
        ConsumerProjectWriter.Write(
            _projectDir,
            _fx.PropsFile,
            _fx.TargetsFile,
            generatedDir: Path.Combine(_projectDir, "DamlGenerated") + Path.DirectorySeparatorChar,
            darPaths: [dar],
            damlCodegenMode: "Standalone");

        var (exit, stdout, stderr) = MsBuildProcessRunner.RunDotnetBuild(
            _projectDir, "--target DamlGenerateBindings --verbosity minimal");
        return (exit, $"STDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }

    private static byte[] ArchiveWithVerifiedHash(byte[] payload)
    {
        var hash = Encoding.ASCII.GetBytes(Convert.ToHexStringLower(SHA256.HashData(payload)));
        return Concat([0x1A, (byte)payload.Length], payload, [0x22, (byte)hash.Length], hash);
    }

    private static void WriteDar(string path, byte[] dalf)
    {
        using var file = new FileStream(path, FileMode.Create);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        WriteEntry(zip, "META-INF/MANIFEST.MF", Encoding.UTF8.GetBytes($"Manifest-Version: 1.0\nMain-Dalf: {LegacyDalfName}\nDalfs: {LegacyDalfName}\n"));
        WriteEntry(zip, LegacyDalfName, dalf);
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] content)
    {
        using var stream = zip.CreateEntry(name).Open();
        stream.Write(content);
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(part => part).ToArray();
}
