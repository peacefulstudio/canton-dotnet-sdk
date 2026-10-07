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

public class InterfaceAcsSnapshotEntryJsonTests
{
    private static readonly JsonSerializerOptions Options =
        new JsonSerializerOptions { Converters = { new DamlValueJsonConverter() } }.AddDamlConverters();

    private static readonly ContractId<TestInterface> Id = new("c1");
    private static readonly Identifier InterfaceId = new("pkg", "M", "TestInterface");
    private static readonly SynchronizerId Synchronizer = new("sync");
    private static readonly EquatableArray<Party> Witnesses = [new Party("alice")];

    [Fact]
    public void Created_declared_as_its_base_type_writes_the_case_discriminator_and_reads_back()
    {
        InterfaceAcsSnapshotEntry<TestInterface, TestView> value = new InterfaceAcsSnapshotEntry<TestInterface, TestView>.Created(
            Id, new TestView("bronze"), null, LedgerOffset.At(1), Synchronizer, Witnesses);

        var written = JsonSerializer.Serialize(value);

        written.Should().Be(
            """{"$case":"Created","ContractId":"c1","Payload":{"Tier":"bronze"},"Key":null,"Offset":1,"SynchronizerId":"sync","WitnessParties":["alice"],"Disclosure":null}""");
        JsonSerializer.Deserialize<InterfaceAcsSnapshotEntry<TestInterface, TestView>>(written).Should().Be(value);
    }

    [Fact]
    public void Created_reads_its_own_write_back_with_a_key_and_a_disclosure()
    {
        InterfaceAcsSnapshotEntry<TestInterface, TestView> value = new InterfaceAcsSnapshotEntry<TestInterface, TestView>.Created(
            Id,
            new TestView("bronze"),
            new ContractKey(new DamlText("savings"), InterfaceId) { KeyHash = "hash-1" },
            LedgerOffset.At(1),
            Synchronizer,
            Witnesses)
        {
            Disclosure = new DisclosedContract("c1", InterfaceId, new byte[] { 1, 2, 3 }),
        };

        var written = JsonSerializer.Serialize(value, Options);

        JsonSerializer.Deserialize<InterfaceAcsSnapshotEntry<TestInterface, TestView>>(written, Options).Should().Be(value);
    }

    [Fact]
    public void Unclassified_declared_as_its_base_type_writes_the_case_discriminator_and_reads_back()
    {
        InterfaceAcsSnapshotEntry<TestInterface, TestView> value = new InterfaceAcsSnapshotEntry<TestInterface, TestView>.Unclassified(
            LedgerOffset.At(7), UnclassifiedKind.Unknown, "TopologyEvent");

        var written = JsonSerializer.Serialize(value);

        written.Should().Be("""{"$case":"Unclassified","Offset":7,"Kind":0,"RawKind":"TopologyEvent"}""");
        JsonSerializer.Deserialize<InterfaceAcsSnapshotEntry<TestInterface, TestView>>(written).Should().Be(value);
    }

    [Fact]
    public void Unclassified_reads_its_own_write_back_with_a_null_offset()
    {
        InterfaceAcsSnapshotEntry<TestInterface, TestView> value = new InterfaceAcsSnapshotEntry<TestInterface, TestView>.Unclassified(
            null, UnclassifiedKind.DecodeFailure);

        var written = JsonSerializer.Serialize(value);

        JsonSerializer.Deserialize<InterfaceAcsSnapshotEntry<TestInterface, TestView>>(written).Should().Be(value);
    }

    [Fact]
    public void Checkpoint_declared_as_its_base_type_writes_the_case_discriminator_and_reads_back()
    {
        InterfaceAcsSnapshotEntry<TestInterface, TestView> value = new InterfaceAcsSnapshotEntry<TestInterface, TestView>.Checkpoint(
            new StakeholderResume(LedgerOffset.At(6)));

        var written = JsonSerializer.Serialize(value);

        written.Should().Be("""{"$case":"Checkpoint","Resume":{"Offset":6}}""");
        JsonSerializer.Deserialize<InterfaceAcsSnapshotEntry<TestInterface, TestView>>(written).Should().Be(value);
    }

