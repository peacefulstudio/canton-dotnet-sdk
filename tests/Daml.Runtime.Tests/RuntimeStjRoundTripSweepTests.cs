// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Serialization;
using Daml.Testing.StjRoundTrip;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Round-trips a hand-written sample of every public value type in <c>Daml.Runtime</c> through
/// <see cref="System.Text.Json"/>, on default options and on <c>AddDamlConverters()</c> options.
/// </summary>
public class RuntimeStjRoundTripSweepTests
{
    private static readonly Type[] Exported = typeof(LedgerOffset).Assembly.GetExportedTypes();

    private static readonly IReadOnlySet<Type> NamedNotAValueType = new HashSet<Type>
    {
        typeof(DamlTypeDescriptor),
        typeof(KeyDescriptor<,>),
        typeof(ViewDescriptor<,>),
        typeof(Choice<,,>),
        typeof(DamlLfJsonDecodeContext),
        typeof(EquatableArray<>.Enumerator),
    };

    private static readonly IReadOnlySet<Type> NotInScope =
        NamedNotAValueType.Concat(RuntimeStjSampleTable.WriteOnly.Keys).ToHashSet();

    private static readonly IReadOnlyList<Type> ValueTypes = StjSweepScope.ValueTypes(Exported, NotInScope);

    private static readonly IReadOnlyList<StjRoundTripCase> Cases =
        StjSweepScope.CasesWithAsSelfArms(RuntimeStjSampleTable.Samples);

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
    public void RuntimeStjRoundTripSweep_reads_each_sample_back_equivalent_equal_and_hash_equal(string caseId, string legName)
    {
        var roundTripCase = CasesById[caseId];
        var leg = Enum.Parse<StjOptionsLeg>(legName);
        var rootCause = StjKnownBroken.RootCause(RuntimeStjSampleTable.KnownBroken, caseId, leg);

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
    public void RuntimeStjRoundTripSweep_runs_every_case_on_the_default_and_the_AddDamlConverters_legs()
    {
        var casesNotOnBothLegs = CaseAndLeg()
            .Select(row => ((ITheoryDataRow)row).GetData())
            .GroupBy(data => (string)data[0]!, data => (string)data[1]!)
            .Select(legs => $"{legs.Key}: {string.Join(",", legs.Order(StringComparer.Ordinal))}")
            .Where(line => !line.EndsWith(": AddDamlConverters,Default", StringComparison.Ordinal));

        string.Join(Environment.NewLine, casesNotOnBothLegs).Should().BeEmpty();
    }

    [Fact]
    public void RuntimeStjRoundTripSweep_covers_every_value_type_with_a_sample()
    {
        var uncovered = StjSweepScope.Uncovered(ValueTypes, Cases);

        string.Join(Environment.NewLine, uncovered.Select(type => type.FullName)).Should().BeEmpty();
    }

    [Fact]
    public void RuntimeStjRoundTripSweep_fills_every_nullable_member_in_one_sample_per_arm()
    {
        var sparseArms = StjSweepScope.ArmsWithNoFullyPopulatedSample(Cases, RuntimeStjSampleTable.ArmsWithNoFullyPopulatedForm.Keys.ToHashSet());

        string.Join(Environment.NewLine, sparseArms).Should().BeEmpty();
    }

    [Fact]
    public void RuntimeStjRoundTripSweep_names_only_types_and_cases_that_exist()
    {
        StjSweepScope.StaleExclusions(Exported, NotInScope).Should().BeEmpty();
        StjSweepScope.OutOfScopeKeys(ValueTypes, Cases).Should().BeEmpty();
        StjSweepScope.StaleKnownBrokenKeys(Cases, RuntimeStjSampleTable.KnownBroken.Keys).Should().BeEmpty();
        StjSweepScope.StaleExemptions(Cases, RuntimeStjSampleTable.ArmsWithNoFullyPopulatedForm.Keys.ToHashSet()).Should().BeEmpty();
    }

    [Fact]
    public void RuntimeStjRoundTripSweep_excludes_exactly_the_write_only_types()
    {
        var names = RuntimeStjSampleTable.WriteOnly.Keys.Select(type => type.Name).Order(StringComparer.Ordinal);

        names.Should().Equal(
            "CommandsSubmission",
            "ContractId",
            "CreateAndExerciseCommand",
            "CreateCommand",
            "ExerciseByKeyCommand",
            "ExerciseCommand",
            "MinLedgerTime");
    }

    [Fact]
    public void RuntimeStjRoundTripSweep_states_a_reason_for_every_write_only_type()
    {
        var withoutReason = RuntimeStjSampleTable.WriteOnly
            .Where(entry => entry.Value.Length < 40 || !entry.Value.Contains("never read back", StringComparison.Ordinal))
            .Select(entry => entry.Key.Name);

        withoutReason.Should().BeEmpty();
    }

    [Fact]
    public void RuntimeStjRoundTripSweep_keeps_a_ContractId_of_T_declared_as_itself_in_the_sweep()
    {
        var asSelfIds = Cases
            .Where(roundTripCase => roundTripCase.DeclaredType.IsConstructedGenericType
                && roundTripCase.DeclaredType.GetGenericTypeDefinition() == typeof(ContractId<>))
            .Select(roundTripCase => roundTripCase.Id)
            .Order(StringComparer.Ordinal);

        asSelfIds.Should().Equal("ContractId<ISampleInterface>", "ContractId<SampleTemplate>");
    }

    [Fact]
    public void RuntimeStjRoundTripSweep_discovers_exactly_209_public_types()
    {
        Exported.Should().HaveCount(209);
    }

    [Fact]
    public void RuntimeStjRoundTripSweep_finds_exactly_154_value_types()
    {
        ValueTypes.Should().HaveCount(154);
    }
}
