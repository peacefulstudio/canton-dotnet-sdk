// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using System.Text;

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.DarParser;

/// <summary>
/// Represents a Daml Archive (DAR) file containing compiled Daml packages.
/// </summary>
public sealed class DarArchive : IDarSource
{
    internal const long MaxDalfEntryUncompressedBytes = 256L * 1024 * 1024;
    internal const long MaxTotalUncompressedBytes = 1024L * 1024 * 1024;

    /// <summary>
    /// The real manifest is a handful of short lines; this only needs to be large
    /// enough for a pathological but legitimate one, not for the .dalf caps above.
    /// </summary>
    internal const long MaxManifestUncompressedBytes = 1L * 1024 * 1024;

    private const int MaxManifestLineBytes = 511;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Gets the main package in the DAR.
    /// </summary>
    public required DamlPackage MainPackage { get; init; }

    /// <summary>
    /// Gets the dependency packages included in the DAR.
    /// </summary>
    public required IReadOnlyList<DamlPackage> Dependencies { get; init; }

    /// <summary>
    /// Reads a DAR file asynchronously.
    /// </summary>
    /// <remarks>
    /// Dependency references are resolved against the packages bundled in the
    /// archive, by exact match on the package id — which
    /// <see cref="DalfReader.Parse"/> holds to Daml-LF's lowercase hex form, so
    /// that a malformed id fails at the DALF that declares it rather than turning
    /// into an unresolved reference here. A reference whose package id is not
    /// present in the archive is a
    /// legal state (the package may be provided externally, e.g. by the
    /// participant's package store): such a reference keeps its
    /// <c>PackageId</c> but carries <c>null</c>
    /// <see cref="Daml.Codegen.Intermediate.Model.DamlPackageReference.Name"/> and
    /// <see cref="Daml.Codegen.Intermediate.Model.DamlPackageReference.Version"/>.
    /// <para>
    /// The manifest follows the JAR convention of folding long values across lines, where a line
    /// beginning with a space continues the previous one. Those continuations are rejoined before
    /// the manifest is split into entries, or a long <c>Dalfs:</c> list parses as garbage. Each
    /// physical line, continuations included, may be at most 511 bytes before its terminator:
    /// <c>java.util.jar.Manifest</c>, which the JVM helper reads the manifest with, rejects a
    /// longer one, so the same archive is an <see cref="InvalidDataException"/> here.
    /// </para>
    /// <para>
    /// The manifest's <c>Dalfs:</c> list decides which entries are packages, as it does for the JVM
    /// helper: an entry the list omits is ignored without being parsed, and a listed entry the
    /// archive lacks is an <see cref="InvalidDataException"/>. The <c>Main-Dalf:</c> entry is
    /// always loaded. A manifest with no <c>Dalfs:</c> header at all falls back to loading every
    /// <c>.dalf</c> entry. An archive carrying two entries of the same name is rejected, since
    /// readers disagree about which one wins.
    /// </para>
    /// </remarks>
    public static async Task<DarArchive> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var zipArchive = ZipFile.OpenRead(path);

        var entryNames = CollectEntryNamesRejectingDuplicates(zipArchive);

        var manifestEntry = zipArchive.GetEntry("META-INF/MANIFEST.MF");
        if (manifestEntry is null)
        {
            throw new InvalidDataException("DAR file is missing META-INF/MANIFEST.MF");
        }

        string? mainDalfPath = null;
        List<string>? listedDalfPaths = null;

