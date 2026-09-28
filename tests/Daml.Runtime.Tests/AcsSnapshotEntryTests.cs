// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using Daml.Runtime.Streams;
using Xunit;

namespace Daml.Runtime.Tests;

public sealed class AcsSnapshotEntryTests
{
    private const string LedgerKeyHash = "6CgQL9eNNqIjS5cB6/kK1IsqdxjcgXl/3kxSiUEkiBA=";

    [Fact]
    public void Checkpoint_carries_the_terminal_offset_as_a_StakeholderResume()
    {
        var resume = new StakeholderResume(LedgerOffset.At(9));
        var entry = new AcsSnapshotEntry<TestTemplate>.Checkpoint(resume);
        entry.Resume.Should().Be(resume);
        entry.Resume.Offset.Should().Be(LedgerOffset.At(9));
    }

    [Fact]
    public void Variants_are_distinguishable_via_pattern_match()
    {
        AcsSnapshotEntry<TestTemplate> entry =
            new AcsSnapshotEntry<TestTemplate>.Unclassified(LedgerOffset.At(3), UnclassifiedKind.DecodeFailure);

        var matched = entry switch
        {
            AcsSnapshotEntry<TestTemplate>.Created => "created",
            AcsSnapshotEntry<TestTemplate>.Unclassified => "unclassified",
            AcsSnapshotEntry<TestTemplate>.Checkpoint => "checkpoint",
            AcsSnapshotEntry<TestTemplate>.StreamError => "error",
            _ => "other",
        };

        matched.Should().Be("unclassified");
    }

    [Fact]
    public void StreamError_is_distinguishable_via_pattern_match()
    {
        AcsSnapshotEntry<TestTemplate> entry =
            new AcsSnapshotEntry<TestTemplate>.StreamError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "unavailable");

        var matched = entry switch
        {
            AcsSnapshotEntry<TestTemplate>.Created => "created",
            AcsSnapshotEntry<TestTemplate>.Unclassified => "unclassified",
            AcsSnapshotEntry<TestTemplate>.Checkpoint => "checkpoint",
            AcsSnapshotEntry<TestTemplate>.StreamError => "error",
            _ => "other",
        };

