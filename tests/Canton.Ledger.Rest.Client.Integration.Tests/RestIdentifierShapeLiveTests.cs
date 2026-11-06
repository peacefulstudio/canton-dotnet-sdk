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
/// LocalNet conformance coverage that a template identifier reaches the participant in the shape it
/// reads. Our specification declares an identifier as the structured
/// <c>{"packageId":…,"moduleName":…,"entityName":…}</c> object, while the participant reads the flat
/// <c>packageId:moduleName:entityName</c> string and refuses the declared form.
/// <para>
/// The same create is submitted twice with only the template identifier differing: once through the
/// converter, which must be accepted, and once in the structured form the specification declares,
/// which must be refused. The refused leg is what makes the accepted one mean anything, and it is the
/// leg that starts failing the day the participant reads the declared form, at which point the
/// converter can retire.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public class RestIdentifierShapeLiveTests
{
    private const string CreateArm = "CreateCommand";
    private const string TemplateIdProperty = "templateId";

    [Fact]
    [Trait("Retires", nameof(WireIdentifierJsonConverter))]
    public async Task A_template_identifier_is_accepted_as_the_flat_string_and_refused_as_the_declared_object()
    {
        await using var lane = await RestLiveLane.OpenAsync(TestContext.Current.CancellationToken);
        var owner = await RestWireSubmission.NewOwnerAsync(
            lane, "rest-identifier-shape", TestContext.Current.CancellationToken);
        using var wireClient = lane.CreateWireLevelClient();

        var adapted = CreateBody(owner);
        var declared = CreateBody(owner);
        CommandOf(declared)[TemplateIdProperty] = JsonNode.Parse(
            """{"packageId":"#richtypes-h07285b495f9e","moduleName":"RichTypes","entityName":"Marker"}""");

        var adaptedOutcome = await RestWireSubmission.SubmitAsync(
            wireClient, adapted, TestContext.Current.CancellationToken);
        var declaredOutcome = await RestWireSubmission.SubmitAsync(
            wireClient, declared, TestContext.Current.CancellationToken);

        CommandOf(adapted)[TemplateIdProperty]!.ToJsonString().Should().Be(
            "\"#richtypes-h07285b495f9e:RichTypes:Marker\"");
        adaptedOutcome.Accepted.Should().BeTrue(
            $"the flat identifier must be accepted, but the participant answered {adaptedOutcome.Describe()}");
        declaredOutcome.Refused.Should().BeTrue(
            "the participant reads an identifier as one colon-separated string, so the structured object "
            + $"our specification declares must be refused; it answered {declaredOutcome.Describe()}");
    }

    private static JsonObject CreateBody(Party owner) =>
        RestWireSubmission.AdaptedBodyOf(CreateCommand.For(new Marker(owner)), owner);

    private static JsonObject CommandOf(JsonObject body) =>
        (JsonObject)body["commands"]!.AsArray().Single()![CreateArm]!;
}