        using (var manifestStream = manifestEntry.Open())
        using (var manifestBuffer = new MemoryStream())
        {
            await CopyBoundedAsync(
                manifestStream, manifestBuffer, MaxManifestUncompressedBytes,
                "META-INF/MANIFEST.MF", cancellationToken).ConfigureAwait(false);

            var manifestBytes = manifestBuffer.ToArray();
            RejectManifestLinesJavaUtilJarManifestCannotRead(manifestBytes);

            string manifestContent;
            try
            {
                manifestContent = StrictUtf8.GetString(manifestBytes);
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException(
                    "DAR entry 'META-INF/MANIFEST.MF' is not valid UTF-8 text.", ex);
            }

            if (manifestContent.Length > 0 && manifestContent[0] == (char)0xFEFF)
            {
                manifestContent = manifestContent[1..];
            }

            manifestContent = manifestContent.Replace("\r\n ", "").Replace("\n ", "");

            foreach (var line in manifestContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("Main-Dalf:", StringComparison.OrdinalIgnoreCase))
                {
                    mainDalfPath = line["Main-Dalf:".Length..].Trim();
                }
                else if (line.StartsWith("Dalfs:", StringComparison.OrdinalIgnoreCase))
                {
                    var paths = line["Dalfs:".Length..].Trim();
                    listedDalfPaths ??= [];
                    listedDalfPaths.AddRange(paths.Split(',').Select(p => p.Trim()));
                }
            }
        }

        if (mainDalfPath is null)
        {
            throw new InvalidDataException("DAR manifest is missing Main-Dalf entry");
        }

        var packages = new Dictionary<string, DamlPackage>();
        long totalUncompressedBytes = 0;

        foreach (var entry in SelectDalfEntries(zipArchive.Entries, entryNames, mainDalfPath, listedDalfPaths))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.Length > MaxDalfEntryUncompressedBytes)
            {
                throw new InvalidDataException(
                    $"DAR entry '{entry.FullName}' declares an uncompressed size of {entry.Length} bytes, " +
                    $"exceeding the {MaxDalfEntryUncompressedBytes}-byte per-entry cap.");
            }

            using var dalfStream = entry.Open();
            using var memoryStream = new MemoryStream();
            totalUncompressedBytes += await CopyBoundedAsync(
                dalfStream, memoryStream, MaxDalfEntryUncompressedBytes, entry.FullName, cancellationToken).ConfigureAwait(false);

            if (totalUncompressedBytes > MaxTotalUncompressedBytes)
            {
                throw new InvalidDataException(
                    $"DAR archive's combined .dalf uncompressed size exceeds the " +
                    $"{MaxTotalUncompressedBytes}-byte total cap.");
            }

            packages[entry.FullName] = ParseDalfNamingEntryOnFailure(memoryStream.ToArray(), entry.FullName, cancellationToken);
        }

        if (!packages.ContainsKey(mainDalfPath))
        {
            throw new InvalidDataException($"Main DALF not found: {mainDalfPath}");
        }

        var packagesById = new Dictionary<string, DamlPackage>(StringComparer.Ordinal);
        foreach (var package in packages.Values)
        {
            if (!packagesById.TryAdd(package.PackageId, package))
            {
                throw new InvalidDataException(
                    $"DAR contains two DALFs with the same package id '{package.PackageId}'. " +
                    "Package ids are content hashes, so a well-formed DAR carries each package at most once.");
            }
        }
        var resolved = packages.ToDictionary(
            p => p.Key,
            p => WithResolvedDependencyReferences(p.Value, packagesById));

        return new DarArchive
        {
            MainPackage = resolved[mainDalfPath],
            Dependencies = resolved
                .Where(p => p.Key != mainDalfPath)
                .Select(p => p.Value)
                .ToList()
        };
    }

    private static void RejectManifestLinesJavaUtilJarManifestCannotRead(byte[] manifestBytes)
    {
        var lineNumber = 1;
        var lineBytes = 0;
        for (var i = 0; i < manifestBytes.Length; i++)
        {
            var current = manifestBytes[i];
            if (current is (byte)'\r' or (byte)'\n')
            {
                var endsWithLineFeedAfterCarriageReturn = current == (byte)'\n' && i > 0 && manifestBytes[i - 1] == (byte)'\r';
                if (!endsWithLineFeedAfterCarriageReturn)
                {
                    lineNumber++;
                }
                lineBytes = 0;
                continue;
            }

            lineBytes++;
            if (lineBytes > MaxManifestLineBytes)
            {
                var lineLength = LineLengthFrom(manifestBytes, i - lineBytes + 1);
                throw new InvalidDataException(
                    $"DAR entry 'META-INF/MANIFEST.MF' line {lineNumber} is {lineLength} bytes, " +
                    $"over the {MaxManifestLineBytes}-byte limit java.util.jar.Manifest enforces " +
                    "when the JVM helper reads the same manifest.");
            }
        }
    }

    private static int LineLengthFrom(byte[] bytes, int lineStart)
    {
        var lineEnd = lineStart;
        while (lineEnd < bytes.Length && bytes[lineEnd] is not ((byte)'\r' or (byte)'\n'))
        {
            lineEnd++;
        }
        return lineEnd - lineStart;
    }

    private static HashSet<string> CollectEntryNamesRejectingDuplicates(ZipArchive zipArchive)
    {
        var entryNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in zipArchive.Entries)
        {
            if (!entryNames.Add(entry.FullName))
            {
                throw new InvalidDataException(
                    $"DAR contains two entries named '{entry.FullName}'. A well-formed DAR carries each entry once, " +
                    "and readers disagree about which of two same-named entries they use.");
            }
        }
        return entryNames;
    }

    private static IEnumerable<ZipArchiveEntry> SelectDalfEntries(
        IEnumerable<ZipArchiveEntry> entries,
        IReadOnlySet<string> entryNames,
        string mainDalfPath,
        IReadOnlyCollection<string>? listedDalfPaths)
    {
        if (listedDalfPaths is null)
        {
            return entries.Where(entry => entry.FullName.EndsWith(".dalf", StringComparison.OrdinalIgnoreCase));
        }

        if (!entryNames.Contains(mainDalfPath))
        {
            throw new InvalidDataException($"Main DALF not found: {mainDalfPath}");
        }

        foreach (var listedPath in listedDalfPaths)
        {
            if (listedPath.Length == 0)
            {
                throw new InvalidDataException("DAR manifest lists an empty entry in its Dalfs header.");
            }

            if (!entryNames.Contains(listedPath))
            {
                throw new InvalidDataException(
                    $"DAR manifest lists '{listedPath}' in its Dalfs header but the archive has no such entry.");
            }
        }

        var selectedPaths = new HashSet<string>(listedDalfPaths, StringComparer.Ordinal) { mainDalfPath };
        return entries.Where(entry => selectedPaths.Contains(entry.FullName));
    }

    private static DamlPackage ParseDalfNamingEntryOnFailure(byte[] dalfBytes, string entryName, CancellationToken cancellationToken)
    {
        try
        {
            return DalfReader.ParseObservingCancellation(dalfBytes, cancellationToken);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"DAR entry '{entryName}': {ex.Message}", ex);
        }
        catch (NotSupportedException ex)
        {
            throw new NotSupportedException($"DAR entry '{entryName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Copies <paramref name="source"/> into <paramref name="destination"/>,
    /// throwing <see cref="InvalidDataException"/> as soon as more than
    /// <paramref name="maxBytes"/> have been produced. A zip entry's declared
    /// <see cref="ZipArchiveEntry.Length"/> is attacker-controlled metadata,
    /// not a guarantee of the actual inflate output, so the cap is enforced
    /// against bytes actually read rather than trusted from the header.
    /// </summary>
    internal static async Task<long> CopyBoundedAsync(
        Stream source, Stream destination, long maxBytes, string entryName,
        CancellationToken cancellationToken = default)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException(
                    $"DAR entry '{entryName}' decompresses past the {maxBytes}-byte per-entry cap; " +
                    "rejecting as a likely zip bomb.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        return total;
    }

    private static DamlPackage WithResolvedDependencyReferences(
        DamlPackage package,
        IReadOnlyDictionary<string, DamlPackage> packagesById)
    {
        return new DamlPackage
        {
            PackageId = package.PackageId,
            Name = package.Name,
            Version = package.Version,
            RawVersion = package.RawVersion,
            LfVersion = package.LfVersion,
            Modules = package.Modules,
            DependencyReferences = package.DependencyReferences
                .Select(reference =>
                {
                    var resolved = packagesById.GetValueOrDefault(reference.PackageId);
                    return new DamlPackageReference
                    {
                        PackageId = reference.PackageId,
                        Name = resolved?.Name,
                        Version = resolved?.Version
                    };
                })
                .ToList(),
            UpgradedPackageId = package.UpgradedPackageId
        };
    }
}
