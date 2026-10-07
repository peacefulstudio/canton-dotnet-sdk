// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

public sealed class ArchivalDroppingConformanceTests
{
    private const string AcsDeltaArchivalContract =
        "the ACS-delta shape conveys archival as a first-class Archived event";

    private const string LedgerEffectsArchivalContract =
        "the ledger-effects shape conveys archival as a consuming Exercised event";

    [Fact]
    public async Task Acs_delta_shape_check_fails_against_a_projector_that_drops_every_archival()
    {
        var kit = new ArchivalDroppingKit();

        var run = await Record.ExceptionAsync(() => kit.Acs_delta_subscription_never_yields_Exercised());

        ShouldBeTheContractFailure(run, AcsDeltaArchivalContract);
    }

    [Fact]
    public async Task Ledger_effects_shape_check_fails_against_a_projector_that_drops_every_archival()
    {
        var kit = new ArchivalDroppingKit();

        var run = await Record.ExceptionAsync(() => kit.Ledger_effects_subscription_never_yields_Archived());

        ShouldBeTheContractFailure(run, LedgerEffectsArchivalContract);
    }

    [Fact]
    public async Task Interface_acs_delta_shape_check_fails_against_an_interface_projector_that_drops_every_archival()
    {
        var kit = new InterfaceArchivalDroppingKit();

        var run = await Record.ExceptionAsync(() => kit.Interface_acs_delta_subscription_never_yields_Exercised());

        ShouldBeTheContractFailure(run, AcsDeltaArchivalContract);
    }

    [Fact]
    public async Task Interface_ledger_effects_shape_check_fails_against_an_interface_projector_that_drops_every_archival()
    {
        var kit = new InterfaceArchivalDroppingKit();

        var run = await Record.ExceptionAsync(() => kit.Interface_ledger_effects_subscription_never_yields_Archived());

        ShouldBeTheContractFailure(run, LedgerEffectsArchivalContract);
    }

    private static void ShouldBeTheContractFailure(Exception? run, string contractFragment)
    {
        run.Should().NotBeNull(
            "a client whose projector drops every archival event must fail this check; the shape's "
            + "defining content is missing, not merely carried by the wrong variant");
        run!.Message.Should().Contain(
            contractFragment,
            "the failure must come from the archival-presence assertion itself, so an implementer "
            + "reads which signal their projector never emitted");
        run.Should().NotBeOfType<TimeoutException>(
            "a stream budget that fired would prove only that the fake hangs, not that the check "
            + "rejects a stream missing its archival signal; CollectWithinBudget's message names "
            + "the contract, never the exception type, so only the type test rules that path out");
    }

    private sealed class ArchivalDroppingKit : LedgerClientConformanceTests<ConformanceProbe>
    {
        protected override ILedgerClient CreateClient() => new ArchivalDroppingFakeClient();

        protected override SubmitterInfo Reader { get; } = new Party("alice");
    }

    private sealed class InterfaceArchivalDroppingKit : LedgerClientConformanceTests<ConformanceProbe>
    {
        protected override ILedgerClient CreateClient() =>
            new ArchivalDroppingFakeClient(dropsTemplateArchivals: false, dropsInterfaceArchivals: true);

        protected override SubmitterInfo Reader { get; } = new Party("alice");
    }
}
