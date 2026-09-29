// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Ledger.Abstractions;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Xunit;

#pragma warning disable xUnit1051

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// Proves a generated dynamic-signatory <c>TryCreateAsync</c> call that supplies only <c>configure</c> binds to
/// the generated helper, with the ledger-extensions and generated namespaces both imported.
/// </summary>
public class DynamicSignatoryCreateOverloadTests
{
    private static readonly Party Custodian = new("custodian");
    private static readonly Schedule Payload = new(new Conformance.KeyBuilders.ScheduleView(Custodian, "2026-Q1"));

    [Fact]
    public async Task Create_with_configure_binds_unambiguously_with_the_ledger_extensions_and_generated_namespaces_imported()
    {
        using var client = new FakeLedgerClient();
        var fiveMinutes = new DeduplicationPeriod.Duration(TimeSpan.FromMinutes(5));

        await client.TryCreateAsync(
            Payload,
            (SubmitterInfo)Custodian,
            configure: submission => submission.WithDeduplicationPeriod(fiveMinutes),
            cancellationToken: TestContext.Current.CancellationToken);

        client.LastSubmission.Should().NotBeNull();
        client.LastSubmission!.DeduplicationPeriod.Should().Be(fiveMinutes);
    }

    [Fact]
    public async Task Create_with_only_configure_supplied_binds_unambiguously_through_the_writer_interface()
    {
        using var client = new FakeLedgerClient();
        ILedgerWriter writer = client;

        await writer.TryCreateAsync(
            Payload,
            (SubmitterInfo)Custodian,
            configure: submission => submission.WithCommandId(new CommandId("cmd-configure")));

        client.LastSubmission.Should().NotBeNull();
        client.LastSubmission!.CommandId.Should().Be(new CommandId("cmd-configure"));
    }

    [Fact]
    public async Task Create_without_configure_still_binds_to_the_ledger_writers_instance_method()
    {
        using var client = new FakeLedgerClient();

        await client.TryCreateAsync(
            Payload,
            (SubmitterInfo)Custodian,
            cancellationToken: TestContext.Current.CancellationToken);

        client.LastCreateSubmitter.Should().NotBeNull();
        client.LastSubmission.Should().BeNull("an instance method wins over any extension when no hook is supplied");
    }
}
