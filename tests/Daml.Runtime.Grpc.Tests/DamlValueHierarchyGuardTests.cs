// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;

namespace Daml.Runtime.Grpc.Tests;

public class DamlValueHierarchyGuardTests
{
    private sealed record Representative(
        Func<DamlValue> Build,
        string? ExpectedJson,
        ProtoValue.SumOneofCase? ExpectedProtoCase);

    private static readonly Identifier SampleId = new("pkg", "Mod", "Sample");

    private static readonly IReadOnlyDictionary<Type, Representative> Representatives =
        new Dictionary<Type, Representative>
        {
            [typeof(DamlUnit)] = new(() => DamlUnit.Instance, "{}", ProtoValue.SumOneofCase.Unit),
            [typeof(DamlBool)] = new(() => new DamlBool(true), "true", ProtoValue.SumOneofCase.Bool),
            [typeof(DamlInt64)] = new(() => new DamlInt64(7), "\"7\"", ProtoValue.SumOneofCase.Int64),
            [typeof(DamlText)] = new(() => new DamlText("hello"), "\"hello\"", ProtoValue.SumOneofCase.Text),
            [typeof(DamlParty)] = new(() => new DamlParty("Alice::1220"), "\"Alice::1220\"", ProtoValue.SumOneofCase.Party),
            [typeof(DamlNumeric)] = new(() => new DamlNumeric(1.5m), "\"1.5\"", ProtoValue.SumOneofCase.Numeric),
            [typeof(DamlDate)] = new(() => new DamlDate(new DateOnly(2026, 1, 2)), "\"2026-01-02\"", ProtoValue.SumOneofCase.Date),
            [typeof(DamlTimestamp)] = new(
                () => new DamlTimestamp(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)),
                "\"2026-01-02T03:04:05Z\"",
                ProtoValue.SumOneofCase.Timestamp),
            [typeof(DamlContractId)] = new(() => new DamlContractId("cid-1"), "\"cid-1\"", ProtoValue.SumOneofCase.ContractId),
            [typeof(DamlRecord)] = new(
                () => DamlRecord.Create(DamlField.Create("n", new DamlInt64(1))),
                "{\"n\":\"1\"}",
                ProtoValue.SumOneofCase.Record),
            [typeof(DamlVariant)] = new(
                () => DamlVariant.Create("Some", new DamlInt64(1)),
                "{\"tag\":\"Some\",\"value\":\"1\"}",
                ProtoValue.SumOneofCase.Variant),
            [typeof(DamlEnum)] = new(() => new DamlEnum(SampleId, "Red"), "\"Red\"", ProtoValue.SumOneofCase.Enum),
            [typeof(DamlList)] = new(() => DamlList.Create(new DamlInt64(1)), "[\"1\"]", ProtoValue.SumOneofCase.List),
            [typeof(DamlOptional)] = new(() => new DamlOptional(new DamlInt64(1)), "\"1\"", ProtoValue.SumOneofCase.Optional),
            [typeof(DamlOptionalChain)] = new(
                () => DamlOptionalChain.Some(new DamlInt64(1)),
                "[\"1\"]",
                ProtoValue.SumOneofCase.Optional),
            [typeof(DamlTextMap)] = new(
                () => DamlTextMap.Create(("k", new DamlInt64(1))),
                "{\"k\":\"1\"}",
                ProtoValue.SumOneofCase.TextMap),
            [typeof(DamlGenMap)] = new(
                () => DamlGenMap.Create((new DamlInt64(1), new DamlText("v"))),
                "[[\"1\",\"v\"]]",
                ProtoValue.SumOneofCase.GenMap),
            [typeof(DamlUndecodedJson)] = new(() => new DamlUndecodedJson("{\"a\":1}"), null, null),
        };

    private static IEnumerable<Type> ConcreteDamlValueSubtypes() =>
        typeof(DamlValue).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false } && typeof(DamlValue).IsAssignableFrom(t));

    public static IEnumerable<object[]> EveryDamlValueSubtype() =>
        Representatives.Keys.Select(type => new object[] { type });

    public static IEnumerable<object[]> EverySerializableDamlValueSubtype() =>
        Representatives.Where(entry => entry.Value.ExpectedJson is not null).Select(entry => new object[] { entry.Key });

    public static IEnumerable<object[]> EveryProtoConvertibleDamlValueSubtype() =>
        Representatives.Where(entry => entry.Value.ExpectedProtoCase is not null).Select(entry => new object[] { entry.Key });

    [Fact]
    public void DamlValueHierarchy_guard_covers_every_concrete_subtype_discovered_by_reflection()
    {
        ConcreteDamlValueSubtypes()
            .Should().BeEquivalentTo(Representatives.Keys,
                "every concrete DamlValue subtype needs a representative so the hierarchy guard exercises it");
    }

    [Theory]
    [MemberData(nameof(EveryDamlValueSubtype))]
    public void DamlValueHierarchy_representative_is_an_instance_of_its_subtype(Type subtype)
    {
        Representatives[subtype].Build().Should().BeOfType(subtype);
    }

    [Theory]
    [MemberData(nameof(EveryDamlValueSubtype))]
    public void DamlValueHierarchy_two_independently_built_instances_are_equal_with_equal_hash_codes(Type subtype)
    {
        var left = Representatives[subtype].Build();
        var right = Representatives[subtype].Build();

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Theory]
    [MemberData(nameof(EverySerializableDamlValueSubtype))]
    public void DamlValueHierarchy_serializer_writes_the_pinned_daml_lf_json(Type subtype)
    {
        DamlJsonSerializer.Serialize(Representatives[subtype].Build())
            .Should().Be(Representatives[subtype].ExpectedJson);
    }

    [Fact]
    public void DamlValueHierarchy_serializer_rejects_every_subtype_without_a_pinned_json()
    {
        var unserializable = Representatives.Where(entry => entry.Value.ExpectedJson is null).Select(entry => entry.Key).ToList();

        unserializable.Should().BeEquivalentTo([typeof(DamlUndecodedJson)]);
        foreach (var subtype in unserializable)
        {
            var serialize = () => DamlJsonSerializer.Serialize(Representatives[subtype].Build());
            serialize.Should().Throw<JsonException>();
        }
    }

    [Theory]
    [MemberData(nameof(EveryProtoConvertibleDamlValueSubtype))]
    public void DamlValueHierarchy_proto_converter_emits_the_pinned_value_case(Type subtype)
    {
        DamlValueConverter.ToProtoValue(Representatives[subtype].Build()).SumCase
            .Should().Be(Representatives[subtype].ExpectedProtoCase);
    }

    [Fact]
    public void DamlValueHierarchy_proto_converter_rejects_every_subtype_without_a_pinned_value_case()
    {
        var unconvertible = Representatives.Where(entry => entry.Value.ExpectedProtoCase is null).Select(entry => entry.Key).ToList();

        unconvertible.Should().BeEquivalentTo([typeof(DamlUndecodedJson)]);
        foreach (var subtype in unconvertible)
        {
            var convert = () => DamlValueConverter.ToProtoValue(Representatives[subtype].Build());
            convert.Should().Throw<NotSupportedException>();
        }
    }
}
