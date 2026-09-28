// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Streams;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class WitnessPartiesTypedTests
{
    private static readonly Party Alice = new("alice");
    private static readonly Party Bob = new("bob");

    [Fact]
    public void Created_WitnessParties_should_be_Party_list()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Created(
            new ContractId<TestTemplate>("c1"),
            new TestTemplate("alice"),
            null,
            LedgerOffset.At(1),
            new SynchronizerId("sync"),
            [Alice, Bob]);

        ev.WitnessParties.Should().BeAssignableTo<IReadOnlyList<Party>>();
        ev.WitnessParties.Should().Equal(Alice, Bob);
    }

    [Fact]
    public void Archived_WitnessParties_should_be_Party_list()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Archived(
            new ContractId<TestTemplate>("c1"),
            LedgerOffset.At(2),
            new SynchronizerId("sync"),
            [Alice]);

        ev.WitnessParties.Should().BeAssignableTo<IReadOnlyList<Party>>();
        ev.WitnessParties.Should().ContainSingle().Which.Should().Be(Alice);
    }

    [Fact]
    public void Exercised_WitnessParties_should_be_Party_list()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Exercised(
            new ContractId<TestTemplate>("c1"),
            new ChoiceName("Accept"),
            DamlUnit.Instance,
            DamlUnit.Instance,
            Consuming: true,
            Offset: LedgerOffset.At(3),
            SynchronizerId: new SynchronizerId("sync"),
            WitnessParties: [Alice, Bob]);

        ev.WitnessParties.Should().BeAssignableTo<IReadOnlyList<Party>>();
        ev.WitnessParties.Should().Equal(Alice, Bob);
    }

    [Fact]
    public void Assigned_WitnessParties_should_be_Party_list()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Assigned(
            new ContractId<TestTemplate>("c1"),
            new TestTemplate("alice"),
            null,
            LedgerOffset.At(4),
            new SynchronizerId("src"),
            new SynchronizerId("tgt"),
            "reassignment-1",
            7L,
            [Alice]);

        ev.WitnessParties.Should().BeAssignableTo<IReadOnlyList<Party>>();
        ev.WitnessParties.Should().ContainSingle().Which.Should().Be(Alice);
    }

    [Fact]
    public void Unassigned_WitnessParties_should_be_Party_list()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Unassigned(
            new ContractId<TestTemplate>("c1"),
            LedgerOffset.At(5),
            new SynchronizerId("src"),
            new SynchronizerId("tgt"),
            "reassignment-1",
            7L,
            [Alice]);

        ev.WitnessParties.Should().BeAssignableTo<IReadOnlyList<Party>>();
        ev.WitnessParties.Should().ContainSingle().Which.Should().Be(Alice);
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
