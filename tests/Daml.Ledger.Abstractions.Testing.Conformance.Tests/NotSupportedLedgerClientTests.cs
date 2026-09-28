// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

/// <summary>
/// Verifies that a bare <see cref="NotSupportedLedgerClient"/> subclass overriding none
/// of the 11 throwing members still throws <see cref="NotSupportedException"/> from each
/// of them, so a fake deriving from it that forgets to override a member it actually
/// exercises fails loudly rather than silently returning a default value.
/// </summary>
public sealed class NotSupportedLedgerClientTests
{
    private static readonly SubmitterInfo Submitter = new(new Party("alice"));

    private sealed class BareClient : NotSupportedLedgerClient
    {
    }

    [Fact]
    public void TryExerciseAsync_throws_NotSupportedException()
    {
        var client = new BareClient();

        Action act = () => client.TryExerciseAsync<int>(
            new ExerciseCommand(
                new Identifier("pkg", "M", "T"),
                new ContractId<ConformanceProbe>("c1"),
                new ChoiceName("Do"),
                DamlRecord.Create()),
            Submitter);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void SubmitAndWaitAsync_throws_NotSupportedException()
    {
        var client = new BareClient();

        Action act = () => client.SubmitAndWaitAsync(
            new CommandsSubmission([]), Submitter);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void TrySubmitAndWaitForTransactionAsync_throws_NotSupportedException()
    {
        var client = new BareClient();

        Action act = () => client.TrySubmitAndWaitForTransactionAsync(
            new CommandsSubmission([]), Submitter);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void TryCreateAsync_throws_NotSupportedException()
    {
        var client = new BareClient();

        Action act = () => client.TryCreateAsync(new ConformanceProbe("alice"), Submitter);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void SubscribeAsync_throws_NotSupportedException()
    {
        var client = new BareClient();

        var act = () => client.SubscribeAsync<ConformanceProbe>(Submitter);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void SubscribeLedgerEffectsAsync_throws_NotSupportedException()
    {
        var client = new BareClient();

        var act = () => client.SubscribeLedgerEffectsAsync<ConformanceProbe>(Submitter);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void SubscribeActiveAsync_throws_NotSupportedException()
    {
        var client = new BareClient();

        var act = () => client.SubscribeActiveAsync<ConformanceProbe>(Submitter);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task GetLedgerEndAsync_throws_NotSupportedException()
    {
        var client = new BareClient();

        var act = () => client.GetLedgerEndAsync();

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public void SubscribeAsync_interface_family_throws_NotSupportedException()
    {
        var client = new BareClient();

        var act = () => client.SubscribeAsync(FakeViewDescriptor, Submitter);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void SubscribeLedgerEffectsAsync_interface_family_throws_NotSupportedException()
    {
        var client = new BareClient();

        var act = () => client.SubscribeLedgerEffectsAsync(FakeViewDescriptor, Submitter);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void SubscribeActiveAsync_interface_family_throws_NotSupportedException()
    {
        var client = new BareClient();

        var act = () => client.SubscribeActiveAsync(FakeViewDescriptor, Submitter);

        act.Should().Throw<NotSupportedException>();
    }

    private static ViewDescriptor<FakeInterfaceMarker, FakeInterfaceView> FakeViewDescriptor { get; } = new();

    private sealed record FakeInterfaceMarker : IDamlInterface, IHasView<FakeInterfaceView>
    {
        public static Identifier InterfaceId { get; } = new("iface-pkg", "M", "IFake");
        public static string PackageId => "iface-pkg";
        public static string PackageName => "fake-iface";
        public static Version PackageVersion { get; } = new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
    }

    private sealed record FakeInterfaceView : IDamlRecord<FakeInterfaceView>
    {
        public DamlRecord ToRecord() => DamlRecord.Create();

        public static FakeInterfaceView FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            throw new NotSupportedException();
    }
}
