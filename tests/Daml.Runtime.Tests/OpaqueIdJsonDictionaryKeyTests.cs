// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Text.Json;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class OpaqueIdJsonDictionaryKeyTests
{
    [Fact]
    public void Party_should_serialize_as_a_dictionary_key()
    {
        var payload = new Dictionary<Party, long> { [new Party("alice")] = 1 };

        var json = JsonSerializer.Serialize(payload);

        json.Should().Be("{\"alice\":1}");
    }

    [Fact]
    public void Party_should_deserialize_as_a_dictionary_key()
    {
        var payload = JsonSerializer.Deserialize<Dictionary<Party, long>>("{\"alice\":1}");

        payload.Should().ContainKey(new Party("alice"));
        payload![new Party("alice")].Should().Be(1);
    }

    [Fact]
    public void Party_should_round_trip_as_a_dictionary_key()
    {
        var original = new Dictionary<Party, long> { [new Party("alice")] = 1, [new Party("bob")] = 2 };

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<Dictionary<Party, long>>(json);

        roundTripped.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Party_should_throw_JsonException_when_dictionary_key_is_blank()
    {
        var deserialize = () => JsonSerializer.Deserialize<Dictionary<Party, long>>("{\"\":1}");

        deserialize.Should().Throw<JsonException>();
    }

    [Fact]
    public void Party_keyed_dictionary_should_round_trip_inside_a_record_payload()
    {
        var json = "{\"QuotaByParty\":{\"alice\":1,\"bob\":2}}";

        var payload = JsonSerializer.Deserialize<QuotaPayload>(json);

        payload!.QuotaByParty.Should().BeEquivalentTo(
            new Dictionary<Party, long> { [new Party("alice")] = 1, [new Party("bob")] = 2 });
        JsonSerializer.Serialize(payload).Should().Be(json);
    }

    [Fact]
    public void SynchronizerId_should_round_trip_as_a_dictionary_key()
    {
        var original = new Dictionary<SynchronizerId, long> { [new SynchronizerId("global_sync::abc")] = 1 };

        var json = JsonSerializer.Serialize(original);

        json.Should().Be("{\"global_sync::abc\":1}");
        JsonSerializer.Deserialize<Dictionary<SynchronizerId, long>>(json).Should().BeEquivalentTo(original);
    }

    [Fact]
    public void CommandId_should_round_trip_as_a_dictionary_key()
    {
        var original = new Dictionary<CommandId, long> { [new CommandId("cmd-1")] = 1 };

        var json = JsonSerializer.Serialize(original);

        json.Should().Be("{\"cmd-1\":1}");
        JsonSerializer.Deserialize<Dictionary<CommandId, long>>(json).Should().BeEquivalentTo(original);
    }

    [Fact]
    public void ChoiceName_should_round_trip_as_a_dictionary_key()
    {
        var original = new Dictionary<ChoiceName, long> { [new ChoiceName("Archive")] = 1 };

        var json = JsonSerializer.Serialize(original);

        json.Should().Be("{\"Archive\":1}");
        JsonSerializer.Deserialize<Dictionary<ChoiceName, long>>(json).Should().BeEquivalentTo(original);
    }

    [Fact]
    public void WorkflowId_should_round_trip_as_a_dictionary_key()
    {
        var original = new Dictionary<WorkflowId, long> { [new WorkflowId("wf-1")] = 1 };

        var json = JsonSerializer.Serialize(original);

        json.Should().Be("{\"wf-1\":1}");
        JsonSerializer.Deserialize<Dictionary<WorkflowId, long>>(json).Should().BeEquivalentTo(original);
    }

    [Fact]
    public void WorkflowId_should_permit_a_blank_dictionary_key()
    {
        var original = new Dictionary<WorkflowId, long> { [new WorkflowId("")] = 1 };

        var json = JsonSerializer.Serialize(original);

        json.Should().Be("{\"\":1}");
        JsonSerializer.Deserialize<Dictionary<WorkflowId, long>>(json).Should().BeEquivalentTo(original);
    }

    [Fact]
    public void ContractId_should_round_trip_as_a_dictionary_key()
    {
        var original = new Dictionary<ContractId<Marker>, long> { [new ContractId<Marker>("00abc")] = 1 };

        var json = JsonSerializer.Serialize(original);

        json.Should().Be("{\"00abc\":1}");
        JsonSerializer.Deserialize<Dictionary<ContractId<Marker>, long>>(json).Should().BeEquivalentTo(original);
    }

    [Fact]
    public void ContractId_should_throw_JsonException_when_dictionary_key_is_blank()
    {
        var deserialize = () => JsonSerializer.Deserialize<Dictionary<ContractId<Marker>, long>>("{\"\":1}");

        deserialize.Should().Throw<JsonException>();
    }

    private sealed record QuotaPayload(IReadOnlyDictionary<Party, long> QuotaByParty);

    private sealed record Marker : ITemplate
    {
        public static Identifier TemplateId { get; } = new("pkg", "M", "Marker");
        public static string PackageId => "pkg";
        public static string PackageName => "test";
        public static System.Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
    }
}
