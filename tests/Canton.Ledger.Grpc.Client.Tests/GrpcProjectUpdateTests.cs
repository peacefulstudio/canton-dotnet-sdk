// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Testing.Helpers;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class GrpcProjectUpdateTests
{
    private static GetUpdatesResponse CheckpointAt(long offset) =>
        new() { OffsetCheckpoint = new OffsetCheckpoint { Offset = offset } };

    [Fact]
    public void ProjectUpdate_turns_an_offset_checkpoint_into_a_contract_checkpoint_at_its_offset()
    {
        var projected = GrpcContractStreamProjector.ProjectUpdate<TemplateMarker>(CheckpointAt(57L)).ToList();

        projected.Should().ContainSingle()
            .Which.Should().Be(new ContractStreamEvent<TemplateMarker>.Checkpoint(LedgerOffset.At(57L)));
    }

    [Fact]
    public void ProjectUpdate_turns_an_offset_checkpoint_into_an_interface_checkpoint_at_its_offset()
    {
        var projected = GrpcInterfaceStreamProjector
            .ProjectUpdate<InterfaceMarker, InterfaceMarkerView>(CheckpointAt(58L)).ToList();

        projected.Should().ContainSingle()
            .Which.Should().Be(new InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Checkpoint(
                LedgerOffset.At(58L)));
    }

    [Fact]
    public void ProjectUpdate_skips_an_update_variant_it_does_not_project_and_logs_it_at_debug()
    {
        var loggerFactory = new CapturingLoggerFactory();

        var projected = GrpcContractStreamProjector
            .ProjectUpdate<TemplateMarker>(new GetUpdatesResponse(), loggerFactory.CreateLogger("projection"))
            .ToList();

        projected.Should().BeEmpty();
        loggerFactory.Records.Should().ContainSingle().Which.Should().Be(
            ("projection", LogLevel.Debug, "Subscribe stream for TemplateMarker skipped variant None", (Exception?)null));
    }

    [Fact]
    public void ProjectUpdate_for_an_interface_marker_logs_the_skipped_variant_under_the_interface_name()
    {
        var loggerFactory = new CapturingLoggerFactory();

        var projected = GrpcInterfaceStreamProjector
            .ProjectUpdate<InterfaceMarker, InterfaceMarkerView>(
                new GetUpdatesResponse(), loggerFactory.CreateLogger("projection"))
            .ToList();

        projected.Should().BeEmpty();
        loggerFactory.Records.Should().ContainSingle().Which.Should().Be(
            ("projection", LogLevel.Debug, "Subscribe stream for InterfaceMarker skipped variant None", (Exception?)null));
    }
}
