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

public class SynchronizerIdFieldTypingTests
{
    private static readonly SynchronizerId Source = new("global_sync::abc::35-0");
    private static readonly SynchronizerId Target = new("local_sync::def::35-0");
    private static readonly SynchronizerId Domain = new("event_sync::xyz::35-0");
    private static readonly Party Alice = new("alice");

    [Fact]
    public void Created_SynchronizerId_should_be_typed_as_SynchronizerId()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Created(
            new ContractId<TestTemplate>("c1"),
            new TestTemplate("alice"),
            null,
            LedgerOffset.At(1),
            Domain,
            [Alice]);

        ev.SynchronizerId.Should().BeOfType<SynchronizerId>().And.Be(Domain);
    }

    [Fact]
    public void Archived_SynchronizerId_should_be_typed_as_SynchronizerId()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Archived(
            new ContractId<TestTemplate>("c1"),
            LedgerOffset.At(2),
            Domain,
            [Alice]);

        ev.SynchronizerId.Should().BeOfType<SynchronizerId>().And.Be(Domain);
    }

    [Fact]
    public void Exercised_SynchronizerId_should_be_typed_as_SynchronizerId()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Exercised(
            new ContractId<TestTemplate>("c1"),
            new ChoiceName("Accept"),
            DamlUnit.Instance,
            DamlUnit.Instance,
            true,
            LedgerOffset.At(3),
            Domain,
            [Alice]);

        ev.SynchronizerId.Should().BeOfType<SynchronizerId>().And.Be(Domain);
    }

    [Fact]
    public void Assigned_Source_should_be_typed_as_SynchronizerId()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Assigned(
            new ContractId<TestTemplate>("c1"),
            new TestTemplate("alice"),
            null,
            LedgerOffset.At(4),
            Source,
            Target,
            "reassignment-1",
            7L,
            [Alice]);

        ev.Source.Should().BeOfType<SynchronizerId>().And.Be(Source);
    }

    [Fact]
    public void Assigned_Target_should_be_typed_as_SynchronizerId()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Assigned(
            new ContractId<TestTemplate>("c1"),
            new TestTemplate("alice"),
            null,
            LedgerOffset.At(4),
            Source,
            Target,
            "reassignment-1",
            7L,
            [Alice]);

        ev.Target.Should().BeOfType<SynchronizerId>().And.Be(Target);
    }

    [Fact]
    public void Unassigned_Source_should_be_typed_as_SynchronizerId()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Unassigned(
            new ContractId<TestTemplate>("c1"),
            LedgerOffset.At(5),
            Source,
            Target,
            "reassignment-1",
            7L,
            [Alice]);

        ev.Source.Should().BeOfType<SynchronizerId>().And.Be(Source);
    }

    [Fact]
    public void Unassigned_Target_should_be_typed_as_SynchronizerId()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Unassigned(
            new ContractId<TestTemplate>("c1"),
            LedgerOffset.At(5),
            Source,
            Target,
            "reassignment-1",
            7L,
            [Alice]);

        ev.Target.Should().BeOfType<SynchronizerId>().And.Be(Target);
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
