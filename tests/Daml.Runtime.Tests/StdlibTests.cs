// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Tests for the hand-coded Daml.Runtime.Stdlib types — the stand-ins for Daml
/// stdlib types that we cannot, or do not want to, generate per consumer package.
/// </summary>
public class StdlibTests
{
    #region RelTime

    [Fact]
    public void RelTime_satisfies_the_generic_IDamlRecord_facet()
    {
        var original = new RelTime(60_000_000);

        DamlRecordFacet.Materialize<RelTime>(original.ToRecord()).Should().Be(original);
    }

    [Fact]
    public void RelTime_should_round_trip_through_ToRecord_FromRecord()
    {
        // Arrange
        var original = new RelTime(60_000_000); // 60 seconds, in microseconds

        // Act
        var record = original.ToRecord();
        var recovered = RelTime.FromRecord(record);

        // Assert
        recovered.Should().Be(original);
        recovered.Microseconds.Should().Be(60_000_000);
    }

    [Fact]
    public void RelTime_ToRecord_uses_microseconds_field_name()
    {
        // The Daml stdlib type is `RelTime { microseconds : Int }`. The field name
        // must match the wire shape exactly — anything else would fail to round-trip
        // through DamlRecord-encoded payloads coming from the ledger.
        var rel = new RelTime(123);

        var record = rel.ToRecord();

        record.Fields.Should().HaveCount(1);
        record.Fields[0].Label.Should().Be("microseconds");
        record.Fields[0].Value.Should().BeOfType<DamlInt64>().Which.Value.Should().Be(123L);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(-1L)]                  // Daml RelTime is signed; negatives represent "in the past".
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void RelTime_should_round_trip_signed_int64_boundaries(long microseconds)
    {
        var rel = new RelTime(microseconds);

        var recovered = RelTime.FromRecord(rel.ToRecord());

        recovered.Microseconds.Should().Be(microseconds);
    }

    [Fact]
    public void RelTime_FromRecord_should_throw_when_microseconds_field_missing()
    {
        var emptyRecord = DamlRecord.Create();

        var act = () => RelTime.FromRecord(emptyRecord);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*microseconds*");
    }

    [Fact]
    public void RelTime_Microseconds_carries_the_DamlField_attribute_its_wire_label_needs()
    {
        var property = typeof(RelTime).GetProperty(nameof(RelTime.Microseconds))!;

        var damlField = property.GetCustomAttribute<DamlFieldAttribute>();

        damlField.Should().NotBeNull(
            "DamlLfJsonReader.ResolveDamlFields only includes properties carrying [DamlField] — " +
            "one missing here silently drops the property from every reflection-driven decode, " +
            "including the PQS field DSL, rather than raising an error");
        damlField!.Name.Should().Be("microseconds");
    }

    [Fact]
    public void RelTime_should_decode_through_DamlLfJsonReader_by_its_DamlField_label()
    {
        var record = DamlLfJsonReader.ReadRecord<RelTime>("""{"microseconds":"60000000"}""");

        record.Fields.Should().Equal(new DamlField("microseconds", new DamlInt64(60_000_000L)));
    }

    [Fact]
    public void RelTime_decoded_through_DamlLfJsonReader_round_trips_via_FromRecord()
    {
        var record = DamlLfJsonReader.ReadRecord<RelTime>("""{"microseconds":"60000000"}""");

        RelTime.FromRecord(record).Should().Be(new RelTime(60_000_000));
    }

    #endregion

    #region Tuple2

    [Fact]
    public void Tuple2_should_round_trip_with_primitive_arguments()
    {
        var original = new Tuple2<long, string>(42L, "hello");

        var record = original.ToRecord(
            i => new DamlInt64(i),
            s => new DamlText(s));
        var recovered = Tuple2<long, string>.FromRecord(
            record,
            v => v.As<DamlInt64>().Value,
            null,
            v => v.As<DamlText>().Value,
            null);

        recovered.Should().Be(original);
    }

    [Fact]
    public void Tuple2_ToRecord_should_emit_underscore_one_underscore_two_field_names()
    {
        // The Daml stdlib type is `data Tuple2 a b = Tuple2 { _1 : a, _2 : b }`.
        // The field names must match exactly to round-trip with the Ledger API.
        var t = new Tuple2<long, long>(1L, 2L);

        var record = t.ToRecord(i => new DamlInt64(i), i => new DamlInt64(i));

        record.Fields.Should().HaveCount(2);
        record.Fields[0].Label.Should().Be("_1");
        record.Fields[1].Label.Should().Be("_2");
    }

