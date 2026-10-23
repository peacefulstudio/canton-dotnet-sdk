// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using System.Text;
using Daml.Codegen.DarParser;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.DarParser.Tests;

public sealed class DarArchiveReadTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    [Fact]
    public async Task ReadAsync_throws_when_manifest_is_missing()
    {
        var path = await CreateDar(manifest: null);

        var act = async () => await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("DAR file is missing META-INF/MANIFEST.MF");
    }

    [Fact]
    public async Task ReadAsync_with_cancelled_token_throws_OperationCanceledException()
    {
        var path = await CreateDar(
            "Manifest-Version: 1.0\nMain-Dalf: contracts/main.dalf\n",
            ("contracts/main.dalf", DalfTestFixtures.MinimalDalf("cancel-pkg", "1.0.0")));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await DarArchive.ReadAsync(path, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReadAsync_throws_when_manifest_has_no_main_dalf_entry()
    {
        var path = await CreateDar("Manifest-Version: 1.0\n");

        var act = async () => await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("DAR manifest is missing Main-Dalf entry");
    }

    [Fact]
    public async Task ReadAsync_throws_when_main_dalf_path_is_not_in_zip()
    {
        var path = await CreateDar("Manifest-Version: 1.0\nMain-Dalf: contracts/main.dalf\n");

        var act = async () => await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("Main DALF not found: contracts/main.dalf");
    }

    [Fact]
    public async Task ReadAsync_joins_manifest_continuation_lines_so_main_dalf_path_resolves()
    {
        var manifest =
            "Manifest-Version: 1.0\n" +
            "Main-Dalf: contracts/\n" +
            " main.dalf\n";

        var path = await CreateDar(
            manifest,
            ("contracts/main.dalf", DalfTestFixtures.MinimalDalf("joined-pkg", "3.0.0")));

        var archive = await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        archive.MainPackage.Name.Should().Be("joined-pkg");
    }

    [Fact]
    public async Task ReadAsync_loads_dependency_dalfs_and_resolves_their_names()
    {
        var manifest =
            "Manifest-Version: 1.0\n" +
            "Main-Dalf: contracts/main.dalf\n" +
            "Dalfs: contracts/main.dalf, contracts/dep.dalf\n";

        var path = await CreateDar(
            manifest,
            ("contracts/main.dalf", DalfTestFixtures.MinimalDalf("main-pkg", "1.0.0")),
            ("contracts/dep.dalf",  DalfTestFixtures.MinimalDalf("dep-pkg",  "2.0.0")));

        var archive = await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        archive.MainPackage.Name.Should().Be("main-pkg");
        archive.Dependencies.Should().HaveCount(1);
        archive.Dependencies[0].Name.Should().Be("dep-pkg");
        archive.Dependencies[0].Version.Should().Be(new Version(2, 0, 0));
    }

    [Fact]
    public async Task ReadAsync_returns_packages_with_dependency_references_already_resolved()
    {
        var manifest =
            "Manifest-Version: 1.0\n" +
            "Main-Dalf: contracts/main.dalf\n" +
            "Dalfs: contracts/main.dalf, contracts/dep.dalf\n";

        var dep = DalfTestFixtures.MinimalDalf("dep-pkg", "2.0.0");
        var depPackageId = DalfTestFixtures.PackageIdOf(dep);

        var path = await CreateDar(
            manifest,
            ("contracts/main.dalf", DalfTestFixtures.MinimalDalfImporting("main-pkg", "1.0.0", depPackageId, "absent-hash")),
            ("contracts/dep.dalf",  dep));

        var archive = await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        var references = archive.MainPackage.DependencyReferences;
        references.Should().HaveCount(2);
        var resolved = references.Single(r => r.PackageId == depPackageId);
        resolved.Name.Should().Be("dep-pkg");
        resolved.Version.Should().Be(new Version(2, 0, 0));
        var unresolved = references.Single(r => r.PackageId == "absent-hash");
        unresolved.Name.Should().BeNull();
        unresolved.Version.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsync_rejects_a_manifest_whose_uncompressed_size_exceeds_the_cap()
    {
        var oversizedManifest = "Manifest-Version: 1.0\n" + new string('x', (int)DarArchive.MaxManifestUncompressedBytes);

        var path = await CreateDar(oversizedManifest);

        var act = async () => await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*META-INF/MANIFEST.MF*cap*");
    }

    [Fact]
    public async Task ReadAsync_names_the_manifest_when_it_is_not_valid_utf8_text()
    {
        var path = await CreateDar(manifest: null, ("META-INF/MANIFEST.MF", new byte[] { 0x80 }));

        var act = async () => await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*META-INF/MANIFEST.MF*UTF-8*");
    }

    private const string ValidManifestHead = "Manifest-Version: 1.0\nMain-Dalf: contracts/main.dalf\n";

    private static string PaddingLine(int contentBytes) => "Padding: " + new string('a', contentBytes - "Padding: ".Length);

    private static string ContinuationLine(int contentBytes) => " " + new string('a', contentBytes - 1);

    private static string MultibytePaddingLine(int contentBytes)
    {
        var twoByteLetters = (contentBytes - "Padding: ".Length) / 2;
        var oddByte = (contentBytes - "Padding: ".Length) % 2 == 1 ? "a" : string.Empty;
        return "Padding: " + new string('\u00e9', twoByteLetters) + oddByte;
    }

    public static TheoryData<string, string> ManifestLinesAtTheLimit => new()
    {
        { "LF-terminated", ValidManifestHead + PaddingLine(511) + "\n" },
        { "CRLF-terminated", ValidManifestHead.Replace("\n", "\r\n") + PaddingLine(511) + "\r\n" },
        { "CR-terminated", ValidManifestHead + PaddingLine(511) + "\rX: y\n" },
        { "unterminated last line", ValidManifestHead + PaddingLine(511) },
        { "multibyte", ValidManifestHead + MultibytePaddingLine(511) + "\n" },
        { "continuation", ValidManifestHead + "Padding: x\n" + ContinuationLine(511) + "\n" },
    };

    public static TheoryData<string, string, int, int> ManifestLinesOneByteOverTheLimit => new()
    {
        { "LF-terminated", ValidManifestHead + PaddingLine(512) + "\n", 3, 512 },
        { "CRLF-terminated", ValidManifestHead.Replace("\n", "\r\n") + PaddingLine(512) + "\r\n", 3, 512 },
        { "CR-terminated", ValidManifestHead + PaddingLine(512) + "\rX: y\n", 3, 512 },
        { "unterminated last line", ValidManifestHead + PaddingLine(512), 3, 512 },
        { "multibyte", ValidManifestHead + MultibytePaddingLine(512) + "\n", 3, 512 },
        { "continuation", ValidManifestHead + "Padding: x\n" + ContinuationLine(512) + "\n", 4, 512 },
        { "long line", ValidManifestHead + PaddingLine(4096) + "\n", 3, 4096 },
    };

    [Theory]
    [MemberData(nameof(ManifestLinesAtTheLimit))]
    public async Task ReadAsync_accepts_a_manifest_line_of_511_bytes_as_java_util_jar_Manifest_does(string shape, string manifest)
    {
        var path = await CreateDar(
            manifest,
            ("contracts/main.dalf", DalfTestFixtures.MinimalDalf("main-pkg", "1.0.0")));

        var archive = await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        archive.MainPackage.Name.Should().Be("main-pkg", shape);
    }

    [Theory]
    [MemberData(nameof(ManifestLinesOneByteOverTheLimit))]
    public async Task ReadAsync_rejects_a_manifest_line_of_512_bytes_or_more_as_java_util_jar_Manifest_does(
        string shape, string manifest, int offendingLine, int offendingLineBytes)
    {
        var path = await CreateDar(
            manifest,
            ("contracts/main.dalf", DalfTestFixtures.MinimalDalf("main-pkg", "1.0.0")));

        var act = async () => await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidDataException>(shape))
            .WithMessage($"*META-INF/MANIFEST.MF*line {offendingLine}*{offendingLineBytes} bytes*511*");
    }

    [Fact]
    public async Task ReadAsync_rejects_a_dalf_entry_whose_uncompressed_size_exceeds_the_per_entry_cap()
    {
        var manifest = "Manifest-Version: 1.0\nMain-Dalf: contracts/main.dalf\n";
        var bomb = new byte[DarArchive.MaxDalfEntryUncompressedBytes + 1];

        var path = await CreateDar(manifest, ("contracts/main.dalf", bomb));

        var act = async () => await DarArchive.ReadAsync(path);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*per-entry cap*");
    }

    [Fact]
    public async Task CopyBoundedAsync_rejects_a_source_stream_whose_actual_byte_count_exceeds_the_cap_regardless_of_any_declared_length()
    {
        using var source = new MemoryStream(new byte[101]);
        using var destination = new MemoryStream();

        var act = async () => await DarArchive.CopyBoundedAsync(
            source, destination, maxBytes: 100, entryName: "hostile.dalf",
            cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*zip bomb*");
    }

    [Fact]
    public async Task CopyBoundedAsync_allows_a_stream_at_exactly_the_cap()
    {
        var data = new byte[100];
        using var source = new MemoryStream(data);
        using var destination = new MemoryStream();

        var total = await DarArchive.CopyBoundedAsync(
            source, destination, maxBytes: 100, entryName: "fine.dalf",
            cancellationToken: TestContext.Current.CancellationToken);

        total.Should().Be(100);
        destination.ToArray().Should().Equal(data);
    }

    [Fact]
    public async Task ReadAsync_throws_a_contextual_error_when_two_dalfs_carry_the_same_package_id()
    {
        var manifest =
            "Manifest-Version: 1.0\n" +
            "Main-Dalf: contracts/main.dalf\n" +
            "Dalfs: contracts/main.dalf, contracts/twin.dalf\n";

        var dalf = DalfTestFixtures.MinimalDalf("main-pkg", "1.0.0");
        var duplicatedPackageId = DalfTestFixtures.PackageIdOf(dalf);

        var path = await CreateDar(
            manifest,
            ("contracts/main.dalf", dalf),
            ("contracts/twin.dalf", dalf));

        var act = async () => await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage($"*{duplicatedPackageId}*", "the bare ArgumentException from ToDictionary names neither the DAR nor the offending package id");
    }

    [Fact]
    public async Task ReadAsync_names_the_offending_entry_when_a_dalf_declares_a_hash_that_does_not_match()
    {
        var manifest =
            "Manifest-Version: 1.0\n" +
            "Main-Dalf: contracts/main.dalf\n" +
            "Dalfs: contracts/main.dalf, contracts/dep.dalf\n";

        var path = await CreateDar(
            manifest,
            ("contracts/main.dalf", DalfTestFixtures.MinimalDalf("main-pkg", "1.0.0")),
            ("contracts/dep.dalf", DalfTestFixtures.DalfDeclaringAMismatchedHash(
                DalfTestFixtures.MinimalDalf("dep-pkg", "1.0.0"))));

        var act = async () => await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*contracts/dep.dalf*", "two bare hashes do not say which of the DAR's entries is corrupt");
    }

    [Fact]
    public async Task ReadAsync_names_the_offending_entry_when_a_dalf_declares_its_hash_in_uppercase()
    {
        var manifest =
            "Manifest-Version: 1.0\n" +
            "Main-Dalf: contracts/main.dalf\n" +
            "Dalfs: contracts/main.dalf, contracts/dep.dalf\n";

        var dep = DalfTestFixtures.MinimalDalf("dep-pkg", "2.0.0");
        var uppercasedDependencyId = DalfTestFixtures.PackageIdOf(dep).ToUpperInvariant();

        var path = await CreateDar(
            manifest,
            ("contracts/main.dalf", DalfTestFixtures.MinimalDalfImporting("main-pkg", "1.0.0", uppercasedDependencyId)),
            ("contracts/dep.dalf", DalfTestFixtures.DalfDeclaringItsHashInUppercase(dep)));

        var act = async () => await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage(
                "*contracts/dep.dalf*",
                "an uppercase-emitting producer uses the same form in PackageImports, so the DAR used "
                + "to parse with every cross-package reference silently unresolved rather than failing "
                + "at the file that is malformed");
    }

    [Fact]
    public async Task ReadAsync_names_the_offending_entry_when_a_dalf_is_an_unsupported_lf_version()
    {
        var manifest =
            "Manifest-Version: 1.0\n" +
            "Main-Dalf: contracts/main.dalf\n" +
            "Dalfs: contracts/main.dalf, contracts/legacy.dalf\n";

        var path = await CreateDar(
            manifest,
            ("contracts/main.dalf", DalfTestFixtures.MinimalDalf("main-pkg", "1.0.0")),
            ("contracts/legacy.dalf", DalfTestFixtures.Lf1Dalf()));

        var act = async () => await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*contracts/legacy.dalf*", "the LF version message alone does not say which entry is legacy");
    }

    [Fact]
    public async Task ReadAsync_rejects_a_dar_carrying_two_manifest_entries()
    {
        var firstManifest = Encoding.UTF8.GetBytes("Manifest-Version: 1.0\nMain-Dalf: contracts/main.dalf\n");
        var secondManifest = Encoding.UTF8.GetBytes("Manifest-Version: 1.0\nMain-Dalf: contracts/other.dalf\n");

        var path = await CreateDar(
            manifest: null,
            ("META-INF/MANIFEST.MF", firstManifest),
            ("contracts/main.dalf", DalfTestFixtures.MinimalDalf("main-pkg", "1.0.0")),
            ("META-INF/MANIFEST.MF", secondManifest));

        var act = async () => await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*two entries named 'META-INF/MANIFEST.MF'*");
    }

    [Fact]
    public async Task ReadAsync_rejects_a_dar_carrying_two_dalf_entries_with_the_same_name()
    {
        var dalf = DalfTestFixtures.MinimalDalf("main-pkg", "1.0.0");

        var path = await CreateDar(
            "Manifest-Version: 1.0\nMain-Dalf: contracts/main.dalf\n",
            ("contracts/main.dalf", dalf),
            ("contracts/main.dalf", dalf));

        var act = async () => await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*two entries named 'contracts/main.dalf'*");
    }

    [Fact]
    public async Task ReadAsync_ignores_a_dalf_entry_the_manifest_does_not_list()
    {
        var manifest =
            "Manifest-Version: 1.0\n" +
            "Main-Dalf: contracts/main.dalf\n" +
            "Dalfs: contracts/main.dalf, contracts/dep.dalf\n";

        var path = await CreateDar(
            manifest,
            ("contracts/main.dalf", DalfTestFixtures.MinimalDalf("main-pkg", "1.0.0")),
            ("contracts/dep.dalf", DalfTestFixtures.MinimalDalf("dep-pkg", "2.0.0")),
            ("extra/unlisted.dalf", [0xFF, 0xFF, 0xFF]));

        var archive = await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        archive.Dependencies.Select(dependency => dependency.Name).Should().Equal("dep-pkg");
    }

    [Fact]
    public async Task ReadAsync_throws_naming_a_dalf_the_manifest_lists_but_the_archive_lacks()
    {
        var manifest =
            "Manifest-Version: 1.0\n" +
            "Main-Dalf: contracts/main.dalf\n" +
            "Dalfs: contracts/main.dalf, contracts/dep.dalf\n";

        var path = await CreateDar(
            manifest,
            ("contracts/main.dalf", DalfTestFixtures.MinimalDalf("main-pkg", "1.0.0")));

        var act = async () => await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*contracts/dep.dalf*");
    }

    [Fact]
    public async Task ReadAsync_throws_on_an_empty_item_in_the_dalfs_list()
    {
        var manifest =
            "Manifest-Version: 1.0\n" +
            "Main-Dalf: contracts/main.dalf\n" +
            "Dalfs: contracts/main.dalf, ,\n";

        var path = await CreateDar(
            manifest,
            ("contracts/main.dalf", DalfTestFixtures.MinimalDalf("main-pkg", "1.0.0")));

        var act = async () => await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*lists an empty*");
    }

    [Fact]
    public async Task ReadAsync_loads_the_main_dalf_even_when_the_dalfs_list_omits_it()
    {
        var manifest =
            "Manifest-Version: 1.0\n" +
            "Main-Dalf: contracts/main.dalf\n" +
            "Dalfs: contracts/dep.dalf\n";

        var path = await CreateDar(
            manifest,
            ("contracts/main.dalf", DalfTestFixtures.MinimalDalf("main-pkg", "1.0.0")),
            ("contracts/dep.dalf", DalfTestFixtures.MinimalDalf("dep-pkg", "2.0.0")));

        var archive = await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        archive.MainPackage.Name.Should().Be("main-pkg");
        archive.Dependencies.Select(dependency => dependency.Name).Should().Equal("dep-pkg");
    }

    [Fact]
    public async Task ReadAsync_loads_every_dalf_entry_when_the_manifest_has_no_dalfs_header()
    {
        var manifest = "Manifest-Version: 1.0\nMain-Dalf: contracts/main.dalf\n";

        var path = await CreateDar(
            manifest,
            ("contracts/main.dalf", DalfTestFixtures.MinimalDalf("main-pkg", "1.0.0")),
            ("contracts/dep.dalf", DalfTestFixtures.MinimalDalf("dep-pkg", "2.0.0")));

        var archive = await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        archive.Dependencies.Select(dependency => dependency.Name).Should().Equal("dep-pkg");
    }

    private async Task<string> CreateDar(string? manifest, params (string Path, byte[] Data)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".dar");
        _tempFiles.Add(path);

        using (var fs = new FileStream(path, FileMode.Create))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            if (manifest != null)
            {
                var manifestEntry = zip.CreateEntry("META-INF/MANIFEST.MF");
                using var ms = manifestEntry.Open();
                await ms.WriteAsync(Encoding.UTF8.GetBytes(manifest));
            }

            foreach (var (entryPath, data) in entries)
            {
                var entry = zip.CreateEntry(entryPath);
                using var es = entry.Open();
                await es.WriteAsync(data);
            }
        }

        return path;
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
            try { File.Delete(path); } catch { }
    }
}
