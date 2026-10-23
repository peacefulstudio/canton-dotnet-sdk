// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// The participant captures under <c>conformance/&lt;corpus&gt;/wire/</c>, and the generated
/// readers each payload in them is decoded with.
/// </summary>
public static class WireCaptures
{
    internal const string NestedOptionalProbe = "probe_nested_optional_matrix.json";

    internal static readonly string CorpusDirectory = Path.Combine(AppContext.BaseDirectory, "wire");

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

    private static readonly (string Label, Type DamlType)[] RichRecordFields =
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

    private const string MembershipKeyShape = "Membership.Key";

    private const string CreatedEvent = "response/transaction/events/0/CreatedEvent/";

    private const string ExercisedEvent = "response/transaction/events/0/ExercisedEvent/";

    private const string FirstActiveContract = "response/0/contractEntry/JsActiveContract/createdEvent/";

    public static TheoryData<string, string, string, (string Label, Type DamlType)[]> SupportedWireSamplePayloads => new()
    {
        { "create_marker.json", CreatedEvent + "createArgument", nameof(Marker), [("owner", typeof(DamlParty))] },
        { "acs_wildcard_verbose_false.json", FirstActiveContract + "createArgument", nameof(Marker), [("owner", typeof(DamlParty))] },
        { "acs_wildcard_verbose_true.json", FirstActiveContract + "createArgument", nameof(Marker), [("owner", typeof(DamlParty))] },
        { "exercise_ping.json", ExercisedEvent + "choiceArgument", nameof(RichRecord.Ping), [] },
        { "create_asset_numeric_edges.json", CreatedEvent + "createArgument", nameof(Asset), [("issuer", typeof(DamlParty)), ("amount", typeof(DamlNumeric))] },
        { "create_keyed_contract_key.json", CreatedEvent + "contractKey", MembershipKeyShape, [("_1", typeof(DamlParty)), ("_2", typeof(DamlText))] },
        { "create_wirerecord_empty.json", CreatedEvent + "createArgument", nameof(RichRecord), RichRecordFields },
        { "create_wirerecord_populated.json", CreatedEvent + "createArgument", nameof(RichRecord), RichRecordFields },
        { "exercise_describe.json", ExercisedEvent + "choiceArgument", nameof(RichRecord.DescribeOutcome), [("probe", typeof(DamlOptional))] },
        { "exercise_relabel.json", ExercisedEvent + "choiceArgument", nameof(RichRecord.Relabel), [("newLabel", typeof(DamlText))] },
        { "acs_interface_view_holding.json", FirstActiveContract + "interfaceViews/0/viewValue", nameof(HoldingView), [("amount", typeof(DamlNumeric))] },
    };

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

    public static TheoryData<string> AcceptedNestedOptionalCandidates => NestedOptionalCandidates(accepted: true);

    public static TheoryData<string> RejectedNestedOptionalCandidates => NestedOptionalCandidates(accepted: false);

    public static TheoryData<string> NestedOptionalCandidatesOnCapture => NestedOptionalCandidates(accepted: null);

    private static TheoryData<string> NestedOptionalCandidates(bool? accepted)
    {
        using var document = LoadWireSample(NestedOptionalProbe);
        var candidates = new TheoryData<string>();
        foreach (var candidate in document.RootElement.GetProperty("response").EnumerateObject())
        {
            if (accepted is null || candidate.Value.GetProperty("accepted").GetBoolean() == accepted)
                candidates.Add(candidate.Name);
        }
        return candidates;
    }

    internal static JsonElement SentNestedOptionalCandidate(JsonDocument capture, string candidate) =>
        capture.RootElement.GetProperty("response").GetProperty(candidate).GetProperty("sent");

    internal static bool ParticipantAccepted(JsonDocument capture, string candidate) =>
        capture.RootElement.GetProperty("response").GetProperty(candidate).GetProperty("accepted").GetBoolean();

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
            [nameof(RichRecord.Ping)] = json => DamlLfJsonReader.ReadRecord<RichRecord.Ping>(json),
            [nameof(Asset)] = json => DamlLfJsonReader.ReadRecord<Asset>(json),
            [MembershipKeyShape] = json =>
                (DamlRecord)Membership.Key.KeyJsonReader(json, DamlLfJsonDecodeContext.Root(MembershipKeyShape)),
            [nameof(RichRecord)] = json => DamlLfJsonReader.ReadRecord<RichRecord>(json),
            [nameof(RichRecord.Relabel)] = json => DamlLfJsonReader.ReadRecord<RichRecord.Relabel>(json),
            [nameof(RichRecord.DescribeOutcome)] = json => DamlLfJsonReader.ReadRecord<RichRecord.DescribeOutcome>(json),
            [nameof(HoldingView)] = json => DamlLfJsonReader.ReadRecord<HoldingView>(json),
        };
}