    [Fact]
    public void Tuple2_FromRecord_should_throw_when_field_missing()
    {
        var emptyRecord = DamlRecord.Create();

        var act = () => Tuple2<long, long>.FromRecord(
            emptyRecord,
            v => v.As<DamlInt64>().Value,
            null,
            v => v.As<DamlInt64>().Value,
            null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*_1*");
    }

    [Fact]
    public void Tuple2_FromRecord_should_report_an_omitted_required_component_without_calling_its_converter()
    {
        var record = DamlRecord.Create(DamlField.Create("_2", new DamlText("note")));

        var act = () => Tuple2<string, Optional<string>>.FromRecord(
            record,
            _ => throw new Xunit.Sdk.XunitException("the converter must not be called"),
            null,
            v => Optional<string>.FromValue(v, text => text.As<DamlText>().Value),
            DamlOptional.None);

        act.Should().Throw<InvalidOperationException>().WithMessage("Required field '_1' not found in record.");
    }

    [Fact]
    public void Tuple2_FromRecord_should_read_an_omitted_Optional_component_as_None()
    {
        var record = DamlRecord.Create(DamlField.Create("_1", new DamlParty("alice")));

        var tuple = Tuple2<string, Optional<string>>.FromRecord(
            record,
            v => v.As<DamlParty>().Value,
            null,
            v => Optional<string>.FromValue(v, text => text.As<DamlText>().Value),
            DamlOptional.None);

        tuple.Should().Be(new Tuple2<string, Optional<string>>("alice", new Optional<string>.None()));
    }

    [Fact]
    public void Tuple3_FromRecord_should_read_an_omitted_Optional_component_as_None()
    {
        var record = DamlRecord.Create(
            DamlField.Create("_1", new DamlText("a")),
            DamlField.Create("_2", new DamlInt64(2)));

        var tuple = Tuple3<string, long, Optional<string>>.FromRecord(
            record,
            v => v.As<DamlText>().Value,
            null,
            v => v.As<DamlInt64>().Value,
            null,
            v => Optional<string>.FromValue(v, text => text.As<DamlText>().Value),
            DamlOptional.None);

        tuple.Should().Be(new Tuple3<string, long, Optional<string>>("a", 2, new Optional<string>.None()));
    }

    [Fact]
    public void Tuple2_should_round_trip_with_party_argument()
    {
        // Realistic shape — Splice DARs use `Tuple2 Party Int` in beneficiary lists.
        var original = new Tuple2<Party, long>(new Party("Alice"), 100L);

        var record = original.ToRecord(
            p => p.ToDamlValue(),
            i => new DamlInt64(i));
        var recovered = Tuple2<Party, long>.FromRecord(
            record,
            v => Party.FromDamlValue(v.As<DamlParty>()),
            null,
            v => v.As<DamlInt64>().Value,
            null);

        recovered.Should().Be(original);
    }

    #endregion

    #region Tuple3

    [Fact]
    public void Tuple3_should_round_trip_three_components()
    {
        var original = new Tuple3<long, string, bool>(42L, "hello", true);

        var record = original.ToRecord(
            i => new DamlInt64(i),
            s => new DamlText(s),
            b => new DamlBool(b));
        var recovered = Tuple3<long, string, bool>.FromRecord(
            record,
            v => v.As<DamlInt64>().Value,
            null,
            v => v.As<DamlText>().Value,
            null,
            v => v.As<DamlBool>().Value,
            null);

        recovered.Should().Be(original);
    }

    [Fact]
    public void Tuple3_ToRecord_should_emit_underscore_indexed_field_names()
    {
        var t = new Tuple3<long, long, long>(1L, 2L, 3L);

        var record = t.ToRecord(
            i => new DamlInt64(i),
            i => new DamlInt64(i),
            i => new DamlInt64(i));

        record.Fields.Select(f => f.Label).Should().Equal("_1", "_2", "_3");
    }

    [Fact]
    public void Tuple3_FromRecord_should_throw_when_field_missing()
    {
        var emptyRecord = DamlRecord.Create();

        var act = () => Tuple3<long, long, long>.FromRecord(
            emptyRecord,
            v => v.As<DamlInt64>().Value,
            null,
            v => v.As<DamlInt64>().Value,
            null,
            v => v.As<DamlInt64>().Value,
            null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*_1*");
    }

    [Fact]
    public void Tuple3_FromRecord_should_report_an_omitted_required_component_without_calling_its_converter()
    {
        var record = DamlRecord.Create(
            DamlField.Create("_1", new DamlText("a")),
            DamlField.Create("_3", new DamlText("note")));

        var act = () => Tuple3<string, long, Optional<string>>.FromRecord(
            record,
            v => v.As<DamlText>().Value,
            null,
            _ => throw new Xunit.Sdk.XunitException("the converter must not be called"),
            null,
            v => Optional<string>.FromValue(v, text => text.As<DamlText>().Value),
            DamlOptional.None);

        act.Should().Throw<InvalidOperationException>().WithMessage("Required field '_2' not found in record.");
    }

    #endregion

    #region Set

    [Fact]
    public void Set_should_round_trip_an_empty_collection()
    {
        var original = new Set<string>([]);

        var record = original.ToRecord(s => new DamlText(s));
        var recovered = Set<string>.FromRecord(record, v => v.As<DamlText>().Value);

        recovered.Elements.Should().BeEmpty();
    }

    [Fact]
    public void Set_should_round_trip_a_populated_collection()
    {
        var original = new Set<string>(["a", "b", "c"]);

        var record = original.ToRecord(s => new DamlText(s));
        var recovered = Set<string>.FromRecord(record, v => v.As<DamlText>().Value);

        recovered.Should().Be(original);
    }

    [Fact]
    public void Set_should_not_observe_mutations_to_the_source_collection()
    {
        var source = new HashSet<string> { "a", "b" };
        var set = new Set<string>(source);
        var hashBeforeMutation = set.GetHashCode();

        source.Add("c");

        set.Count.Should().Be(2);
        set.Contains("c").Should().BeFalse();
        set.GetHashCode().Should().Be(hashBeforeMutation);
        set.Should().Be(new Set<string>(["a", "b"]));
    }

    [Fact]
    public void Set_with_expression_should_keep_the_defensive_copy()
    {
        var source = new HashSet<string> { "a" };
        var set = new Set<string>(source);

        var copied = set with { };
        source.Add("b");

        copied.Count.Should().Be(1);
        copied.Should().Be(new Set<string>(["a"]));
    }

    [Fact]
    public void Set_ToRecord_should_wrap_a_GenMap_under_field_named_map()
    {
        // Per the Daml-LF spec the wire shape is `Set { map : Map k () }`. The single
        // wrapping field must be named exactly `map`, and each entry must pair the
        // element with DamlUnit.
        var s = new Set<string>(["a"]);

        var record = s.ToRecord(x => new DamlText(x));

        record.Fields.Should().HaveCount(1);
        record.Fields[0].Label.Should().Be("map");
        var map = record.Fields[0].Value.As<DamlGenMap>();
        map.Entries.Should().ContainSingle();
        map.Entries[0].Value.Should().BeOfType<DamlUnit>();
    }

    [Fact]
    public void Set_Contains_should_use_default_equality()
    {
        var s = new Set<long>([1L, 2L, 3L]);

        s.Contains(2L).Should().BeTrue();
        s.Contains(99L).Should().BeFalse();
    }

    [Fact]
    public void Set_constructor_should_deduplicate_input()
    {
        // The Daml stdlib Set is a set, not a multiset. Constructing from a
        // sequence with duplicates must collapse them so the wire representation
        // never emits duplicate keys in the inner Map.
        var s = new Set<long>([1L, 1L, 2L, 2L, 3L]);

        s.Count.Should().Be(3);
        s.Elements.Order().Should().Equal(1L, 2L, 3L);

        var record = s.ToRecord(i => new DamlInt64(i));
        var map = record.Fields[0].Value.As<DamlGenMap>();
        map.Entries.Should().HaveCount(3);
    }

    [Fact]
    public void Set_FromRecord_should_throw_when_map_field_missing()
    {
        var emptyRecord = DamlRecord.Create();

        var act = () => Set<string>.FromRecord(emptyRecord, v => v.As<DamlText>().Value);

        act.Should().Throw<InvalidOperationException>().WithMessage("*map*");
    }

    [Fact]
    public void Constructor_rejects_a_null_element_of_Set_naming_the_index()
    {
        var act = () => new Set<string>(["a", null!]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Element 1 is null*")
            .WithParameterName("elements");
    }

    [Fact]
    public void Constructor_accepts_a_None_Optional_element_in_Set()
    {
        var set = new Set<Optional<string>>([new Optional<string>.None()]);

        set.Count.Should().Be(1);
        set.Contains(new Optional<string>.None()).Should().BeTrue();
    }

    [Fact]
    public void Constructor_accepts_a_null_element_of_a_nullable_value_type_in_Set()
    {
#pragma warning disable CS8714
        var set = new Set<long?>([1L, null]);
#pragma warning restore CS8714

        set.Count.Should().Be(2);
        set.Contains(1L).Should().BeTrue();
        set.Contains(null).Should().BeTrue();
    }

    [Fact]
    public void FromRecord_rejects_a_null_element_payload_for_Set()
    {
        var record = DamlRecord.Create(
            DamlField.Create("map", DamlGenMap.Create((new DamlText("a"), (DamlValue)DamlUnit.Instance))));

        var act = () => Set<string>.FromRecord(record, _ => null!);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Element 0 is null*")
            .WithParameterName("elements");
    }

    #endregion

    #region NonEmpty

    [Fact]
    public void NonEmpty_should_round_trip_singleton()
    {
        var original = new NonEmpty<long>(42L, []);

        var record = original.ToRecord(i => new DamlInt64(i));
        var recovered = NonEmpty<long>.FromRecord(record, v => v.As<DamlInt64>().Value);

        recovered.Should().Be(original);
    }

    [Fact]
    public void NonEmpty_should_round_trip_with_tail_elements()
    {
        var original = new NonEmpty<long>(1L, [2L, 3L, 4L]);

        var record = original.ToRecord(i => new DamlInt64(i));
        var recovered = NonEmpty<long>.FromRecord(record, v => v.As<DamlInt64>().Value);

        recovered.Should().Be(original);
        recovered.All.Should().Equal(1L, 2L, 3L, 4L);
    }

    [Fact]
    public void NonEmpty_should_not_observe_mutations_to_the_source_tail()
    {
        var tail = new List<long> { 2L };
        var nonEmpty = new NonEmpty<long>(1L, tail);
        var hashBeforeMutation = nonEmpty.GetHashCode();

        tail.Add(3L);

        nonEmpty.Tl.Should().Equal(2L);
        nonEmpty.All.Should().Equal(1L, 2L);
        nonEmpty.GetHashCode().Should().Be(hashBeforeMutation);
        nonEmpty.Should().Be(new NonEmpty<long>(1L, [2L]));
    }

    [Fact]
    public void NonEmpty_with_expression_should_copy_the_replacement_tail()
    {
        var replacement = new List<long> { 2L };
        var nonEmpty = new NonEmpty<long>(1L, []) with { Tl = replacement };

        replacement.Add(3L);

        nonEmpty.Tl.Should().Equal(2L);
    }

    [Fact]
    public void NonEmpty_ToRecord_should_emit_hd_and_tl_field_names()
    {
        var n = new NonEmpty<long>(1L, [2L]);

        var record = n.ToRecord(i => new DamlInt64(i));

        record.Fields.Select(f => f.Label).Should().Equal("hd", "tl");
        record.Fields[1].Value.Should().BeOfType<DamlList>();
    }

    [Fact]
    public void NonEmpty_FromRecord_should_throw_when_hd_field_missing()
    {
        var emptyRecord = DamlRecord.Create();

        var act = () => NonEmpty<long>.FromRecord(emptyRecord, v => v.As<DamlInt64>().Value);

        act.Should().Throw<InvalidOperationException>().WithMessage("*hd*");
    }

    [Fact]
    public void Constructor_rejects_a_null_tail_element_of_NonEmpty_naming_the_index()
    {
        var act = () => new NonEmpty<string>("a", ["b", null!]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Element 1 is null*")
            .WithParameterName("Tl");
    }

    [Fact]
    public void Constructor_accepts_a_None_Optional_tail_element_in_NonEmpty()
    {
        var nonEmpty = new NonEmpty<Optional<string>>(
            new Optional<string>.Some("a"), [new Optional<string>.None()]);

        nonEmpty.Tl.Should().Equal(new Optional<string>.None());
    }

    [Fact]
    public void Constructor_accepts_a_null_tail_element_of_a_nullable_value_type_in_NonEmpty()
    {
#pragma warning disable CS8714
        var nonEmpty = new NonEmpty<long?>(1L, [null]);
#pragma warning restore CS8714

        nonEmpty.Tl.Should().Equal(new long?[] { null });
    }

    [Fact]
    public void FromRecord_rejects_a_null_tail_element_payload_for_NonEmpty()
    {
        var record = DamlRecord.Create(
            DamlField.Create("hd", new DamlText("a")),
            DamlField.Create("tl", new DamlList([new DamlText("b")])));

        var act = () => NonEmpty<string>.FromRecord(record, v => v.As<DamlText>().Value == "a" ? "a" : null!);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Element 0 is null*")
            .WithParameterName("Tl");
    }

    [Fact]
    public void Constructor_rejects_a_null_Hd_of_NonEmpty()
    {
        var act = () => new NonEmpty<string>(null!, []);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Hd is null*a NonEmpty<String> holds no null elements*")
            .WithParameterName("Hd");
    }

    [Fact]
    public void With_expression_rejects_a_null_replacement_Hd_of_NonEmpty()
    {
        var original = new NonEmpty<string>("a", []);

        var act = () => original with { Hd = null! };

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Hd is null*a NonEmpty<String> holds no null elements*")
            .WithParameterName("Hd");
    }

    [Fact]
    public void Constructor_accepts_a_None_Optional_Hd_in_NonEmpty()
    {
        var nonEmpty = new NonEmpty<Optional<string>>(new Optional<string>.None(), []);

        nonEmpty.Hd.Should().Be(new Optional<string>.None());
    }

    [Fact]
    public void Constructor_accepts_a_null_Hd_of_a_nullable_value_type_in_NonEmpty()
    {
#pragma warning disable CS8714
        var nonEmpty = new NonEmpty<long?>(null, []);
#pragma warning restore CS8714

        nonEmpty.Hd.Should().BeNull();
    }

    [Fact]
    public void FromRecord_rejects_a_null_Hd_payload_for_NonEmpty()
    {
        var record = DamlRecord.Create(
            DamlField.Create("hd", new DamlText("a")),
            DamlField.Create("tl", new DamlList([])));

        var act = () => NonEmpty<string>.FromRecord(record, _ => null!);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Hd is null*")
            .WithParameterName("Hd");
    }

    #endregion

    #region Map

    [Fact]
    public void Map_should_round_trip_with_string_keys_and_int_values()
    {
        var original = new Map<string, long>([
            new KeyValuePair<string, long>("alice", 1L),
            new KeyValuePair<string, long>("bob", 2L),
        ]);

        var record = original.ToRecord(
            s => new DamlText(s),
            i => new DamlInt64(i));
        var recovered = Map<string, long>.FromRecord(
            record,
            v => v.As<DamlText>().Value,
            v => v.As<DamlInt64>().Value);

        recovered.Should().Be(original);
    }

    [Fact]
    public void Map_should_not_observe_mutations_to_the_source_entries()
    {
        var source = new List<KeyValuePair<string, long>> { new("alice", 1L) };
        var map = new Map<string, long>(source);
        var hashBeforeMutation = map.GetHashCode();

        source.Add(new KeyValuePair<string, long>("bob", 2L));

        map.Count.Should().Be(1);
        map.GetHashCode().Should().Be(hashBeforeMutation);
        map.Should().Be(new Map<string, long>([new KeyValuePair<string, long>("alice", 1L)]));
    }

    [Fact]
    public void Map_with_expression_should_copy_the_replacement_entries()
    {
        var replacement = new List<KeyValuePair<string, long>> { new("alice", 1L) };
        var map = new Map<string, long>([]) with { Entries = replacement };

        replacement.Add(new KeyValuePair<string, long>("bob", 2L));

        map.Count.Should().Be(1);
        map.Entries.Should().Equal(new List<KeyValuePair<string, long>> { new("alice", 1L) });
    }

    [Fact]
    public void Map_ToRecord_should_wrap_a_GenMap_under_field_named_map()
    {
        var m = new Map<string, long>([new KeyValuePair<string, long>("k", 1L)]);

        var record = m.ToRecord(s => new DamlText(s), i => new DamlInt64(i));

        record.Fields.Should().HaveCount(1);
        record.Fields[0].Label.Should().Be("map");
        record.Fields[0].Value.Should().BeOfType<DamlGenMap>();
    }

    [Fact]
    public void Map_should_preserve_entry_order()
    {
        // GenMap is order-preserving on the wire. The Map wrapper must round-trip
        // entries in their original order (callers building from a sorted source
        // depend on it).
        var original = new Map<long, long>([
            new KeyValuePair<long, long>(3L, 30L),
            new KeyValuePair<long, long>(1L, 10L),
            new KeyValuePair<long, long>(2L, 20L),
        ]);

        var record = original.ToRecord(i => new DamlInt64(i), i => new DamlInt64(i));
        var recovered = Map<long, long>.FromRecord(
            record,
            v => v.As<DamlInt64>().Value,
            v => v.As<DamlInt64>().Value);

        recovered.Entries.Select(kv => kv.Key).Should().Equal(3L, 1L, 2L);
    }

    [Fact]
    public void Map_FromRecord_should_throw_when_map_field_missing()
    {
        var emptyRecord = DamlRecord.Create();

        var act = () => Map<string, long>.FromRecord(
            emptyRecord,
            v => v.As<DamlText>().Value,
            v => v.As<DamlInt64>().Value);

        act.Should().Throw<InvalidOperationException>().WithMessage("*map*");
    }

    [Fact]
    public void Constructor_rejects_a_null_key_of_Map_naming_the_index()
    {
        var act = () => new Map<string, long>([new KeyValuePair<string, long>(null!, 1L)]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Entry 0 has a null key*a Map<String, Int64> holds no null keys*")
            .WithParameterName("Entries");
    }

    [Fact]
    public void Constructor_rejects_a_null_value_of_Map_naming_the_index()
    {
        var act = () => new Map<string, string>([new KeyValuePair<string, string>("a", null!)]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Entry 0 has a null value*a Map<String, String> holds no null values*")
            .WithParameterName("Entries");
    }

    [Fact]
    public void Constructor_accepts_a_None_Optional_value_in_Map()
    {
        var map = new Map<string, Optional<string>>([
            new KeyValuePair<string, Optional<string>>("a", new Optional<string>.None()),
        ]);

        map.Count.Should().Be(1);
        map.Entries[0].Value.Should().Be(new Optional<string>.None());
    }

    [Fact]
    public void Constructor_accepts_a_null_value_of_a_nullable_value_type_in_Map()
    {
#pragma warning disable CS8714
        var map = new Map<string, long?>([
            new KeyValuePair<string, long?>("a", null),
        ]);
#pragma warning restore CS8714

        map.Count.Should().Be(1);
        map.Entries[0].Value.Should().BeNull();
    }

    [Fact]
    public void FromRecord_rejects_a_null_value_payload_for_Map()
    {
        var record = DamlRecord.Create(
            DamlField.Create("map", DamlGenMap.Create((new DamlText("a"), (DamlValue)new DamlInt64(1)))));

        var act = () => Map<string, string>.FromRecord(
            record,
            v => v.As<DamlText>().Value,
            _ => null!);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Entry 0 has a null value*")
            .WithParameterName("Entries");
    }

    #endregion

    #region GenericStub

    [Fact]
    public void GenericStub_NotImplemented_should_throw_with_context_in_message()
    {
        var act = () => GenericStub.NotImplemented<string>("amulet");

        act.Should().Throw<NotImplementedException>()
            .WithMessage("*amulet*")
            .And.Message.Should().Contain("Workaround",
                because: "the message must point at the workaround so consumers can keep moving");
    }

    [Fact]
    public void GenericStub_NotImplemented_should_throw_for_any_type_parameter()
    {
        // The signature returns T so the call can sit in expression position. Verify
        // the throw is unconditional regardless of the type parameter.
        var actString = () => GenericStub.NotImplemented<string>("ctx");
        var actInt = () => GenericStub.NotImplemented<int>("ctx");
        var actRecord = () => GenericStub.NotImplemented<DamlRecord>("ctx");

        actString.Should().Throw<NotImplementedException>();
        actInt.Should().Throw<NotImplementedException>();
        actRecord.Should().Throw<NotImplementedException>();
    }

    #endregion

    #region Either

    private static DamlValue Either_ToText(string value) => new DamlText(value);

    private static DamlValue Either_ToInt(long value) => new DamlInt64(value);

    private static string Either_FromText(DamlValue value) => ((DamlText)value).Value;

    private static long Either_FromInt(DamlValue value) => ((DamlInt64)value).Value;

    [Fact]
    public void Either_Left_ToValue_should_emit_left_variant_with_bare_payload()
    {
        Either<string, long> either = new Either<string, long>.Left("hello");

        var value = either.ToValue(Either_ToText, Either_ToInt);

        var variant = value.Should().BeOfType<DamlVariant>().Subject;
        variant.Constructor.Should().Be("Left");
        variant.Value.Should().Be(new DamlText("hello"));
    }

    [Fact]
    public void Either_Right_ToValue_should_emit_right_variant_with_bare_payload()
    {
        Either<string, long> either = new Either<string, long>.Right(7L);

        var value = either.ToValue(Either_ToText, Either_ToInt);

        var variant = value.Should().BeOfType<DamlVariant>().Subject;
        variant.Constructor.Should().Be("Right");
        variant.Value.Should().Be(new DamlInt64(7L));
    }

    [Fact]
    public void Either_Left_should_round_trip_through_value()
    {
        Either<string, long> original = new Either<string, long>.Left("hello");

        var restored = Either<string, long>.FromValue(
            original.ToValue(Either_ToText, Either_ToInt), Either_FromText, Either_FromInt);

        restored.Should().Be(original);
    }

    [Fact]
    public void Either_Right_should_round_trip_through_value()
    {
        Either<string, long> original = new Either<string, long>.Right(7L);

        var restored = Either<string, long>.FromValue(
            original.ToValue(Either_ToText, Either_ToInt), Either_FromText, Either_FromInt);

        restored.Should().Be(original);
    }

    #endregion

    #region Optional

    private static DamlValue Optional_ToText(string value) => new DamlText(value);

    private static string Optional_FromText(DamlValue value) => ((DamlText)value).Value;

    [Fact]
    public void Optional_Some_should_report_a_value_and_carry_it()
    {
        Optional<string> optional = new Optional<string>.Some("deep");

        optional.HasValue.Should().BeTrue();
        optional.GetValueOrDefault().Should().Be("deep");
        optional.GetValueOrThrow().Should().Be("deep");
        optional.TryGetValue(out var carried).Should().BeTrue();
        carried.Should().Be("deep");
    }

    [Fact]
    public void Optional_None_should_report_no_value_and_throw_on_demand()
    {
        Optional<string> optional = new Optional<string>.None();

        optional.HasValue.Should().BeFalse();
        optional.GetValueOrDefault().Should().BeNull();
        optional.TryGetValue(out var carried).Should().BeFalse();
        carried.Should().BeNull();
        optional.Invoking(o => o.GetValueOrThrow()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Optional_Match_should_select_the_arm_and_return_its_projection()
    {
        Optional<string> some = new Optional<string>.Some("deep");
        Optional<string> none = new Optional<string>.None();

        some.Match(value => $"some:{value}", () => "none").Should().Be("some:deep");
        none.Match(value => $"some:{value}", () => "none").Should().Be("none");
    }

    [Fact]
    public void Optional_Match_should_reject_a_null_handler_on_either_arm()
    {
        Optional<string> some = new Optional<string>.Some("deep");
        Optional<string> none = new Optional<string>.None();

        some.Invoking(o => o.Match(null!, () => "none")).Should().Throw<ArgumentNullException>();
        some.Invoking(o => o.Match(value => value, null!)).Should().Throw<ArgumentNullException>();
        none.Invoking(o => o.Match(null!, () => "none")).Should().Throw<ArgumentNullException>();
        none.Invoking(o => o.Match(value => value, null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Optional_should_compare_by_value_and_distinguish_the_arms()
    {
        new Optional<string>.Some("deep").Should().Be(new Optional<string>.Some("deep"));
        new Optional<string>.Some("deep").Should().NotBe(new Optional<string>.Some("other"));
        new Optional<string>.None().Should().Be(new Optional<string>.None());
        ((Optional<string>)new Optional<string>.None()).Should().NotBe(new Optional<string>.Some("deep"));
    }

    [Fact]
    public void Optional_should_be_usable_as_a_dictionary_key()
    {
        var byOptional = new Dictionary<Optional<string>, long>
        {
            [new Optional<string>.Some("deep")] = 1,
            [new Optional<string>.None()] = 2,
        };

        byOptional[new Optional<string>.Some("deep")].Should().Be(1);
        byOptional[new Optional<string>.None()].Should().Be(2);
    }

    [Fact]
    public void Optional_should_round_trip_both_arms_through_the_flat_wire_form()
    {
        Optional<string> some = new Optional<string>.Some("deep");
        Optional<string> none = new Optional<string>.None();

        some.ToValue(Optional_ToText).Should().Be(DamlOptional.Some(new DamlText("deep")));
        none.ToValue(Optional_ToText).Should().Be(DamlOptional.None);
        Optional<string>.FromValue(some.ToValue(Optional_ToText), Optional_FromText).Should().Be(some);
        Optional<string>.FromValue(none.ToValue(Optional_ToText), Optional_FromText).Should().Be(none);
    }

    [Fact]
    public void Optional_FromValue_should_reject_a_value_that_is_not_a_daml_optional()
    {
        var act = () => Optional<string>.FromValue(new DamlText("deep"), Optional_FromText);

        act.Should().Throw<InvalidCastException>();
    }

    [Fact]
    public void Optional_ToChainValue_should_produce_the_chain_node_and_leave_ToValue_flat()
    {
        Optional<string> some = new Optional<string>.Some("deep");
        Optional<string> none = new Optional<string>.None();

        some.ToChainValue(Optional_ToText).Should().Be(DamlOptionalChain.Some(new DamlText("deep")));
        none.ToChainValue(Optional_ToText).Should().Be(DamlOptionalChain.None);
        some.ToValue(Optional_ToText).Should().Be(DamlOptional.Some(new DamlText("deep")));
    }

    [Fact]
    public void Optional_should_round_trip_both_arms_through_the_chain_wire_form()
    {
        Optional<string> some = new Optional<string>.Some("deep");
        Optional<string> none = new Optional<string>.None();

        Optional<string>.FromChainValue(some.ToChainValue(Optional_ToText), Optional_FromText).Should().Be(some);
        Optional<string>.FromChainValue(none.ToChainValue(Optional_ToText), Optional_FromText).Should().Be(none);
    }

    [Fact]
    public void Optional_FromChainValue_should_read_a_flat_DamlOptional_level()
    {
        Optional<string>.FromChainValue(DamlOptional.Some(new DamlText("deep")), Optional_FromText)
            .Should().Be(new Optional<string>.Some("deep"));
        Optional<string>.FromChainValue(DamlOptional.None, Optional_FromText)
            .Should().Be(new Optional<string>.None());
    }

    [Fact]
    public void Optional_FromValue_should_read_a_chain_level()
    {
        Optional<string>.FromValue(DamlOptionalChain.Some(new DamlText("deep")), Optional_FromText)
            .Should().Be(new Optional<string>.Some("deep"));
        Optional<string>.FromValue(DamlOptionalChain.None, Optional_FromText)
            .Should().Be(new Optional<string>.None());
    }

    [Fact]
    public void Optional_FromValue_should_still_reject_a_value_that_is_neither_Optional_tag()
    {
        var act = () => Optional<string>.FromValue(new DamlText("deep"), Optional_FromText);

        act.Should().Throw<InvalidCastException>().WithMessage("*Cannot cast DamlText to DamlOptional*");
    }

    [Fact]
    public void Optional_should_read_a_nested_value_whose_levels_carry_mixed_tags()
    {
        var mixed = DamlOptional.Some(DamlOptionalChain.Some(new DamlText("deep")));

        var read = Optional<Optional<string>>.FromChainValue(
            mixed, inner => Optional<string>.FromValue(inner, Optional_FromText));

        read.Should().Be(new Optional<Optional<string>>.Some(new Optional<string>.Some("deep")));
    }

    [Fact]
    public void Optional_should_nest_without_collapsing_the_inner_arm()
    {
        Optional<Optional<string>> someNone = new Optional<Optional<string>>.Some(new Optional<string>.None());
        Optional<Optional<string>> none = new Optional<Optional<string>>.None();

        someNone.Should().NotBe(none);
        someNone.GetValueOrThrow().HasValue.Should().BeFalse();
    }

    #endregion
}
