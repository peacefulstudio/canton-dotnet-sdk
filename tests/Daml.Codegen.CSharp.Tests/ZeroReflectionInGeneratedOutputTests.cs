// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Pins F5.1, the acceptance gate for the CHANGELOG promise (E4) that generated bindings
/// reach a Daml-LF JSON decoder without reflection. Scans the three committed generated trees the
/// spec names — the Quickstart sample, the conformance corpus and the snapshot expectations — for
/// the ten obsolete <see cref="Daml.Runtime.Serialization.DamlLfJsonReader"/>/
/// <see cref="Daml.Runtime.Serialization.DamlLfJsonDecoders"/> members (A9), file-wide. A second
/// group of tokens is scoped to a <c>__ReadDamlLfJson</c> body or a descriptor-initializer lambda
/// (<c>ArgumentJsonReader</c>/<c>ResultJsonReader</c>/<c>KeyJsonReader</c>) by parsing rather than by
/// line grep: <c>GenericStub.NotImplemented</c> and <c>typeof(</c>, F5.1's own list, plus
/// <c>GetType(</c>, <c>Activator.</c> and <c>System.Reflection</c>, added after a mutation probe
/// showed a bare <c>obj.GetType().GetProperty(...).GetValue(...)</c> call inside a decoder body was
/// invisible to F5.1's literal six-token list even though it is exactly the shape reflection E4
/// promises against. Scoping (not a file-wide ban) is required because <c>typeof(</c> also appears
/// legitimately in <c>[JsonConverter(typeof(...))]</c> attributes throughout the conformance tree.
/// </summary>
public class ZeroReflectionInGeneratedOutputTests
{
    private static readonly string[] FileWideForbiddenTokens =
    [
        "DamlLfJsonReader.ReadRecord(",
        "DamlLfJsonReader.ReadValue",
        "DamlLfJsonDecoders.ReadRecord(",
        "DamlLfJsonDecoders.ReadVariant(",
        "ReadEnum<",
        "ReadEnum(",
    ];

    private static readonly string[] ScopedForbiddenTokens =
    [
        "GenericStub.NotImplemented",
        "typeof(",
        "GetType(",
        "Activator.",
        "System.Reflection",
    ];

    private static readonly string[] DescriptorInitializerReaderNames =
        ["ArgumentJsonReader", "ResultJsonReader", "KeyJsonReader"];

    private sealed record GeneratedTree(string RelativePath, int FileCountAsOfRevision10, bool RestrictToExpectedSubtree);

    private static readonly GeneratedTree[] Trees =
    [
        new(Path.Combine("samples", "QuickstartExample", "Generated"), 3, RestrictToExpectedSubtree: false),
        new(
            Path.Combine("src", "Daml.Codegen.Testing.Conformance", "Generated"),
            54,
            RestrictToExpectedSubtree: false),
        new(
            Path.Combine("tests", "Daml.Codegen.CSharp.Tests", "Snapshots"),
            129,
            RestrictToExpectedSubtree: true),
    ];

    [Fact]
    public void GeneratedTrees_contain_no_legacy_reflection_reference()
    {
        var repoRoot = LocateRepoRoot();
        var offenders = new List<string>();

        foreach (var tree in Trees)
        {
            var directory = Path.Combine(repoRoot, tree.RelativePath);
            Directory.Exists(directory).Should().BeTrue(
                "F5.1's committed tree {0} must exist", tree.RelativePath);

            var files = EnumerateTreeFiles(directory, tree.RestrictToExpectedSubtree)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            files.Should().NotBeEmpty(
                "the glob under {0} must match at least one file (it matched {1} as of revision 10 " +
                "of the spec); zero files means the tree was renamed or moved and the gate would " +
                "otherwise pass by examining nothing",
                tree.RelativePath,
                tree.FileCountAsOfRevision10);

            foreach (var file in files)
            {
                offenders.AddRange(ScanFile(file));
            }
        }

        offenders.Should().BeEmpty(
            "generated bindings must reach a Daml-LF JSON decoder without reflection (E4); every " +
            "offender is listed above as file:line, the matched token and the line's text");
    }

    private static IEnumerable<string> EnumerateTreeFiles(string directory, bool restrictToExpectedSubtree)
    {
        var files = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories);
        if (!restrictToExpectedSubtree)
        {
            return files;
        }

        return files.Where(path => path.Replace(Path.DirectorySeparatorChar, '/').Contains(
            "/expected/", StringComparison.Ordinal));
    }

    private static IEnumerable<string> ScanFile(string path)
    {
        var sourceText = SourceText.From(File.ReadAllText(path));
        var fullText = sourceText.ToString();

        foreach (var token in FileWideForbiddenTokens)
        {
            foreach (Match match in Regex.Matches(fullText, Regex.Escape(token)))
            {
                yield return FormatOffense(path, sourceText, match.Index, token);
            }
        }

        var tree = CSharpSyntaxTree.ParseText(sourceText, path: path);
        var root = tree.GetRoot();

        foreach (var span in DecoderBodySpans(root).Concat(DescriptorInitializerSpans(root)))
        {
            var spanText = sourceText.ToString(span);
            foreach (var token in ScopedForbiddenTokens)
            {
                foreach (Match match in Regex.Matches(spanText, Regex.Escape(token)))
                {
                    yield return FormatOffense(path, sourceText, span.Start + match.Index, token);
                }
            }
        }
    }

    private static IEnumerable<TextSpan> DecoderBodySpans(SyntaxNode root) =>
        root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText == "__ReadDamlLfJson")
            .Select(method => ((SyntaxNode?)method.Body ?? method.ExpressionBody)?.Span)
            .Where(span => span.HasValue)
            .Select(span => span!.Value);

    private static IEnumerable<TextSpan> DescriptorInitializerSpans(SyntaxNode root) =>
        root.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(assignment =>
                DescriptorInitializerReaderNames.Contains(AssignedPropertyName(assignment.Left)))
            .Select(assignment => assignment.Span);

    private static string? AssignedPropertyName(ExpressionSyntax left) => left switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        _ => null,
    };

    private static string FormatOffense(string path, SourceText sourceText, int index, string token)
    {
        var line = sourceText.Lines.GetLineFromPosition(index);
        return $"{path}:{line.LineNumber + 1}: matched '{token}' — {line.ToString().Trim()}";
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
