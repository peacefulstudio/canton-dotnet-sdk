// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

public sealed class VacuousStreamCheckConformanceTests
{
    private const string EmptyResumeContract =
        "resuming from an offset must deliver exactly the events after it";

    private const string NoEventAfterFirstPositionContract =
        "must seed at least one event after the first position of the subscription stream";

    private const string TransportErrorContract =
        "a bounded conformance subscription must not surface a transport StreamError";

    private const string IdleTemplateCancellationContract =
        "a live subscription cancelled while its next read is pending must throw OperationCanceledException";

    private const string IdleInterfaceCancellationContract =
        "a live interface subscription cancelled while its next read is pending must throw OperationCanceledException";

    [Fact]
    public async Task Exclusive_fromOffset_check_fails_against_a_client_that_returns_nothing_for_a_resumed_read() =>
        await AssertFailsWith(
            BrokenFamily.Template,
            StreamBreakage.EmptyWhenResumed,
            kit => kit.Subscribing_from_an_offset_excludes_the_event_at_that_offset(),
            EmptyResumeContract);

    [Fact]
    public async Task Interface_exclusive_fromOffset_check_fails_against_a_client_that_returns_nothing_for_a_resumed_read() =>
        await AssertFailsWith(
            BrokenFamily.Interface,
            StreamBreakage.EmptyWhenResumed,
            kit => kit.Interface_subscribing_from_an_offset_excludes_the_event_at_that_offset(),
            EmptyResumeContract);

    [Fact]
    public async Task Exclusive_fromOffset_check_fails_when_the_stream_seeds_no_event_after_its_first_position() =>
        await AssertFailsWith(
            BrokenFamily.Template,
            StreamBreakage.OnlyTheFirstEvent,
            kit => kit.Subscribing_from_an_offset_excludes_the_event_at_that_offset(),
            NoEventAfterFirstPositionContract);

    [Fact]
    public async Task Interface_exclusive_fromOffset_check_fails_when_the_stream_seeds_no_event_after_its_first_position() =>
        await AssertFailsWith(
            BrokenFamily.Interface,
            StreamBreakage.OnlyTheFirstEvent,
            kit => kit.Interface_subscribing_from_an_offset_excludes_the_event_at_that_offset(),
            NoEventAfterFirstPositionContract);

    [Fact]
    public async Task Cancellation_check_fails_when_the_token_is_honoured_only_at_enumeration_start() =>
        await AssertFailsWithTimeout(
            BrokenFamily.Template,
            StreamBreakage.BlockedAfterCheckingTheTokenOnce,
            kit => kit.Cancelling_a_live_subscription_throws_OperationCanceledException(),
            IdleTemplateCancellationContract);

    [Fact]
    public async Task Interface_cancellation_check_fails_when_the_token_is_honoured_only_at_enumeration_start() =>
        await AssertFailsWithTimeout(
            BrokenFamily.Interface,
            StreamBreakage.BlockedAfterCheckingTheTokenOnce,
            kit => kit.Interface_cancelling_a_live_subscription_throws_OperationCanceledException(),
            IdleInterfaceCancellationContract);

    [Fact]
    public async Task Cancellation_check_fails_when_the_live_subscription_completes_on_its_own() =>
        await AssertFailsWith(
            BrokenFamily.Template,
            StreamBreakage.None,
            kit => kit.Cancelling_a_live_subscription_throws_OperationCanceledException(),
            "a live subscription must stay open until its token is cancelled");

    [Fact]
    public async Task Interface_cancellation_check_fails_when_the_live_interface_subscription_completes_on_its_own() =>
        await AssertFailsWith(
            BrokenFamily.Interface,
            StreamBreakage.None,
            kit => kit.Interface_cancelling_a_live_subscription_throws_OperationCanceledException(),
            "a live interface subscription must stay open until its token is cancelled");

    [Fact]
    public async Task Cancellation_check_passes_for_a_live_subscription_held_open_until_cancelled() =>
        await AssertPasses(
            BrokenFamily.Template,
            StreamBreakage.HeldOpenUntilCancelled,
            kit => kit.Cancelling_a_live_subscription_throws_OperationCanceledException());

    [Fact]
    public async Task Interface_cancellation_check_passes_for_a_live_interface_subscription_held_open_until_cancelled() =>
        await AssertPasses(
            BrokenFamily.Interface,
            StreamBreakage.HeldOpenUntilCancelled,
            kit => kit.Interface_cancelling_a_live_subscription_throws_OperationCanceledException());

