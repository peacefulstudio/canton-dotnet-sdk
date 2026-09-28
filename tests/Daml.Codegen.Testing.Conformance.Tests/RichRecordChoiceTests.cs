// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class RichRecordChoiceTests
{
    [Fact]
    public void Relabel_argument_round_trips_through_its_record()
    {
        var argument = new RichRecord.Relabel("renamed");

        var restored = RichRecord.Relabel.FromRecord(argument.ToRecord());

        restored.Should().Be(argument);
        argument.ToRecord().GetRequiredField("newLabel").As<DamlText>().Value.Should().Be("renamed");
    }

    [Fact]
    public void Relabel_choice_is_non_consuming_and_named_relabel()
    {
        RichRecord.ChoiceRelabel.Name.Value.Should().Be("Relabel");
        RichRecord.ChoiceRelabel.Consuming.Should().BeFalse();
    }

    [Fact]
    public void RichRecordChoice_archive_choice_is_consuming_and_named_archive()
    {
        RichRecord.ChoiceArchive.Name.Value.Should().Be("Archive");
        RichRecord.ChoiceArchive.Consuming.Should().BeTrue();
    }

    [Fact]
    public void RichRecordChoice_contract_from_created_event_pairs_typed_id_with_decoded_payload()
    {
        var payload = new RichRecord(
            Owner: new Party("alice"),
            Count: 1,
            Amount: 1m,
            Label: "l",
            Active: false,
            AsOf: new DateOnly(2026, 1, 1),
            ObservedAt: DateTimeOffset.UnixEpoch,
            Note: null,
            Tags: new List<string>(),
            Attributes: new Dictionary<string, string>(),
            Marker: new ContractId<Marker>("m"),
            HoldingCid: new ContractId<IHolding>("00h"),
            HoldingCids: new List<ContractId<IHolding>>(),
            Profile: new Profile("n", 0),
            Outcome: new Outcome.Pending(),
            Suit: Suit.Spades,
            Fee: 0m);
        var @event = new CreatedEvent(
            EventId: "ev-1",
            ContractId: "rich-cid",
            TemplateId: RichRecord.TemplateId,
            CreateArguments: payload.ToRecord(),
            WitnessParties: [],
            Signatories: [],
            Observers: [],
            ContractKey: null);

        var contract = Contract<RichRecord>.FromCreatedEvent(@event, RichRecord.FromRecord);

        contract.Id.Value.Should().Be("rich-cid");
        contract.Data.Label.Should().Be("l");
    }

    [Fact]
    public void GetTemplateId_exposes_the_template_ids_raw_pqs_queries_filter_on()
    {
        TemplateExtensions.GetTemplateId<Marker>().Should().EndWith(":RichTypes:Marker");
        TemplateExtensions.GetTemplateId<RichRecord>().Should().EndWith(":RichTypes:RichRecord");
    }
}
