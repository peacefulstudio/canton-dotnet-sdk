// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.Intermediate;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Drift gate for the committed Quickstart showcase sample
/// (<c>samples/QuickstartExample/Generated</c>). Regenerates the sample from
/// the vendored <c>QuickstartSample/intermediate.binpb</c> proto using the
/// current codegen source and byte-compares the result against the committed
/// tree, so the sample fails CI when it falls behind emitter output — the same
/// guarantee the in-process snapshot drift tests already provide. Unlike the
/// snapshot fixtures, the expected tree here is the live sample in the repo,
/// located relative to the repo root, so the showcase itself is what gets
/// guarded. Refresh with <c>scripts/regenerate.sh write --only quickstart</c>.
/// </summary>
public class QuickstartSampleDriftTests
{
    [Fact]
    public async Task Quickstart_sample_matches_current_codegen()
    {
        var repoRoot = LocateRepoRoot();
        var protoPath = Path.Combine(
            repoRoot, "tests", "Daml.Codegen.CSharp.Tests", "QuickstartSample", "intermediate.binpb");
        var sampleDir = Path.Combine(repoRoot, "samples", "QuickstartExample", "Generated");

        File.Exists(protoPath).Should().BeTrue(
            "the Quickstart intermediate.binpb fixture must exist at {0}", protoPath);
        Directory.Exists(sampleDir).Should().BeTrue(
            "the committed Quickstart Generated tree must exist at {0}", sampleDir);

        IntermediateDar proto;
        await using (var stream = File.OpenRead(protoPath))
        {
            proto = IntermediateDar.Parser.ParseFrom(stream);
        }
        var dar = IntermediateDarReader.Read(proto);
        var generated = new CSharpCodeGenerator(new CodeGenOptions()).Generate(dar);

        var actualFiles = generated
            .Where(f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal))
            .Select(f => new { f.RelativePath, f.Content })
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();

        var committedFiles = Directory.EnumerateFiles(sampleDir, "*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".cs", StringComparison.Ordinal))
            .Select(absPath => new
            {
                RelativePath = Path.GetRelativePath(sampleDir, absPath).Replace('\\', '/'),
                AbsolutePath = absPath,
            })
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();

        var refreshHint =
            "The committed Quickstart sample drifted from current codegen output. If the change is " +
            "intentional, refresh it with scripts/regenerate.sh write --only quickstart and re-commit; " +
            "if not, fix the codegen.";

        actualFiles.Should().NotBeEmpty(
            "codegen must emit at least one file from the Quickstart proto; zero output indicates a " +
            "regression in IntermediateDarReader.Read or Generate.");

        actualFiles.Select(f => f.RelativePath).Should().Equal(
            committedFiles.Select(f => f.RelativePath),
            because: "the set of generated files must match the committed sample. " + refreshHint);

        foreach (var (actual, committed) in actualFiles.Zip(committedFiles))
        {
            var actualBytes = System.Text.Encoding.UTF8.GetBytes(actual.Content);
            var committedBytes = await File.ReadAllBytesAsync(
                committed.AbsolutePath, TestContext.Current.CancellationToken);

            if (!actualBytes.SequenceEqual(committedBytes))
            {
                var diff = UnifiedDiff.Render(committedBytes, actualBytes)
                    ?? "(files differ only in BOM or line-ending encoding — re-run scripts/regenerate.sh write --only quickstart to normalise)";
                throw new Xunit.Sdk.XunitException(
                    $"`{actual.RelativePath}` does not match the committed Quickstart sample byte-for-byte.\n\n" +
                    $"{diff}\n{refreshHint}");
            }
        }
    }

    private static string LocateRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Daml.Codegen.CSharp.slnx"))
                && Directory.Exists(Path.Combine(current.FullName, "samples", "QuickstartExample")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException(
            $"Cannot locate repo root from {AppContext.BaseDirectory}. " +
            "Expected Daml.Codegen.CSharp.slnx alongside samples/QuickstartExample.");
    }
}
