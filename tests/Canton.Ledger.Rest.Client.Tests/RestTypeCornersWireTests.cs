// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestTypeCornersWireTests
{
    public static TheoryData<string, string> MaybeMaybeNoteStates => new()
    {
        { "None", "[]" },
        { "Some None", "[[]]" },
        { "Some (Some deep)", """[["deep"]]""" },
    };

    [Theory]
    [MemberData(nameof(MaybeMaybeNoteStates))]
    public void WriteRecord_writes_TypeCorners_maybeMaybeNote_as_one_array_level_per_Optional(string state, string expected)
    {
        using var written = JsonDocument.Parse(WriteThroughRest(Sample(MaybeMaybeNote(state))));

        written.RootElement.GetProperty("maybeMaybeNote").GetRawText().Should().Be(expected);
    }

    [Fact]
    public void WriteRecord_writes_both_TypeCorners_Map_fields_as_arrays_of_key_value_pairs()
    {
        using var written = JsonDocument.Parse(WriteThroughRest(Sample(new Optional<Optional<string>>.None())));

        written.RootElement.GetProperty("quotaByParty").GetRawText().Should().Be("""[["alice::1220ab","7"]]""");
        written.RootElement.GetProperty("labelByRank").GetRawText().Should().Be("""[["1","gold"]]""");
    }

    [Theory]
    [InlineData("None")]
    [InlineData("Some None")]
    [InlineData("Some (Some deep)")]
    public void WriteRecord_output_reads_back_through_the_typed_reader_as_the_same_TypeCorners(string state)
    {
        var original = Sample(MaybeMaybeNote(state));

        var restored = TypeCorners.FromRecord(DamlLfJsonReader.ReadRecord<TypeCorners>(WriteThroughRest(original)));

        restored.Should().Be(original);
    }

    private static string WriteThroughRest(TypeCorners corners) =>
        JsonSerializer.Serialize(RestValueEncoder.ToWireRecord(corners.ToRecord()), RestRefitSettings.SerializerOptions);

    private static Optional<Optional<string>> MaybeMaybeNote(string state) => state switch
    {
        "None" => new Optional<Optional<string>>.None(),
        "Some None" => new Optional<Optional<string>>.Some(new Optional<string>.None()),
        _ => new Optional<Optional<string>>.Some(new Optional<string>.Some("deep")),
    };

    private static TypeCorners Sample(Optional<Optional<string>> maybeMaybeNote) => new(
        Owner: new Party("alice::1220ab"),
        BoxedText: new Box<string>("boxed"),
        BoxedProfile: new Box<Profile>(new Profile("ace", 7)),
        Slot: new Slot<long>.Filled(11),
        NestedNote: new Box<Optional<string>>(new Optional<string>.Some("inner")),
        MaybeMaybeNote: maybeMaybeNote,
        Crate: new Crate<string>(new Optional<string>.Some("crated")),
        QuotaByParty: new Dictionary<Party, long> { [new Party("alice::1220ab")] = 7 },
        LabelByRank: new Dictionary<long, string> { [1] = "gold" },
        RankOrLabel: new Either<long, string>.Right("runner-up"),
        NoteOrRank: new Either<Optional<string>, long>.Left(new Optional<string>.Some("noted")),
        Pair: new Tuple2<string, long>("pair", 3),
        Triple: new Tuple3<string, long, bool>("triple", 4, true),
        Branch: new Branch("root", [new Branch("leaf", [])]),
        Whole: 42m,
        Finest: 0.5m);
}
