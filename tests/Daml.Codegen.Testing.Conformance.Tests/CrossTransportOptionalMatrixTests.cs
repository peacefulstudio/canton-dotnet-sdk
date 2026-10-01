// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Grpc;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// Crosses every corpus shape that carries an <c>Optional (Optional Text)</c> with the two ways a
/// read reaches a typed decoder, offline: the gRPC converter, which cannot tell a nested level
/// from a flat one, and the JSON writer and reader, which can. Each shape is exercised in every
/// state, and again with the trailing <c>None</c> fields and components the ledger leaves out.
/// A new shape is one line in <see cref="Shapes"/>.
/// </summary>
public class CrossTransportOptionalMatrixTests
{
    private const string NoneState = "None";
    private const string SomeNoneState = "Some None";
    private const string SomeSomeState = "Some (Some deep)";

    private static readonly Party Owner = new("alice::1220");

    private sealed record ShapeCase(
        object Original,
        DamlValue Encoded,
        Func<DamlValue, object> Decode,
        Func<string, DamlValue> ReadJson);

    private static ShapeCase RecordShape<T>(T original) where T : IDamlRecord<T> => new(
        original,
        original.ToRecord(),
        value => T.FromRecord((DamlRecord)value),
        json => DamlLfJsonReader.ReadRecord<T>(json));

    private static ShapeCase KeyShape(
        Tuple2<Party, Optional<Optional<string>>> original,
        KeyDescriptor<Registration, Tuple2<Party, Optional<Optional<string>>>> key) => new(
        original,
        key.KeyEncoder(original),
        value => key.KeyDecoder(value),
        json =>
        {
            using var document = JsonDocument.Parse(json);
            return key.KeyJsonReader(document.RootElement, DamlLfJsonDecodeContext.Root("key"));
        });

    private static ShapeCase ChoiceResultShape(Optional<Optional<string>> original) => new(
        original,
        original.ToChainValue(inner => inner.ToChainValue(text => new DamlText(text))),
        value => GenericResults.ChoiceReturnNestedOptional.ResultDecoder!(value),
        json =>
        {
            using var document = JsonDocument.Parse(json);
            return GenericResults.ChoiceReturnNestedOptional.ResultJsonReader!(
                document.RootElement, DamlLfJsonDecodeContext.Root("result"));
        });

    private static readonly IReadOnlyDictionary<string, Func<string, ShapeCase>> Shapes =
        new Dictionary<string, Func<string, ShapeCase>>(StringComparer.Ordinal)
        {
            ["TypeCorners.maybeMaybeNote"] = state => RecordShape(TypeCornersWith(MaybeMaybeNote(state))),
            ["TypeCorners.nestedNote"] = state => RecordShape(TypeCornersWithNestedNote(state)),
            ["NestedOptionalTails.midMaybe"] = state => RecordShape(
                new NestedOptionalTails(Owner, MaybeMaybeNote(state), MaybeMaybeNote(SomeSomeState))),
            ["NestedOptionalTails.tailMaybe"] = state => RecordShape(
                new NestedOptionalTails(Owner, MaybeMaybeNote(SomeSomeState), MaybeMaybeNote(state))),
            ["NestedOptionalShapes.boxed"] = state => RecordShape(
                new NestedOptionalShapes(
                    Owner, new Box<Optional<Optional<string>>>(MaybeMaybeNote(state)), TrailingPair(SomeSomeState))),
            ["NestedOptionalShapes.trailing"] = state => RecordShape(
                new NestedOptionalShapes(
                    Owner, new Box<Optional<Optional<string>>>(MaybeMaybeNote(SomeSomeState)), TrailingPair(state))),
            ["Registration.key"] = state => KeyShape(
                new Tuple2<Party, Optional<Optional<string>>>(Owner, MaybeMaybeNote(state)), Registration.Key),
            ["AnnotationView.nested"] = state => RecordShape(new AnnotationView("view", MaybeMaybeNote(state))),
            ["GenericResults.ReturnNestedOptional"] = state => ChoiceResultShape(MaybeMaybeNote(state)),
        };

    private static readonly string[] States = [NoneState, SomeNoneState, SomeSomeState];

