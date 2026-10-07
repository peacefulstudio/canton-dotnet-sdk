// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using AwesomeAssertions;
using Daml.Testing.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Compiles every C# snippet in <c>src/Daml.Runtime/README.md</c>, which
/// <c>Daml.Runtime.csproj</c> packs as the package readme — the first code a
/// nuget.org visitor copies. Snippets are compiled against the framework the package
/// targets plus <c>Daml.Runtime</c>, <c>Daml.Ledger.Abstractions</c> and the generated
/// <c>samples/QuickstartExample</c> the prose says it quotes, so a snippet may not lean
/// on anything a consumer does not have. A fence that cannot compile on its own opts out
/// by carrying <c>&lt;!-- snippet: not-compiled: &lt;reason&gt; --&gt;</c> on the line above it,
/// so the skip is visible to a reader of the shipped readme rather than only to this gate.
/// </summary>
public sealed class RuntimeReadmeSnippetsCompileTests
{
    private const string ReadmeFileName = "Daml.Runtime.README.md";

    private const string IdentifiersTheProseIntroduces = """
                private static readonly Daml.Runtime.Contracts.TransactionResult tx = null!;
                private static readonly Daml.Runtime.Contracts.ContractId<Iou.Iou> contractId = null!;
                private static readonly string json = "{}";
                private static readonly string pqsRowJson = "{}";
        """;

    private static readonly string[] CSharpFenceLanguages = ["csharp", "cs", "c#"];

    private static readonly IReadOnlyList<MetadataReference> ConsumerReferences =
    [
        .. ConsumerReferenceSet.Assemblies,
        MetadataReference.CreateFromFile(typeof(Iou.Iou).Assembly.Location),
    ];

    private static readonly IReadOnlyList<MarkdownCodeFence> CompiledSnippets = ReadCompiledSnippets();

    public static TheoryData<int> CompiledSnippetLineNumbers =>
        new(CompiledSnippets.Select(snippet => snippet.LineNumber));

    [Theory]
    [MemberData(nameof(CompiledSnippetLineNumbers))]
    public void RuntimeReadme_snippet_compiles_against_what_a_consumer_of_the_package_has(int lineNumber)
    {
        var snippet = CompiledSnippets.Single(candidate => candidate.LineNumber == lineNumber);

        var errors = ErrorsIn(snippet);

        errors.Should().BeEmpty(
            "src/Daml.Runtime/README.md:{0} ships inside the NuGet package, so a snippet that no longer "
            + "compiles is documentation a consumer cannot follow",
            lineNumber);
    }

    [Fact]
    public void RuntimeReadme_offers_a_non_empty_set_of_snippets_to_the_compiler()
    {
        CompiledSnippets.Should().NotBeEmpty(
            "a gate that discovers no snippets passes vacuously — every C# fence in the shipped readme is "
            + "compiled unless it carries an exclusion marker naming the reason it cannot be");
    }

    [Fact]
    public void RuntimeReadme_snippet_body_reaches_the_compiler()
    {
        var errors = ErrorsFrom("var drifted = new IouContract.ChoiceTheGeneratorNoLongerEmits();");

        errors.Should().NotBeEmpty(
            "a wrapper that dropped the snippet body would compile an empty method and pass on every "
            + "drifted snippet in the readme");
    }

    [Fact]
    public void RuntimeReadme_snippet_may_declare_the_type_it_demonstrates()
    {
        var errors = ErrorsIn(new MarkdownCodeFence(
            0,
            "csharp",
            "record Wrapper(string Value);\n\nvar wrapper = new Wrapper(\"x\");",
            null));

        errors.Should().BeEmpty(
            "a snippet that declares the type it demonstrates is ordinary documentation, so the "
            + "declaration belongs beside the wrapper rather than inside its method, where C# forbids "
            + "it and the reader gets a syntax error about the wrapper instead of about the snippet");
    }

    [Fact]
    public void RuntimeReadme_snippet_cannot_reach_an_assembly_only_the_test_host_has()
    {
        var errors = ErrorsFrom("var attribute = new Xunit.FactAttribute();");

        errors.Should().NotBeEmpty(
            "the reference set is the framework plus the shipped packages, not whatever the test host "
            + "happens to have loaded — a snippet that compiles here must compile for a consumer");
    }

    private static IReadOnlyList<string> ErrorsFrom(string snippetCode) =>
        ErrorsIn(new MarkdownCodeFence(0, "csharp", "using IouContract = Iou.Iou;\n\n" + snippetCode, null));

    private static IReadOnlyList<string> ErrorsIn(MarkdownCodeFence snippet) =>
        Compile(snippet)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}")
            .ToList();

    private static IReadOnlyList<MarkdownCodeFence> ReadCompiledSnippets() =>
        MarkdownCodeFence
            .ReadFrom(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, ReadmeFileName)))
            .Where(fence => CSharpFenceLanguages.Contains(fence.Language, StringComparer.OrdinalIgnoreCase))
            .Where(fence => fence.ExclusionReason is null)
            .ToList();

    private static IReadOnlyList<Diagnostic> Compile(MarkdownCodeFence snippet)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: $"RuntimeReadmeSnippet-line-{snippet.LineNumber}",
            syntaxTrees: [CSharpSyntaxTree.ParseText(
                AsCompilationUnit(snippet.Code),
                path: $"{ReadmeFileName}:{snippet.LineNumber}")],
            references: ConsumerReferences,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return compilation.GetDiagnostics();
    }

    private static string AsCompilationUnit(string snippet)
    {
        var parsed = SyntaxFactory.ParseCompilationUnit(snippet);
        var statements = parsed.Members.OfType<GlobalStatementSyntax>();
        var declarations = parsed.Members.Where(member => member is not GlobalStatementSyntax);

        return $$"""
            {{string.Join("\n", parsed.Usings.Select(directive => directive.ToFullString().Trim()))}}

            {{string.Join("\n", declarations.Select(declaration => declaration.ToFullString()))}}

            internal static class ReadmeSnippet
            {
            {{IdentifiersTheProseIntroduces}}

                internal static void Run()
                {
            {{string.Join("\n", statements.Select(statement => statement.ToFullString()))}}
                }
            }
            """;
    }
}
