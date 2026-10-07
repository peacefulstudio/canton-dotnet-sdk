// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Testing.Helpers;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class GrpcUntouchedBindingLoadingTests
{
    [Fact]
    public async Task A_gRPC_exercise_returning_a_variant_named_by_its_binding_type_decodes_on_the_first_call()
    {
        using var isolated = new IsolatedSdkLoadContext();

        var report = await isolated.RunAsync(
            typeof(GrpcUntouchedBindingScenarios), nameof(GrpcUntouchedBindingScenarios.ReturnOutcomeNamingTheBindingType));

        report.Should().Be("first call: One(Win(12.5, gold))");
    }

    [Fact]
    public async Task A_gRPC_exercise_asked_for_an_object_decodes_through_a_binding_nothing_has_touched()
    {
        using var isolated = new IsolatedSdkLoadContext();

        var report = await isolated.RunAsync(
            typeof(GrpcUntouchedBindingScenarios), nameof(GrpcUntouchedBindingScenarios.ReturnOutcomeAsAnObject));

        report.Should().Be("first call: One(Daml.Codegen.Testing.Conformance.RichTypes.Outcome+Win)");
    }

    [Fact]
    public async Task A_gRPC_exercise_decodes_through_the_exact_version_when_an_older_version_is_registered_and_the_exact_binding_is_untouched()
    {
        using var isolated = new IsolatedSdkLoadContext();

        var report = await isolated.RunAsync(
            typeof(GrpcUntouchedBindingScenarios),
            nameof(GrpcUntouchedBindingScenarios.ReturnOutcomeAsAnObjectWhileAnOlderVersionIsRegistered));

        report.Should().Be("first call: One(Daml.Codegen.Testing.Conformance.RichTypes.Outcome+Win)");
    }
}