    [Fact]
    public async Task Ledger_effects_shape_check_fails_against_a_window_ending_in_a_transport_StreamError() =>
        await AssertFailsWith(
            BrokenFamily.Template,
            StreamBreakage.TransportErrorAfterTheLedgerEffectsWindow,
            kit => kit.Ledger_effects_subscription_never_yields_Archived(),
            TransportErrorContract);

    [Fact]
    public async Task Acs_delta_shape_check_fails_against_a_window_ending_in_a_transport_StreamError() =>
        await AssertFailsWith(
            BrokenFamily.Template,
            StreamBreakage.TransportErrorAfterTheAcsDeltaWindow,
            kit => kit.Acs_delta_subscription_never_yields_Exercised(),
            TransportErrorContract);

    [Fact]
    public async Task Interface_ledger_effects_shape_check_fails_against_a_window_ending_in_a_transport_StreamError() =>
        await AssertFailsWith(
            BrokenFamily.Interface,
            StreamBreakage.TransportErrorAfterTheLedgerEffectsWindow,
            kit => kit.Interface_ledger_effects_subscription_never_yields_Archived(),
            TransportErrorContract);

    [Fact]
    public async Task Interface_acs_delta_shape_check_fails_against_a_window_ending_in_a_transport_StreamError() =>
        await AssertFailsWith(
            BrokenFamily.Interface,
            StreamBreakage.TransportErrorAfterTheAcsDeltaWindow,
            kit => kit.Interface_acs_delta_subscription_never_yields_Exercised(),
            TransportErrorContract);

    [Fact]
    public async Task Tightened_checks_pass_against_the_seeded_shipped_fake_held_open_after_its_rows()
    {
        var kit = new StreamBreakingKit(BrokenFamily.Template, StreamBreakage.HeldOpenUntilCancelled);
        var interfaceKit = new StreamBreakingKit(BrokenFamily.Interface, StreamBreakage.HeldOpenUntilCancelled);
        Func<Task>[] checks =
        [
            () => kit.Subscribing_from_an_offset_excludes_the_event_at_that_offset(),
            () => kit.Ledger_effects_subscription_never_yields_Archived(),
            () => kit.Acs_delta_subscription_never_yields_Exercised(),
            () => kit.Cancelling_a_live_subscription_throws_OperationCanceledException(),
            () => interfaceKit.Interface_subscribing_from_an_offset_excludes_the_event_at_that_offset(),
            () => interfaceKit.Interface_ledger_effects_subscription_never_yields_Archived(),
            () => interfaceKit.Interface_acs_delta_subscription_never_yields_Exercised(),
            () => interfaceKit.Interface_cancelling_a_live_subscription_throws_OperationCanceledException(),
        ];

        foreach (var check in checks)
        {
            var run = await Record.ExceptionAsync(check);

            run.Should().BeNull("the seeded scenario conforms to every tightened check");
        }
    }

    private static async Task AssertFailsWith(
        BrokenFamily family,
        StreamBreakage breakage,
        Func<LedgerClientConformanceTests<ConformanceProbe>, Task> check,
        string expectedContract)
    {
        var run = await Record.ExceptionAsync(() => check(new StreamBreakingKit(family, breakage)));

        run.Should().NotBeNull("a client with this fault must fail the check, not satisfy it vacuously");
        run!.Message.Should().Contain(expectedContract);
        run.Should().NotBeOfType<TimeoutException>(
            "the check must reject the fault with its own assertion, not by running out the stream budget");
    }

    private static async Task AssertFailsWithTimeout(
        BrokenFamily family,
        StreamBreakage breakage,
        Func<LedgerClientConformanceTests<ConformanceProbe>, Task> check,
        string expectedContract)
    {
        var run = await Record.ExceptionAsync(() => check(new StreamBreakingKit(family, breakage)));

        run.Should().NotBeNull("a client with this fault must fail the check, not satisfy it vacuously");
        run!.Message.Should().Contain(nameof(TimeoutException));
        run.Message.Should().Contain(expectedContract);
    }

    private static async Task AssertPasses(
        BrokenFamily family,
        StreamBreakage breakage,
        Func<LedgerClientConformanceTests<ConformanceProbe>, Task> check)
    {
        var run = await Record.ExceptionAsync(() => check(new StreamBreakingKit(family, breakage)));

        run.Should().BeNull("a stream that honours the contract must not be failed by the check");
    }

    private sealed class StreamBreakingKit(BrokenFamily family, StreamBreakage breakage)
        : LedgerClientConformanceTests<ConformanceProbe>
    {
        protected override ILedgerClient CreateClient() => new StreamBreakingFakeClient(family, breakage);

        protected override SubmitterInfo Reader { get; } = new Party("alice");

        protected override TimeSpan StreamTimeout => TimeSpan.FromMilliseconds(400);
    }
}
