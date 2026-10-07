// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestUntouchedBindingLoadingTests
{
    [Fact]
    public async Task A_REST_exercise_of_a_referenced_binding_nothing_has_touched_decodes_on_the_first_call()
    {
        using var isolated = new IsolatedSdkLoadContext();

        var report = await isolated.RunAsync(
            typeof(UntouchedBindingScenarios), nameof(UntouchedBindingScenarios.DescribeThroughTheHoldingInterface));

        report.Should().Be("conformance loaded before: False; first call: One(audit: 12.5)");
    }

    [Fact]
    public async Task A_REST_exercise_returning_a_variant_through_an_untouched_binding_decodes_on_the_first_call()
    {
        using var isolated = new IsolatedSdkLoadContext();

        var report = await isolated.RunAsync(
            typeof(UntouchedBindingScenarios), nameof(UntouchedBindingScenarios.ReturnOutcomeThroughGenericResults));

        report.Should().Be("first call: One(Win(12.5, gold))");
    }

    [Fact]
    public async Task A_host_whose_binding_cannot_be_loaded_by_name_gets_the_carried_result_then_the_decoded_one_once_it_runs_the_module_constructor_itself()
    {
        using var isolated = new IsolatedSdkLoadContext("Daml.Codegen.Testing.Conformance");

        var report = await isolated.RunAsync(
            typeof(UntouchedBindingScenarios),
            nameof(UntouchedBindingScenarios.DescribeAfterTheHostRunsTheBindingModuleConstructor));

        report.Should().Be(
            "first call: One(carried \"audit: 12.5\"); after RunModuleConstructor: One(audit: 12.5)");
    }

    [Fact]
    public async Task A_REST_exercise_decodes_through_the_exact_version_when_an_older_version_is_registered_and_the_exact_binding_is_untouched()
    {
        using var isolated = new IsolatedSdkLoadContext();

        var report = await isolated.RunAsync(
            typeof(UntouchedBindingScenarios),
            nameof(UntouchedBindingScenarios.ReturnOutcomeWhileAnOlderVersionIsRegistered));

        report.Should().Be("first call: One(Win, prize=12.5, tier=gold)");
    }
}