    [Fact]
    public void StreamError_declared_as_its_base_type_writes_the_case_discriminator_and_reads_back()
    {
        InterfaceAcsSnapshotEntry<TestInterface, TestView> value = new InterfaceAcsSnapshotEntry<TestInterface, TestView>.StreamError(
            new TransportStatus.Grpc(GrpcStatusCode.Aborted),
            "the stream authorization is stale",
            DamlErrorCategory.ContentionOnSharedResources,
            "STALE_STREAM_AUTHORIZATION");

        var written = JsonSerializer.Serialize(value);

        written.Should().Be(
            """{"$case":"StreamError","Status":{"$case":"Grpc","StatusCode":10},"Message":"the stream authorization is stale","Category":2,"ErrorId":"STALE_STREAM_AUTHORIZATION"}""");
        JsonSerializer.Deserialize<InterfaceAcsSnapshotEntry<TestInterface, TestView>>(written).Should().Be(value);
    }

    [Fact]
    public void StreamError_carrying_an_exception_writes_without_it_and_reads_back_without_it()
    {
        InterfaceAcsSnapshotEntry<TestInterface, TestView> value = new InterfaceAcsSnapshotEntry<TestInterface, TestView>.StreamError(
            new TransportStatus.Grpc(GrpcStatusCode.Unavailable),
            "unavailable",
            DamlErrorCategory.TransientServerFailure,
            "STREAM_UNAVAILABLE",
            ThrownException("transport reset"));

        var written = JsonSerializer.Serialize(value);
        var read = JsonSerializer.Deserialize<InterfaceAcsSnapshotEntry<TestInterface, TestView>>(written);

        written.Should().NotContain("SourceException");
        read.Should().Be(new InterfaceAcsSnapshotEntry<TestInterface, TestView>.StreamError(
            new TransportStatus.Grpc(GrpcStatusCode.Unavailable),
            "unavailable",
            DamlErrorCategory.TransientServerFailure,
            "STREAM_UNAVAILABLE"));
    }

    [Fact]
    public void InterfaceAcsSnapshotEntry_reads_back_on_options_that_register_the_daml_converters()
    {
        InterfaceAcsSnapshotEntry<TestInterface, TestView> value = new InterfaceAcsSnapshotEntry<TestInterface, TestView>.Checkpoint(
            new StakeholderResume(LedgerOffset.At(6)));

        var written = JsonSerializer.Serialize(value, Options);

        written.Should().Be("""{"$case":"Checkpoint","Resume":{"Offset":6}}""");
        JsonSerializer.Deserialize<InterfaceAcsSnapshotEntry<TestInterface, TestView>>(written, Options).Should().Be(value);
    }

    [Fact]
    public void InterfaceAcsSnapshotEntry_refuses_an_unknown_case()
    {
        var act = () => JsonSerializer.Deserialize<InterfaceAcsSnapshotEntry<TestInterface, TestView>>("""{"$case":"Archived"}""");

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

    private interface TestInterface : IDamlInterface, IHasView<TestView>
    {
        static Identifier IDamlInterface.InterfaceId => new("pkg", "M", "TestInterface");
        static string IDamlInterface.PackageId => "pkg";
        static string IDamlInterface.PackageName => "test";
        static Version IDamlInterface.PackageVersion => new(0, 1, 0);
        static DamlTypeDescriptor IDamlType.DamlTypeId =>
            new(new Identifier("pkg", "M", "TestInterface"), DamlTypeKind.Interface, "test");
    }

    private sealed record TestView(string Tier) : IDamlRecord<TestView>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(new DamlField("tier", new DamlText(Tier)));

        public static TestView FromRecord(DamlRecord record) =>
            new((record.GetField("tier") as DamlText)?.Value ?? string.Empty);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            throw new NotSupportedException();
    }
}