    public static TheoryData<string, string> Rows()
    {
        var rows = new TheoryData<string, string>();
        foreach (var shape in Shapes.Keys)
        {
            foreach (var state in States)
            {
                rows.Add(shape, state);
            }
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void OptionalMatrix_round_trips_through_the_gRPC_converter(string shape, string state)
    {
        var shapeCase = Shapes[shape](state);

        var decoded = shapeCase.Decode(ThroughGrpcConverter(shapeCase.Encoded));

        decoded.Should().Be(shapeCase.Original);
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void OptionalMatrix_round_trips_through_the_JSON_writer_and_reader(string shape, string state)
    {
        var shapeCase = Shapes[shape](state);

        var decoded = shapeCase.Decode(shapeCase.ReadJson(DamlJsonSerializer.Serialize(shapeCase.Encoded)));

        decoded.Should().Be(shapeCase.Original);
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void OptionalMatrix_round_trips_through_the_gRPC_converter_when_the_ledger_omits_every_trailing_None(
        string shape, string state)
    {
        var shapeCase = Shapes[shape](state);

        var decoded = shapeCase.Decode(ThroughGrpcConverter(WithoutTrailingNone(shapeCase.Encoded, dropNested: true)));

        decoded.Should().Be(shapeCase.Original);
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void OptionalMatrix_round_trips_through_the_JSON_reader_when_the_ledger_omits_every_trailing_flat_None(
        string shape, string state)
    {
        var shapeCase = Shapes[shape](state);

        var json = DamlJsonSerializer.Serialize(WithoutTrailingNone(shapeCase.Encoded, dropNested: false));
        var decoded = shapeCase.Decode(shapeCase.ReadJson(json));

        decoded.Should().Be(shapeCase.Original);
    }

    [Fact]
    public void OptionalMatrix_serializes_the_three_states_as_the_ledger_writes_them()
    {
        var written = States
            .Select(state => DamlJsonSerializer.Serialize(ChoiceResultShape(MaybeMaybeNote(state)).Encoded))
            .ToArray();

        written.Should().Equal("[]", "[[]]", """[["deep"]]""");
    }

    [Theory]
    [InlineData("""{"owner":"alice::1220","midMaybe":null,"tailMaybe":[]}""")]
    [InlineData("""{"owner":"alice::1220","midMaybe":[null],"tailMaybe":[]}""")]
    [InlineData("""{"owner":"alice::1220","midMaybe":["x"],"tailMaybe":[]}""")]
    public void OptionalMatrix_still_rejects_malformed_nested_Optional_JSON_before_any_decode(string json)
    {
        var act = () => DamlLfJsonReader.ReadRecord<NestedOptionalTails>(json);

        act.Should().Throw<JsonException>();
    }

    private static DamlValue ThroughGrpcConverter(DamlValue value) =>
        DamlValueConverter.FromProtoValue(DamlValueConverter.ToProtoValue(value));

    private static DamlValue WithoutTrailingNone(DamlValue value, bool dropNested) => value switch
    {
        DamlRecord record => new DamlRecord(
            record.RecordId,
            [.. TrimTrailingNone(record.Fields, dropNested)
                .Select(field => new DamlField(field.Label, WithoutTrailingNone(field.Value, dropNested)))]),
        _ => value,
    };

    private static IEnumerable<DamlField> TrimTrailingNone(IEnumerable<DamlField> fields, bool dropNested)
    {
        var kept = fields.ToList();
        while (kept.Count > 0 && IsNone(kept[^1].Value, dropNested))
        {
            kept.RemoveAt(kept.Count - 1);
        }

        return kept;
    }

    private static bool IsNone(DamlValue value, bool dropNested) => value switch
    {
        DamlOptional { Value: null } => true,
        DamlOptionalChain { Value: null } => dropNested,
        _ => false,
    };

    private static Optional<Optional<string>> MaybeMaybeNote(string state) => state switch
    {
        NoneState => new Optional<Optional<string>>.None(),
        SomeNoneState => new Optional<Optional<string>>.Some(new Optional<string>.None()),
        SomeSomeState => new Optional<Optional<string>>.Some(new Optional<string>.Some("deep")),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "not a matrix state"),
    };

    private static Tuple2<string, Optional<Optional<string>>> TrailingPair(string state) =>
        new("head", MaybeMaybeNote(state));

    private static Box<Optional<string>>? NestedNote(string state) => state switch
    {
        NoneState => null,
        SomeNoneState => new Box<Optional<string>>(new Optional<string>.None()),
        SomeSomeState => new Box<Optional<string>>(new Optional<string>.Some("deep")),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "not a matrix state"),
    };

    private static TypeCorners TypeCornersWithNestedNote(string state) =>
        TypeCornersWith(MaybeMaybeNote(SomeSomeState)) with { NestedNote = NestedNote(state) };

    private static TypeCorners TypeCornersWith(Optional<Optional<string>> maybeMaybeNote) => new(
        Owner: Owner,
        BoxedText: new Box<string>("boxed"),
        BoxedProfile: new Box<Profile>(new Profile("ace", 7)),
        Slot: new Slot<long>.Filled(11),
        NestedNote: new Box<Optional<string>>(new Optional<string>.Some("inner")),
        MaybeMaybeNote: maybeMaybeNote,
        Crate: new Crate<string>(new Optional<string>.Some("crated")),
        QuotaByParty: new Dictionary<Party, long> { [Owner] = 1 },
        LabelByRank: new Dictionary<long, string> { [1] = "gold" },
        RankOrLabel: new Either<long, string>.Right("runner-up"),
        NoteOrRank: new Either<Optional<string>, long>.Left(new Optional<string>.Some("noted")),
        Pair: new Tuple2<string, long>("pair", 3),
        Triple: new Tuple3<string, long, bool>("triple", 4, true),
        Branch: new Branch("root", [new Branch("leaf", [])]),
        Whole: 42m,
        Finest: 0.5m);
}
