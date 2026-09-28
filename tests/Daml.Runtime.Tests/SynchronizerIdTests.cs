// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class SynchronizerIdTests
{
    // Canton 3.4 shape: <name>::<fingerprint>
    private const string Canton34Id = "global_sync::12204457ac942c4d839331d402f82ecc941c6232de06a88097ade653350a2d6fc9c5";

    // Canton 3.5 shape: <name>::<fingerprint>::<protocol-version>
    private const string Canton35Id = "global_sync::12204457ac942c4d839331d402f82ecc941c6232de06a88097ade653350a2d6fc9c5::35-0";

    private static readonly JsonSerializerOptions CaseInsensitiveOptions =
        new() { PropertyNameCaseInsensitive = true };

    private sealed record ReassignmentPayload(SynchronizerId Source, string Note);

    private sealed record OptionalSynchronizerPayload(SynchronizerId? Target, string Note);

    [Fact]
    public void Construct_should_store_id_verbatim_for_3_4_shape()
    {
        var sid = new SynchronizerId(Canton34Id);
        sid.Value.Should().Be(Canton34Id);
    }

    [Fact]
    public void Construct_should_store_id_verbatim_for_3_5_shape_treating_id_as_opaque()
    {
        // Per Canton's official guidance, SynchronizerId metadata should be treated
        // as an opaque string. The wrapper deliberately does NOT decompose into
        // name/fingerprint/version — that would lock in a format that's already
        // changed once.
        var sid = new SynchronizerId(Canton35Id);
        sid.Value.Should().Be(Canton35Id);
    }

    [Fact]
    public void Construct_should_throw_when_id_is_null()
    {
        Action act = () => _ = new SynchronizerId(null!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construct_should_throw_when_id_is_whitespace()
    {
        Action act = () => _ = new SynchronizerId("   ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Default_uninitialized_value_should_throw_on_Id_access()
    {
        var defaulted = default(SynchronizerId);
        Action act = () => _ = defaulted.Value;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Explicit_conversion_to_string_should_return_id()
    {
        var sid = new SynchronizerId(Canton34Id);
        var s = (string)sid;
        s.Should().Be(Canton34Id);
    }

    [Fact]
    public void Explicit_conversion_from_string_should_construct()
    {
        var sid = (SynchronizerId)Canton34Id;
        sid.Value.Should().Be(Canton34Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromWire_should_return_null_when_wire_id_is_null_empty_or_whitespace(string? id)
    {
        (SynchronizerId.FromWire(id) is null).Should().BeTrue();
    }

    [Fact]
    public void FromWire_should_round_trip_a_non_blank_wire_id_through_Id()
    {
        var sid = SynchronizerId.FromWire(Canton35Id);

        (sid is null).Should().BeFalse();
        sid!.Value.Value.Should().Be(Canton35Id);
    }

    [Fact]
    public void FromWire_should_store_a_padded_id_verbatim()
    {
        var padded = $"  {Canton35Id}  ";

        var sid = SynchronizerId.FromWire(padded);

        sid!.Value.Value.Should().Be(padded);
    }

    [Fact]
    public void Default_returned_from_a_SynchronizerId_typed_helper_should_wrap_non_null_unlike_FromWire()
    {
        static SynchronizerId LegacyTransportHelper(string wire) =>
            wire.Length == 0 ? default : (SynchronizerId)wire;

        SynchronizerId? viaHelper = LegacyTransportHelper("");
        Action readViaHelper = () => _ = viaHelper?.Value;

        (viaHelper is null).Should().BeFalse();
        readViaHelper.Should().Throw<InvalidOperationException>();
        (SynchronizerId.FromWire("") is null).Should().BeTrue();
    }

    [Fact]
    public void Equality_should_be_value_based()
    {
        var a = new SynchronizerId(Canton34Id);
        var b = new SynchronizerId(Canton34Id);
        var c = new SynchronizerId(Canton35Id);
        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    [Fact]
    public void Post_LSU_3_4_and_3_5_strings_should_NOT_compare_equal_as_raw_ids()
    {
        // Documented caveat: post-Logical-Synchronizer-Upgrade, a 3.4 id and a
        // 3.5 id refer to the SAME synchronizer, but their wrapper-level
        // equality is raw-string equality. Higher-level migration code must
        // handle the cross-format comparison itself; the wrapper preserves the
        // wire representation.
        new SynchronizerId(Canton34Id).Should().NotBe(new SynchronizerId(Canton35Id));
    }

    [Fact]
    public void ToString_should_return_id_for_logging()
    {
        var sid = new SynchronizerId(Canton34Id);
        sid.ToString().Should().Be(Canton34Id);
    }

    [Fact]
    public void ToString_on_default_should_return_uninitialized_marker()
    {
        var defaulted = default(SynchronizerId);
        defaulted.ToString().Should().Contain("uninitialized");
    }

    [Fact]
    public void Json_should_round_trip_as_plain_string()
    {
        var sid = new SynchronizerId(Canton34Id);
        var json = JsonSerializer.Serialize(sid);
        json.Should().Be($"\"{Canton34Id}\"");

        var roundTripped = JsonSerializer.Deserialize<SynchronizerId>(json);
        roundTripped.Should().Be(sid);
    }

    [Fact]
    public void Json_should_round_trip_inside_a_record_payload()
    {
        // The actual production shape: SynchronizerId arrives as a string field
        // inside a containing record (Assigned/Unassigned, or PQS reassignment rows).
        var json = $"{{\"source\":\"{Canton35Id}\",\"note\":\"reassign-1\"}}";

        var payload = JsonSerializer.Deserialize<ReassignmentPayload>(json, CaseInsensitiveOptions);

        payload.Should().NotBeNull();
        payload!.Source.Value.Should().Be(Canton35Id);
        payload.Note.Should().Be("reassign-1");
    }

    [Fact]
    public void Json_should_throw_on_non_string_token()
    {
        Action act = () => JsonSerializer.Deserialize<SynchronizerId>("123");
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Json_should_throw_on_bare_null_token()
    {
        Action act = () => JsonSerializer.Deserialize<SynchronizerId>("null");
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Json_should_throw_when_non_nullable_field_receives_null()
    {
        // The actual PQS failure mode we're defending against: a payload where a
        // required SynchronizerId field comes back as JSON null. Must fail loudly,
        // not silently produce default(SynchronizerId).
        var json = "{\"source\":null,\"note\":\"reassign-1\"}";

        Action act = () => JsonSerializer.Deserialize<ReassignmentPayload>(json, CaseInsensitiveOptions);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Json_should_throw_on_empty_string()
    {
        Action act = () => JsonSerializer.Deserialize<SynchronizerId>("\"\"");
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Json_should_throw_on_whitespace_string()
    {
        Action act = () => JsonSerializer.Deserialize<SynchronizerId>("\" \"");
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Json_should_throw_when_serializing_uninitialized()
    {
        SynchronizerId sid = default;

        Action act = () => JsonSerializer.Serialize(sid);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Json_should_deserialize_null_inside_nullable_wrapper()
    {
        var json = "{\"target\":null,\"note\":\"none\"}";

        var payload = JsonSerializer.Deserialize<OptionalSynchronizerPayload>(json, CaseInsensitiveOptions);

        payload.Should().NotBeNull();
        payload!.Target.Should().BeNull();
        payload.Note.Should().Be("none");
    }

    [Fact]
    public void Json_should_deserialize_string_inside_nullable_wrapper()
    {
        var json = $"{{\"target\":\"{Canton35Id}\",\"note\":\"set\"}}";

        var payload = JsonSerializer.Deserialize<OptionalSynchronizerPayload>(json, CaseInsensitiveOptions);

        payload.Should().NotBeNull();
        payload!.Target.Should().NotBeNull();
        payload!.Target!.Value.Value.Should().Be(Canton35Id);
    }

    [Fact]
    public void Json_should_serialize_null_inside_nullable_wrapper()
    {
        // Locks in STJ's Nullable<T> short-circuit: when SynchronizerId?'s
        // HasValue is false, STJ writes `null` directly without invoking
        // SynchronizerIdJsonConverter.Write — so a legitimately-absent
        // SynchronizerId? field doesn't over-fire the serialize-uninitialized
        // JsonException guard. (A SynchronizerId? whose HasValue is true with
        // an uninitialized inner Value still invokes the converter and throws,
        // which is the intended behavior.)
        var payload = new OptionalSynchronizerPayload(Target: null, Note: "none");

        var json = JsonSerializer.Serialize(payload);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.ValueKind.Should().Be(JsonValueKind.Object);
        root.TryGetProperty("Target", out var target).Should().BeTrue();
        target.ValueKind.Should().Be(JsonValueKind.Null);
        root.TryGetProperty("Note", out var note).Should().BeTrue();
        note.GetString().Should().Be("none");
    }
}
