// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class PartyJsonTests
{
    private static readonly JsonSerializerOptions CaseInsensitiveOptions =
        new() { PropertyNameCaseInsensitive = true };

    private sealed record TemplatePayload(Party Operator, string MarketId);

    private sealed record OptionalPartyPayload(Party? Delegate, string Note);

    [Fact]
    public void Party_should_deserialize_from_json_string()
    {
        // PQS payloads encode parties as raw JSON strings, e.g.
        //   {"operator": "Platform::1220abcd..."}
        var json = "\"Alice::122012ab\"";

        var party = JsonSerializer.Deserialize<Party>(json);

        party.Value.Should().Be("Alice::122012ab");
    }

    [Fact]
    public void Party_should_serialize_as_json_string()
    {
        var party = new Party("Alice::122012ab");

        var json = JsonSerializer.Serialize(party);

        json.Should().Be("\"Alice::122012ab\"");
    }

    [Fact]
    public void Party_should_round_trip_inside_a_record_payload()
    {
        var json = "{\"operator\":\"Platform::1220abcd\",\"marketId\":\"BTC-USD\"}";

        var payload = JsonSerializer.Deserialize<TemplatePayload>(json, CaseInsensitiveOptions);

        payload.Should().NotBeNull();
        payload!.Operator.Value.Should().Be("Platform::1220abcd");
        payload.MarketId.Should().Be("BTC-USD");
    }

    [Fact]
    public void Party_should_throw_JsonException_when_token_is_not_a_string()
    {
        var json = "123";

        var act = () => JsonSerializer.Deserialize<Party>(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Party_should_throw_JsonException_when_string_is_empty()
    {
        var json = "\"\"";

        var act = () => JsonSerializer.Deserialize<Party>(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Party_should_throw_JsonException_when_string_is_whitespace()
    {
        var json = "\" \"";

        var act = () => JsonSerializer.Deserialize<Party>(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Party_should_throw_JsonException_when_serializing_uninitialized()
    {
        Party party = default;

        var act = () => JsonSerializer.Serialize(party);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Party_should_throw_JsonException_when_token_is_null()
    {
        var json = "null";

        var act = () => JsonSerializer.Deserialize<Party>(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Party_should_throw_JsonException_when_non_nullable_field_receives_null()
    {
        // The actual PQS failure mode we're defending against: a payload where a
        // required Party field comes back as JSON null. Must fail loudly, not
        // silently produce default(Party).
        var json = "{\"operator\":null,\"marketId\":\"BTC-USD\"}";

        var act = () => JsonSerializer.Deserialize<TemplatePayload>(json, CaseInsensitiveOptions);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Party_should_name_the_offending_value_when_the_id_is_whitespace()
    {
        var json = "{\"operator\":\"   \",\"marketId\":\"BTC-USD\"}";

        var act = () => JsonSerializer.Deserialize<TemplatePayload>(json, CaseInsensitiveOptions);

        act.Should().Throw<JsonException>().WithMessage(
            "*'   '*",
            "echoing the rejected value is what makes a whitespace id diagnosable in a PQS row, and the "
            + "sibling SynchronizerId converter has always done it");
    }

    [Fact]
    public void Party_should_deserialize_null_inside_nullable_wrapper()
    {
        var json = "{\"delegate\":null,\"note\":\"none\"}";

        var payload = JsonSerializer.Deserialize<OptionalPartyPayload>(json, CaseInsensitiveOptions);

        payload.Should().NotBeNull();
        payload!.Delegate.Should().BeNull();
        payload.Note.Should().Be("none");
    }

    [Fact]
    public void Party_should_deserialize_string_inside_nullable_wrapper()
    {
        var json = "{\"delegate\":\"Alice::122012ab\",\"note\":\"set\"}";

        var payload = JsonSerializer.Deserialize<OptionalPartyPayload>(json, CaseInsensitiveOptions);

        payload.Should().NotBeNull();
        payload!.Delegate.Should().NotBeNull();
        payload!.Delegate!.Value.Value.Should().Be("Alice::122012ab");
    }

    [Fact]
    public void Party_should_serialize_null_inside_nullable_wrapper()
    {
        // Locks in STJ's Nullable<T> short-circuit: when the field value is null,
        // STJ writes `null` directly without invoking PartyJsonConverter.Write,
        // so a default(Party) inside Party? never leaks through as a JsonException.
        var payload = new OptionalPartyPayload(Delegate: null, Note: "none");

        var json = JsonSerializer.Serialize(payload);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.ValueKind.Should().Be(JsonValueKind.Object);
        root.TryGetProperty("Delegate", out var @delegate).Should().BeTrue();
        @delegate.ValueKind.Should().Be(JsonValueKind.Null);
        root.TryGetProperty("Note", out var note).Should().BeTrue();
        note.GetString().Should().Be("none");
    }
}
