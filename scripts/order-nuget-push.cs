// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

if (args is ["-h" or "--help", ..])
{
    Console.Out.Write(OrderNugetPush.Usage);
    return 0;
}

var invocation = Invocation.Parse(args);
if (invocation is null)
{
    Console.Error.Write(OrderNugetPush.Usage);
    return ExitCodes.UsageOrEnvironment;
}

try
{
    return OrderNugetPush.Run(
        invocation,
        OrderNugetPush.PublishedPackageIdsFile(),
        Environment.GetEnvironmentVariable("NUGET_API_KEY"));
}
catch (GateFailure failure)
{
    Console.Error.WriteLine($"order-nuget-push: {failure.Message}");
    return failure.ExitCode;
}

internal static class ExitCodes
{
    public const int Success = 0;
    public const int InvalidPackageSetOrGraphOrPush = 1;
    public const int UsageOrEnvironment = 2;
}

internal sealed class GateFailure(string message, int exitCode) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}

internal sealed record Invocation(string NupkgDirectory, string? PushSource)
{
    public static Invocation? Parse(string[] args) => args switch
    {
        [var directory] => new Invocation(directory, null),
        [var directory, "--push", var source] when source.Length > 0 => new Invocation(directory, source),
        _ => null,
    };
}

internal sealed record PackedPackage(string Path, IReadOnlySet<string> Dependencies);

internal static class OrderNugetPush
{
    public const string Usage = """
        Usage: dotnet run scripts/order-nuget-push.cs -- <nupkg-dir> [--push <source>]

        Compute (and optionally run) the dependency-ordered nuget.org push for the
        packages this repository publishes (scripts/published-package-ids.txt).

          1. Fails unless the set of package ids under <nupkg-dir> is EXACTLY the
             list in scripts/published-package-ids.txt (no missing id, no extra
             package), naming the difference.
          2. Reads each package's nuspec <dependency id> entries and topologically
             sorts the packed set, dependencies first, breaking ties alphabetically
             by package id. Fails on a dependency cycle. A dependency on an id
             outside the published set (e.g. a third-party package) is ignored for
             ordering purposes.
          3. Prints the ordered .nupkg paths, one per line, to stdout.
          4. With --push <source>, additionally pushes each package to <source> in
             that order via 'dotnet nuget push --skip-duplicate', stopping at the
             first failure so that every package already pushed stays a
             dependency-complete prefix. Reads the api key from the NUGET_API_KEY
             environment variable, which must be set when --push is used.

        Exit codes: 0 ordered (and pushed, with --push); 1 the packed set or the
        dependency graph is invalid, or a push failed; 2 usage or environment error.

        """;

    public static string PublishedPackageIdsFile()
    {
        var scriptDirectory = AppContext.GetData("EntryPointFileDirectoryPath") as string
            ?? throw new GateFailure("run this file with 'dotnet run scripts/order-nuget-push.cs'", ExitCodes.UsageOrEnvironment);
        return Path.Combine(scriptDirectory, "published-package-ids.txt");
    }

    public static int Run(Invocation invocation, string publishedIdsFile, string? apiKey)
    {
        if (!Directory.Exists(invocation.NupkgDirectory))
        {
            throw new GateFailure($"not a directory: {invocation.NupkgDirectory}", ExitCodes.UsageOrEnvironment);
        }
        if (!File.Exists(publishedIdsFile))
        {
            throw new GateFailure($"missing {publishedIdsFile}", ExitCodes.UsageOrEnvironment);
        }
        if (invocation.PushSource is not null && string.IsNullOrEmpty(apiKey))
        {
            throw new GateFailure("--push requires the NUGET_API_KEY environment variable", ExitCodes.UsageOrEnvironment);
        }

        var publishedIds = ReadPublishedIds(publishedIdsFile);
        var packagesById = ReadPackedPackages(invocation.NupkgDirectory);
        RequireExactSet(invocation.NupkgDirectory, publishedIdsFile, publishedIds, packagesById.Keys.ToHashSet());

        var orderedPaths = DependencyFirstOrder(publishedIds, packagesById)
            .Select(id => packagesById[id].Path)
            .ToList();
        foreach (var path in orderedPaths)
        {
            Console.Out.Write($"{path}\n");
        }

        return invocation.PushSource is null
            ? ExitCodes.Success
            : Push(orderedPaths, invocation.PushSource, apiKey!);
    }

    private static HashSet<string> ReadPublishedIds(string publishedIdsFile)
    {
        var ids = File.ReadAllLines(publishedIdsFile)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
        var distinct = ids.ToHashSet(StringComparer.Ordinal);
        return distinct.Count == ids.Count
            ? distinct
            : throw new GateFailure($"{publishedIdsFile} lists a duplicate package id", ExitCodes.UsageOrEnvironment);
    }

