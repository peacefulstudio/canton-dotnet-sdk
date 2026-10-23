// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Microsoft.Extensions.Logging;

namespace Daml.Codegen.CSharp.Cli;

/// <summary>
/// Writes emitted files into the output directory and keeps it free of stale ones. A
/// <c>.codegen-manifest</c> beside the output lists what the last run wrote; the next run
/// deletes the files it listed that the new run no longer emits, so a regeneration from a
/// DAR that dropped a template stops compiling the old binding. Only manifest-listed files
/// are ever deleted, and every path is confined to the output directory.
/// </summary>
internal static partial class GeneratedOutputWriter
{
    internal const string ManifestFileName = ".codegen-manifest";

    internal static async Task WriteAsync(
        IReadOnlyList<GeneratedFile> generatedFiles,
        DirectoryInfo outputDirectory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory.FullName));
        var emitted = new Dictionary<string, (GeneratedFile File, string FullPath)>(StringComparer.Ordinal);
        foreach (var file in generatedFiles)
        {
            emitted[NormalizeRelativePath(file.RelativePath)] = (file, ResolveWithinRoot(root, file.RelativePath));
        }

        DeleteStaleFiles(root, emitted.Keys.ToHashSet(StringComparer.Ordinal), logger);

        foreach (var (relativePath, (file, fullPath)) in emitted)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            if (file.IsBinary)
            {
                await File.WriteAllBytesAsync(fullPath, file.BinaryContent!, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await File.WriteAllTextAsync(fullPath, file.Content, cancellationToken).ConfigureAwait(false);
            }
            LogGeneratedFile(logger, relativePath);
        }

        var manifestLines = emitted.Keys.Order(StringComparer.Ordinal).ToList();
        await File.WriteAllLinesAsync(Path.Combine(root, ManifestFileName), manifestLines, cancellationToken).ConfigureAwait(false);
    }

    private static void DeleteStaleFiles(string root, HashSet<string> emittedPaths, ILogger logger)
    {
        var manifestPath = Path.Combine(root, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return;
        }

        foreach (var line in File.ReadAllLines(manifestPath))
        {
            var relativePath = line.Trim();
            if (relativePath.Length == 0 || emittedPaths.Contains(relativePath))
            {
                continue;
            }

            if (!TryResolveWithinRoot(root, relativePath, out var fullPath))
            {
                LogIgnoredManifestEntry(logger, relativePath);
                continue;
            }

            if (PassesThroughSymbolicLink(root, fullPath))
            {
                LogIgnoredManifestEntry(logger, relativePath);
                continue;
            }

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                LogDeletedStaleFile(logger, relativePath);
                DeleteEmptyDirectoriesUpTo(root, Path.GetDirectoryName(fullPath)!);
            }
        }
    }

    private static bool PassesThroughSymbolicLink(string root, string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath);
        while (directory is not null && !string.Equals(directory, root, StringComparison.Ordinal))
        {
            if (new DirectoryInfo(directory).LinkTarget is not null)
            {
                return true;
            }
            directory = Path.GetDirectoryName(directory);
        }
        return false;
    }

    private static void DeleteEmptyDirectoriesUpTo(string root, string directory)
    {
        while (!string.Equals(directory, root, StringComparison.Ordinal)
               && Directory.Exists(directory)
               && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory)!;
        }
    }

    private static string NormalizeRelativePath(string relativePath) =>
        relativePath.Replace('\\', '/');

    private static string ResolveWithinRoot(string root, string relativePath) =>
        TryResolveWithinRoot(root, relativePath, out var fullPath)
            ? fullPath
            : throw new InvalidDataException(
                $"Generated file path '{relativePath}' resolves outside the output directory '{root}'.");

    private static bool TryResolveWithinRoot(string root, string relativePath, out string fullPath)
    {
        fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal);
    }

    [LoggerMessage(EventId = 2012, Level = LogLevel.Debug, Message = "  Generated: {RelativePath}")]
    private static partial void LogGeneratedFile(ILogger logger, string relativePath);

    [LoggerMessage(EventId = 2013, Level = LogLevel.Information, Message = "  Deleted stale file from a previous run: {RelativePath}")]
    private static partial void LogDeletedStaleFile(ILogger logger, string relativePath);

    [LoggerMessage(EventId = 2014, Level = LogLevel.Warning, Message = "  Ignored manifest entry outside the output directory: {RelativePath}")]
    private static partial void LogIgnoredManifestEntry(ILogger logger, string relativePath);
}
