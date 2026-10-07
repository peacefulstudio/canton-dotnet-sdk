// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

public sealed class InterfaceViewConformanceTests
{
    private const string SameContractIds = "under the same contract ids as the template family";

    private const string SameSynchronizer = "under the same SynchronizerId as the template family";

    private const string RenderedAmount = "must carry the view amount 42.5";

    [Fact]
    public Task Interface_active_snapshot_view_check_fails_against_a_wrong_amount() =>
        ShouldFailWith(
            InterfaceRead.Snapshot, ViewMisrendering.WrongAmount, RenderedAmount,
            kit => kit.Interface_active_snapshot_serves_the_template_contracts_with_the_view_amount_42_5());

    [Fact]
    public Task Interface_active_snapshot_view_check_fails_against_a_contract_id_the_template_family_never_served() =>
        ShouldFailWith(
            InterfaceRead.Snapshot, ViewMisrendering.ForeignContractId, SameContractIds,
            kit => kit.Interface_active_snapshot_serves_the_template_contracts_with_the_view_amount_42_5());

    [Fact]
    public Task Interface_active_snapshot_view_check_fails_against_a_row_served_under_a_foreign_SynchronizerId() =>
        ShouldFailWith(
            InterfaceRead.Snapshot, ViewMisrendering.ForeignSynchronizer, SameSynchronizer,
            kit => kit.Interface_active_snapshot_serves_the_template_contracts_with_the_view_amount_42_5());

    [Fact]
    public Task Interface_active_snapshot_view_check_fails_against_a_row_served_without_its_view() =>
        ShouldFailWith(
            InterfaceRead.Snapshot, ViewMisrendering.DroppedView, SameContractIds,
            kit => kit.Interface_active_snapshot_serves_the_template_contracts_with_the_view_amount_42_5());

    [Fact]
    public Task Interface_acs_delta_view_check_fails_against_a_wrong_amount() =>
        ShouldFailWith(
            InterfaceRead.AcsDelta, ViewMisrendering.WrongAmount, RenderedAmount,
            kit => kit.Interface_acs_delta_subscription_serves_the_template_contracts_with_the_view_amount_42_5());

    [Fact]
    public Task Interface_acs_delta_view_check_fails_against_a_contract_id_the_template_family_never_served() =>
        ShouldFailWith(
            InterfaceRead.AcsDelta, ViewMisrendering.ForeignContractId, SameContractIds,
            kit => kit.Interface_acs_delta_subscription_serves_the_template_contracts_with_the_view_amount_42_5());

    [Fact]
    public Task Interface_acs_delta_view_check_fails_against_a_row_served_without_its_view() =>
        ShouldFailWith(
            InterfaceRead.AcsDelta, ViewMisrendering.DroppedView, SameContractIds,
            kit => kit.Interface_acs_delta_subscription_serves_the_template_contracts_with_the_view_amount_42_5());

    [Fact]
    public Task Interface_ledger_effects_view_check_fails_against_a_wrong_amount() =>
        ShouldFailWith(
            InterfaceRead.LedgerEffects, ViewMisrendering.WrongAmount, RenderedAmount,
            kit => kit.Interface_ledger_effects_subscription_serves_the_template_contracts_with_the_view_amount_42_5());

    [Fact]
    public Task Interface_ledger_effects_view_check_fails_against_a_contract_id_the_template_family_never_served() =>
        ShouldFailWith(
            InterfaceRead.LedgerEffects, ViewMisrendering.ForeignContractId, SameContractIds,
            kit => kit.Interface_ledger_effects_subscription_serves_the_template_contracts_with_the_view_amount_42_5());

    [Fact]
    public Task Interface_ledger_effects_view_check_fails_against_a_row_served_without_its_view() =>
        ShouldFailWith(
            InterfaceRead.LedgerEffects, ViewMisrendering.DroppedView, SameContractIds,
            kit => kit.Interface_ledger_effects_subscription_serves_the_template_contracts_with_the_view_amount_42_5());

    private static async Task ShouldFailWith(
        InterfaceRead brokenRead,
        ViewMisrendering misrendering,
        string contractFragment,
        Func<MisrenderingKit, Task> check)
    {
        var kit = new MisrenderingKit(brokenRead, misrendering);

        var run = await Record.ExceptionAsync(() => check(kit));

        run.Should().NotBeNull("a read that misrenders the probe's Created rows must fail its view check");
        run!.Message.Should().Contain(
            contractFragment,
            "the failure must come from the view assertion itself, so an implementer reads which part of "
            + "the rendering their read got wrong");
        run.Should().NotBeOfType<TimeoutException>(
            "a stream budget that fired would prove only that the read hangs, not that the check rejects "
            + "a misrendered view");
    }

    private sealed class MisrenderingKit(InterfaceRead brokenRead, ViewMisrendering misrendering)
        : LedgerClientConformanceTests<ConformanceProbe>
    {
        protected override ILedgerClient CreateClient() => new ViewMisrenderingFakeClient(brokenRead, misrendering);

        protected override SubmitterInfo Reader { get; } = new Party("alice");
    }
}
