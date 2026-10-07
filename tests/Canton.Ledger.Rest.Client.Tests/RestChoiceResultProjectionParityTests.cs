// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Stdlib;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestChoiceResultProjectionParityTests : ChoiceResultProjectionParityTests, IDisposable
{
    private static readonly Party Alice = new("party::alice");

    private const string UnflattenedChainMessage =
        "Cannot convert Daml.Runtime.Data.DamlOptionalChain to Daml.Runtime.Data.DamlOptional. "
        + "Use a DamlValue-derived type as TResult for direct access.";

    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    protected override async Task<ExerciseOutcome<TResult>> ExerciseOnWireAsync<TResult>(WireExercise exercise)
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, ExercisedTransaction(exercise));
        var factory = new StubHttpClientFactory(transport);
        _factories.Add(factory);
        var client = new RestLedgerClient(factory);
        var command = new ExerciseCommand(
            exercise.CommandTarget,
            new ContractId<HoldingOwner>(exercise.ContractId),
            new ChoiceName(exercise.Choice),
            DamlUnit.Instance);

        return await client.TryExerciseAsync<TResult>(
            command, Alice, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[[]]")]
    [InlineData("[[\"x\"]]")]
    public async Task A_nested_Optional_without_a_registered_binding_read_as_a_nullable_string_is_CommittedUndecodable(string lfJson)
    {
        var outcome = await ExerciseOnWireAsync<string?>(PickOn(
            ChoiceResultParityBindings.Unbound, null, DamlOptionalChain.None, lfJson));

        outcome.Should().BeOfType<ExerciseOutcome<string?>.CommittedUndecodable>().Subject.Message.Should().Be(
            "Choice 'Pick' of 'parity-pkg-unbound:Parity:Unbound' did not resolve to a generated result decoder: "
            + "no generated binding for it is registered. Decoding its result as System.String through "
            + "FromDamlValue failed: Expected JSON String at 'String' but found Array");
    }

    [Fact]
    public async Task A_one_element_list_without_a_registered_binding_read_as_a_nullable_string_is_CommittedUndecodable()
    {
        var outcome = await ExerciseOnWireAsync<string?>(PickOn(
            ChoiceResultParityBindings.Unbound, null, DamlList.Create(new DamlText("x")), """["x"]"""));

        outcome.Should().BeOfType<ExerciseOutcome<string?>.CommittedUndecodable>().Subject.Message.Should().Be(
            "Choice 'Pick' of 'parity-pkg-unbound:Parity:Unbound' did not resolve to a generated result decoder: "
            + "no generated binding for it is registered. Decoding its result as System.String through "
            + "FromDamlValue failed: Expected JSON String at 'String' but found Array");
    }

    [Fact]
    public async Task A_nested_Optional_without_a_registered_binding_read_as_DamlValue_is_the_raw_Daml_LF_JSON()
    {
        var outcome = await ExerciseOnWireAsync<DamlValue>(PickOn(
            ChoiceResultParityBindings.Unbound, null, DamlOptionalChain.None, """[["x"]]"""));

        outcome.Should().BeOfType<ExerciseOutcome<DamlValue>.One>()
            .Subject.Result.Should().Be(new DamlUndecodedJson("""[["x"]]"""));
    }

    [Fact]
    public async Task A_nested_Optional_None_read_as_DamlOptional_is_CommittedUndecodable_not_a_null_result()
    {
        var outcome = await ExerciseOnWireAsync<DamlOptional>(
            OnGenericResults(
                "ReturnNestedOptional",
                """{"outer": false, "inner": false}""",
                DamlOptionalChain.None,
                "[]"));

        outcome.Should().BeOfType<ExerciseOutcome<DamlOptional>.CommittedUndecodable>()
            .Subject.Message.Should().Be(UnflattenedChainMessage);
    }

    [Fact]
    public async Task A_nested_Optional_Some_of_None_read_as_DamlOptional_is_CommittedUndecodable_not_a_null_result()
    {
        var outcome = await ExerciseOnWireAsync<DamlOptional>(
            OnGenericResults(
                "ReturnNestedOptional",
                """{"outer": true, "inner": false}""",
                DamlOptionalChain.Some(DamlOptionalChain.None),
                "[[]]"));

        outcome.Should().BeOfType<ExerciseOutcome<DamlOptional>.CommittedUndecodable>()
            .Subject.Message.Should().Be(UnflattenedChainMessage);
    }

    private static string ExercisedTransaction(WireExercise exercise) =>
        $$"""
        {
          "transaction": {
            "updateId": "upd-1",
            "offset": "1",
            "events": [
              {
                "ExercisedEvent": {
                  "offset": "1",
                  "nodeId": 0,
                  "contractId": "{{exercise.ContractId}}",
                  "templateId": {{IdentifierJson(exercise.TemplateId)}},
                  {{InterfaceIdMember(exercise.InterfaceId)}}
                  "choice": "{{exercise.Choice}}",
                  "choiceArgument": {{exercise.ArgumentLfJson}},
                  "actingParties": ["party::alice"],
                  "consuming": false,
                  "witnessParties": ["party::alice"],
                  "exerciseResult": {{exercise.ResultLfJson}}
                }
              }
            ]
          }
        }
        """;

    private static string InterfaceIdMember(RuntimeIdentifier? interfaceId) =>
        interfaceId is { } identifier ? $"\"interfaceId\": {IdentifierJson(identifier)}," : "";

    private static string IdentifierJson(RuntimeIdentifier identifier) =>
        $$"""{"packageId": "{{identifier.PackageId}}", "moduleName": "{{identifier.ModuleName}}", "entityName": "{{identifier.EntityName}}"}""";

    protected override ExerciseOutcome<TResult> ProjectChoiceResult<TResult>(
        ExerciseOutcome<TransactionResult> outcome, ExerciseCommand command) =>
        RestTransactionResultProjector.ProjectChoiceResult<TResult>(outcome, command);
}
