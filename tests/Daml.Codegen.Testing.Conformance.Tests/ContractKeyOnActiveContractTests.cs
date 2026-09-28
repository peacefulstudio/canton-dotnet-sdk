// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Codegen.Testing.Conformance.KeyBuilders;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// The key travels on the created event, so the keyed <c>Contract&lt;T, TKey&gt;</c> — not the
/// template payload a caller constructs locally — is where it lands, decoded through the
/// template's generated key witness. These tests run against the corpus's own generated types
/// for a record key, a record key built by a helper in another module, and a bare
/// <c>Party</c> key.
/// </summary>
public class ContractKeyOnActiveContractTests
{
    private static readonly Party Custodian = new("custodian::1220");

    private const string LedgerKeyHash = "6CgQL9eNNqIjS5cB6/kK1IsqdxjcgXl/3kxSiUEkiBA=";

    private static CreatedEvent CreatedEvent(Identifier templateId, DamlRecord createArguments, DamlValue? key) =>
        new(
            EventId: "event-1",
            ContractId: "contract-1",
            TemplateId: templateId,
            CreateArguments: createArguments,
            WitnessParties: [],
            Signatories: [Custodian],
            Observers: [],
            ContractKey: key is null ? null : new ContractKey(key));

    [Fact]
    public void FromCreatedEvent_reads_a_record_key_off_the_created_event()
    {
        var payload = new Account(Custodian, "savings", 42);
        var key = new AccountKey(Custodian, "savings");

        var contract = Contract<Account, AccountKey>.FromCreatedEvent(
            CreatedEvent(Account.TemplateId, payload.ToRecord(), key.ToRecord()), Account.FromRecord);

        contract.Key.Value.Should().Be(key);
        contract.Data.Should().Be(payload);
        contract.Id.Value.Should().Be("contract-1");
    }

    [Fact]
    public void FromCreatedEvent_reads_a_bare_party_key_off_the_created_event()
    {
        var payload = new Steward(Custodian, "charter");

        var contract = Contract<Steward, Party>.FromCreatedEvent(
            CreatedEvent(Steward.TemplateId, payload.ToRecord(), Custodian.ToDamlValue()), Steward.FromRecord);

        contract.Key.Value.Should().Be(Custodian);
    }

    [Fact]
    public void FromCreatedEvent_reads_a_key_whose_fields_share_no_name_with_the_payload()
    {
        var payload = new Schedule(new ScheduleView(Custodian, "2026-Q1"));
        var key = new ScheduleKey(Custodian, "2026-Q1");

        var contract = Contract<Schedule, ScheduleKey>.FromCreatedEvent(
            CreatedEvent(Schedule.TemplateId, payload.ToRecord(), key.ToRecord()), Schedule.FromRecord);

        contract.Key.Value.Should().Be(key);
    }

    [Fact]
    public void FromCreatedEvent_carries_the_ledgers_hash_of_the_key()
    {
        var payload = new Account(Custodian, "savings", 42);
        var key = new AccountKey(Custodian, "savings");
        var createdEvent = CreatedEvent(Account.TemplateId, payload.ToRecord(), key.ToRecord()) with
        {
            ContractKey = new ContractKey(key.ToRecord()) { KeyHash = LedgerKeyHash },
        };

        var contract = Contract<Account, AccountKey>.FromCreatedEvent(createdEvent, Account.FromRecord);

        contract.Key.Hash.Should().Be(
            LedgerKeyHash,
            "the hash is Canton-computed over the key and the template id, so a keyed contract "
            + "that drops it leaves the caller unable to address the contract by key");
    }

    [Fact]
    public void FromCreatedEvent_rejects_a_keyed_event_carrying_no_key()
    {
        var payload = new Account(Custodian, "savings", 42);

        var decoding = () => Contract<Account, AccountKey>.FromCreatedEvent(
            CreatedEvent(Account.TemplateId, payload.ToRecord(), key: null), Account.FromRecord);

        decoding.Should().Throw<InvalidOperationException>(
            "a keyed template's Contract<T, TKey> declares a non-nullable key, so an event that "
            + "carries none has to fail loudly rather than reach a caller through that shape")
            .WithMessage("*carried no contract key*");
    }
}
