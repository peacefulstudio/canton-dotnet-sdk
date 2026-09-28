// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Rest.Client;
using Daml.Runtime;
using Daml.Runtime.Data;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Pins that the <c>DeduplicationPeriod</c> a caller sets on a
/// <see cref="RuntimeCommands.CommandsSubmission"/> reaches the wire as the same arm of the
/// <c>deduplication_period</c> oneof on both transports. gRPC carries a protobuf <c>Duration</c> or
/// an <c>int64</c> offset, REST the served document's duration string or offset string, so the
/// assertion is on the period both encodings denote, not on their bytes. Both command builders are
/// <c>internal</c>; this project reaches them through <c>InternalsVisibleTo</c> from each client.
/// </summary>
public class LedgerDeduplicationWireParityTests
{
    private static readonly Party Alice = new("party::alice");

    private static RuntimeCommands.CommandsSubmission Submission(RuntimeCommands.DeduplicationPeriod? period) =>
        RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(new RuntimeIdentifier("pkg", "Module", "Template"), new DamlRecord(null, [])))
            .WithActAs(Alice)
            .WithDeduplicationPeriod(period);

    private static Com.Daml.Ledger.Api.V2.Commands OverGrpc(RuntimeCommands.CommandsSubmission submission) =>
        new GrpcCommandBuilder(new LedgerClientOptions { GrpcAddress = "https://localhost:5001", UserId = "u" })
            .BuildCommands(submission);

    private static Rest.Client.Raw.Commands OverRest(RuntimeCommands.CommandsSubmission submission) =>
        RestCommandBuilder.BuildCommands(submission, userId: "u");

    [Fact]
    public void A_duration_period_reaches_the_wire_as_the_same_length_on_both_transports()
    {
        var submission = Submission(new RuntimeCommands.DeduplicationPeriod.Duration(TimeSpan.FromMinutes(5)));

        var overGrpc = OverGrpc(submission);
        var overRest = OverRest(submission);

        overGrpc.DeduplicationDuration.ToTimeSpan().Should().Be(TimeSpan.FromSeconds(300));
        overRest.DeduplicationPeriod.DeduplicationDuration.Should().Be("300s");
        overRest.DeduplicationPeriod.DeduplicationOffset.Should().BeNull();
    }

    [Fact]
    public void An_offset_period_reaches_the_wire_as_the_same_offset_on_both_transports()
    {
        var submission = Submission(new RuntimeCommands.DeduplicationPeriod.Offset(LedgerOffset.At(17)));

        var overGrpc = OverGrpc(submission);
        var overRest = OverRest(submission);

        overGrpc.DeduplicationOffset.Should().Be(17L);
        overRest.DeduplicationPeriod.DeduplicationOffset.Should().Be("17");
        overRest.DeduplicationPeriod.DeduplicationDuration.Should().BeNull();
    }

    [Fact]
    public void No_period_leaves_the_oneof_unset_on_both_transports()
    {
        var submission = Submission(period: null);

        var overGrpc = OverGrpc(submission);
        var overRest = OverRest(submission);

        overGrpc.DeduplicationPeriodCase.Should().Be(
            Com.Daml.Ledger.Api.V2.Commands.DeduplicationPeriodOneofCase.None);
        overRest.DeduplicationPeriod.Should().BeNull();
    }
}
