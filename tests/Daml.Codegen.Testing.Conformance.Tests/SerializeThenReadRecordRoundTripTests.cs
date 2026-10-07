// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// Pipes the writer's output straight into the typed reader for the whole conformance corpus.
/// The writer and the untyped reader form a self-consistent pair, so only crossing the seam to
/// the typed reader — the one the ledger's own grammar is pinned against — can catch a wire-shape
/// divergence.
/// </summary>
public class SerializeThenReadRecordRoundTripTests
{
    private sealed record CorpusEntry(DamlRecord Record, Func<string, DamlRecord> Read);

    private static CorpusEntry Entry<T>(DamlRecord record) where T : IDamlRecord<T> =>
        new(record, json => DamlLfJsonReader.ReadRecord<T>(json));

    private static readonly IReadOnlyDictionary<string, CorpusEntry> Corpus =
        new Dictionary<string, CorpusEntry>(StringComparer.Ordinal)
        {
            [nameof(RichRecord)] = Entry<RichRecord>(GeneratedStjSampleTable.RichRecordSample(new Outcome.Win(
                new Outcome_Win(Prize: 12.34m, Tier: "gold"))).ToRecord()),
            ["RichRecord_nullary_variant_arm"] = Entry<RichRecord>(
                GeneratedStjSampleTable.RichRecordSample(new Outcome.Pending()).ToRecord()),
            [nameof(TypeCorners)] = Entry<TypeCorners>(GeneratedStjSampleTable.TypeCornersSample().ToRecord()),
            [nameof(Profile)] = Entry<Profile>(new Profile("ace", 7).ToRecord()),
            [nameof(Outcome_Win)] = Entry<Outcome_Win>(new Outcome_Win(Prize: 12.34m, Tier: "gold").ToRecord()),
            [nameof(Account)] = Entry<Account>(
                new Account(new Party("alice"), "savings", 1_000).ToRecord()),
            [nameof(AccountKey)] = Entry<AccountKey>(new AccountKey(new Party("alice"), "savings").ToRecord()),
        };

    public static TheoryData<string> RoundTrippableCorpusEntries => [.. Corpus.Keys];

    [Theory]
    [MemberData(nameof(RoundTrippableCorpusEntries))]
    public void ReadRecord_round_trips_the_writer_output_for_every_corpus_record(string entry)
    {
        var corpusEntry = Corpus[entry];

        var restored = corpusEntry.Read(DamlJsonSerializer.Serialize(corpusEntry.Record));

        restored.Should().Be(WithoutTypeIdentifiers(corpusEntry.Record));
    }

    [Fact]
    public void SerializeThenReadRecordRoundTrip_covers_every_arm_of_the_writer_switch()
    {
        var reached = Corpus.Values
            .SelectMany(entry => ValuesReachableFrom(entry.Record))
            .Select(value => value.GetType().Name)
            .Distinct();

        reached.Should().BeEquivalentTo(
            WriterSwitchArms(),
            "the corpus is only a wire-grammar gate for the arms it actually exercises, and a new "
            + "DamlValue arm that no corpus record reaches would ship unchecked");
    }

    private static IEnumerable<string> WriterSwitchArms() =>
        typeof(DamlValue).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(DamlValue)) && !type.IsAbstract)
            .Where(type => type != typeof(DamlUndecodedJson))
            .Select(type => type.Name);

    private static IEnumerable<DamlValue> ValuesReachableFrom(DamlValue value) =>
        [value, .. DirectChildrenOf(value).SelectMany(ValuesReachableFrom)];

    private static IEnumerable<DamlValue> DirectChildrenOf(DamlValue value) => value switch
    {
        DamlRecord record => record.Fields.Select(field => field.Value),
        DamlVariant variant => [variant.Value],
        DamlList list => list.Values,
        DamlTextMap map => map.Values.Values,
        DamlGenMap map => map.Entries.SelectMany(entry => new[] { entry.Key, entry.Value }),
        DamlOptional { Value: { } inner } => [inner],
        DamlOptionalChain { Value: { } inner } => [inner],
        _ => [],
    };

    /// <summary>
    /// Drops the Daml type identifiers the LF-JSON wire never carries, so the two sides of the
    /// round trip are compared on wire content alone. <c>ContractId&lt;T&gt;.ToDamlValue()</c>
    /// stamps a template id and the generated enum and variant converters may stamp a type id;
    /// the reader can only ever produce them unstamped.
    /// </summary>
    private static DamlValue WithoutTypeIdentifiers(DamlValue value) => value switch
    {
        DamlRecord record => new DamlRecord(null,
            [.. record.Fields.Select(field => new DamlField(field.Label, WithoutTypeIdentifiers(field.Value)))]),
        DamlVariant variant => new DamlVariant(null, variant.Constructor, WithoutTypeIdentifiers(variant.Value)),
        DamlEnum enumValue => new DamlEnum(null, enumValue.Constructor),
        DamlContractId contractId => new DamlContractId(contractId.Value),
        DamlList list => new DamlList([.. list.Values.Select(WithoutTypeIdentifiers)]),
        DamlTextMap map => new DamlTextMap(map.Values.ToDictionary(
            entry => entry.Key, entry => WithoutTypeIdentifiers(entry.Value))),
        DamlGenMap map => new DamlGenMap([.. map.Entries.Select(entry =>
            (WithoutTypeIdentifiers(entry.Key), WithoutTypeIdentifiers(entry.Value)))]),
        DamlOptional { Value: { } inner } => new DamlOptional(WithoutTypeIdentifiers(inner)),
        DamlOptionalChain { Value: { } inner } => new DamlOptionalChain(WithoutTypeIdentifiers(inner)),
        _ => value,
    };
}
