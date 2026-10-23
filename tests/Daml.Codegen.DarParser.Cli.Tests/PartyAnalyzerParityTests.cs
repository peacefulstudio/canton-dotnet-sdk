// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.DarParser;
using Daml.Codegen.Intermediate;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.DarParser.Cli.Tests;

/// <summary>
/// Cross-implementation guard on the two party-expression analyzers: the Scala
/// one in the JVM helper, whose verdicts reach this test frozen into the
/// committed <c>intermediate.binpb</c>, and the C# one in the parser, which runs
/// live here against the same DAR.
///
/// <para>
/// The corpus is every snapshot fixture carrying both a DAR and an
/// <c>intermediate.binpb</c>. An interface-only DAR declares no template
/// <c>signatory</c> / <c>observer</c> clause, so non-vacuity is asserted over the
/// corpus as a whole: the template-bearing fixtures give the comparison its clauses
/// and the interface-bearing ones give it interface choices.
/// </para>
///
/// <para>
/// Clauses cover template signatories, observers and choices, and the choices of
/// every interface. The Scala analyzer is the oracle: it pins interface choices
/// <c>Dynamic</c>, and the C# one writes the same. The two must agree on every
/// clause of every snapshot. Verdicts are compared as text — <c>Static(field, ...)</c>
/// or <c>Dynamic</c> — so a failure names the clause and quotes both sides' actual
/// verdicts.
/// </para>
///
/// <para>
/// This is a co-movement pin, not an oracle. The two analyzers are edited in the
/// same changes and the Scala side's verdicts arrive frozen in
/// <c>intermediate.binpb</c>, so an empty set records that the two were made to
/// agree — not that either of them reads the Daml clause correctly.
/// </para>
///
/// <para>
/// Parity also cannot reach the decoding surface that exists only below the AST:
/// interned expressions and the package-import table, both of which appear from
/// Daml-LF 2.2 on. The Scala analyzer runs on an already-decoded AST in which
/// neither is present, so a disagreement about either can never surface here.
/// That surface is covered by <c>ConformanceCorpusPartyAnalysisTests</c>.
/// </para>
/// </summary>
public class PartyAnalyzerParityTests
{
    private const int LeastClausesWorthComparing = 100;
    private const int LeastResolvedClausesWorthComparing = 100;

    public static TheoryData<string> SnapshotNames() => ParitySnapshots.Names();

    private static async Task<IDarSource> ReadWithCSharpAnalyzerAsync(string snapshotName, CancellationToken cancellationToken)
    {
        var darPath = ParitySnapshots.DarPath(snapshotName);
        File.Exists(darPath).Should().BeTrue($"the fixture DAR must ship at {darPath}");
        return await DarArchive.ReadAsync(darPath, cancellationToken);
    }

    private static IDarSource ReadWithScalaAnalyzer(string snapshotName)
    {
        var protoPath = ParitySnapshots.ProtoPath(snapshotName);
        File.Exists(protoPath).Should().BeTrue(
            $"the JVM-helper-produced IntermediateDar snapshot must ship at {protoPath}; " +
            "refresh with `java -jar jvm-helper/target/scala-2.13/daml-dar-to-proto.jar " +
            $"--dar tests/Daml.Codegen.CSharp.Tests/Snapshots/{snapshotName}/{snapshotName}.dar " +
            $"--out tests/Daml.Codegen.CSharp.Tests/Snapshots/{snapshotName}/intermediate.binpb`");

        using var stream = File.OpenRead(protoPath);
        return IntermediateDarReader.Read(IntermediateDar.Parser.ParseFrom(stream));
    }

    private static string Describe(DamlPartyAnalysis analysis) =>
        analysis.Source == DamlPartySource.Static
            ? $"Static({string.Join(", ", analysis.Parties.Select(DescribeParty))})"
            : "Dynamic";

    private static string DescribeParty(DamlPartyReference party) => party switch
    {
        DamlPartyPayloadField payloadField => payloadField.FieldName,
        _ => party.ToString() ?? "<unrecognized party reference>",
    };

    private static bool NamesPayloadFields(string verdict) =>
        verdict.StartsWith("Static(", StringComparison.Ordinal) && verdict != "Static()";

    private static bool IsStatic(string verdict) => verdict.StartsWith("Static(", StringComparison.Ordinal);

    private static bool IsInterfaceChoiceClause(string clause) =>
        clause.Contains(":interface:", StringComparison.Ordinal);

    private static IEnumerable<KeyValuePair<string, string>> Verdicts(IDarSource source) =>
        TemplateClauses(source).Concat(InterfaceClauses(source));

    private static IEnumerable<KeyValuePair<string, string>> TemplateClauses(IDarSource source) =>
        from package in source.AllPackages
        from module in package.Modules
        from template in module.Templates
        from verdict in TemplateVerdicts($"{package.Name}-{package.Version}:{module.Name}:{template.Name}", template)
        select verdict;

    private static IEnumerable<KeyValuePair<string, string>> InterfaceClauses(IDarSource source) =>
        from package in source.AllPackages
        from module in package.Modules
        from iface in module.Interfaces
        from choice in iface.Choices
        from verdict in ChoiceVerdicts($"{package.Name}-{package.Version}:{module.Name}:interface:{iface.Name}", choice)
        select verdict;

    private static IEnumerable<KeyValuePair<string, string>> TemplateVerdicts(string prefix, DamlTemplate template)
    {
        yield return new KeyValuePair<string, string>($"{prefix} signatory", Describe(template.Signatories));
        yield return new KeyValuePair<string, string>($"{prefix} observer", Describe(template.Observers));

        foreach (var choice in template.Choices)
        {
            foreach (var verdict in ChoiceVerdicts(prefix, choice))
            {
                yield return verdict;
            }
        }
    }

