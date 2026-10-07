// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Testing.StjRoundTrip;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// Round-trips a hand-written sample of every generated value type in the conformance corpus
/// through <see cref="System.Text.Json"/>, on default options and on <c>AddDamlConverters()</c>
/// options.
/// </summary>
public class GeneratedStjRoundTripSweepTests
{
    private const string GeneratedNamespacePrefix = "Daml.Codegen.Testing.Conformance.";

    private static readonly Type[] Exported =
        [.. typeof(ConformanceCorpus).Assembly.GetExportedTypes()
            .Where(type => type.Namespace?.StartsWith(GeneratedNamespacePrefix, StringComparison.Ordinal) == true)];

    private static readonly IReadOnlySet<Type> NamedNotAValueType = new HashSet<Type>();

    private static readonly IReadOnlyList<Type> ValueTypes = StjSweepScope.ValueTypes(Exported, NamedNotAValueType);

    private static readonly IReadOnlyList<StjRoundTripCase> Cases =
        StjSweepScope.CasesWithAsSelfArms(GeneratedStjSampleTable.Samples);

    private static readonly IReadOnlyDictionary<string, StjRoundTripCase> CasesById =
        Cases.ToDictionary(roundTripCase => roundTripCase.Id, StringComparer.Ordinal);

    public static TheoryData<string, string> CaseAndLeg()
    {
        var data = new TheoryData<string, string>();
        foreach (var roundTripCase in Cases)
        {
            foreach (var leg in Enum.GetValues<StjOptionsLeg>())
            {
                data.Add(roundTripCase.Id, leg.ToString());
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CaseAndLeg))]
    public void GeneratedStjRoundTripSweep_reads_each_sample_back_equivalent_equal_and_hash_equal(string caseId, string legName)
    {
        var roundTripCase = CasesById[caseId];
        var leg = Enum.Parse<StjOptionsLeg>(legName);
        var rootCause = StjKnownBroken.RootCause(GeneratedStjSampleTable.KnownBroken, caseId, leg);

        if (rootCause is null)
        {
            StjRoundTrip.AssertReadsBackEquivalentEqualAndHashEqual(roundTripCase, leg);
        }
        else
        {
            StjRoundTrip.AssertStillFails(roundTripCase, leg, rootCause);
        }
    }

    [Fact]
    public void GeneratedStjRoundTripSweep_runs_every_case_on_the_default_and_the_AddDamlConverters_legs()
    {
        var casesNotOnBothLegs = CaseAndLeg()
            .Select(row => ((ITheoryDataRow)row).GetData())
            .GroupBy(data => (string)data[0]!, data => (string)data[1]!)
            .Select(legs => $"{legs.Key}: {string.Join(",", legs.Order(StringComparer.Ordinal))}")
            .Where(line => !line.EndsWith(": AddDamlConverters,Default", StringComparison.Ordinal));

        string.Join(Environment.NewLine, casesNotOnBothLegs).Should().BeEmpty();
    }

    [Fact]
    public void GeneratedStjRoundTripSweep_covers_every_value_type_with_a_sample()
    {
        var uncovered = StjSweepScope.Uncovered(ValueTypes, Cases);

        string.Join(Environment.NewLine, uncovered.Select(type => type.FullName)).Should().BeEmpty();
    }

    [Fact]
    public void GeneratedStjRoundTripSweep_fills_every_nullable_member_in_one_sample_per_arm()
    {
        var sparseArms = StjSweepScope.ArmsWithNoFullyPopulatedSample(Cases, GeneratedStjSampleTable.ArmsWithNoFullyPopulatedForm.Keys.ToHashSet());

        string.Join(Environment.NewLine, sparseArms).Should().BeEmpty();
    }

    [Fact]
    public void GeneratedStjRoundTripSweep_names_only_types_and_cases_that_exist()
    {
        StjSweepScope.StaleExclusions(Exported, NamedNotAValueType).Should().BeEmpty();
        StjSweepScope.OutOfScopeKeys(ValueTypes, Cases).Should().BeEmpty();
        StjSweepScope.StaleKnownBrokenKeys(Cases, GeneratedStjSampleTable.KnownBroken.Keys).Should().BeEmpty();
        StjSweepScope.StaleExemptions(Cases, GeneratedStjSampleTable.ArmsWithNoFullyPopulatedForm.Keys.ToHashSet()).Should().BeEmpty();
    }

    [Fact]
    public void GeneratedStjRoundTripSweep_discovers_exactly_142_public_types()
    {
        Exported.Should().HaveCount(142);
    }

    [Fact]
    public void GeneratedStjRoundTripSweep_finds_exactly_81_value_types()
    {
        ValueTypes.Should().HaveCount(81);
    }
}
