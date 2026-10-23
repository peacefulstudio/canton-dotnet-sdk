// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.Intermediate;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Drift-detection snapshot tests. Each sub-directory of <c>Snapshots/</c>
/// that contains an <c>intermediate.binpb</c> proto snapshot becomes one
/// theory invocation; the <c>expected/</c> sub-tree is asserted per test case
/// so that a partially-committed snapshot fails explicitly rather than being
/// silently skipped at discovery time.
///
/// Catches accidental codegen output changes — even semantically-equivalent
/// reformatting — before they ship as a behavior change in the published
/// per-family Splice NuGet packages (the drift-detection suite). When
/// codegen output legitimately changes, refresh the snapshot by following the
/// procedure in <c>Snapshots/&lt;name&gt;/README.md</c>.
///
/// The <c>richtypes</c>, <c>contractkeys</c> and <c>defaulttarget</c> snapshots own no
/// <c>expected/</c> files: the test project links the matching module folders of
/// <c>src/Daml.Codegen.Testing.Conformance/Generated</c> in, and the comparison drops the
/// <c>Daml.Codegen.Testing.Conformance.</c> root namespace that tree is emitted under.
///
/// A snapshot for a package that emits no C# types (a Daml helper library of
/// functions and re-exports, for example) is pinned by placing an
/// <c>emits-no-types</c> marker file in its snapshot directory. Such a snapshot
/// asserts that codegen produces zero <c>.cs</c> output; the day the package
/// starts emitting a type, the drift test fails so the change is not missed.
///
/// Each snapshot is also handed to Roslyn before its bytes are compared, so a
/// tree cannot be pinned — or re-blessed — containing C# that does not compile.
/// Byte equality alone only proves the emitter is deterministic; emitted files
/// are exempt from this repository's code-analysis analyzers, so nothing else
/// puts a compiler in front of them.
/// </summary>
public class DriftDetectionTests
{
    /// <summary>
    /// Snapshots whose emitted C# is known not to compile, mapped to the emitter defect
    /// that keeps them there. Each entry is asserted to still fail, so it cannot outlive
    /// its fix: the day the emitter stops producing the defect, this test goes red until
    /// the snapshot is removed from the list and joins the gate.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> SnapshotsWithKnownEmitterCompileDefects =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Enumerates every sub-directory under <c>Snapshots/</c> that has an
    /// <c>intermediate.binpb</c> proto snapshot, yielding the directory name
    /// (snapshot name) as the sole theory parameter. The presence of the
    /// <c>expected/</c> sub-tree is validated inside each theory case, not
    /// here, so that a half-committed snapshot produces an explicit failure
    /// rather than being silently excluded from discovery. Sorted by name
    /// (<see cref="StringComparer.Ordinal"/>) so discovery order is
    /// deterministic across platforms.
    /// </summary>
    public static TheoryData<string> SnapshotNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in DiscoveredSnapshotNames())
        {
            data.Add(name);
        }
        return data;
    }

    private static IReadOnlyList<string> DiscoveredSnapshotNames()
    {
        var snapshotsRoot = Path.Combine(AppContext.BaseDirectory, "Snapshots");

        if (!Directory.Exists(snapshotsRoot))
            throw new DirectoryNotFoundException(
                $"Snapshots root not found at '{snapshotsRoot}'. " +
                "Ensure the Snapshots/ directory is present in the test output; " +
                "check that snapshot fixture content is copied to the output directory in the .csproj.");

        var names = Directory.EnumerateDirectories(snapshotsRoot)
            .Where(d => File.Exists(Path.Combine(d, "intermediate.binpb")))
            .Select(d => Path.GetFileName(d)!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (names.Count == 0)
            throw new InvalidOperationException(
                $"No snapshot directories with an intermediate.binpb proto snapshot were found under '{snapshotsRoot}'. " +
                "The expected/ sub-directory is validated later for each discovered snapshot. " +
                "A zero-case theory would silently skip drift detection.");

        return names;
    }

    [Fact]
    public void SnapshotsWithKnownEmitterCompileDefects_names_only_snapshots_that_exist()
    {
        SnapshotsWithKnownEmitterCompileDefects.Keys.Should().BeSubsetOf(
            DiscoveredSnapshotNames(),
            "an entry naming a snapshot that no longer exists exempts nothing and hides that the list is stale");
    }

    [Theory]
    [MemberData(nameof(SnapshotNames))]
    public async Task Codegen_output_matches_snapshot(string snapshotName)
    {
        var snapshotDir = Path.Combine(AppContext.BaseDirectory, "Snapshots", snapshotName);
        var protoPath = Path.Combine(snapshotDir, "intermediate.binpb");
        var expectedDir = Path.Combine(snapshotDir, "expected");

        File.Exists(protoPath).Should().BeTrue(
            "the intermediate.binpb proto snapshot must ship alongside the test assembly at {0}",
            protoPath);
        Directory.Exists(expectedDir).Should().BeTrue(
            "the snapshot fixtures directory must ship alongside the test assembly at {0}",
            expectedDir);

        var generator = new CSharpCodeGenerator(new CodeGenOptions());

        IntermediateDar proto;
        await using (var stream = File.OpenRead(protoPath))
        {
            proto = IntermediateDar.Parser.ParseFrom(stream);
        }
        var dar = IntermediateDarReader.Read(proto);
        var allGenerated = generator.Generate(dar);

        var actualFiles = allGenerated
            .Where(f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal))
            .Select(f => new { f.RelativePath, f.Content })
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();

        var expectedFiles = Directory.EnumerateFiles(expectedDir, "*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".cs", StringComparison.Ordinal))
            .Select(absPath => new
            {
                RelativePath = Path.GetRelativePath(expectedDir, absPath).Replace('\\', '/'),
                AbsolutePath = absPath,
            })
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();

        var refreshHint =
            $"Codegen output drifted from the snapshot. If the change is intentional, refresh the snapshot " +
            $"per tests/Daml.Codegen.CSharp.Tests/Snapshots/{snapshotName}/README.md and re-commit. " +
            $"If the change is unintentional, fix the codegen. " +
            $"Re-run only this snapshot with: dotnet test " +
            $"--project tests/Daml.Codegen.CSharp.Tests/Daml.Codegen.CSharp.Tests.csproj --minimum-expected-tests 1 " +
            $"--filter \"FullyQualifiedName~DriftDetectionTests&DisplayName~{snapshotName}\"";

        var pinnedEmpty = File.Exists(Path.Combine(snapshotDir, "emits-no-types"));

        if (pinnedEmpty)
        {
            expectedFiles.Should().NotContain(
                f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal),
                "the 'emits-no-types' marker pins this package as emitting no C# types, but the expected/ tree contains a .cs; refresh the snapshot or remove the marker. " + refreshHint);
            actualFiles.Should().NotContain(
                f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal),
                "this package is pinned as emitting no C# types (its 'emits-no-types' marker), but codegen now produced a .cs — its emitted surface changed. If the package legitimately gained types, remove the marker and refresh the snapshot. " + refreshHint);
        }
        else
        {
            expectedFiles.Should().Contain(
                f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal),
                "the snapshot must contain at least one .cs file; an empty fixture would let the test pass vacuously (pin a genuinely type-less package with an 'emits-no-types' marker instead). " + refreshHint);
            actualFiles.Should().Contain(
                f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal),
                "codegen must emit at least one .cs file from the proto snapshot; zero .cs output indicates a regression in IntermediateDarReader.Read or Generate.");
        }

        var compileInput = MainAndNonStdlibDependencySources(generator, dar, allGenerated);

        if (!pinnedEmpty)
        {
            compileInput.Should().NotBeEmpty(
                "compiling an empty file set asserts nothing; this snapshot is not pinned as emitting no types, so its emitted sources must reach Roslyn");
        }

        var compileErrors = EmittedCodeCompilesTestHelpers.CompileEmittedFiles(compileInput)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .OrderBy(d => d.Location.GetLineSpan().Path, StringComparer.Ordinal)
            .ThenBy(d => d.Location.GetLineSpan().StartLinePosition.Line)
            .ThenBy(d => d.Location.GetLineSpan().StartLinePosition.Character)
            .ThenBy(d => d.Id, StringComparer.Ordinal)
            .ToList();

        if (SnapshotsWithKnownEmitterCompileDefects.TryGetValue(snapshotName, out var knownDefect))
        {
            compileErrors.Should().NotBeEmpty(
                "snapshot `{0}` is listed in SnapshotsWithKnownEmitterCompileDefects because {1}; its emitted C# now " +
                "compiles, so delete that entry and let the compile gate protect this snapshot from here on",
                snapshotName,
                knownDefect);
        }
        else if (compileErrors.Count > 0)
        {
            throw new Xunit.Sdk.XunitException(
                $"Snapshot `{snapshotName}` emits C# that does not compile: " +
                $"{compileErrors.Count} error-severity diagnostic(s) from {compileInput.Count} emitted file(s) " +
                "(the snapshot's own output plus its non-stdlib dependencies, so the compilation is self-contained).\n\n" +
                string.Join("\n", compileErrors.Select(RenderDiagnostic)) +
                "\n\nFix the emitter — do not hand-edit or re-bless the snapshot around a compile error.");
        }

        actualFiles.Select(f => f.RelativePath).Should().Equal(
            expectedFiles.Select(f => f.RelativePath),
            because: "the set of generated files must match the snapshot. " + refreshHint);

        foreach (var (actual, expected) in actualFiles.Zip(expectedFiles))
        {
            var actualBytes = System.Text.Encoding.UTF8.GetBytes(actual.Content);
            var expectedBytes = WithoutConformanceRootNamespace(
                await File.ReadAllBytesAsync(expected.AbsolutePath, TestContext.Current.CancellationToken));

            if (!actualBytes.SequenceEqual(expectedBytes))
            {
                var diff = UnifiedDiff.Render(expectedBytes, actualBytes)
                    ?? "(files differ in encoding or BOM but produce identical text)";
                throw new Xunit.Sdk.XunitException(
                    $"`{actual.RelativePath}` does not match the snapshot byte-for-byte.\n\n" +
                    $"{diff}\n{refreshHint}");
            }
        }
    }

    private static byte[] WithoutConformanceRootNamespace(byte[] sourceBytes) =>
        System.Text.Encoding.UTF8.GetBytes(
            System.Text.Encoding.UTF8.GetString(sourceBytes).Replace("Daml.Codegen.Testing.Conformance.", "", StringComparison.Ordinal));

    private static IReadOnlyList<GeneratedFile> MainAndNonStdlibDependencySources(
        CSharpCodeGenerator generator,
        DarModel dar,
        IReadOnlyList<GeneratedFile> mainGenerated)
    {
        var dependencySources = dar.Dependencies
            .Where(dep => !StdlibPackages.IsStdlibPackage(dep.Name)
                          && !StdlibPackages.IsPlaceholderPackageName(dep.Name))
            .SelectMany(dep => generator.Generate(
                new DarModel { MainPackage = dep, Dependencies = dar.Dependencies }));

        return mainGenerated
            .Concat(dependencySources)
            .Where(f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal))
            .DistinctBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();
    }

    private static string RenderDiagnostic(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        var origin = string.IsNullOrEmpty(span.Path)
            ? "(no source location)"
            : $"{span.Path}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1})";
        return $"  {origin}: {diagnostic.Id}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}";
    }
}
