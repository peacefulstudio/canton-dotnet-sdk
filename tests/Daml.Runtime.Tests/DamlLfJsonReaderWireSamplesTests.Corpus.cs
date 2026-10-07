// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using Xunit;

namespace Daml.Runtime.Tests;

public partial class DamlLfJsonReaderWireSamplesTests
{
    private const string WireTypesPackageId = "f7230672ba7d77a5adba89f092d50b7c957d0eb5030c1e4f96706dcca93f2200";
    private const string WireTypesPackageName = "wiretypes23";
    private const string WireTypesModuleName = "WireTypes";

    internal static readonly string CorpusDirectory =
        Path.Combine(AppContext.BaseDirectory, "wire-samples", "data");

    internal static JsonDocument LoadWireSample(string fileName) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(CorpusDirectory, fileName)));

    internal static JsonElement ResolvePayload(JsonElement root, string payloadPath)
    {
        var current = root;
        foreach (var segment in payloadPath.Split('/'))
        {
            current = int.TryParse(segment, out var index) ? current[index] : current.GetProperty(segment);
        }
        return current;
    }

    private static readonly (string Label, Type DamlType)[] WireRecordFields =
    [
        ("owner", typeof(DamlParty)),
        ("count", typeof(DamlInt64)),
        ("amount", typeof(DamlNumeric)),
        ("label", typeof(DamlText)),
        ("active", typeof(DamlBool)),
        ("asOf", typeof(DamlDate)),
        ("observedAt", typeof(DamlTimestamp)),
        ("note", typeof(DamlOptional)),
        ("nestedNote", typeof(DamlOptionalChain)),
        ("tags", typeof(DamlList)),
        ("attributes", typeof(DamlTextMap)),
        ("genMap", typeof(DamlGenMap)),
        ("unitField", typeof(DamlUnit)),
        ("marker", typeof(DamlContractId)),
        ("holdingCid", typeof(DamlContractId)),
        ("holdingCids", typeof(DamlList)),
        ("profile", typeof(DamlRecord)),
        ("outcome", typeof(DamlVariant)),
        ("suit", typeof(DamlEnum)),
        ("fee", typeof(DamlNumeric)),
    ];

    public static TheoryData<string, string, string, (string Label, Type DamlType)[]> SupportedWireSamplePayloads => new()
    {
        { "create_marker.json", "response/transaction/events/0/CreatedEvent/createArgument", nameof(Marker), [("owner", typeof(DamlParty))] },
        { "acs_wildcard_verbose_false.json", "response/0/contractEntry/JsActiveContract/createdEvent/createArgument", nameof(Marker), [("owner", typeof(DamlParty))] },
        { "acs_wildcard_verbose_true.json", "response/0/contractEntry/JsActiveContract/createdEvent/createArgument", nameof(Marker), [("owner", typeof(DamlParty))] },
        { "exercise_ping.json", "response/transaction/events/0/ExercisedEvent/choiceArgument", nameof(Ping), [] },
        { "create_asset_numeric_edges.json", "response/transaction/events/0/CreatedEvent/createArgument", nameof(Asset), [("issuer", typeof(DamlParty)), ("amount", typeof(DamlNumeric))] },
        { "create_keyed_contract_key.json", "response/transaction/events/0/CreatedEvent/createArgument", nameof(Keyed), [("owner", typeof(DamlParty)), ("label", typeof(DamlText))] },
        { "create_keyed_contract_key.json", "response/transaction/events/0/CreatedEvent/contractKey", nameof(KeyedKey), [("_1", typeof(DamlParty)), ("_2", typeof(DamlText))] },
        { "create_wirerecord_empty.json", "response/transaction/events/0/CreatedEvent/createArgument", nameof(WireRecord), WireRecordFields },
        { "create_wirerecord_populated.json", "response/transaction/events/0/CreatedEvent/createArgument", nameof(WireRecord), WireRecordFields },
        { "exercise_describe.json", "response/transaction/events/0/ExercisedEvent/choiceArgument", nameof(Describe), [("probe", typeof(DamlOptional))] },
        { "exercise_relabel.json", "response/transaction/events/0/ExercisedEvent/choiceArgument", nameof(Relabel), [("newLabel", typeof(DamlText))] },
        { "acs_interface_view_holding.json", "response/0/contractEntry/JsActiveContract/createdEvent/interfaceViews/0/viewValue", nameof(HoldingView), [("amount", typeof(DamlNumeric))] },
    };

    internal const string NestedOptionalProbe = "probe_nested_optional_matrix.json";

    public static TheoryData<string, string, string> DecodedWireSamplePayloads
    {
        get
        {
            var payloads = new TheoryData<string, string, string>();
            foreach (ITheoryDataRow row in SupportedWireSamplePayloads)
            {
                var data = row.GetData();
                payloads.Add((string)data[0]!, (string)data[1]!, (string)data[2]!);
            }
            return payloads;
        }
    }

    public static TheoryData<string> AcceptedNestedOptionalCandidates
    {
        get
        {
            using var document = LoadWireSample(NestedOptionalProbe);
            var candidates = new TheoryData<string>();
            foreach (var candidate in document.RootElement.GetProperty("response").EnumerateObject())
            {
                if (candidate.Value.GetProperty("accepted").GetBoolean())
                    candidates.Add(candidate.Name);
            }
            return candidates;
        }
    }

    internal static DamlOptionalChain ReadOptionalOptionalText(JsonElement json) =>
        DamlLfJsonDecoders.ReadOptionalChain(
            json,
            DamlLfJsonDecodeContext.Root("nestedNote"),
            (outer, outerContext) => DamlLfJsonDecoders.ReadOptionalChain(
                outer, outerContext, (inner, innerContext) => DamlLfJsonDecoders.ReadText(inner, innerContext)));

    internal static readonly IReadOnlyDictionary<string, Func<JsonElement, DamlRecord>> DeclaredShapes =
        new Dictionary<string, Func<JsonElement, DamlRecord>>
        {
            [nameof(Marker)] = json => DamlLfJsonReader.ReadRecord<Marker>(json),
            [nameof(Ping)] = json => DamlLfJsonReader.ReadRecord<Ping>(json),
            [nameof(Asset)] = json => DamlLfJsonReader.ReadRecord<Asset>(json),
            [nameof(Keyed)] = json => DamlLfJsonReader.ReadRecord<Keyed>(json),
            [nameof(KeyedKey)] = json => DamlLfJsonReader.ReadRecord<KeyedKey>(json),
            [nameof(WireRecord)] = json => DamlLfJsonReader.ReadRecord<WireRecord>(json),
            [nameof(Relabel)] = json => DamlLfJsonReader.ReadRecord<Relabel>(json),
            [nameof(Describe)] = json => DamlLfJsonReader.ReadRecord<Describe>(json),
            [nameof(HoldingView)] = json => DamlLfJsonReader.ReadRecord<HoldingView>(json),
        };

    private static NotSupportedException DecodeOnlyShape() =>
        new("Wire-sample shapes are decode-only stand-ins for the corpus's generated types.");

    public sealed record Marker([property: DamlFieldAttribute("owner")] Party Owner) : IDamlRecord<Marker>, IDamlType
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier(WireTypesPackageId, WireTypesModuleName, nameof(Marker)),
            DamlTypeKind.Template,
            WireTypesPackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("owner", Owner.ToDamlValue()));

        public static Marker FromRecord(DamlRecord record) => throw DecodeOnlyShape();

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(
                DamlField.Create("owner", DamlLfJsonDecoders.ReadParty(
                    DamlLfJsonDecoders.RequireField(json, context, "owner"), context.Field("owner"))));
        }
    }

    public sealed record Ping : IDamlRecord<Ping>
    {
        public DamlRecord ToRecord() => DamlRecord.Create();

        public static Ping FromRecord(DamlRecord record) => throw DecodeOnlyShape();

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create();
        }
    }

    public interface IHolding : IDamlType
    {
        static DamlTypeDescriptor IDamlType.DamlTypeId =>
            new(new Identifier(WireTypesPackageId, WireTypesModuleName, "Holding"),
                DamlTypeKind.Interface,
                WireTypesPackageName);
    }

    public sealed record Asset(
        [property: DamlFieldAttribute("issuer")] Party Issuer,
        [property: DamlFieldAttribute("amount")] decimal Amount) : IDamlRecord<Asset>
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();

        public static Asset FromRecord(DamlRecord record) => throw DecodeOnlyShape();

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(
                DamlField.Create("issuer", DamlLfJsonDecoders.ReadParty(
                    DamlLfJsonDecoders.RequireField(json, context, "issuer"), context.Field("issuer"))),
                DamlField.Create("amount", DamlLfJsonDecoders.ReadNumeric(
                    DamlLfJsonDecoders.RequireField(json, context, "amount"), context.Field("amount"))));
        }
    }

    public sealed record Keyed(
        [property: DamlFieldAttribute("owner")] Party Owner,
        [property: DamlFieldAttribute("label")] string Label) : IDamlRecord<Keyed>
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();

        public static Keyed FromRecord(DamlRecord record) => throw DecodeOnlyShape();

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(
                DamlField.Create("owner", DamlLfJsonDecoders.ReadParty(
                    DamlLfJsonDecoders.RequireField(json, context, "owner"), context.Field("owner"))),
                DamlField.Create("label", DamlLfJsonDecoders.ReadText(
                    DamlLfJsonDecoders.RequireField(json, context, "label"), context.Field("label"))));
        }
    }

    public sealed record KeyedKey(
        [property: DamlFieldAttribute("_1")] Party Maintainer,
        [property: DamlFieldAttribute("_2")] string Label) : IDamlRecord<KeyedKey>
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();

        public static KeyedKey FromRecord(DamlRecord record) => throw DecodeOnlyShape();

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(
                DamlField.Create("_1", DamlLfJsonDecoders.ReadParty(
                    DamlLfJsonDecoders.RequireField(json, context, "_1"), context.Field("_1"))),
                DamlField.Create("_2", DamlLfJsonDecoders.ReadText(
                    DamlLfJsonDecoders.RequireField(json, context, "_2"), context.Field("_2"))));
        }
    }

    public sealed record TupleKeyHolder(
        [property: DamlFieldAttribute("key")] Tuple2<Party, string> Key) : IDamlRecord<TupleKeyHolder>
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();

        public static TupleKeyHolder FromRecord(DamlRecord record) => throw DecodeOnlyShape();

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(
                DamlField.Create("key", DamlLfJsonDecoders.ReadTuple2(
                    DamlLfJsonDecoders.RequireField(json, context, "key"), context.Field("key"),
                    DamlLfJsonDecoders.ReadParty, null, DamlLfJsonDecoders.ReadText, null)));
        }
    }

    public sealed record Profile(
        [property: DamlFieldAttribute("nickname")] string Nickname,
        [property: DamlFieldAttribute("level")] long Level) : IDamlRecord<Profile>
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();

        public static Profile FromRecord(DamlRecord record) => throw DecodeOnlyShape();

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
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

    public sealed record HoldingView(
        [property: DamlFieldAttribute("amount")] decimal Amount) : IDamlRecord<HoldingView>
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();

        public static HoldingView FromRecord(DamlRecord record) => throw DecodeOnlyShape();

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(
                DamlField.Create("amount", DamlLfJsonDecoders.ReadNumeric(
                    DamlLfJsonDecoders.RequireField(json, context, "amount"), context.Field("amount"))));
        }
    }

    public sealed record WinDetails(
        [property: DamlFieldAttribute("prize")] decimal Prize,
        [property: DamlFieldAttribute("tier")] string Tier) : IDamlRecord<WinDetails>
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();

        public static WinDetails FromRecord(DamlRecord record) => throw DecodeOnlyShape();

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
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
        public DamlVariant ToVariant() => throw DecodeOnlyShape();

        public sealed record Win(WinDetails Details) : Outcome
        {
            public string Tag => "Win";
        }

        public sealed record Pending : Outcome
        {
            public string Tag => "Pending";
        }

        private static readonly string[] ExpectedConstructors = ["Win", "Pending"];

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static DamlVariant __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            var tag = DamlLfJsonDecoders.ReadVariantTag(json, context);
            return tag switch
            {
                "Win" => DamlVariant.Create("Win", WinDetails.__ReadDamlLfJson(
                    DamlLfJsonDecoders.RequireVariantValue(json, context), context.Field("value"))),
                "Pending" => DamlVariant.Create("Pending", DamlLfJsonDecoders.ReadUnit(
                    DamlLfJsonDecoders.RequireVariantValue(json, context), context.Field("value"))),
                _ => throw DamlLfJsonDecoders.UnknownConstructor("variant constructor", tag, context, ExpectedConstructors),
            };
        }
    }

    public enum Suit
    {
        Clubs,
        Diamonds,
        Hearts,
        Spades,
    }

    public static class SuitExtensions
    {
        private static readonly string[] ExpectedConstructors = ["Clubs", "Diamonds", "Hearts", "Spades"];

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static DamlEnum __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            DamlLfJsonDecoders.ReadEnumConstructor(json, context, ExpectedConstructors);
    }

    public sealed record WireRecord(
        [property: DamlFieldAttribute("owner")] Party Owner,
        [property: DamlFieldAttribute("count")] long Count,
        [property: DamlFieldAttribute("amount")] decimal Amount,
        [property: DamlFieldAttribute("label")] string Label,
        [property: DamlFieldAttribute("active")] bool Active,
        [property: DamlFieldAttribute("asOf")] DateOnly AsOf,
        [property: DamlFieldAttribute("observedAt")] DateTimeOffset ObservedAt,
        [property: DamlFieldAttribute("note")] string? Note,
        [property: DamlFieldAttribute("nestedNote")] Optional<Optional<string>> NestedNote,
        [property: DamlFieldAttribute("tags")] IReadOnlyList<string> Tags,
        [property: DamlFieldAttribute("attributes")] IReadOnlyDictionary<string, string> Attributes,
        [property: DamlFieldAttribute("genMap")] IReadOnlyDictionary<Party, long> GenMap,
        [property: DamlFieldAttribute("unitField")] DamlUnit UnitField,
        [property: DamlFieldAttribute("marker")] ContractId<Marker> Marker,
        [property: DamlFieldAttribute("holdingCid")] ContractId<IHolding> HoldingCid,
        [property: DamlFieldAttribute("holdingCids")] IReadOnlyList<ContractId<IHolding>> HoldingCids,
        [property: DamlFieldAttribute("profile")] Profile Profile,
        [property: DamlFieldAttribute("outcome")] Outcome Outcome,
        [property: DamlFieldAttribute("suit")] Suit Suit,
        [property: DamlFieldAttribute("fee")] decimal Fee) : IDamlRecord<WireRecord>
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();

        public static WireRecord FromRecord(DamlRecord record) => throw DecodeOnlyShape();

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(
                DamlField.Create("owner", DamlLfJsonDecoders.ReadParty(
                    DamlLfJsonDecoders.RequireField(json, context, "owner"), context.Field("owner"))),
                DamlField.Create("count", DamlLfJsonDecoders.ReadInt64(
                    DamlLfJsonDecoders.RequireField(json, context, "count"), context.Field("count"))),
                DamlField.Create("amount", DamlLfJsonDecoders.ReadNumeric(
                    DamlLfJsonDecoders.RequireField(json, context, "amount"), context.Field("amount"))),
                DamlField.Create("label", DamlLfJsonDecoders.ReadText(
                    DamlLfJsonDecoders.RequireField(json, context, "label"), context.Field("label"))),
                DamlField.Create("active", DamlLfJsonDecoders.ReadBool(
                    DamlLfJsonDecoders.RequireField(json, context, "active"), context.Field("active"))),
                DamlField.Create("asOf", DamlLfJsonDecoders.ReadDate(
                    DamlLfJsonDecoders.RequireField(json, context, "asOf"), context.Field("asOf"))),
                DamlField.Create("observedAt", DamlLfJsonDecoders.ReadTimestamp(
                    DamlLfJsonDecoders.RequireField(json, context, "observedAt"), context.Field("observedAt"))),
                DamlField.Create("note", DamlLfJsonDecoders.ReadOptional(
                    DamlLfJsonDecoders.RequireField(json, context, "note"), context.Field("note"),
                    DamlLfJsonDecoders.ReadText)),
                DamlField.Create("nestedNote", DamlLfJsonDecoders.ReadOptionalChain(
                    DamlLfJsonDecoders.RequireField(json, context, "nestedNote"), context.Field("nestedNote"),
                    (inner, innerContext) => DamlLfJsonDecoders.ReadOptionalChain(inner, innerContext, DamlLfJsonDecoders.ReadText))),
                DamlField.Create("tags", DamlLfJsonDecoders.ReadList(
                    DamlLfJsonDecoders.RequireField(json, context, "tags"), context.Field("tags"),
                    DamlLfJsonDecoders.ReadText)),
                DamlField.Create("attributes", DamlLfJsonDecoders.ReadTextMap(
                    DamlLfJsonDecoders.RequireField(json, context, "attributes"), context.Field("attributes"),
                    DamlLfJsonDecoders.ReadText)),
                DamlField.Create("genMap", DamlLfJsonDecoders.ReadGenMap(
                    DamlLfJsonDecoders.RequireField(json, context, "genMap"), context.Field("genMap"),
                    DamlLfJsonDecoders.ReadParty, DamlLfJsonDecoders.ReadInt64)),
                DamlField.Create("unitField", DamlLfJsonDecoders.ReadUnit(
                    DamlLfJsonDecoders.RequireField(json, context, "unitField"), context.Field("unitField"))),
                DamlField.Create("marker", DamlLfJsonDecoders.ReadContractId(
                    DamlLfJsonDecoders.RequireField(json, context, "marker"), context.Field("marker"))),
                DamlField.Create("holdingCid", DamlLfJsonDecoders.ReadContractId(
                    DamlLfJsonDecoders.RequireField(json, context, "holdingCid"), context.Field("holdingCid"))),
                DamlField.Create("holdingCids", DamlLfJsonDecoders.ReadList(
                    DamlLfJsonDecoders.RequireField(json, context, "holdingCids"), context.Field("holdingCids"),
                    DamlLfJsonDecoders.ReadContractId)),
                DamlField.Create("profile", Profile.__ReadDamlLfJson(
                    DamlLfJsonDecoders.RequireField(json, context, "profile"), context.Field("profile"))),
                DamlField.Create("outcome", Outcome.__ReadDamlLfJson(
                    DamlLfJsonDecoders.RequireField(json, context, "outcome"), context.Field("outcome"))),
                DamlField.Create("suit", SuitExtensions.__ReadDamlLfJson(
                    DamlLfJsonDecoders.RequireField(json, context, "suit"), context.Field("suit"))),
                DamlField.Create("fee", DamlLfJsonDecoders.ReadNumeric(
                    DamlLfJsonDecoders.RequireField(json, context, "fee"), context.Field("fee"))));
        }
    }

    public sealed record Relabel(
        [property: DamlFieldAttribute("newLabel")] string NewLabel) : IDamlRecord<Relabel>
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();

        public static Relabel FromRecord(DamlRecord record) => throw DecodeOnlyShape();

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(
                DamlField.Create("newLabel", DamlLfJsonDecoders.ReadText(
                    DamlLfJsonDecoders.RequireField(json, context, "newLabel"), context.Field("newLabel"))));
        }
    }

    public sealed record Describe(
        [property: DamlFieldAttribute("probe")] long? Probe) : IDamlRecord<Describe>
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();

        public static Describe FromRecord(DamlRecord record) => throw DecodeOnlyShape();

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(
                DamlField.Create("probe", DamlLfJsonDecoders.ReadOptional(
                    DamlLfJsonDecoders.RequireField(json, context, "probe"), context.Field("probe"),
                    DamlLfJsonDecoders.ReadInt64)));
        }
    }

    public sealed record NestedNoteHolder(
        [property: DamlFieldAttribute("nestedNote")] Optional<Optional<string>> NestedNote) : IDamlRecord<NestedNoteHolder>
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();

        public static NestedNoteHolder FromRecord(DamlRecord record) => throw DecodeOnlyShape();

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(
                DamlField.Create("nestedNote", DamlLfJsonDecoders.ReadOptionalChain(
                    DamlLfJsonDecoders.RequireField(json, context, "nestedNote"), context.Field("nestedNote"),
                    (inner, innerContext) => DamlLfJsonDecoders.ReadOptionalChain(inner, innerContext, DamlLfJsonDecoders.ReadText))));
        }
    }

    public sealed record DeepNoteHolder(
        [property: DamlFieldAttribute("deepNote")] Optional<Optional<Optional<string>>> DeepNote) : IDamlRecord<DeepNoteHolder>
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();

        public static DeepNoteHolder FromRecord(DamlRecord record) => throw DecodeOnlyShape();

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(
                DamlField.Create("deepNote", DamlLfJsonDecoders.ReadOptionalChain(
                    DamlLfJsonDecoders.RequireField(json, context, "deepNote"), context.Field("deepNote"),
                    (outer, outerContext) => DamlLfJsonDecoders.ReadOptionalChain(outer, outerContext,
                        (inner, innerContext) => DamlLfJsonDecoders.ReadOptionalChain(inner, innerContext, DamlLfJsonDecoders.ReadText)))));
        }
    }

    public sealed record FlatNoteHolder(
        [property: DamlFieldAttribute("note")] Optional<string> Note) : IDamlRecord<FlatNoteHolder>
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();

        public static FlatNoteHolder FromRecord(DamlRecord record) => throw DecodeOnlyShape();

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(
                DamlField.Create("note", DamlLfJsonDecoders.ReadOptional(
                    DamlLfJsonDecoders.RequireField(json, context, "note"), context.Field("note"),
                    DamlLfJsonDecoders.ReadText)));
        }
    }

    [SuppressMessage(
        "Design",
        "CA1000:Do not declare static members on generic types",
        Justification = "Mirrors the generated emitted-style decoder for a generic record (see Box<TA> in src/Daml.Codegen.Testing.Conformance/Generated/RichTypes/Box.cs), which every consumer of a generic record's __ReadDamlLfJson calls the same way.")]
    public sealed record Boxed<TA>(
        [property: DamlFieldAttribute("item")] TA Item) : IDamlRecord
        where TA : notnull
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context, DamlLfElementReader readItem)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(
                DamlField.Create("item", readItem(
                    DamlLfJsonDecoders.RequireField(json, context, "item"), context.Field("item"))));
        }
    }

    public sealed record BoxedNoteHolder(
        [property: DamlFieldAttribute("boxed")] Boxed<Optional<string>> Boxed) : IDamlRecord<BoxedNoteHolder>
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();

        public static BoxedNoteHolder FromRecord(DamlRecord record) => throw DecodeOnlyShape();

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(
                DamlField.Create("boxed", Boxed<Optional<string>>.__ReadDamlLfJson(
                    DamlLfJsonDecoders.RequireField(json, context, "boxed"), context.Field("boxed"),
                    (element, elementContext) => DamlLfJsonDecoders.ReadOptional(element, elementContext, DamlLfJsonDecoders.ReadText))));
        }
    }
}
