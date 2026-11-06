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
/// LocalNet conformance coverage that the Daml-LF JSON a command carries is the shape the participant
/// binds. Contract arguments and choice arguments travel as Daml-LF JSON — <c>{"owner":"…"}</c> for a
/// record, whose field types only the template can supply — where our specification declares the
/// structured <c>fields</c> and <c>label</c> form of a proto3 record and a tagged union for a value.
/// <para>
/// One submission creates a <c>Marker</c> and exercises its <c>Mint</c> choice, so a single command
/// carries both a record and a value. Each test submits it once through the converters, which must be
/// accepted, and once with exactly one of the two replaced by the structured form the specification
/// declares, which must be refused. The refused leg is what makes the accepted one mean anything, and
/// it starts failing the day the participant binds the declared form.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public class RestDamlLfJsonLiveTests
{
    private const string CreateAndExerciseArm = "CreateAndExerciseCommand";
    private const string CreateArgumentsProperty = "createArguments";
    private const string ChoiceArgumentProperty = "choiceArgument";

    [Fact]
    [Trait("Retires", nameof(WireRecordJsonConverter))]
    public async Task A_contract_payload_is_accepted_as_Daml_LF_JSON_and_refused_as_the_declared_record()
    {
        await using var lane = await RestLiveLane.OpenAsync(TestContext.Current.CancellationToken);
        var owner = await RestWireSubmission.NewOwnerAsync(
            lane, "rest-daml-lf-record", TestContext.Current.CancellationToken);
        using var wireClient = lane.CreateWireLevelClient();

        var adapted = MintingBody(owner);
        var declared = MintingBody(owner);
        CommandOf(declared)[CreateArgumentsProperty] = JsonNode.Parse(
            $$$"""{"fields":[{"label":"owner","value":{"party":"{{{owner.Value}}}"}}]}""");

        var adaptedOutcome = await RestWireSubmission.SubmitAsync(
            wireClient, adapted, TestContext.Current.CancellationToken);
        var declaredOutcome = await RestWireSubmission.SubmitAsync(
            wireClient, declared, TestContext.Current.CancellationToken);

        CommandOf(adapted)[CreateArgumentsProperty]!.ToJsonString().Should().Be(
            $$$"""{"owner":"{{{owner.Value}}}"}""");
        adaptedOutcome.Accepted.Should().BeTrue(
            $"the Daml-LF JSON record must be accepted, but the participant answered {adaptedOutcome.Describe()}");
        declaredOutcome.Refused.Should().BeTrue(
            "the participant binds a record by its template's field names, so the structured record our "
            + $"specification declares must be refused; it answered {declaredOutcome.Describe()}");
    }

    [Fact]
    [Trait("Retires", nameof(WireValueJsonConverter))]
    public async Task A_choice_argument_is_accepted_as_Daml_LF_JSON_and_refused_as_the_declared_value()
    {
        await using var lane = await RestLiveLane.OpenAsync(TestContext.Current.CancellationToken);
        var owner = await RestWireSubmission.NewOwnerAsync(
            lane, "rest-daml-lf-value", TestContext.Current.CancellationToken);
        using var wireClient = lane.CreateWireLevelClient();

        var adapted = MintingBody(owner);
        var declared = MintingBody(owner);
        CommandOf(declared)[ChoiceArgumentProperty] = JsonNode.Parse(
            """{"record":{"fields":[{"label":"amount","value":{"numeric":"5.0"}}]}}""");

        var adaptedOutcome = await RestWireSubmission.SubmitAsync(
            wireClient, adapted, TestContext.Current.CancellationToken);
        var declaredOutcome = await RestWireSubmission.SubmitAsync(
            wireClient, declared, TestContext.Current.CancellationToken);

        CommandOf(adapted)[ChoiceArgumentProperty]!.ToJsonString().Should().Be("""{"amount":"5.0"}""");
        adaptedOutcome.Accepted.Should().BeTrue(
            $"the Daml-LF JSON value must be accepted, but the participant answered {adaptedOutcome.Describe()}");
        declaredOutcome.Refused.Should().BeTrue(
            "the participant binds a choice argument by the choice's own Daml type, so the tagged value "
            + $"our specification declares must be refused; it answered {declaredOutcome.Describe()}");
    }

    private static JsonObject MintingBody(Party owner) => RestWireSubmission.AdaptedBodyOf(
        CreateAndExerciseCommand.For(
            new Marker(owner), Marker.ChoiceMint.Name, new Marker.Mint(5.0m).ToRecord()),
        owner);

    private static JsonObject CommandOf(JsonObject body) =>
        (JsonObject)body["commands"]!.AsArray().Single()![CreateAndExerciseArm]!;
}
