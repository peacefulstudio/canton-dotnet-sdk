// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class CompletionEventsTests
{
    private static readonly Party Bob = new("bob");

    private static Completion CompletionFor(string commandId, long offset) =>
        new(
            new CommandId(commandId),
            LedgerOffset.At(offset),
            [Bob],
            new SynchronizerTime("sync1", DateTimeOffset.UnixEpoch),
            SubmissionId: null,
            UserId: null,
            DeduplicationPeriod: null,
            PaidTrafficCost: 0,
            TraceContext: null);

    [Fact]
    public void CompletionEvents_CommandAccepted_wraps_the_CompletionStreamEvent_CommandAccepted_variant()
    {
        var completion = CompletionFor("cmd-1", 7);

        var accepted = CompletionEvents.CommandAccepted(completion, "update-1");

        var variant = accepted.Should().BeOfType<CompletionStreamEvent.CommandAccepted>().Subject;
        variant.Completion.Should().BeSameAs(completion);
        variant.UpdateId.Should().Be("update-1");
    }

    [Fact]
    public void CompletionEvents_CommandRejected_wraps_the_CompletionStreamEvent_CommandRejected_variant()
    {
        var completion = CompletionFor("cmd-2", 8);
        var status = new CompletionStatus(9, "contention", "LOCAL_VERDICT_LOCKED_CONTRACTS", new Dictionary<string, string>());

        var rejected = CompletionEvents.CommandRejected(completion, status);

        var variant = rejected.Should().BeOfType<CompletionStreamEvent.CommandRejected>().Subject;
        variant.Completion.Should().BeSameAs(completion);
        variant.Status.Should().BeSameAs(status);
    }

    [Fact]
    public void CompletionEvents_Checkpoint_wraps_the_CompletionStreamEvent_Checkpoint_variant()
    {
        var checkpoint = CompletionEvents.Checkpoint(LedgerOffset.At(11));

        checkpoint.Should().BeOfType<CompletionStreamEvent.Checkpoint>().Which.Offset.Should().Be(LedgerOffset.At(11));
    }

    [Fact]
    public void CompletionEvents_StreamError_wraps_the_CompletionStreamEvent_StreamError_variant()
    {
        var error = CompletionEvents.StreamError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "boom");

        var variant = error.Should().BeOfType<CompletionStreamEvent.StreamError>().Subject;
        variant.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        variant.Message.Should().Be("boom");
        variant.Category.Should().BeNull();
        variant.ErrorId.Should().BeNull();
        variant.SourceException.Should().BeNull();
    }

    [Fact]
    public void CompletionEvents_StreamError_carries_the_category_errorId_and_source_exception()
    {
        var source = new InvalidOperationException("transport died");

        var error = CompletionEvents.StreamError(
            new TransportStatus.Grpc(GrpcStatusCode.Aborted),
            "stale",
            DamlErrorCategory.ContentionOnSharedResources,
            "STALE_STREAM_AUTHORIZATION",
            source);

        var variant = error.Should().BeOfType<CompletionStreamEvent.StreamError>().Subject;
        variant.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
        variant.ErrorId.Should().Be("STALE_STREAM_AUTHORIZATION");
        variant.SourceException.Should().BeSameAs(source);
    }

    [Fact]
    public void CompletionEvents_StreamError_keeps_errorId_and_source_exception_in_the_slots_the_record_declares()
    {
        var source = new InvalidOperationException("transport died");

        var error = CompletionEvents.StreamError(
            new TransportStatus.Grpc(GrpcStatusCode.Aborted),
            "stale",
            errorId: "PARTICIPANT_BACKPRESSURE",
            sourceException: source);

        var variant = error.Should().BeOfType<CompletionStreamEvent.StreamError>().Subject;
        variant.ErrorId.Should().Be("PARTICIPANT_BACKPRESSURE");
        variant.SourceException.Should().BeSameAs(source);
    }

    [Fact]
    public async Task CompletionStreamAsync_reads_back_a_staged_categorised_fault_with_its_category_errorId_and_source_exception()
    {
        var source = new InvalidOperationException("transport died");
        var client = FakeLedgerClient.Create()
            .WithCompletionEvents(
                CompletionEvents.Checkpoint(LedgerOffset.At(1)),
                CompletionEvents.StreamError(
                    new TransportStatus.Grpc(GrpcStatusCode.Aborted),
                    "stale",
                    DamlErrorCategory.ContentionOnSharedResources,
                    "STALE_STREAM_AUTHORIZATION",
                    source))
            .Build();

        var events = new List<CompletionStreamEvent>();
        await foreach (var completionEvent in client.CompletionStreamAsync(Bob, cancellationToken: TestContext.Current.CancellationToken))
        {
            events.Add(completionEvent);
        }

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<CompletionStreamEvent.Checkpoint>().Which.Offset.Should().Be(LedgerOffset.At(1));
        var fault = events[1].Should().BeOfType<CompletionStreamEvent.StreamError>().Subject;
        fault.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
        fault.ErrorId.Should().Be("STALE_STREAM_AUTHORIZATION");
        fault.SourceException.Should().BeSameAs(source);
    }
}