    private static IEnumerable<KeyValuePair<string, string>> ChoiceVerdicts(string prefix, DamlChoice choice)
    {
        yield return new KeyValuePair<string, string>(
            $"{prefix}#{choice.Name} controller", Describe(choice.Controllers));
        yield return new KeyValuePair<string, string>(
            $"{prefix}#{choice.Name} observer", Describe(choice.Observers));
    }

    private static SortedDictionary<string, string> VerdictsByClause(IDarSource source, string analyzer)
    {
        var byClause = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (clause, verdict) in Verdicts(source))
        {
            if (byClause.TryGetValue(clause, out var alreadySeen))
            {
                throw new Xunit.Sdk.XunitException(
                    $"The {analyzer} analyzer reports clause `{clause}` twice ({alreadySeen} then {verdict}). " +
                    "The clause key must identify a clause uniquely, otherwise one verdict silently replaces " +
                    "another and drops out of the comparison.");
            }
            byClause.Add(clause, verdict);
        }
        return byClause;
    }

    private static async Task<SortedDictionary<string, string>> CorpusVerdictsAsync(
        Func<string, Task<IDarSource>> read, string analyzer)
    {
        var corpus = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var snapshotName in ParitySnapshots.Discovered())
        {
            foreach (var (clause, verdict) in VerdictsByClause(await read(snapshotName), analyzer))
            {
                corpus.Add($"{snapshotName}/{clause}", verdict);
            }
        }
        return corpus;
    }

    private static Task<SortedDictionary<string, string>> CorpusVerdictsFromCSharpAsync(CancellationToken cancellationToken) =>
        CorpusVerdictsAsync(name => ReadWithCSharpAnalyzerAsync(name, cancellationToken), "C#");

    private static Task<SortedDictionary<string, string>> CorpusVerdictsFromScalaAsync() =>
        CorpusVerdictsAsync(name => Task.FromResult(ReadWithScalaAnalyzer(name)), "Scala");

    [Fact]
    public async Task PartyAnalyzerParity_corpus_gives_both_analyzers_something_to_say()
    {
        var csharpVerdicts = await CorpusVerdictsFromCSharpAsync(TestContext.Current.CancellationToken);
        var scalaVerdicts = await CorpusVerdictsFromScalaAsync();

        csharpVerdicts.Should().HaveCountGreaterThanOrEqualTo(LeastClausesWorthComparing,
            "the C# analyzer must be reached on a substantial number of clauses for the comparison to mean anything");
        scalaVerdicts.Should().HaveCountGreaterThanOrEqualTo(LeastClausesWorthComparing,
            "the committed snapshots must carry the Scala analyzer's verdicts, not the --schema-only all-Dynamic shape");

        csharpVerdicts.Values.Count(IsStatic).Should().BeGreaterThanOrEqualTo(LeastResolvedClausesWorthComparing,
            "an all-Dynamic C# side would compare equal to an analyzer that had never run");
        scalaVerdicts.Values.Count(IsStatic).Should().BeGreaterThanOrEqualTo(LeastResolvedClausesWorthComparing,
            "an all-Dynamic Scala side would compare equal to an analyzer that had never run");

        csharpVerdicts.Values.Concat(scalaVerdicts.Values).Should().Contain(verdict => NamesPayloadFields(verdict),
            "at least one clause must resolve to named payload party fields, otherwise the comparison never " +
            "compares a field name and a renamed party would slip through");
    }

    [Fact]
    public async Task PartyAnalyzerParity_corpus_reaches_interface_choices_on_both_analyzers()
    {
        var csharpVerdicts = await CorpusVerdictsFromCSharpAsync(TestContext.Current.CancellationToken);
        var scalaVerdicts = await CorpusVerdictsFromScalaAsync();

        csharpVerdicts.Keys.Should().Contain(clause => IsInterfaceChoiceClause(clause),
            "without an interface choice clause the interface walk is vacuous on the C# side");
        scalaVerdicts.Keys.Should().Contain(clause => IsInterfaceChoiceClause(clause),
            "without an interface choice clause the interface walk is vacuous on the Scala side");
    }

    [Theory]
    [MemberData(nameof(SnapshotNames))]
    public async Task PartyAnalyzerParity_both_analyzers_report_a_verdict_for_the_same_clauses(string snapshotName)
    {
        var csharpVerdicts = VerdictsByClause(
            await ReadWithCSharpAnalyzerAsync(snapshotName, TestContext.Current.CancellationToken), "C#");
        var scalaVerdicts = VerdictsByClause(ReadWithScalaAnalyzer(snapshotName), "Scala");

        csharpVerdicts.Keys.Should().Equal(scalaVerdicts.Keys,
            "a clause only one analyzer knows about is never compared, so the clause sets must match before " +
            "the verdicts can be trusted to have been compared");
    }

    [Fact]
    public async Task PartyAnalyzerParity_analyzers_agree_on_every_clause()
    {
        var csharpVerdicts = await CorpusVerdictsFromCSharpAsync(TestContext.Current.CancellationToken);
        var scalaVerdicts = await CorpusVerdictsFromScalaAsync();

        var divergences = csharpVerdicts.Keys
            .Intersect(scalaVerdicts.Keys, StringComparer.Ordinal)
            .Where(clause => !string.Equals(csharpVerdicts[clause], scalaVerdicts[clause], StringComparison.Ordinal))
            .Select(clause => $"{clause} | C# {csharpVerdicts[clause]} | Scala {scalaVerdicts[clause]}")
            .Order(StringComparer.Ordinal)
            .ToList();

        divergences.Should().BeEmpty(
            "the Scala analyzer is the oracle, so the C# analyzer must return its verdict on every clause");
    }
}
