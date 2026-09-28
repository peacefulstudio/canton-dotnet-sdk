// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// A live stream carries the key in its wire shape on the two arms that re-emit the created
/// contract, so the typed hop off that slot is what a consumer rebuilding state actually calls.
/// These tests run it against the corpus's own generated types — a record key on a created row
/// and a bare <c>Party</c> key on an assigned row — so the real emitted key decoders are exercised.
/// </summary>
public class ContractKeyOnStreamRowTests
{
    private static readonly Party Custodian = new("custodian::1220");

    private const string LedgerKeyHash = "6CgQL9eNNqIjS5cB6/kK1IsqdxjcgXl/3kxSiUEkiBA=";

    [Fact]
    public void ToContract_reads_a_record_key_off_a_created_stream_row()
    {
        var payload = new Account(Custodian, "savings", 42);
        var key = new AccountKey(Custodian, "savings");

        var created = new ContractStreamEvent<Account>.Created(
            new ContractId<Account>("contract-1"),
            payload,
            new ContractKey(key.ToRecord(), Account.TemplateId) { KeyHash = LedgerKeyHash },
            LedgerOffset.At(1),
            new SynchronizerId("sync"),
            [Custodian]);

        var contract = created.ToContract<Account, AccountKey>();

        contract.Id.Value.Should().Be("contract-1");
        contract.Data.Should().Be(payload);
        contract.Key.Value.Should().Be(key);
        contract.Key.Hash.Should().Be(LedgerKeyHash);
    }

    [Fact]
    public void ToContract_reads_a_bare_party_key_off_an_assigned_stream_row()
    {
        var payload = new Steward(Custodian, "charter");

        var assigned = new ContractStreamEvent<Steward>.Assigned(
            new ContractId<Steward>("contract-1"),
            payload,
            new ContractKey(Custodian.ToDamlValue(), Steward.TemplateId) { KeyHash = LedgerKeyHash },
            LedgerOffset.At(4),
            new SynchronizerId("src"),
            new SynchronizerId("tgt"),
            "reassignment-1",
            7L,
            [Custodian]);

        var contract = assigned.ToContract<Steward, Party>();

        contract.Key.Value.Should().Be(
            Custodian,
            "a reassignment re-emits the whole created contract, so the key has to survive the move "
            + "for a consumer that addresses contracts by key");
        contract.Key.Hash.Should().Be(LedgerKeyHash);
    }
}
