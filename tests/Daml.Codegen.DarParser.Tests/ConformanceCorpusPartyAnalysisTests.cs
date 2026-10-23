// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.DarParser;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.DarParser.Tests;

/// <summary>
/// Party-expression verdicts read off the two conformance corpus DARs that are
/// compiled above Daml-LF 2.1: <c>defaulttarget</c>, which declares no
/// <c>--target</c> and so carries whatever damlc emits by default, and
/// <c>contractkeys</c>, pinned to the earliest version that can express a
/// contract key. Every other party-analysis fixture in this project is either
/// synthesized in memory or an LF 2.1 archive, so without these the analyzer's
/// behaviour above 2.1 rests on hand-built expression trees that no compiler
/// produced.
///
/// <para>
/// The loader asserts each corpus's LF version, so every fact below enforces it
/// rather than one guard fact standing apart. A fixture rebuilt against a
/// different target would otherwise leave the verdict facts green while they
/// quietly proved nothing about the version they are named for.
/// </para>
/// </summary>
public sealed class ConformanceCorpusPartyAnalysisTests
{
    private const string LfVersionDamlcEmitsWhenNoTargetIsRequested = "2.3";
    private const string ContractKeyCorpusLfVersion = "2.3";
    private const int LeastClausesNamingPayloadPartyFields = 8;

    private static readonly IReadOnlyDictionary<string, string> ExpectedLfVersionByCorpus =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["defaulttarget"] = LfVersionDamlcEmitsWhenNoTargetIsRequested,
            ["contractkeys"] = ContractKeyCorpusLfVersion,
        };

    private static async Task<DamlPackage> LoadMainPackageAsync(string fixtureName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", $"{fixtureName}.dar");
        var archive = await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);

        archive.MainPackage.LfVersion.Should().Be(
            ExpectedLfVersionByCorpus[fixtureName],
            $"the {fixtureName} verdicts are this analyzer's only coverage on that LF version, and they "
            + "prove nothing about it once the fixture is rebuilt against a different target");

        return archive.MainPackage;
    }

    private static IEnumerable<DamlPartyAnalysis> ClauseAnalyses(DamlTemplate template)
    {
        yield return template.Signatories;
        yield return template.Observers;

        foreach (var choice in template.Choices)
        {
            yield return choice.Controllers;
            yield return choice.Observers;
        }
    }

    private static DamlTemplate Template(DamlPackage package, string name) =>
        package.Modules.SelectMany(m => m.Templates).Single(t => t.Name == name);

    private static DamlChoice Choice(DamlTemplate template, string name) =>
        template.Choices.Single(c => c.Name == name);

    private static void ShouldResolveTo(DamlPartyAnalysis analysis, params string[] payloadFieldNames)
    {
        analysis.Source.Should().Be(DamlPartySource.Static);
        analysis.Parties
            .Select(party => party.Should().BeOfType<DamlPartyPayloadField>().Subject.FieldName)
            .Should().Equal(payloadFieldNames);
    }

    [Fact]
    public async Task ConformanceCorpusPartyAnalysis_corpus_resolves_a_floor_of_clauses_to_named_payload_fields()
    {
        var corpus = new[]
        {
            await LoadMainPackageAsync("defaulttarget"),
            await LoadMainPackageAsync("contractkeys"),
        };

        var namingPayloadFields = corpus
            .SelectMany(package => package.Modules)
            .SelectMany(module => module.Templates)
            .SelectMany(ClauseAnalyses)
            .Count(analysis => analysis.Source == DamlPartySource.Static && analysis.Parties.Count > 0);

        namingPayloadFields.Should().BeGreaterThanOrEqualTo(
            LeastClausesNamingPayloadPartyFields,
            "the facts below assert only the templates they name, so a template added to the corpus later "
            + "could resolve Dynamic across every clause without any of them noticing");
    }

    [Fact]
    public async Task ConformanceCorpusPartyAnalysis_default_target_signatory_names_the_payload_party_field()
    {
        var note = Template(await LoadMainPackageAsync("defaulttarget"), "Note");

        ShouldResolveTo(note.Signatories, "owner");
    }

    [Fact]
    public async Task ConformanceCorpusPartyAnalysis_default_target_template_without_an_observer_clause_resolves_to_an_empty_list()
    {
        var note = Template(await LoadMainPackageAsync("defaulttarget"), "Note");

        note.Observers.Source.Should().Be(DamlPartySource.Static);
        note.Observers.Parties.Should().BeEmpty();
    }

    [Fact]
    public async Task ConformanceCorpusPartyAnalysis_contract_key_corpus_signatories_name_their_payload_party_fields()
    {
        var package = await LoadMainPackageAsync("contractkeys");

        ShouldResolveTo(Template(package, "Account").Signatories, "custodian");
        ShouldResolveTo(Template(package, "Holiday").Signatories, "provider");
        ShouldResolveTo(Template(package, "Steward").Signatories, "steward");
    }

    [Fact]
    public async Task ConformanceCorpusPartyAnalysis_contract_key_corpus_choice_controllers_name_their_payload_party_fields()
    {
        var package = await LoadMainPackageAsync("contractkeys");

        var account = Template(package, "Account");
        ShouldResolveTo(Choice(account, "Credit").Controllers, "custodian");
        ShouldResolveTo(Choice(account, "CurrentBalance").Controllers, "custodian");
        ShouldResolveTo(Choice(Template(package, "Holiday"), "Extend").Controllers, "provider");
        ShouldResolveTo(Choice(Template(package, "Steward"), "Revise").Controllers, "steward");
    }

    [Fact]
    public async Task ConformanceCorpusPartyAnalysis_contract_key_corpus_resolves_a_consuming_and_a_nonconsuming_choice_alike()
    {
        var account = Template(await LoadMainPackageAsync("contractkeys"), "Account");

        Choice(account, "Credit").Consuming.Should().BeTrue(
            "a corpus whose choices were all consuming would leave the nonconsuming controller shape untested");
        Choice(account, "CurrentBalance").Consuming.Should().BeFalse();
    }

    [Fact]
    public async Task ConformanceCorpusPartyAnalysis_party_reached_through_a_nested_projection_stays_dynamic()
    {
        var schedule = Template(await LoadMainPackageAsync("contractkeys"), "Schedule");

        schedule.Signatories.Source.Should().Be(
            DamlPartySource.Dynamic,
            "Schedule's signatory projects through its view field rather than off the payload, and naming a "
            + "field the caller cannot reach as payload.field would emit a wrapper that does not compile");
        Choice(schedule, "Reschedule").Controllers.Source.Should().Be(DamlPartySource.Dynamic);
    }
}
