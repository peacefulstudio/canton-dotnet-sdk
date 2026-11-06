// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// LocalNet conformance coverage that a deduplication duration reaches the participant in the shape it
/// reads. Our specification declares the duration arm as the proto3-canonical <c>"30s"</c> string,
/// while the participant reads a <c>value</c> wrapper around a <c>{"seconds":…,"nanos":…}</c> object
/// and refuses the declared form.
/// <para>
/// The same create is submitted twice with only the period differing: once through the converter,
/// which must be accepted, and once in the form the specification declares, which must be refused. The
/// refused leg is what makes the accepted one mean anything, and it is the leg that starts failing the
/// day the participant reads the declared form, at which point the converter can retire.
/// </para>
/// <para>
/// The offset arm has no such leg: the participant accepts the offset as a string inside the
/// <c>value</c> wrapper as readily as it accepts the number the converter writes, so only the
/// duration arm discriminates.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public class RestDeduplicationPeriodLiveTests
{
    private const string DeduplicationPeriodProperty = "deduplicationPeriod";

    [Fact]
    [Trait("Retires", nameof(DeduplicationPeriodJsonConverter))]
    public async Task A_deduplication_duration_is_accepted_as_the_served_object_and_refused_as_the_declared_string()
    {
        await using var lane = await RestLiveLane.OpenAsync(TestContext.Current.CancellationToken);
        var owner = await RestWireSubmission.NewOwnerAsync(
            lane, "rest-dedup-duration", TestContext.Current.CancellationToken);
        using var wireClient = lane.CreateWireLevelClient();

        var adapted = DeduplicatedCreate(owner);
        var declared = DeduplicatedCreate(owner);
        declared[DeduplicationPeriodProperty] = JsonNode.Parse("""{"DeduplicationDuration":"30s"}""");

        var adaptedOutcome = await RestWireSubmission.SubmitAsync(
            wireClient, adapted, TestContext.Current.CancellationToken);
        var declaredOutcome = await RestWireSubmission.SubmitAsync(
            wireClient, declared, TestContext.Current.CancellationToken);

        adapted[DeduplicationPeriodProperty]!.ToJsonString().Should().Be(
            """{"DeduplicationDuration":{"value":{"seconds":30,"nanos":0}}}""");
        adaptedOutcome.Accepted.Should().BeTrue(
            $"the served duration object must be accepted, but the participant answered {adaptedOutcome.Describe()}");
        declaredOutcome.Refused.Should().BeTrue(
            "the participant reads a wrapped duration object, so the proto3 string our specification "
            + $"declares must be refused; it answered {declaredOutcome.Describe()}");
    }

    private static JsonObject DeduplicatedCreate(Party owner)
    {
        var commands = RestCommandBuilder.BuildCommands(
            CommandsSubmission.Single(CreateCommand.For(new Marker(owner)), owner), userId: null);
        commands.DeduplicationPeriod = new Raw.DeduplicationPeriod { DeduplicationDuration = "30s" };
        return RestWireSubmission.BodyOf(commands);
    }
}
