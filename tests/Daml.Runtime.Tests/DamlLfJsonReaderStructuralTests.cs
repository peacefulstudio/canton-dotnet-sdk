// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlLfJsonReaderStructuralTests
{
    public sealed record NoteHolder([property: DamlFieldAttribute("note")] string? Note) : IDamlRecord<NoteHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "note",
            Note is null ? DamlOptional.None : DamlOptional.Some(new DamlText(Note))));

        public static NoteHolder FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("note").As<DamlOptional>().GetValueOrDefault<DamlText>()?.Value);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var noteJson = DamlLfJsonDecoders.RequireField(json, context, "note");
            var noteContext = context.Field("note");
            var note = DamlLfJsonDecoders.ReadOptional(noteJson, noteContext, DamlLfJsonDecoders.ReadText);
            return DamlRecord.Create(DamlField.Create("note", note));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_present_optional_field_from_its_bare_wire_value()
    {
        var record = DamlLfJsonReader.ReadRecord<NoteHolder>("""{"note":"present"}""");

        record.GetRequiredField("note").Should().BeOfType<DamlOptional>()
            .Which.Value.Should().BeOfType<DamlText>().Which.Value.Should().Be("present");
    }

    [Fact]
    public void ReadRecord_should_decode_an_absent_optional_field_from_json_null()
    {
        var record = DamlLfJsonReader.ReadRecord<NoteHolder>("""{"note":null}""");

        record.GetRequiredField("note").Should().BeOfType<DamlOptional>()
            .Which.Should().Be(DamlOptional.None);
    }

    public sealed record LevelHolder([property: DamlFieldAttribute("level")] long? Level) : IDamlRecord<LevelHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "level",
            Level is null ? DamlOptional.None : DamlOptional.Some(new DamlInt64(Level.Value))));

        public static LevelHolder FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("level").As<DamlOptional>().GetValueOrDefault<DamlInt64>()?.Value);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var levelJson = DamlLfJsonDecoders.RequireField(json, context, "level");
            var levelContext = context.Field("level");
            var level = DamlLfJsonDecoders.ReadOptional(levelJson, levelContext, DamlLfJsonDecoders.ReadInt64);
            return DamlRecord.Create(DamlField.Create("level", level));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_present_optional_value_type_field()
    {
        var record = DamlLfJsonReader.ReadRecord<LevelHolder>("""{"level":"3"}""");

        record.GetRequiredField("level").Should().BeOfType<DamlOptional>()
            .Which.Value.Should().BeOfType<DamlInt64>().Which.Value.Should().Be(3L);
    }

    [Fact]
    public void ReadRecord_should_decode_an_absent_optional_value_type_field()
    {
        var record = DamlLfJsonReader.ReadRecord<LevelHolder>("""{"level":null}""");

        record.GetRequiredField("level").Should().BeOfType<DamlOptional>()
            .Which.Should().Be(DamlOptional.None);
    }

    public sealed record NoteListHolder(
        [property: DamlFieldAttribute("notes")] IReadOnlyList<string?> Notes) : IDamlRecord<NoteListHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "notes",
            new DamlList(Notes
                .Select(note => (DamlValue)(note is null ? DamlOptional.None : DamlOptional.Some(new DamlText(note))))
                .ToList())));

        public static NoteListHolder FromRecord(DamlRecord record) => new(record
            .GetRequiredField("notes").As<DamlList>().Values
            .Select(value => value.As<DamlOptional>().GetValueOrDefault<DamlText>()?.Value)
            .ToList());

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var notesJson = DamlLfJsonDecoders.RequireField(json, context, "notes");
            var notesContext = context.Field("notes");
            var notes = DamlLfJsonDecoders.ReadList(notesJson, notesContext, (element, elementContext) =>
                DamlLfJsonDecoders.ReadOptional(element, elementContext, DamlLfJsonDecoders.ReadText));
            return DamlRecord.Create(DamlField.Create("notes", notes));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_optionals_nested_inside_a_list()
    {
        var record = DamlLfJsonReader.ReadRecord<NoteListHolder>("""{"notes":["present",null]}""");

        record.GetRequiredField("notes").Should().BeOfType<DamlList>()
            .Which.Values.Should().Equal(
                DamlOptional.Some(new DamlText("present")),
                DamlOptional.None);
    }

    public sealed record AttributesHolder(
        [property: DamlFieldAttribute("attributes")] IReadOnlyDictionary<string, string> Attributes)
        : IDamlRecord<AttributesHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "attributes",
            new DamlTextMap(Attributes.ToDictionary(entry => entry.Key, entry => (DamlValue)new DamlText(entry.Value)))));

        public static AttributesHolder FromRecord(DamlRecord record) => new(record
            .GetRequiredField("attributes").As<DamlTextMap>().Values
            .ToDictionary(entry => entry.Key, entry => entry.Value.As<DamlText>().Value));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var attributesJson = DamlLfJsonDecoders.RequireField(json, context, "attributes");
            var attributesContext = context.Field("attributes");
            DamlValue attributes = attributesJson.ValueKind switch
            {
                JsonValueKind.Array => DamlLfJsonDecoders.ReadGenMap(
                    attributesJson, attributesContext, DamlLfJsonDecoders.ReadText, DamlLfJsonDecoders.ReadText),
                JsonValueKind.Object => DamlLfJsonDecoders.ReadTextMap(
                    attributesJson, attributesContext, DamlLfJsonDecoders.ReadText),
                _ => throw new JsonException(
                    "Expected JSON object (TextMap) or array of entry pairs (GenMap) "
                    + $"at '{attributesContext.Path}' but found {attributesJson.ValueKind}")
            };
            return DamlRecord.Create(DamlField.Create("attributes", attributes));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_text_map_field_from_its_wire_object_form()
    {
        var record = DamlLfJsonReader.ReadRecord<AttributesHolder>("""{"attributes":{"a":"1"}}""");

        record.GetRequiredField("attributes").Should().BeOfType<DamlTextMap>()
            .Which.Should().Be(DamlTextMap.Create(("a", new DamlText("1"))));
    }

    [Fact]
    public void ReadRecord_should_decode_an_empty_text_map_field()
    {
        var record = DamlLfJsonReader.ReadRecord<AttributesHolder>("""{"attributes":{}}""");

        record.GetRequiredField("attributes").Should().BeOfType<DamlTextMap>()
            .Which.Values.Should().BeEmpty();
    }

    [Fact]
    public void ReadRecord_should_reject_duplicate_text_map_keys_in_a_caller_parsed_document()
    {
        using var document = JsonDocument.Parse("""{"attributes":{"a":"1","a":"2"}}""");

        var act = () => DamlLfJsonReader.ReadRecord<AttributesHolder>(document.RootElement);

        act.Should().Throw<JsonException>()
            .WithMessage("Duplicate key 'a' at 'AttributesHolder.attributes' in a Daml TextMap");
    }

    [Fact]
    public void ReadRecord_should_bracket_the_map_key_when_reporting_an_error_inside_a_text_map_value()
    {
        var act = () => DamlLfJsonReader.ReadRecord<AttributesHolder>("""{"attributes":{"a.b":5}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON String at 'AttributesHolder.attributes['a.b']' but found Number");
    }

    [Fact]
    public void ReadRecord_should_escape_a_quote_inside_a_bracketed_map_key()
    {
        var act = () => DamlLfJsonReader.ReadRecord<AttributesHolder>("""{"attributes":{"o'brien":5}}""");

        act.Should().Throw<JsonException>()
            .WithMessage(@"Expected JSON String at 'AttributesHolder.attributes['o\'brien']' but found Number");
    }

    [Fact]
    public void ReadRecord_should_escape_a_backslash_inside_a_bracketed_map_key()
    {
        var act = () => DamlLfJsonReader.ReadRecord<AttributesHolder>("""{"attributes":{"a\\b":5}}""");

        act.Should().Throw<JsonException>()
            .WithMessage(@"Expected JSON String at 'AttributesHolder.attributes['a\\b']' but found Number");
    }

    [Fact]
    public void ReadRecord_should_escape_a_backslash_that_precedes_a_quote_inside_a_bracketed_map_key()
    {
        var act = () => DamlLfJsonReader.ReadRecord<AttributesHolder>("""{"attributes":{"a\\'b":5}}""");

        act.Should().Throw<JsonException>()
            .WithMessage(@"Expected JSON String at 'AttributesHolder.attributes['a\\\'b']' but found Number");
    }

    [Fact]
    public void ReadRecord_should_elide_an_oversized_map_key_in_a_bracketed_path()
    {
        var oversizedKey = new string('k', 70);

        var act = () => DamlLfJsonReader.ReadRecord<AttributesHolder>($$$"""{"attributes":{"{{{oversizedKey}}}":5}}""");

        act.Should().Throw<JsonException>()
            .WithMessage(
                $"Expected JSON String at 'AttributesHolder.attributes['{new string('k', 64)}…']' but found Number");
    }

    private const string WireParty =
        "wiree3ed3454::1220141a01c00ef277c31ca4eb0e82ee3de7f790eb25f3787f8195f117af8668bf3b";

    public sealed record GenMapHolder(
        [property: DamlFieldAttribute("genMap")] IReadOnlyDictionary<Party, long> GenMap) : IDamlRecord<GenMapHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "genMap",
            new DamlGenMap(GenMap
                .Select(entry => ((DamlValue)entry.Key.ToDamlValue(), (DamlValue)new DamlInt64(entry.Value)))
                .ToList())));

        public static GenMapHolder FromRecord(DamlRecord record) => new(record
            .GetRequiredField("genMap").As<DamlGenMap>().Entries
            .ToDictionary(entry => Party.FromDamlValue(entry.Key.As<DamlParty>()), entry => entry.Value.As<DamlInt64>().Value));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var genMapJson = DamlLfJsonDecoders.RequireField(json, context, "genMap");
            var genMapContext = context.Field("genMap");
            if (genMapJson.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException(
                    $"Expected JSON array of entry pairs (GenMap) at '{genMapContext.Path}' but found {genMapJson.ValueKind}");
            }
            var genMap = DamlLfJsonDecoders.ReadGenMap(
                genMapJson, genMapContext, DamlLfJsonDecoders.ReadParty, DamlLfJsonDecoders.ReadInt64);
            return DamlRecord.Create(DamlField.Create("genMap", genMap));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_gen_map_field_from_its_wire_pair_array_form()
    {
        var record = DamlLfJsonReader.ReadRecord<GenMapHolder>($$"""{"genMap":[["{{WireParty}}","7"]]}""");

        record.GetRequiredField("genMap").Should().BeOfType<DamlGenMap>()
            .Which.Should().Be(DamlGenMap.Create((new DamlParty(WireParty), new DamlInt64(7))));
    }

    [Fact]
    public void ReadRecord_should_decode_an_empty_gen_map_field()
    {
        var record = DamlLfJsonReader.ReadRecord<GenMapHolder>("""{"genMap":[]}""");

        record.GetRequiredField("genMap").Should().BeOfType<DamlGenMap>()
            .Which.Entries.Should().BeEmpty();
    }

    [Fact]
    public void ReadRecord_should_reject_a_gen_map_entry_that_is_not_a_key_value_pair()
    {
        var act = () => DamlLfJsonReader.ReadRecord<GenMapHolder>($$"""{"genMap":[["{{WireParty}}"]]}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected a two-element key/value pair at 'GenMapHolder.genMap[0]' but found 1 element(s)");
    }

    [Fact]
    public void ReadRecord_should_reject_a_gen_map_with_a_duplicate_key()
    {
        var act = () => DamlLfJsonReader.ReadRecord<GenMapHolder>($$"""{"genMap":[["{{WireParty}}","1"],["{{WireParty}}","2"]]}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Duplicate key at 'GenMapHolder.genMap[1]' in a Daml GenMap");
    }

    [Fact]
    public void ReadRecord_should_reject_a_gen_map_entry_that_is_not_an_array()
    {
        var act = () => DamlLfJsonReader.ReadRecord<GenMapHolder>("""{"genMap":["nope"]}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Array at 'GenMapHolder.genMap[0]' but found String");
    }

    [Fact]
    public void ReadRecord_should_reject_a_gen_map_field_encoded_as_a_json_object()
    {
        var act = () => DamlLfJsonReader.ReadRecord<GenMapHolder>("""{"genMap":{"a":"1"}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON array of entry pairs (GenMap) at 'GenMapHolder.genMap' but found Object");
    }

    [Fact]
    public void ReadRecord_should_name_both_map_wire_forms_when_rejecting_a_string_keyed_map()
    {
        var act = () => DamlLfJsonReader.ReadRecord<AttributesHolder>("""{"attributes":"nope"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON object (TextMap) or array of entry pairs (GenMap) "
                + "at 'AttributesHolder.attributes' but found String");
    }

    public sealed record UnitHolder([property: DamlFieldAttribute("unitField")] DamlUnit UnitField) : IDamlRecord<UnitHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("unitField", UnitField));

        public static UnitHolder FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("unitField").As<DamlUnit>());

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var unitFieldJson = DamlLfJsonDecoders.RequireField(json, context, "unitField");
            var unitFieldContext = context.Field("unitField");
            var unitField = DamlLfJsonDecoders.ReadUnit(unitFieldJson, unitFieldContext);
            return DamlRecord.Create(DamlField.Create("unitField", unitField));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_unit_field_from_its_wire_empty_object_form()
    {
        var record = DamlLfJsonReader.ReadRecord<UnitHolder>("""{"unitField":{}}""");

        record.GetRequiredField("unitField").Should().BeOfType<DamlUnit>()
            .Which.Should().BeSameAs(DamlUnit.Instance);
    }

    [Fact]
    public void ReadRecord_should_reject_a_unit_field_encoded_as_a_json_string()
    {
        var act = () => DamlLfJsonReader.ReadRecord<UnitHolder>("""{"unitField":"nope"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Object at 'UnitHolder.unitField' but found String");
    }

    public enum Suit
    {
        Clubs,
        Diamonds,
        Hearts,
        Spades
    }

    public sealed record SuitHolder([property: DamlFieldAttribute("suit")] Suit Suit) : IDamlRecord<SuitHolder>
    {
        private static readonly IReadOnlyList<string> KnownConstructors =
            Enum.GetNames<Suit>().Order(StringComparer.Ordinal).ToList();

        public DamlRecord ToRecord() =>
            DamlRecord.Create(DamlField.Create("suit", DamlEnum.Create(Suit.ToString())));

        public static SuitHolder FromRecord(DamlRecord record) =>
            new(Enum.Parse<Suit>(record.GetRequiredField("suit").As<DamlEnum>().Constructor));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var suitJson = DamlLfJsonDecoders.RequireField(json, context, "suit");
            var suitContext = context.Field("suit");
            var suit = DamlLfJsonDecoders.ReadEnumConstructor(suitJson, suitContext, KnownConstructors);
            return DamlRecord.Create(DamlField.Create("suit", suit));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_an_enum_field_from_its_bare_wire_string()
    {
        var record = DamlLfJsonReader.ReadRecord<SuitHolder>("""{"suit":"Hearts"}""");

        record.GetRequiredField("suit").Should().BeOfType<DamlEnum>()
            .Which.Should().Be(DamlEnum.Create("Hearts"));
    }

    [Fact]
    public void ReadRecord_should_reject_an_unknown_enum_constructor()
    {
        var act = () => DamlLfJsonReader.ReadRecord<SuitHolder>("""{"suit":"Wands"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Unknown Daml enum constructor 'Wands' at 'SuitHolder.suit'; "
                + "expected one of Clubs, Diamonds, Hearts, Spades");
    }

    [Fact]
    public void ReadRecord_should_reject_an_enum_field_encoded_as_a_json_number()
    {
        var act = () => DamlLfJsonReader.ReadRecord<SuitHolder>("""{"suit":2}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON String at 'SuitHolder.suit' but found Number");
    }

    public enum Zigzag
    {
        Zig,
        Alpha
    }

    public sealed record ZigzagHolder([property: DamlFieldAttribute("zigzag")] Zigzag Zigzag) : IDamlRecord<ZigzagHolder>
    {
        private static readonly IReadOnlyList<string> KnownConstructors =
            Enum.GetNames<Zigzag>().Order(StringComparer.Ordinal).ToList();

        public DamlRecord ToRecord() =>
            DamlRecord.Create(DamlField.Create("zigzag", DamlEnum.Create(Zigzag.ToString())));

        public static ZigzagHolder FromRecord(DamlRecord record) =>
            new(Enum.Parse<Zigzag>(record.GetRequiredField("zigzag").As<DamlEnum>().Constructor));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var zigzagJson = DamlLfJsonDecoders.RequireField(json, context, "zigzag");
            var zigzagContext = context.Field("zigzag");
            var zigzag = DamlLfJsonDecoders.ReadEnumConstructor(zigzagJson, zigzagContext, KnownConstructors);
            return DamlRecord.Create(DamlField.Create("zigzag", zigzag));
        }
    }

    [Fact]
    public void ReadRecord_should_sort_the_expected_set_when_rejecting_an_unknown_enum_constructor()
    {
        var act = () => DamlLfJsonReader.ReadRecord<ZigzagHolder>("""{"zigzag":"Zag"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Unknown Daml enum constructor 'Zag' at 'ZigzagHolder.zigzag'; "
                + "expected one of Alpha, Zig");
    }

    public sealed record OutcomeWin(
        [property: DamlFieldAttribute("prize")] decimal Prize,
        [property: DamlFieldAttribute("tier")] string Tier) : IDamlRecord<OutcomeWin>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("prize", new DamlNumeric(Prize)),
            DamlField.Create("tier", new DamlText(Tier)));

        public static OutcomeWin FromRecord(DamlRecord record) => new(
            record.GetRequiredField("prize").As<DamlNumeric>().Value,
            record.GetRequiredField("tier").As<DamlText>().Value);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(
                DamlField.Create("prize", DamlLfJsonDecoders.ReadNumeric(
                    DamlLfJsonDecoders.RequireField(json, context, "prize"), context.Field("prize"))),
                DamlField.Create("tier", DamlLfJsonDecoders.ReadText(
                    DamlLfJsonDecoders.RequireField(json, context, "tier"), context.Field("tier"))));
        }
    }

    public abstract record Outcome : IDamlVariant<Outcome>
    {
        private static readonly string[] ExpectedConstructors = ["Pending", "Win"];

        public abstract string Tag { get; }

        public abstract DamlVariant ToVariant();

        public static Outcome FromVariant(DamlVariant variant) =>
            variant.Constructor switch
            {
                "Win" => new Win(OutcomeWin.FromRecord(variant.Value.As<DamlRecord>())),
                "Pending" => new Pending(),
                _ => throw new ArgumentOutOfRangeException(nameof(variant), variant.Constructor, null)
            };

        public static DamlVariant __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            var tag = DamlLfJsonDecoders.ReadVariantTag(json, context);
            return tag switch
            {
                "Win" => DamlVariant.Create("Win", OutcomeWin.__ReadDamlLfJson(
                    DamlLfJsonDecoders.RequireVariantValue(json, context), context.Field("value"))),
                "Pending" => DamlVariant.Create("Pending", DamlLfJsonDecoders.ReadUnit(
                    DamlLfJsonDecoders.RequireVariantValue(json, context), context.Field("value"))),
                _ => throw DamlLfJsonDecoders.UnknownConstructor(
                    "variant constructor", tag, context, ExpectedConstructors)
            };
        }

        public sealed record Win(OutcomeWin Value) : Outcome
        {
            public override string Tag => "Win";

            public override DamlVariant ToVariant() => DamlVariant.Create("Win", Value.ToRecord());
        }

        public sealed record Pending : Outcome
        {
            public override string Tag => "Pending";

            public override DamlVariant ToVariant() => DamlVariant.Create("Pending", DamlUnit.Instance);
        }
    }

    public sealed record OutcomeHolder([property: DamlFieldAttribute("outcome")] Outcome Outcome) : IDamlRecord<OutcomeHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("outcome", Outcome.ToVariant()));

        public static OutcomeHolder FromRecord(DamlRecord record) =>
            new(Outcome.FromVariant(record.GetRequiredField("outcome").As<DamlVariant>()));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var outcome = DamlLfJsonDecoders.ReadVariant<Outcome>(
                DamlLfJsonDecoders.RequireField(json, context, "outcome"), context.Field("outcome"));
            return DamlRecord.Create(DamlField.Create("outcome", outcome));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_tagged_variant_arm_with_its_record_payload()
    {
        var record = DamlLfJsonReader.ReadRecord<OutcomeHolder>("""{"outcome":{"tag":"Win","value":{"prize":"1.25","tier":"gold"}}}""");

        var variant = record.GetRequiredField("outcome").Should().BeOfType<DamlVariant>().Which;
        variant.Constructor.Should().Be("Win");
        variant.Value.Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("prize", new DamlNumeric(1.25m)),
            new DamlField("tier", new DamlText("gold")));
    }

    [Fact]
    public void ReadRecord_should_decode_a_nullary_variant_arm_from_its_empty_object_value()
    {
        var record = DamlLfJsonReader.ReadRecord<OutcomeHolder>("""{"outcome":{"tag":"Pending","value":{}}}""");

        var variant = record.GetRequiredField("outcome").Should().BeOfType<DamlVariant>().Which;
        variant.Constructor.Should().Be("Pending");
        variant.Value.Should().BeSameAs(DamlUnit.Instance);
    }

    [Fact]
    public void ReadRecord_should_reject_an_unknown_variant_constructor()
    {
        var act = () => DamlLfJsonReader.ReadRecord<OutcomeHolder>("""{"outcome":{"tag":"Draw","value":{}}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Unknown Daml variant constructor 'Draw' at 'OutcomeHolder.outcome'; "
                + "expected one of Pending, Win");
    }

    [Fact]
    public void ReadRecord_should_reject_a_variant_without_a_tag()
    {
        var act = () => DamlLfJsonReader.ReadRecord<OutcomeHolder>("""{"outcome":{"value":{}}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml variant field 'OutcomeHolder.outcome.tag' is missing from the JSON object");
    }

    [Fact]
    public void ReadRecord_should_reject_a_variant_tag_that_is_not_a_string()
    {
        var act = () => DamlLfJsonReader.ReadRecord<OutcomeHolder>("""{"outcome":{"tag":5,"value":{}}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON String at 'OutcomeHolder.outcome.tag' but found Number");
    }

    [Fact]
    public void ReadRecord_should_reject_a_nullary_variant_value_that_is_not_an_object()
    {
        var act = () => DamlLfJsonReader.ReadRecord<OutcomeHolder>("""{"outcome":{"tag":"Pending","value":"nope"}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Object at 'OutcomeHolder.outcome.value' but found String");
    }

    [Fact]
    public void ReadRecord_should_reject_a_variant_missing_its_value_field()
    {
        var act = () => DamlLfJsonReader.ReadRecord<OutcomeHolder>("""{"outcome":{"tag":"Pending"}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml variant field 'OutcomeHolder.outcome.value' is missing from the JSON object");
    }

    [Fact]
    public void ReadRecord_should_reject_a_variant_field_encoded_as_a_bare_string()
    {
        var act = () => DamlLfJsonReader.ReadRecord<OutcomeHolder>("""{"outcome":"Pending"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Object at 'OutcomeHolder.outcome' but found String");
    }

    public sealed record Profile(
        [property: DamlFieldAttribute("nickname")] string Nickname,
        [property: DamlFieldAttribute("level")] long Level) : IDamlRecord<Profile>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("nickname", new DamlText(Nickname)),
            DamlField.Create("level", new DamlInt64(Level)));

        public static Profile FromRecord(DamlRecord record) => new(
            record.GetRequiredField("nickname").As<DamlText>().Value,
            record.GetRequiredField("level").As<DamlInt64>().Value);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(
                DamlField.Create("nickname", DamlLfJsonDecoders.ReadText(
                    DamlLfJsonDecoders.RequireField(json, context, "nickname"), context.Field("nickname"))),
                DamlField.Create("level", DamlLfJsonDecoders.ReadInt64(
                    DamlLfJsonDecoders.RequireField(json, context, "level"), context.Field("level"))));
        }
    }

    public sealed record ProfileHolder([property: DamlFieldAttribute("profile")] Profile Profile) : IDamlRecord<ProfileHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("profile", Profile.ToRecord()));

        public static ProfileHolder FromRecord(DamlRecord record) =>
            new(Profile.FromRecord(record.GetRequiredField("profile").As<DamlRecord>()));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var profileJson = DamlLfJsonDecoders.RequireField(json, context, "profile");
            var profileContext = context.Field("profile");
            var profile = Profile.__ReadDamlLfJson(profileJson, profileContext);
            return DamlRecord.Create(DamlField.Create("profile", profile));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_nested_record_field_keyed_by_field_name()
    {
        var record = DamlLfJsonReader.ReadRecord<ProfileHolder>("""{"profile":{"nickname":"nick","level":"3"}}""");

        record.GetRequiredField("profile").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("nickname", new DamlText("nick")),
            new DamlField("level", new DamlInt64(3L)));
    }

    public sealed record TagsHolder(
        [property: DamlFieldAttribute("tags")] IReadOnlyList<string> Tags) : IDamlRecord<TagsHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "tags",
            new DamlList(Tags.Select(tag => (DamlValue)new DamlText(tag)).ToList())));

        public static TagsHolder FromRecord(DamlRecord record) => new(record
            .GetRequiredField("tags").As<DamlList>().Values
            .Select(value => value.As<DamlText>().Value)
            .ToList());

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var tagsJson = DamlLfJsonDecoders.RequireField(json, context, "tags");
            var tagsContext = context.Field("tags");
            var tags = DamlLfJsonDecoders.ReadList(tagsJson, tagsContext, DamlLfJsonDecoders.ReadText);
            return DamlRecord.Create(DamlField.Create("tags", tags));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_list_field_from_its_wire_array_form()
    {
        var record = DamlLfJsonReader.ReadRecord<TagsHolder>("""{"tags":["x","y"]}""");

        record.GetRequiredField("tags").Should().BeOfType<DamlList>()
            .Which.Values.Should().Equal(new DamlText("x"), new DamlText("y"));
    }

    [Fact]
    public void ReadRecord_should_decode_an_empty_list_field()
    {
        var record = DamlLfJsonReader.ReadRecord<TagsHolder>("""{"tags":[]}""");

        record.GetRequiredField("tags").Should().BeOfType<DamlList>().Which.Values.Should().BeEmpty();
    }

    public sealed record ProfileTallyHolder(
        [property: DamlFieldAttribute("tally")] IReadOnlyDictionary<Profile, long> Tally)
        : IDamlRecord<ProfileTallyHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "tally",
            new DamlGenMap(Tally
                .Select(entry => ((DamlValue)entry.Key.ToRecord(), (DamlValue)new DamlInt64(entry.Value)))
                .ToList())));

        public static ProfileTallyHolder FromRecord(DamlRecord record) => new(record
            .GetRequiredField("tally").As<DamlGenMap>().Entries
            .ToDictionary(entry => Profile.FromRecord(entry.Key.As<DamlRecord>()), entry => entry.Value.As<DamlInt64>().Value));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var tallyJson = DamlLfJsonDecoders.RequireField(json, context, "tally");
            var tallyContext = context.Field("tally");
            var tally = DamlLfJsonDecoders.ReadGenMap(
                tallyJson, tallyContext, Profile.__ReadDamlLfJson, DamlLfJsonDecoders.ReadInt64);
            return DamlRecord.Create(DamlField.Create("tally", tally));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_gen_map_keyed_by_a_record()
    {
        var record = DamlLfJsonReader.ReadRecord<ProfileTallyHolder>("""{"tally":[[{"nickname":"nick","level":"3"},"7"]]}""");

        var entry = record.GetRequiredField("tally").Should().BeOfType<DamlGenMap>()
            .Which.Entries.Should().ContainSingle().Which;
        entry.Key.Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("nickname", new DamlText("nick")),
            new DamlField("level", new DamlInt64(3L)));
        entry.Value.Should().Be(new DamlInt64(7L));
    }

    public abstract record Reading : IDamlVariant
    {
        public abstract string Tag { get; }

        public abstract DamlVariant ToVariant();

        public sealed record Measured(decimal Value) : Reading
        {
            public override string Tag => "Measured";

            public override DamlVariant ToVariant() => DamlVariant.Create("Measured", new DamlNumeric(Value));
        }
    }

    public sealed record ReadingHolder([property: DamlFieldAttribute("reading")] Reading Reading) : IDamlRecord<ReadingHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("reading", Reading.ToVariant()));

        public static ReadingHolder FromRecord(DamlRecord record)
        {
            var variant = record.GetRequiredField("reading").As<DamlVariant>();
            Reading reading = variant.Constructor switch
            {
                "Measured" => new Reading.Measured(variant.Value.As<DamlNumeric>().Value),
                _ => throw new ArgumentOutOfRangeException(nameof(record), variant.Constructor, null)
            };
            return new ReadingHolder(reading);
        }

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var readingJson = DamlLfJsonDecoders.RequireField(json, context, "reading");
            var readingContext = context.Field("reading");
            var tag = DamlLfJsonDecoders.ReadVariantTag(readingJson, readingContext);
            var valueJson = DamlLfJsonDecoders.RequireVariantValue(readingJson, readingContext);
            var valueContext = readingContext.Field("value");
            DamlValue payload = tag switch
            {
                "Measured" => DamlLfJsonDecoders.ReadNumeric(valueJson, valueContext),
                _ => throw DamlLfJsonDecoders.UnknownConstructor(
                    "variant constructor", tag, readingContext, ["Measured"])
            };
            return DamlRecord.Create(DamlField.Create("reading", DamlVariant.Create(tag, payload)));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_variant_arm_carrying_a_scalar_payload()
    {
        var record = DamlLfJsonReader.ReadRecord<ReadingHolder>("""{"reading":{"tag":"Measured","value":"1.25"}}""");

        var variant = record.GetRequiredField("reading").Should().BeOfType<DamlVariant>().Which;
        variant.Constructor.Should().Be("Measured");
        variant.Value.Should().BeOfType<DamlNumeric>().Which.Value.Should().Be(1.25m);
    }

    public abstract record Shape : IDamlVariant
    {
        public abstract string Tag { get; }

        public abstract DamlVariant ToVariant();

        public sealed record Shape_(string Value) : Shape
        {
            public override string Tag => "Shape";

            public override DamlVariant ToVariant() => DamlVariant.Create("Shape", new DamlText(Value));
        }

        public sealed record Blank : Shape
        {
            public override string Tag => "Blank";

            public override DamlVariant ToVariant() => DamlVariant.Create("Blank", DamlUnit.Instance);
        }
    }

    public sealed record ShapeHolder([property: DamlFieldAttribute("shape")] Shape Shape) : IDamlRecord<ShapeHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("shape", Shape.ToVariant()));

        public static ShapeHolder FromRecord(DamlRecord record)
        {
            var variant = record.GetRequiredField("shape").As<DamlVariant>();
            Shape shape = variant.Constructor switch
            {
                "Shape" => new Shape.Shape_(variant.Value.As<DamlText>().Value),
                "Blank" => new Shape.Blank(),
                _ => throw new ArgumentOutOfRangeException(nameof(record), variant.Constructor, null)
            };
            return new ShapeHolder(shape);
        }

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var shapeJson = DamlLfJsonDecoders.RequireField(json, context, "shape");
            var shapeContext = context.Field("shape");
            var tag = DamlLfJsonDecoders.ReadVariantTag(shapeJson, shapeContext);
            var valueJson = DamlLfJsonDecoders.RequireVariantValue(shapeJson, shapeContext);
            var valueContext = shapeContext.Field("value");
            DamlValue payload = tag switch
            {
                "Shape" => DamlLfJsonDecoders.ReadText(valueJson, valueContext),
                "Blank" => DamlLfJsonDecoders.ReadUnit(valueJson, valueContext),
                _ => throw DamlLfJsonDecoders.UnknownConstructor(
                    "variant constructor", tag, shapeContext, ["Blank", "Shape"])
            };
            return DamlRecord.Create(DamlField.Create("shape", DamlVariant.Create(tag, payload)));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_variant_arm_whose_csharp_name_was_disambiguated_from_its_wire_tag()
    {
        var record = DamlLfJsonReader.ReadRecord<ShapeHolder>("""{"shape":{"tag":"Shape","value":"round"}}""");

        var variant = record.GetRequiredField("shape").Should().BeOfType<DamlVariant>().Which;
        variant.Constructor.Should().Be("Shape");
        variant.Value.Should().BeOfType<DamlText>().Which.Value.Should().Be("round");
    }

    [Fact]
    public void ReadRecord_should_list_wire_tags_rather_than_csharp_names_for_an_unknown_variant_constructor()
    {
        var act = () => DamlLfJsonReader.ReadRecord<ShapeHolder>("""{"shape":{"tag":"Round","value":{}}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Unknown Daml variant constructor 'Round' at 'ShapeHolder.shape'; "
                + "expected one of Blank, Shape");
    }

    public sealed record DirectionHolder(
        [property: DamlFieldAttribute("direction")] Direction Direction) : IDamlRecord<DirectionHolder>
    {
        private static readonly IReadOnlyDictionary<string, Direction> ByConstructor = Enum.GetValues<Direction>()
            .ToDictionary(value => value.ToDamlEnum().Constructor);

        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("direction", Direction.ToDamlEnum()));

        public static DirectionHolder FromRecord(DamlRecord record) =>
            new(ByConstructor[record.GetRequiredField("direction").As<DamlEnum>().Constructor]);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var directionJson = DamlLfJsonDecoders.RequireField(json, context, "direction");
            var directionContext = context.Field("direction");
            var direction = DirectionExtensions.__ReadDamlLfJson(directionJson, directionContext);
            return DamlRecord.Create(DamlField.Create("direction", direction));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_an_enum_constructor_whose_wire_name_differs_from_its_csharp_member()
    {
        var record = DamlLfJsonReader.ReadRecord<DirectionHolder>("""{"direction":"U$u0020Turn"}""");

        record.GetRequiredField("direction").Should().BeOfType<DamlEnum>()
            .Which.Should().Be(DamlEnum.Create("U$u0020Turn"));
    }

    [Fact]
    public void ReadRecord_should_decode_an_enum_constructor_whose_wire_name_survives_sanitization()
    {
        var record = DamlLfJsonReader.ReadRecord<DirectionHolder>("""{"direction":"Forward"}""");

        record.GetRequiredField("direction").Should().BeOfType<DamlEnum>()
            .Which.Should().Be(DamlEnum.Create("Forward"));
    }

    [Fact]
    public void ReadRecord_should_list_wire_constructors_rather_than_csharp_members_for_an_unknown_enum_constructor()
    {
        var act = () => DamlLfJsonReader.ReadRecord<DirectionHolder>("""{"direction":"Sideways"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Unknown Daml enum constructor 'Sideways' at 'DirectionHolder.direction'; "
                + "expected one of Forward, U$u0020Turn");
    }

    private static DamlVariant ReadTopLevelVariant<T>(string json)
        where T : IDamlVariant<T>
    {
        using var document = JsonDocument.Parse(json);
        return DamlLfJsonDecoders.ReadVariant<T>(document.RootElement, DamlLfJsonDecodeContext.Root(typeof(T).Name));
    }

    [Fact]
    public void ReadVariant_should_decode_a_top_level_variant_arm_with_its_record_payload()
    {
        var variant = ReadTopLevelVariant<Outcome>("""{"tag":"Win","value":{"prize":"1.25","tier":"gold"}}""");

        variant.Constructor.Should().Be("Win");
        variant.Value.Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("prize", new DamlNumeric(1.25m)),
            new DamlField("tier", new DamlText("gold")));
    }

    [Fact]
    public void ReadVariant_should_decode_a_top_level_nullary_variant_arm_from_its_empty_object_value()
    {
        var variant = ReadTopLevelVariant<Outcome>("""{"tag":"Pending","value":{}}""");

        variant.Constructor.Should().Be("Pending");
        variant.Value.Should().BeSameAs(DamlUnit.Instance);
    }

    [Fact]
    public void ReadVariant_should_reject_an_unknown_top_level_variant_constructor()
    {
        var act = () => ReadTopLevelVariant<Outcome>("""{"tag":"Draw","value":{}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Unknown Daml variant constructor 'Draw' at 'Outcome'; expected one of Pending, Win");
    }

    [Fact]
    public void ReadVariant_should_reject_a_top_level_variant_missing_its_tag()
    {
        var act = () => ReadTopLevelVariant<Outcome>("""{"value":{}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml variant field 'Outcome.tag' is missing from the JSON object");
    }

    [Fact]
    public void ReadVariant_should_name_the_payload_path_of_a_top_level_variant_arm()
    {
        var act = () => ReadTopLevelVariant<Outcome>("""{"tag":"Win","value":{"prize":"1.25","tier":42}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON String at 'Outcome.value.tier' but found Number");
    }

    public abstract record Scribble : IDamlVariant<Scribble>
    {
        private static readonly string[] ExpectedConstructors = ["Scrawled"];

        public abstract string Tag { get; }

        public abstract DamlVariant ToVariant();

        public static DamlVariant __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            var tag = DamlLfJsonDecoders.ReadVariantTag(json, context);
            return tag switch
            {
                "Scrawled" => DamlVariant.Create("Scrawled", DamlLfJsonDecoders.ReadOptional(
                    DamlLfJsonDecoders.RequireVariantValue(json, context),
                    context.Field("value"),
                    DamlLfJsonDecoders.ReadText)),
                _ => throw DamlLfJsonDecoders.UnknownConstructor(
                    "variant constructor", tag, context, ExpectedConstructors)
            };
        }

        public sealed record Scrawled(string? Value) : Scribble
        {
            public override string Tag => "Scrawled";

            public override DamlVariant ToVariant() => DamlVariant.Create(
                "Scrawled",
                Value is null ? DamlOptional.None : DamlOptional.Some(new DamlText(Value)));
        }
    }

    [Fact]
    public void ReadVariant_should_keep_a_top_level_variant_arm_payload_optional_when_the_arm_carries_an_optional()
    {
        ReadTopLevelVariant<Scribble>("""{"tag":"Scrawled","value":null}""")
            .Value.Should().Be(DamlOptional.None);

        ReadTopLevelVariant<Scribble>("""{"tag":"Scrawled","value":"ink"}""")
            .Value.Should().Be(DamlOptional.Some(new DamlText("ink")));
    }
}

public enum Direction
{
    Forward,
    U_u0020Turn
}

public static class DirectionExtensions
{
    private static readonly string[] ExpectedConstructors = ["Forward", "U$u0020Turn"];

    public static DamlEnum ToDamlEnum(this Direction value) =>
        value switch
        {
            Direction.Forward => DamlEnum.Create("Forward"),
            Direction.U_u0020Turn => DamlEnum.Create("U$u0020Turn"),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };

    public static DamlEnum __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
        DamlLfJsonDecoders.ReadEnumConstructor(json, context, ExpectedConstructors);
}
