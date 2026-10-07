// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class FakeLedgerFailureParityTests
{
    private static readonly Party Alice = new("fake::alice");

    private static readonly RuntimeCommands.SubmitterInfo Submitter =
        new(new HashSet<Party> { Alice }, new HashSet<Party>());

    private static LedgerOperationException RejectedRead() => new(
        "contract not found",
        DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
        "CONTRACT_NOT_FOUND",
        new Dictionary<string, string>());

    private static LedgerOperationException UnansweredWrite() => new(
        "participant is unreachable",
        new TransportStatus.Grpc(GrpcStatusCode.Unavailable));

    private static LedgerOperationException UnansweredRead() => new(
        "read got no answer",
        new TransportStatus.NoResponse(),
        CommitState.NotCommitted);

    private static ICantonLedgerClient ClientFailingWith(LedgerOperationException failure) =>
        FakeLedgerClient.Create().WithThrowingCallFailure(failure).Build();

    private static void AssertRejectedReadReachedIntact(LedgerOperationException raised)
    {
        raised.Status.Should().BeNull();
        raised.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing);
        raised.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
        raised.CommitState.Should().Be(CommitState.NotCommitted);
    }

    private static void AssertUnansweredWriteReachedIntact(LedgerOperationException raised)
    {
        raised.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        raised.Category.Should().BeNull();
        raised.ErrorId.Should().BeNull();
        raised.CommitState.Should().Be(CommitState.Unknown);
    }

    [Fact]
    public async Task GetLedgerEndAsync_surfaces_a_staged_read_failure_as_NotCommitted_with_its_contract_fields_intact()
    {
        var client = ClientFailingWith(RejectedRead());

        var act = () => client.GetLedgerEndAsync();

        AssertRejectedReadReachedIntact((await act.Should().ThrowAsync<LedgerOperationException>()).Which);
    }

    [Fact]
    public async Task GetContractAsync_surfaces_a_staged_read_failure_as_NotCommitted_with_its_contract_fields_intact()
    {
        var client = ClientFailingWith(RejectedRead());

        var act = () => client.GetContractAsync(new ContractId<Marker>("00fake-marker"), Submitter);

        AssertRejectedReadReachedIntact((await act.Should().ThrowAsync<LedgerOperationException>()).Which);
    }

    [Fact]
    public async Task SubmitAsync_surfaces_a_staged_unanswered_write_as_Unknown_with_its_contract_fields_intact()
    {
        var client = ClientFailingWith(UnansweredWrite());
        var submission = RuntimeCommands.CommandsSubmission
            .Single(RuntimeCommands.CreateCommand.For(new Marker(Alice)))
            .WithActAs(Alice);

        var act = () => client.SubmitAsync(submission);

        AssertUnansweredWriteReachedIntact((await act.Should().ThrowAsync<LedgerOperationException>()).Which);
    }

    [Fact]
    public async Task GetContractAsync_surfaces_a_staged_unanswered_read_as_the_exact_instance_with_NoResponse_and_NotCommitted()
    {
        var staged = UnansweredRead();
        var client = ClientFailingWith(staged);

        var act = () => client.GetContractAsync(new ContractId<Marker>("00fake-marker"), Submitter);

        var raised = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        raised.Should().BeSameAs(staged);
        raised.Status.Should().Be(new TransportStatus.NoResponse());
        raised.Category.Should().BeNull();
        raised.ErrorId.Should().BeNull();
        raised.CommitState.Should().Be(CommitState.NotCommitted);
    }
}
