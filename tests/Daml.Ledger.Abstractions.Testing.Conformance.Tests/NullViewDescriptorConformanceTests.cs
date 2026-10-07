// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

public sealed class NullViewDescriptorConformanceTests
{
    [Fact]
    public async Task Interface_SubscribeActiveAsync_check_fails_against_a_read_that_ignores_a_null_descriptor()
    {
        var kit = new DescriptorIgnoringKit(InterfaceRead.Snapshot, rejectsOnlyWhenEnumerated: false);

        var run = await Record.ExceptionAsync(
            () => kit.Interface_SubscribeActiveAsync_throws_ArgumentNullException_for_a_null_ViewDescriptor());

        ShouldBeAnAssertionAboutTheCall(run);
    }

    [Fact]
    public async Task Interface_SubscribeActiveAsync_check_fails_against_a_read_that_rejects_a_null_descriptor_only_when_enumerated()
    {
        var kit = new DescriptorIgnoringKit(InterfaceRead.Snapshot, rejectsOnlyWhenEnumerated: true);

        var run = await Record.ExceptionAsync(
            () => kit.Interface_SubscribeActiveAsync_throws_ArgumentNullException_for_a_null_ViewDescriptor());

        ShouldBeAnAssertionAboutTheCall(run);
    }

    [Fact]
    public async Task Interface_SubscribeAsync_check_fails_against_a_read_that_ignores_a_null_descriptor()
    {
        var kit = new DescriptorIgnoringKit(InterfaceRead.AcsDelta, rejectsOnlyWhenEnumerated: false);

        var run = await Record.ExceptionAsync(
            () => kit.Interface_SubscribeAsync_throws_ArgumentNullException_for_a_null_ViewDescriptor());

        ShouldBeAnAssertionAboutTheCall(run);
    }

    [Fact]
    public async Task Interface_SubscribeAsync_check_fails_against_a_read_that_rejects_a_null_descriptor_only_when_enumerated()
    {
        var kit = new DescriptorIgnoringKit(InterfaceRead.AcsDelta, rejectsOnlyWhenEnumerated: true);

        var run = await Record.ExceptionAsync(
            () => kit.Interface_SubscribeAsync_throws_ArgumentNullException_for_a_null_ViewDescriptor());

        ShouldBeAnAssertionAboutTheCall(run);
    }

    [Fact]
    public async Task Interface_SubscribeLedgerEffectsAsync_check_fails_against_a_read_that_ignores_a_null_descriptor()
    {
        var kit = new DescriptorIgnoringKit(InterfaceRead.LedgerEffects, rejectsOnlyWhenEnumerated: false);

        var run = await Record.ExceptionAsync(
            () => kit.Interface_SubscribeLedgerEffectsAsync_throws_ArgumentNullException_for_a_null_ViewDescriptor());

        ShouldBeAnAssertionAboutTheCall(run);
    }

    [Fact]
    public async Task Interface_SubscribeLedgerEffectsAsync_check_fails_against_a_read_that_rejects_a_null_descriptor_only_when_enumerated()
    {
        var kit = new DescriptorIgnoringKit(InterfaceRead.LedgerEffects, rejectsOnlyWhenEnumerated: true);

        var run = await Record.ExceptionAsync(
            () => kit.Interface_SubscribeLedgerEffectsAsync_throws_ArgumentNullException_for_a_null_ViewDescriptor());

        ShouldBeAnAssertionAboutTheCall(run);
    }

    [Fact]
    public async Task Descriptor_checks_pass_against_a_client_whose_three_interface_reads_guard_eagerly()
    {
        var kit = new DescriptorIgnoringKit(InterfaceRead.Snapshot, rejectsOnlyWhenEnumerated: false, breaksNothing: true);

        await kit.Interface_SubscribeActiveAsync_throws_ArgumentNullException_for_a_null_ViewDescriptor();
        await kit.Interface_SubscribeAsync_throws_ArgumentNullException_for_a_null_ViewDescriptor();
        await kit.Interface_SubscribeLedgerEffectsAsync_throws_ArgumentNullException_for_a_null_ViewDescriptor();
    }

    private static void ShouldBeAnAssertionAboutTheCall(Exception? run)
    {
        run.Should().NotBeNull(
            "a read that does not reject a null descriptor at the call must fail the check");
        run!.Message.Should().Contain(
            "at the call",
            "the failure must come from the assertion that the guard is eager, so an implementer "
            + "reads that the null descriptor was not rejected before enumeration");
        run.Should().NotBeOfType<TimeoutException>(
            "a stream budget that fired would prove only that the read hangs, not that the check "
            + "rejects a read that accepts a null descriptor");
    }

    private sealed class DescriptorIgnoringKit(
        InterfaceRead brokenRead,
        bool rejectsOnlyWhenEnumerated,
        bool breaksNothing = false)
        : LedgerClientConformanceTests<ConformanceProbe>
    {
        protected override ILedgerClient CreateClient() => breaksNothing
            ? new ConformingFakeClient()
            : new DescriptorIgnoringFakeClient(brokenRead, rejectsOnlyWhenEnumerated);

        protected override SubmitterInfo Reader { get; } = new Party("alice");
    }
}
