// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using Daml.Runtime.Streams;
using Xunit;

namespace Daml.Runtime.Tests;

public class AcsSnapshotEntryJsonTests
{
    private static readonly JsonSerializerOptions Options =
        new JsonSerializerOptions { Converters = { new DamlValueJsonConverter() } }.AddDamlConverters();

    private static readonly ContractId<TestTemplate> Id = new("c1");
    private static readonly SynchronizerId Synchronizer = new("sync");
    private static readonly EquatableArray<Party> Witnesses = [new Party("alice")];

    [Fact]
    public void Created_declared_as_its_base_type_writes_the_case_discriminator_and_reads_back()
    {
        AcsSnapshotEntry<TestTemplate> value = new AcsSnapshotEntry<TestTemplate>.Created(
            Id, new TestTemplate("alice"), null, LedgerOffset.At(1), Synchronizer, Witnesses);

        var written = JsonSerializer.Serialize(value);

        written.Should().Be(
            """{"$case":"Created","ContractId":"c1","Payload":{"Owner":"alice"},"Key":null,"Offset":1,"SynchronizerId":"sync","WitnessParties":["alice"],"Disclosure":null}""");
        JsonSerializer.Deserialize<AcsSnapshotEntry<TestTemplate>>(written).Should().Be(value);
    }

    [Fact]
    public void Created_reads_its_own_write_back_with_a_key_and_a_disclosure()
    {
        AcsSnapshotEntry<TestTemplate> value = new AcsSnapshotEntry<TestTemplate>.Created(
            Id,
            new TestTemplate("alice"),
            new ContractKey(new DamlText("savings"), TestTemplate.TemplateId) { KeyHash = "hash-1" },
            LedgerOffset.At(1),
            Synchronizer,
            Witnesses)
        {
            Disclosure = new DisclosedContract("c1", TestTemplate.TemplateId, new byte[] { 1, 2, 3 }),
        };

        var written = JsonSerializer.Serialize(value, Options);

        JsonSerializer.Deserialize<AcsSnapshotEntry<TestTemplate>>(written, Options).Should().Be(value);
    }

    [Fact]
    public void Unclassified_declared_as_its_base_type_writes_the_case_discriminator_and_reads_back()
    {
        AcsSnapshotEntry<TestTemplate> value = new AcsSnapshotEntry<TestTemplate>.Unclassified(
            LedgerOffset.At(7), UnclassifiedKind.Unknown, "TopologyEvent");

        var written = JsonSerializer.Serialize(value);

        written.Should().Be("""{"$case":"Unclassified","Offset":7,"Kind":0,"RawKind":"TopologyEvent"}""");
        JsonSerializer.Deserialize<AcsSnapshotEntry<TestTemplate>>(written).Should().Be(value);
    }

    [Fact]
    public void Unclassified_reads_its_own_write_back_with_a_null_offset()
    {
        AcsSnapshotEntry<TestTemplate> value = new AcsSnapshotEntry<TestTemplate>.Unclassified(
            null, UnclassifiedKind.DecodeFailure);

        var written = JsonSerializer.Serialize(value);

        JsonSerializer.Deserialize<AcsSnapshotEntry<TestTemplate>>(written).Should().Be(value);
    }

    [Fact]
    public void Checkpoint_declared_as_its_base_type_writes_the_case_discriminator_and_reads_back()
    {
        AcsSnapshotEntry<TestTemplate> value = new AcsSnapshotEntry<TestTemplate>.Checkpoint(
            new StakeholderResume(LedgerOffset.At(6)));

        var written = JsonSerializer.Serialize(value);

        written.Should().Be("""{"$case":"Checkpoint","Resume":{"Offset":6}}""");
        JsonSerializer.Deserialize<AcsSnapshotEntry<TestTemplate>>(written).Should().Be(value);
    }

    [Fact]
    public void StreamError_declared_as_its_base_type_writes_the_case_discriminator_and_reads_back()
    {
        AcsSnapshotEntry<TestTemplate> value = new AcsSnapshotEntry<TestTemplate>.StreamError(
            new TransportStatus.Grpc(GrpcStatusCode.Aborted),
            "the stream authorization is stale",
            DamlErrorCategory.ContentionOnSharedResources,
            "STALE_STREAM_AUTHORIZATION");

        var written = JsonSerializer.Serialize(value);

        written.Should().Be(
            """{"$case":"StreamError","Status":{"$case":"Grpc","StatusCode":10},"Message":"the stream authorization is stale","Category":2,"ErrorId":"STALE_STREAM_AUTHORIZATION"}""");
        JsonSerializer.Deserialize<AcsSnapshotEntry<TestTemplate>>(written).Should().Be(value);
    }

    [Fact]
    public void StreamError_carrying_an_exception_writes_without_it_and_reads_back_without_it()
    {
        AcsSnapshotEntry<TestTemplate> value = new AcsSnapshotEntry<TestTemplate>.StreamError(
            new TransportStatus.Grpc(GrpcStatusCode.Unavailable),
            "unavailable",
            DamlErrorCategory.TransientServerFailure,
            "STREAM_UNAVAILABLE",
            ThrownException("transport reset"));

        var written = JsonSerializer.Serialize(value);
        var read = JsonSerializer.Deserialize<AcsSnapshotEntry<TestTemplate>>(written);

        written.Should().NotContain("SourceException");
        read.Should().Be(new AcsSnapshotEntry<TestTemplate>.StreamError(
            new TransportStatus.Grpc(GrpcStatusCode.Unavailable),
            "unavailable",
            DamlErrorCategory.TransientServerFailure,
            "STREAM_UNAVAILABLE"));
    }

    [Fact]
    public void AcsSnapshotEntry_reads_back_on_options_that_register_the_daml_converters()
    {
        AcsSnapshotEntry<TestTemplate> value = new AcsSnapshotEntry<TestTemplate>.Checkpoint(
            new StakeholderResume(LedgerOffset.At(6)));

        var written = JsonSerializer.Serialize(value, Options);

        written.Should().Be("""{"$case":"Checkpoint","Resume":{"Offset":6}}""");
        JsonSerializer.Deserialize<AcsSnapshotEntry<TestTemplate>>(written, Options).Should().Be(value);
    }

    [Fact]
    public void AcsSnapshotEntry_refuses_an_unknown_case()
    {
        var act = () => JsonSerializer.Deserialize<AcsSnapshotEntry<TestTemplate>>("""{"$case":"Archived"}""");

        act.Should().Throw<JsonException>().WithMessage("*Archived*");
    }

    private static Exception ThrownException(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException caught)
        {
            return caught;
        }
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
