// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Canton.Ledger.Testing.Helpers;
using Daml.Runtime;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class RestProjectUpdateTests
{
    private static GetUpdatesResponse Wire(string json) =>
        JsonSerializer.Deserialize<GetUpdatesResponse>(json, RestRefitSettings.SerializerOptions)!;

    private const string CheckpointAt57 = """{"update": {"OffsetCheckpoint": {"value": {"offset": 57}}}}""";

    private const string TopologyUpdate =
        """{"update": {"TopologyTransaction": {"value": {"updateId": "u-topology", "offset": 12, "synchronizerId": "sync-1", "recordTime": "2026-07-29T09:50:08.914351Z", "events": []}}}}""";

    [Fact]
    public void ProjectUpdate_turns_an_offset_checkpoint_into_a_contract_checkpoint_at_its_offset()
    {
        var projected = RestContractStreamProjector.ProjectUpdate<TemplateMarker>(Wire(CheckpointAt57)).ToList();

        projected.Should().ContainSingle()
            .Which.Should().Be(new ContractStreamEvent<TemplateMarker>.Checkpoint(LedgerOffset.At(57L)));
    }

    [Fact]
    public void ProjectUpdate_turns_an_offset_checkpoint_into_an_interface_checkpoint_at_its_offset()
    {
        var projected = RestInterfaceStreamProjector
            .ProjectUpdate<InterfaceMarker, InterfaceMarkerView>(Wire(CheckpointAt57)).ToList();

        projected.Should().ContainSingle()
            .Which.Should().Be(new InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.Checkpoint(
                LedgerOffset.At(57L)));
    }

    [Fact]
    public void ProjectUpdate_skips_a_topology_update_and_logs_it_at_debug()
    {
        var loggerFactory = new CapturingLoggerFactory();

        var projected = RestContractStreamProjector
            .ProjectUpdate<TemplateMarker>(Wire(TopologyUpdate), loggerFactory.CreateLogger("projection"))
            .ToList();

        projected.Should().BeEmpty();
        loggerFactory.Records.Should().ContainSingle().Which.Should().Be(
            ("projection", LogLevel.Debug, "Subscribe stream for TemplateMarker skipped variant TopologyTransaction", (Exception?)null));
    }

    [Fact]
    public void ProjectUpdate_skips_an_update_with_no_known_arm_and_logs_it_as_unknown()
    {
        var loggerFactory = new CapturingLoggerFactory();

        var projected = RestInterfaceStreamProjector
            .ProjectUpdate<InterfaceMarker, InterfaceMarkerView>(
                Wire("""{"update": {}}"""), loggerFactory.CreateLogger("projection"))
            .ToList();

        projected.Should().BeEmpty();
        loggerFactory.Records.Should().ContainSingle().Which.Should().Be(
            ("projection", LogLevel.Debug, "Subscribe stream for InterfaceMarker skipped variant Unknown", (Exception?)null));
    }
}