        matched.Should().Be("error");
    }

    [Fact]
    public void StreamError_Status_is_a_closed_TransportStatus_union()
    {
        var error = new AcsSnapshotEntry<TestTemplate>.StreamError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "unavailable");

        error.Status.Should().BeOfType<TransportStatus.Grpc>();
        error.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        error.Message.Should().Be("unavailable");
    }

    [Fact]
    public void StreamError_carries_the_classification_the_transport_determined()
    {
        var error = new AcsSnapshotEntry<TestTemplate>.StreamError(
            new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "unavailable", DamlErrorCategory.TransientServerFailure);

        error.Category.Should().Be(DamlErrorCategory.TransientServerFailure);
    }

    [Fact]
    public void StreamError_leaves_the_classification_null_when_the_transport_determined_none()
    {
        var error = new AcsSnapshotEntry<TestTemplate>.StreamError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "unavailable");

        error.Category.Should().BeNull();
    }

    [Fact]
    public void StreamError_carries_the_error_id_the_transport_parsed()
    {
        var error = new AcsSnapshotEntry<TestTemplate>.StreamError(
            new TransportStatus.Grpc(GrpcStatusCode.Aborted),
            "the stream authorization is stale",
            DamlErrorCategory.ContentionOnSharedResources,
            "STALE_STREAM_AUTHORIZATION");

        error.ErrorId.Should().Be(
            "STALE_STREAM_AUTHORIZATION",
            "a snapshot that faults terminally leaves the caller nothing to classify the fault by unless "
            + "the participant's error id survives to this entry");
    }

    [Fact]
    public void StreamError_leaves_the_error_id_null_when_no_structured_error_was_attached()
    {
        var error = new AcsSnapshotEntry<TestTemplate>.StreamError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "unavailable");

        error.ErrorId.Should().BeNull(
            "a transport that decoded no structured error has to say so, rather than invent a sentinel "
            + "a consumer would then have to recognise as meaning nothing was parsed");
    }

    [Fact]
    public void StreamError_with_same_payload_should_be_value_equal()
    {
        var a = new AcsSnapshotEntry<TestTemplate>.StreamError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "unavailable");
        var b = new AcsSnapshotEntry<TestTemplate>.StreamError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "unavailable");
        a.Should().Be(b);
    }

    [Fact]
    public void Created_carries_its_contract_and_fields()
    {
        var contractId = new ContractId<TestTemplate>("c1");
        var payload = new TestTemplate("alice");
        var offset = LedgerOffset.At(4);
        var synchronizerId = new SynchronizerId("sync");
        EquatableArray<Party> witnessParties = [new Party("alice")];

        var entry = new AcsSnapshotEntry<TestTemplate>.Created(contractId, payload, null, offset, synchronizerId, witnessParties);

        entry.ContractId.Should().Be(contractId);
        entry.Offset.Should().Be(offset);
        entry.Payload.Should().Be(payload);
    }

    [Fact]
    public void Unclassified_should_expose_offset_and_enumerated_kind()
    {
        var unclassified = new AcsSnapshotEntry<TestTemplate>.Unclassified(
            LedgerOffset.At(7), UnclassifiedKind.DecodeFailure);

        unclassified.Offset.Should().Be(
            LedgerOffset.At(7),
            "a projector that does know where the unclassifiable row sat must still report it, so the nullable slot has to round-trip a real offset unchanged");
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.RawKind.Should().BeNull();
    }

    [Fact]
    public void Unclassified_unknown_kind_preserves_the_raw_descriptor()
    {
        var unclassified = new AcsSnapshotEntry<TestTemplate>.Unclassified(
            LedgerOffset.At(7), UnclassifiedKind.Unknown, "ACTIVE_CONTRACT");

        unclassified.Kind.Should().Be(UnclassifiedKind.Unknown);
        unclassified.RawKind.Should().Be("ACTIVE_CONTRACT");
    }

    [Fact]
    public void Unclassified_rejects_a_raw_descriptor_on_an_enumerated_kind()
    {
        var act = () => new AcsSnapshotEntry<TestTemplate>.Unclassified(
            LedgerOffset.At(7), UnclassifiedKind.DecodeFailure, "ACTIVE_CONTRACT");

        act.Should().Throw<ArgumentException>()
            .WithMessage("An Unclassified snapshot row with the enumerated Kind 'DecodeFailure' must not carry a RawKind; RawKind is populated only for Unknown.*")
            .WithParameterName("RawKind");
    }

    [Fact]
    public void Unclassified_rejects_an_unknown_kind_without_a_raw_descriptor()
    {
        var act = () => new AcsSnapshotEntry<TestTemplate>.Unclassified(
            LedgerOffset.At(7), UnclassifiedKind.Unknown, null);

        act.Should().Throw<ArgumentException>()
            .WithMessage("An Unclassified snapshot row with Kind Unknown must carry the transport's raw descriptor in RawKind.*")
            .WithParameterName("RawKind");
    }

    [Fact]
    public void Unclassified_accepts_a_null_offset_and_preserves_it()
    {
        var unclassified = new AcsSnapshotEntry<TestTemplate>.Unclassified(
            null, UnclassifiedKind.DecodeFailure);

        unclassified.Offset.Should().BeNull(
            "a snapshot row the projector could not place on the ledger has no offset to report, and the only alternative is fabricating LedgerOffset.Begin, which is indistinguishable from a genuine ledger start: a consumer persisting it as resume state checkpoints at the beginning of the ledger and re-reads the entire stream");
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.RawKind.Should().BeNull();
    }

    [Fact]
    public void Unclassified_without_an_offset_still_rejects_a_raw_descriptor_on_an_enumerated_kind()
    {
        var act = () => new AcsSnapshotEntry<TestTemplate>.Unclassified(
            null, UnclassifiedKind.DecodeFailure, "ACTIVE_CONTRACT");

        act.Should().Throw<ArgumentException>(
            "widening Offset to a nullable must not relax the Kind and RawKind invariant: an enumerated kind carrying a raw descriptor stays a construction error")
            .WithMessage("An Unclassified snapshot row with the enumerated Kind 'DecodeFailure' must not carry a RawKind; RawKind is populated only for Unknown.*")
            .WithParameterName("RawKind");
    }

    [Fact]
    public void Unclassified_without_an_offset_still_rejects_an_unknown_kind_without_a_raw_descriptor()
    {
        var act = () => new AcsSnapshotEntry<TestTemplate>.Unclassified(
            null, UnclassifiedKind.Unknown, null);

        act.Should().Throw<ArgumentException>(
            "widening Offset to a nullable must not relax the Kind and RawKind invariant: Unknown without the transport's raw descriptor stays a construction error")
            .WithMessage("An Unclassified snapshot row with Kind Unknown must carry the transport's raw descriptor in RawKind.*")
            .WithParameterName("RawKind");
    }

    [Fact]
    public void ToContract_pairs_the_decoded_payload_with_its_contract_id()
    {
        var created = new AcsSnapshotEntry<DecodableTemplate>.Created(
            new ContractId<DecodableTemplate>("c1"),
            new DecodableTemplate(new Party("alice")),
            null,
            LedgerOffset.At(4),
            new SynchronizerId("sync"),
            [new Party("alice")]);

        var contract = created.ToContract();

        contract.Id.Value.Should().Be("c1");
        contract.Data.Owner.Should().Be(new Party("alice"));
    }

    [Fact]
    public void ToContract_carries_the_contract_key_onto_the_contract()
    {
        var created = new AcsSnapshotEntry<DecodableTemplate>.Created(
            new ContractId<DecodableTemplate>("c1"),
            new DecodableTemplate(new Party("alice")),
            new ContractKey(DamlRecord.Create(new DamlField("owner", new DamlParty("alice")))),
            LedgerOffset.At(4),
            new SynchronizerId("sync"),
            [new Party("alice")]);

        var contract = created.ToContract<DecodableTemplate, Party>();

        contract.Key.Value.Should().Be(
            new Party("alice"),
            "Contract is what the snapshot bridge hands back, so a projection that drops the key here "
            + "makes contract.Key unreachable no matter what the transport read off the wire");
    }

    [Fact]
    public void ToContract_rejects_a_keyed_projection_of_a_row_carrying_no_key()
    {
        var created = new AcsSnapshotEntry<DecodableTemplate>.Created(
            new ContractId<DecodableTemplate>("c1"),
            new DecodableTemplate(new Party("alice")),
            null,
            LedgerOffset.At(4),
            new SynchronizerId("sync"),
            [new Party("alice")]);

        var projecting = () => created.ToContract<DecodableTemplate, Party>();

        projecting.Should().Throw<InvalidOperationException>(
            "the keyed contract's key is non-nullable, so a row with no key has to fail loudly "
            + "rather than reach a caller through a shape whose type says the key is present");
    }

    [Fact]
    public void ToContract_carries_the_key_hash_onto_the_contract()
    {
        var created = new AcsSnapshotEntry<DecodableTemplate>.Created(
            new ContractId<DecodableTemplate>("c1"),
            new DecodableTemplate(new Party("alice")),
            new ContractKey(
                DamlRecord.Create(new DamlField("owner", new DamlParty("alice"))),
                DecodableTemplate.TemplateId)
            {
                KeyHash = LedgerKeyHash,
            },
            LedgerOffset.At(4),
            new SynchronizerId("sync"),
            [new Party("alice")]);

        var contract = created.ToContract<DecodableTemplate, Party>();

        contract.Key.Hash.Should().Be(
            LedgerKeyHash,
            "Contract is what the snapshot bridge hands back, so a hash dropped here is unreachable "
            + "no matter what the transport read off the wire");
    }

    [Fact]
    public void ToActiveContract_carries_the_rows_last_update_offset_and_synchronizer()
    {
        var created = new AcsSnapshotEntry<DecodableTemplate>.Created(
            new ContractId<DecodableTemplate>("c1"),
            new DecodableTemplate(new Party("alice")),
            null,
            LedgerOffset.At(4),
            new SynchronizerId("sync"),
            [new Party("alice")]);

        var active = created.ToActiveContract();

        active.Contract.Id.Value.Should().Be("c1");
        active.LastUpdateOffset.Should().Be(
            LedgerOffset.At(4),
            "the carrier exists to keep the row's provenance reachable after the projection");
        active.SynchronizerId.Should().Be(new SynchronizerId("sync"));
    }

    [Fact]
    public void ToActiveContract_carries_the_decoded_key_and_its_hash_alongside_the_provenance()
    {
        var created = new AcsSnapshotEntry<DecodableTemplate>.Created(
            new ContractId<DecodableTemplate>("c1"),
            new DecodableTemplate(new Party("alice")),
            new ContractKey(
                DamlRecord.Create(new DamlField("owner", new DamlParty("alice"))),
                DecodableTemplate.TemplateId)
            {
                KeyHash = LedgerKeyHash,
            },
            LedgerOffset.At(4),
            new SynchronizerId("sync"),
            [new Party("alice")]);

        var active = created.ToActiveContract<DecodableTemplate, Party>();

        active.Contract.Key.Value.Should().Be(new Party("alice"));
        active.Contract.Key.Hash.Should().Be(LedgerKeyHash);
        active.LastUpdateOffset.Should().Be(LedgerOffset.At(4));
        active.SynchronizerId.Should().Be(new SynchronizerId("sync"));
    }

    [Fact]
    public void ToActiveContract_rejects_a_keyed_projection_of_a_row_carrying_no_key()
    {
        var created = new AcsSnapshotEntry<DecodableTemplate>.Created(
            new ContractId<DecodableTemplate>("c1"),
            new DecodableTemplate(new Party("alice")),
            null,
            LedgerOffset.At(4),
            new SynchronizerId("sync"),
            [new Party("alice")]);

        var projecting = () => created.ToActiveContract<DecodableTemplate, Party>();

        projecting.Should().Throw<InvalidOperationException>(
            "wrapping the projection in a provenance carrier must not soften the keyed shape's "
            + "own failure: a row with no key still has to fail loudly rather than reach a caller "
            + "through a shape whose type says the key is present");
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