    private static Dictionary<string, PackedPackage> ReadPackedPackages(string nupkgDirectory)
    {
        var paths = Directory.GetFiles(nupkgDirectory, "*.nupkg").Order(StringComparer.Ordinal).ToList();
        if (paths.Count == 0)
        {
            throw new GateFailure($"no .nupkg files found in {nupkgDirectory}", ExitCodes.UsageOrEnvironment);
        }

        var packagesById = new Dictionary<string, PackedPackage>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var (id, dependencies) = ReadNuspec(path);
            if (packagesById.TryGetValue(id, out var existing))
            {
                throw new GateFailure($"duplicate package id {id}: {existing.Path} and {path}", ExitCodes.UsageOrEnvironment);
            }
            packagesById[id] = new PackedPackage(path, dependencies);
        }
        return packagesById;
    }

    private static (string Id, IReadOnlySet<string> Dependencies) ReadNuspec(string nupkgPath)
    {
        XElement root;
        try
        {
            using var archive = ZipFile.OpenRead(nupkgPath);
            var nuspecEntries = archive.Entries
                .Where(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (nuspecEntries.Count != 1)
            {
                throw new GateFailure(
                    $"{nupkgPath}: expected exactly one .nuspec entry, found {nuspecEntries.Count}",
                    ExitCodes.UsageOrEnvironment);
            }
            using var stream = nuspecEntries[0].Open();
            root = XDocument.Load(stream).Root!;
        }
        catch (Exception unreadable) when (unreadable is InvalidDataException or XmlException)
        {
            throw new GateFailure($"{nupkgPath}: cannot read nuspec: {unreadable.Message}", ExitCodes.UsageOrEnvironment);
        }

        var metadata = root.Elements().FirstOrDefault(element => element.Name.LocalName == "metadata")
            ?? throw new GateFailure($"{nupkgPath}: nuspec has no <metadata>", ExitCodes.UsageOrEnvironment);
        var id = metadata.Elements().FirstOrDefault(element => element.Name.LocalName == "id")?.Value.Trim() ?? "";
        if (id.Length == 0)
        {
            throw new GateFailure($"{nupkgPath}: nuspec <metadata> has no <id>", ExitCodes.UsageOrEnvironment);
        }

        var dependencies = metadata.Elements().Where(element => element.Name.LocalName == "dependencies")
            .Take(1)
            .SelectMany(element => element.Descendants())
            .Where(element => element.Name.LocalName == "dependency")
            .Select(element => (element.Attribute("id")?.Value ?? "").Trim())
            .Where(dependencyId => dependencyId.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        return (id, dependencies);
    }

    private static void RequireExactSet(
        string nupkgDirectory,
        string publishedIdsFile,
        IReadOnlySet<string> publishedIds,
        IReadOnlySet<string> packedIds)
    {
        var missing = publishedIds.Except(packedIds).Order(StringComparer.Ordinal).ToList();
        var extra = packedIds.Except(publishedIds).Order(StringComparer.Ordinal).ToList();
        if (missing.Count == 0 && extra.Count == 0)
        {
            return;
        }

        var lines = new List<string> { $"package set under {nupkgDirectory} does not match {publishedIdsFile}" };
        if (missing.Count > 0)
        {
            lines.Add($"  missing: {string.Join(", ", missing)}");
        }
        if (extra.Count > 0)
        {
            lines.Add($"  extra:   {string.Join(", ", extra)}");
        }
        throw new GateFailure(string.Join('\n', lines), ExitCodes.InvalidPackageSetOrGraphOrPush);
    }

    private static List<string> DependencyFirstOrder(
        IReadOnlySet<string> publishedIds,
        IReadOnlyDictionary<string, PackedPackage> packagesById)
    {
        var publishedDependenciesById = publishedIds.ToDictionary(
            id => id,
            id => packagesById[id].Dependencies.Where(dependency => publishedIds.Contains(dependency) && dependency != id).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var ordered = new List<string>();
        var orderedSet = new HashSet<string>(StringComparer.Ordinal);
        while (ordered.Count < publishedIds.Count)
        {
            var next = publishedIds
                .Where(id => !orderedSet.Contains(id) && publishedDependenciesById[id].IsSubsetOf(orderedSet))
                .Order(StringComparer.Ordinal)
                .Cast<string?>()
                .FirstOrDefault();
            if (next is null)
            {
                var cyclic = publishedIds.Except(orderedSet).Order(StringComparer.Ordinal);
                throw new GateFailure($"dependency cycle among: {string.Join(", ", cyclic)}", ExitCodes.InvalidPackageSetOrGraphOrPush);
            }
            ordered.Add(next);
            orderedSet.Add(next);
        }
        return ordered;
    }

    private static int Push(IReadOnlyList<string> orderedPaths, string source, string apiKey)
    {
        foreach (var path in orderedPaths)
        {
            Console.Out.Write($"Pushing {path}\n");
            if (!PushSucceeds(path, source, apiKey))
            {
                Console.Error.WriteLine(
                    $"order-nuget-push: push failed for {path}: stopping; every package pushed before this one is a dependency-complete prefix");
                return ExitCodes.InvalidPackageSetOrGraphOrPush;
            }
        }
        return ExitCodes.Success;
    }

    private static bool PushSucceeds(string path, string source, string apiKey)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        foreach (var argument in new[] { "nuget", "push", path, "--source", source, "--api-key", apiKey, "--skip-duplicate" })
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)!;
        process.WaitForExit();
        return process.ExitCode == 0;
    }
}
