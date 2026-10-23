// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Xunit;
using Daml.Runtime.Stdlib;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class RichRecordRoundTripTests
{
    private static RichRecord Sample(string? note) => new(
        Owner: new Party("alice"),
        Count: 42,
        Amount: 19.95m,
        Label: "first",
        Active: true,
        AsOf: new DateOnly(2026, 6, 4),
        ObservedAt: new DateTimeOffset(2026, 6, 4, 12, 30, 0, TimeSpan.Zero),
        Note: note,
        NestedNote: new Optional<Optional<string>>.Some(new Optional<string>.Some("deep")),
        Tags: new List<string> { "a", "b" },
        Attributes: new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" },
        GenMap: new Dictionary<Party, long> { [new Party("alice")] = 7L },
        UnitField: DamlUnit.Instance,
        Marker: new ContractId<Marker>("marker-cid"),
        HoldingCid: new ContractId<IHolding>("00aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899"),
        HoldingCids: new List<ContractId<IHolding>>
        {
            new ContractId<IHolding>("0011112222333344445555666677778888999900001111222233334444555566aa"),
            new ContractId<IHolding>("00ffffeeeeddddccccbbbbaaaa99998888777766665555444433332222111100bb"),
        },
        Profile: new Profile("ace", 7),
        Outcome: new Outcome.Win(new Outcome_Win(Prize: 12.34m, Tier: "gold")),
        Suit: Suit.Hearts,
        Fee: 1.5m);

    [Fact]
    public void RichRecordRoundTrip_round_trips_every_field_when_optional_is_present()
    {
        var original = Sample(note: "hello");

        var restored = RichRecord.FromRecord(original.ToRecord());

        restored.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void RichRecordRoundTrip_round_trips_every_field_when_optional_is_absent()
    {
        var original = Sample(note: null);

        var restored = RichRecord.FromRecord(original.ToRecord());

        restored.Note.Should().BeNull();
        restored.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void ToRecord_maps_primitives_to_their_daml_backing_types()
    {
        var record = Sample(note: "hello").ToRecord();

        record.GetRequiredField("count").As<DamlInt64>().Value.Should().Be(42);
        record.GetRequiredField("amount").As<DamlNumeric>().Value.Should().Be(19.95m);
        record.GetRequiredField("label").As<DamlText>().Value.Should().Be("first");
        record.GetRequiredField("active").As<DamlBool>().Value.Should().BeTrue();
        record.GetRequiredField("asOf").As<DamlDate>().Value.Should().Be(new DateOnly(2026, 6, 4));
        record.GetRequiredField("observedAt").As<DamlTimestamp>().Value.Should().Be(new DateTimeOffset(2026, 6, 4, 12, 30, 0, TimeSpan.Zero));
        record.GetRequiredField("owner").As<DamlParty>().Value.Should().Be("alice");
        record.GetRequiredField("marker").As<DamlContractId>().Value.Should().Be("marker-cid");
    }

    [Fact]
    public void ToRecord_emits_none_for_an_absent_optional()
    {
        var record = Sample(note: null).ToRecord();

        record.GetRequiredField("note").As<DamlOptional>().HasValue.Should().BeFalse();
    }

    [Fact]
    public void ToRecord_serializes_list_and_textmap_fields()
    {
        var record = Sample(note: "hello").ToRecord();

        var tags = record.GetRequiredField("tags").As<DamlList>();
        tags.Values.Select(v => v.As<DamlText>().Value).Should().Equal("a", "b");

        var attributes = record.GetRequiredField("attributes").As<DamlTextMap>();
        attributes.Values.Should().ContainKey("k1").WhoseValue.As<DamlText>().Value.Should().Be("v1");
    }

    [Fact]
    public void ToRecord_nests_the_profile_record()
    {
        var record = Sample(note: "hello").ToRecord();

        var profile = record.GetRequiredField("profile").As<DamlRecord>();
        profile.GetRequiredField("nickname").As<DamlText>().Value.Should().Be("ace");
        profile.GetRequiredField("level").As<DamlInt64>().Value.Should().Be(7);
    }

    [Fact]
    public void ToRecord_wires_the_outcome_variant()
    {
        var record = Sample(note: "hello").ToRecord();

        var outcome = record.GetRequiredField("outcome").As<DamlVariant>();
        outcome.Constructor.Should().Be("Win");
        var win = outcome.Value.As<DamlRecord>();
        win.GetRequiredField("prize").As<DamlNumeric>().Value.Should().Be(12.34m);
        win.GetRequiredField("tier").As<DamlText>().Value.Should().Be("gold");
    }

    [Fact]
    public void ToRecord_wires_the_suit_enum()
    {
        var record = Sample(note: "hello").ToRecord();

        record.GetRequiredField("suit").As<DamlEnum>().Constructor.Should().Be("Hearts");
    }

    [Fact]
    public void RichRecordRoundTrip_non_default_scale_fee_serializes_unpadded()
    {
        var record = new RichRecord(
            Owner: new Party("alice"),
            Count: 0,
            Amount: 0m,
            Label: "l",
            Active: false,
            AsOf: new DateOnly(2026, 1, 1),
            ObservedAt: DateTimeOffset.UnixEpoch,
            Note: null,
            NestedNote: new Optional<Optional<string>>.Some(new Optional<string>.Some("deep")),
            Tags: new List<string>(),
            Attributes: new Dictionary<string, string>(),
            GenMap: new Dictionary<Party, long> { [new Party("alice")] = 7L },
            UnitField: DamlUnit.Instance,
            Marker: new ContractId<Marker>("m"),
            HoldingCid: new ContractId<IHolding>("00h"),
            HoldingCids: new List<ContractId<IHolding>>(),
            Profile: new Profile("n", 0),
            Outcome: new Outcome.Pending(),
            Suit: Suit.Clubs,
            Fee: 1.5m).ToRecord();

        var json = DamlJsonSerializer.Serialize(record.GetRequiredField("fee").As<DamlNumeric>());

        json.Should().Be("\"1.5\"");
    }

    [Fact]
    public void RichRecordRoundTrip_interface_typed_contract_id_fields_round_trip_without_throwing()
    {
        var scalarCid = "00aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899";
        var listCids = new[]
        {
            "0011112222333344445555666677778888999900001111222233334444555566aa",
            "00ffffeeeeddddccccbbbbaaaa99998888777766665555444433332222111100bb",
        };
        var original = Sample(note: "hello") with
        {
            HoldingCid = new ContractId<IHolding>(scalarCid),
            HoldingCids = listCids.Select(cid => new ContractId<IHolding>(cid)).ToList(),
        };

        var act = () => RichRecord.FromRecord(original.ToRecord());

        act.Should().NotThrow();
        var restored = act();
        restored.HoldingCid.Value.Should().Be(scalarCid);
        restored.HoldingCids.Select(cid => cid.Value).Should().Equal(listCids);
    }
}
