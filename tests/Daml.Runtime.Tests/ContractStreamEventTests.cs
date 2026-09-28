// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using Daml.Runtime.Streams;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class ContractStreamEventTests
{
    private const string LedgerKeyHash = "6CgQL9eNNqIjS5cB6/kK1IsqdxjcgXl/3kxSiUEkiBA=";

    [Fact]
    public void Created_should_carry_the_key_hash_the_transport_read_off_the_wire()
    {
        var created = new ContractStreamEvent<TestTemplate>.Created(
            new ContractId<TestTemplate>("c1"),
            new TestTemplate("alice"),
            new ContractKey(new DamlText("savings"), TestTemplate.TemplateId) { KeyHash = LedgerKeyHash },
            LedgerOffset.At(1),
            new SynchronizerId("sync"),
            [new Party("alice")]);

        created.Key!.KeyHash.Should().Be(
            LedgerKeyHash,
            "the live stream is the fourth read shape a key travels, so the hash has to survive it "
            + "as well as the snapshot shapes");
    }

    [Fact]
    public void Created_should_leave_the_key_hash_null_when_the_event_carried_none()
    {
        var created = new ContractStreamEvent<TestTemplate>.Created(
            new ContractId<TestTemplate>("c1"),
            new TestTemplate("alice"),
            new ContractKey(new DamlText("savings"), TestTemplate.TemplateId),
            LedgerOffset.At(1),
            new SynchronizerId("sync"),
            [new Party("alice")]);

        created.Key!.KeyHash.Should().BeNull();
    }

    [Fact]
    public void Assigned_should_carry_the_key_of_the_contract_it_reassigns()
    {
        var key = new ContractKey(new DamlText("savings"), TestTemplate.TemplateId) { KeyHash = LedgerKeyHash };

        var assigned = new ContractStreamEvent<TestTemplate>.Assigned(
            new ContractId<TestTemplate>("c1"),
            new TestTemplate("alice"),
            key,
            LedgerOffset.At(4),
            new SynchronizerId("src"),
            new SynchronizerId("tgt"),
            "reassignment-1",
            7L,
            [new Party("alice")]);

        assigned.Key.Should().Be(
            key,
            "an assignment re-emits the whole created contract, so a consumer rebuilding state from "
            + "one stream would lose the key at every reassignment if this variant had nowhere to put it");
        assigned.Key!.KeyHash.Should().Be(LedgerKeyHash);
    }

    [Fact]
    public void ToContract_pairs_a_created_events_payload_with_its_contract_id()
    {
        var created = new ContractStreamEvent<TestTemplate>.Created(
            new ContractId<TestTemplate>("c1"),
            new TestTemplate("alice"),
            null,
            LedgerOffset.At(1),
            new SynchronizerId("sync"),
            [new Party("alice")]);

        var contract = created.ToContract();

        contract.Id.Value.Should().Be("c1");
        contract.Data.Owner.Should().Be("alice");
    }

    [Fact]
    public void ToContract_pairs_an_assigned_events_payload_with_its_contract_id()
    {
        var assigned = new ContractStreamEvent<TestTemplate>.Assigned(
            new ContractId<TestTemplate>("c1"),
            new TestTemplate("alice"),
            null,
            LedgerOffset.At(4),
            new SynchronizerId("src"),
            new SynchronizerId("tgt"),
            "reassignment-1",
            7L,
            [new Party("alice")]);

        var contract = assigned.ToContract();

        contract.Id.Value.Should().Be("c1");
        contract.Data.Owner.Should().Be("alice");
    }

    [Fact]
    public void ToContract_decodes_a_created_events_key_and_carries_the_ledgers_hash_of_it()
    {
        var created = new ContractStreamEvent<DecodableTemplate>.Created(
            new ContractId<DecodableTemplate>("c1"),
            new DecodableTemplate(new Party("alice")),
            new ContractKey(
                DamlRecord.Create(new DamlField("owner", new DamlParty("alice"))),
                DecodableTemplate.TemplateId)
            {
                KeyHash = LedgerKeyHash,
            },
            LedgerOffset.At(1),
            new SynchronizerId("sync"),
            [new Party("alice")]);

        var contract = created.ToContract<DecodableTemplate, Party>();

        contract.Key.Value.Should().Be(
            new Party("alice"),
            "the live stream is where a consumer rebuilds state from, so a key left in its wire "
            + "shape there is a decode every call site has to repeat");
        contract.Key.Hash.Should().Be(
            LedgerKeyHash,
            "the hash is Canton-computed over the key and the template id, so a projection that "
            + "drops it leaves the caller unable to address the contract by key");
    }

    [Fact]
    public void ToContract_decodes_an_assigned_events_key_and_carries_the_ledgers_hash_of_it()
    {
        var assigned = new ContractStreamEvent<DecodableTemplate>.Assigned(
            new ContractId<DecodableTemplate>("c1"),
            new DecodableTemplate(new Party("alice")),
            new ContractKey(
                DamlRecord.Create(new DamlField("owner", new DamlParty("alice"))),
                DecodableTemplate.TemplateId)
            {
                KeyHash = LedgerKeyHash,
            },
            LedgerOffset.At(4),
            new SynchronizerId("src"),
            new SynchronizerId("tgt"),
            "reassignment-1",
            7L,
            [new Party("alice")]);

        var contract = assigned.ToContract<DecodableTemplate, Party>();

        contract.Key.Value.Should().Be(
            new Party("alice"),
            "a consumer rebuilding state from one stream would lose the typed key at every "
            + "reassignment if only the created arm had a hop off the raw slot");
        contract.Key.Hash.Should().Be(LedgerKeyHash);
    }

    [Fact]
    public void ToContract_rejects_a_keyed_projection_of_a_created_row_carrying_no_key()
    {
        var created = new ContractStreamEvent<DecodableTemplate>.Created(
            new ContractId<DecodableTemplate>("c1"),
            new DecodableTemplate(new Party("alice")),
            null,
            LedgerOffset.At(1),
            new SynchronizerId("sync"),
            [new Party("alice")]);

        var projecting = () => created.ToContract<DecodableTemplate, Party>();

        projecting.Should().Throw<InvalidOperationException>(
            "the keyed contract's key is non-nullable, so a row with no key has to fail loudly "
            + "rather than reach a caller through a shape whose type says the key is present")
            .WithMessage("*carried no contract key*");
    }

    [Fact]
    public void ToContract_rejects_a_keyed_projection_of_an_assigned_row_carrying_no_key()
    {
        var assigned = new ContractStreamEvent<DecodableTemplate>.Assigned(
            new ContractId<DecodableTemplate>("c1"),
            new DecodableTemplate(new Party("alice")),
            null,
            LedgerOffset.At(4),
            new SynchronizerId("src"),
            new SynchronizerId("tgt"),
            "reassignment-1",
            7L,
            [new Party("alice")]);

        var projecting = () => assigned.ToContract<DecodableTemplate, Party>();

        projecting.Should().Throw<InvalidOperationException>(
            "an assignment re-emits the created contract, so a missing key there is the same "
            + "loud failure it is on the created arm rather than a guessed one")
            .WithMessage("*carried no contract key*");
    }

    [Fact]
    public void Variants_should_be_distinguishable_via_pattern_match()
    {
        ContractStreamEvent<TestTemplate>[] events =
        [
            new ContractStreamEvent<TestTemplate>.Created(new ContractId<TestTemplate>("c1"), new TestTemplate("alice"), null, LedgerOffset.At(1), new SynchronizerId("sync"), [new Party("alice")]),
            new ContractStreamEvent<TestTemplate>.Archived(new ContractId<TestTemplate>("c1"), LedgerOffset.At(2), new SynchronizerId("sync"), [new Party("alice")]),
            new ContractStreamEvent<TestTemplate>.Exercised(new ContractId<TestTemplate>("c1"), new ChoiceName("Accept"), DamlUnit.Instance, DamlUnit.Instance, true, LedgerOffset.At(3), new SynchronizerId("sync"), [new Party("alice")]),
            new ContractStreamEvent<TestTemplate>.Assigned(new ContractId<TestTemplate>("c1"), new TestTemplate("alice"), null, LedgerOffset.At(4), new SynchronizerId("src"), new SynchronizerId("tgt"), "reassignment-1", 7L, [new Party("alice")]),
            new ContractStreamEvent<TestTemplate>.Unassigned(new ContractId<TestTemplate>("c1"), LedgerOffset.At(5), new SynchronizerId("src"), new SynchronizerId("tgt"), "reassignment-1", 7L, [new Party("alice")]),
            new ContractStreamEvent<TestTemplate>.Checkpoint(LedgerOffset.At(6)),
            new ContractStreamEvent<TestTemplate>.StreamError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "unavailable"),
            new ContractStreamEvent<TestTemplate>.Unclassified(LedgerOffset.At(7), UnclassifiedKind.Unknown, "TopologyEvent"),
        ];

        var seen = events.Select(e => e switch
        {
            ContractStreamEvent<TestTemplate>.Created => "created",
            ContractStreamEvent<TestTemplate>.Archived => "archived",
            ContractStreamEvent<TestTemplate>.Exercised => "exercised",
            ContractStreamEvent<TestTemplate>.Assigned => "assigned",
            ContractStreamEvent<TestTemplate>.Unassigned => "unassigned",
            ContractStreamEvent<TestTemplate>.Checkpoint => "checkpoint",
            ContractStreamEvent<TestTemplate>.StreamError => "error",
            ContractStreamEvent<TestTemplate>.Unclassified => "unclassified",
            _ => "other",
        }).ToList();

        seen.Should().Equal("created", "archived", "exercised", "assigned", "unassigned", "checkpoint", "error", "unclassified");
    }

    [Fact]
    public void Variants_with_same_payload_should_be_value_equal()
    {
        var a = new ContractStreamEvent<TestTemplate>.Checkpoint(LedgerOffset.At(42));
        var b = new ContractStreamEvent<TestTemplate>.Checkpoint(LedgerOffset.At(42));
        a.Should().Be(b);
    }

    [Fact]
    public void Unclassified_with_same_payload_should_be_value_equal()
    {
        var a = new ContractStreamEvent<TestTemplate>.Unclassified(LedgerOffset.At(7), UnclassifiedKind.Unknown, "TopologyEvent");
        var b = new ContractStreamEvent<TestTemplate>.Unclassified(LedgerOffset.At(7), UnclassifiedKind.Unknown, "TopologyEvent");
        a.Should().Be(b);
    }

    [Fact]
    public void StreamError_Status_is_a_closed_TransportStatus_union()
    {
        var err = new ContractStreamEvent<TestTemplate>.StreamError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "transient");

        err.Status.Should().BeOfType<TransportStatus.Grpc>(
            "the closed TransportStatus union is what spares every consumer from misreading a gRPC, " +
            "HTTP, no-response, or undecodable-body fault as the same bare integer");
        err.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
    }

    [Fact]
    public void StreamError_carries_the_classification_the_transport_determined()
    {
        var err = new ContractStreamEvent<TestTemplate>.StreamError(
            new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "transient", DamlErrorCategory.TransientServerFailure);

        err.Category.Should().Be(DamlErrorCategory.TransientServerFailure);
    }

    [Fact]
    public void StreamError_leaves_the_classification_null_when_the_transport_determined_none()
    {
        var err = new ContractStreamEvent<TestTemplate>.StreamError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "transient");

        err.Category.Should().BeNull();
    }

    [Fact]
    public void StreamError_carries_the_error_id_the_transport_parsed()
    {
        var err = new ContractStreamEvent<TestTemplate>.StreamError(
            new TransportStatus.Grpc(GrpcStatusCode.Aborted),
            "the stream authorization is stale",
            DamlErrorCategory.ContentionOnSharedResources,
            "STALE_STREAM_AUTHORIZATION");

        err.ErrorId.Should().Be(
            "STALE_STREAM_AUTHORIZATION",
            "Canton's per-code resolutions are addressed by the error id, so a consumer that never "
            + "receives it cannot look up whether reopening the stream resolves the fault");
    }

    [Fact]
    public void StreamError_leaves_the_error_id_null_when_no_structured_error_was_attached()
    {
        var err = new ContractStreamEvent<TestTemplate>.StreamError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "transient");

        err.ErrorId.Should().BeNull(
            "a transport that decoded no structured error has to say so, rather than invent a sentinel "
            + "a consumer would then have to recognise as meaning nothing was parsed");
    }

    [Fact]
    public void StreamError_distinguishes_two_faults_sharing_a_status_and_a_category()
    {
        var selfClearing = new ContractStreamEvent<TestTemplate>.StreamError(
            new TransportStatus.Grpc(GrpcStatusCode.Aborted),
            "the stream authorization is stale",
            DamlErrorCategory.ContentionOnSharedResources,
            "STALE_STREAM_AUTHORIZATION");
        var reproducible = new ContractStreamEvent<TestTemplate>.StreamError(
            new TransportStatus.Grpc(GrpcStatusCode.Aborted),
            "the maximum number of list elements was reached",
            DamlErrorCategory.ContentionOnSharedResources,
            "JSON_API_MAXIMUM_LIST_ELEMENTS_NUMBER_REACHED");

        selfClearing.Category.Should().Be(reproducible.Category);
        selfClearing.Status.Should().Be(reproducible.Status);
        selfClearing.ErrorId.Should().NotBe(
            reproducible.ErrorId,
            "reopening resolves the first fault and reproduces the second, and the error id is the only "
            + "member that separates them: they agree on category and on status, and matching on the "
            + "message would pin control flow to participant prose");
    }

    [Fact]
    public void Unclassified_should_expose_offset_and_enumerated_kind()
    {
        var unclassified = new ContractStreamEvent<TestTemplate>.Unclassified(LedgerOffset.At(7), UnclassifiedKind.DecodeFailure);

        unclassified.Offset.Should().Be(
            LedgerOffset.At(7),
            "a projector that does know where the unclassifiable event sat must still report it, so the nullable slot has to round-trip a real offset unchanged");
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.RawKind.Should().BeNull();
    }

    [Fact]
    public void Unclassified_unknown_kind_preserves_the_raw_descriptor()
    {
        var unclassified = new ContractStreamEvent<TestTemplate>.Unclassified(LedgerOffset.At(7), UnclassifiedKind.Unknown, "TopologyEvent");

        unclassified.Kind.Should().Be(UnclassifiedKind.Unknown);
        unclassified.RawKind.Should().Be("TopologyEvent");
    }

    [Fact]
    public void Unclassified_rejects_a_raw_descriptor_on_an_enumerated_kind()
    {
        var act = () => new ContractStreamEvent<TestTemplate>.Unclassified(LedgerOffset.At(7), UnclassifiedKind.DecodeFailure, "EventCase_1");

        act.Should().Throw<ArgumentException>()
            .WithMessage("An Unclassified event with the enumerated Kind 'DecodeFailure' must not carry a RawKind; RawKind is populated only for Unknown.*")
            .WithParameterName("RawKind");
    }

    [Fact]
    public void Unclassified_rejects_an_unknown_kind_without_a_raw_descriptor()
    {
        var act = () => new ContractStreamEvent<TestTemplate>.Unclassified(LedgerOffset.At(7), UnclassifiedKind.Unknown, null);

        act.Should().Throw<ArgumentException>()
            .WithMessage("An Unclassified event with Kind Unknown must carry the transport's raw descriptor in RawKind.*")
            .WithParameterName("RawKind");
    }

    [Fact]
    public void Unclassified_accepts_a_null_offset_and_preserves_it()
    {
        var unclassified = new ContractStreamEvent<TestTemplate>.Unclassified(null, UnclassifiedKind.DecodeFailure);

        unclassified.Offset.Should().BeNull(
            "an event the projector could not place on the ledger has no offset to report, and the only alternative is fabricating LedgerOffset.Begin, which is indistinguishable from a genuine ledger start: a consumer persisting it as resume state checkpoints at the beginning of the ledger and re-reads the entire stream");
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.RawKind.Should().BeNull();
    }

    [Fact]
    public void Unclassified_without_an_offset_still_rejects_a_raw_descriptor_on_an_enumerated_kind()
    {
        var act = () => new ContractStreamEvent<TestTemplate>.Unclassified(null, UnclassifiedKind.DecodeFailure, "EventCase_1");

        act.Should().Throw<ArgumentException>(
            "widening Offset to a nullable must not relax the Kind and RawKind invariant: an enumerated kind carrying a raw descriptor stays a construction error")
            .WithMessage("An Unclassified event with the enumerated Kind 'DecodeFailure' must not carry a RawKind; RawKind is populated only for Unknown.*")
            .WithParameterName("RawKind");
    }

    [Fact]
    public void Unclassified_without_an_offset_still_rejects_an_unknown_kind_without_a_raw_descriptor()
    {
        var act = () => new ContractStreamEvent<TestTemplate>.Unclassified(null, UnclassifiedKind.Unknown, null);

        act.Should().Throw<ArgumentException>(
            "widening Offset to a nullable must not relax the Kind and RawKind invariant: Unknown without the transport's raw descriptor stays a construction error")
            .WithMessage("An Unclassified event with Kind Unknown must carry the transport's raw descriptor in RawKind.*")
            .WithParameterName("RawKind");
    }

    [Fact]
    public void Unclassified_with_new_offset_preserves_kind_and_raw_descriptor()
    {
        var original = new ContractStreamEvent<TestTemplate>.Unclassified(LedgerOffset.At(7), UnclassifiedKind.Unknown, "TopologyEvent");

        var moved = original with { Offset = LedgerOffset.At(9) };

        moved.Offset.Should().Be(LedgerOffset.At(9));
        moved.Kind.Should().Be(UnclassifiedKind.Unknown);
        moved.RawKind.Should().Be("TopologyEvent");
    }

    [Fact]
    public void UnclassifiedKind_default_is_Unknown()
    {
        default(UnclassifiedKind).Should().Be(UnclassifiedKind.Unknown);
    }

    private sealed record DecodableTemplate(Party Owner)
        : ITemplate, IDamlRecord<DecodableTemplate>, IHasKey<DecodableTemplate, Party>
    {
        public static Identifier TemplateId { get; } = new("pkg", "M", "DecodableTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "test";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public static KeyDescriptor<DecodableTemplate, Party> Key { get; } = new()
        {
            KeyEncoder = key => DamlRecord.Create(new DamlField("owner", key.ToDamlValue())),
            KeyDecoder = value =>
                Party.FromDamlValue(value.As<DamlRecord>().GetRequiredField("owner").As<DamlParty>()),
            KeyJsonReader = (_, _) => throw new NotImplementedException(),
        };

        public DamlRecord ToRecord() => DamlRecord.Create(new DamlField("owner", Owner.ToDamlValue()));

        public static DecodableTemplate FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            throw new NotSupportedException();
    }

    private sealed record TestTemplate(string Owner) : ITemplate, IDamlRecord<TestTemplate>
    {
        public static Identifier TemplateId { get; } = new("pkg", "M", "TestTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "test";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(new DamlField("owner", new DamlText(Owner)));

        public static TestTemplate FromRecord(DamlRecord record) =>
            new((record.GetField("owner") as DamlText)?.Value ?? string.Empty);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            throw new NotSupportedException();
    }
}
