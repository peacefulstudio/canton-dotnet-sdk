// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Canton.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Outcomes;

namespace Canton.Ledger.Rest.Client.Tests;

/// <summary>
/// Scenarios that run inside an isolated load context, where the generated conformance assembly is
/// referenced by the dependency manifest but no code has touched it. They must not name a conformance
/// type: that would load the assembly and defeat the scenario.
/// </summary>
internal static class UntouchedBindingScenarios
{
    private const string ConformanceAssemblyName = "Daml.Codegen.Testing.Conformance";
    private const string RichTypesPackageId = "e72ec259be271bedbcb0d33f19a47008de832963a569e5a04f1ed7b8efb434e9";

    private sealed record HoldingMarker : IDamlType
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier(RichTypesPackageId, "RichTypes", "Holding"), DamlTypeKind.Interface, "rich-types");
    }

    private sealed record GenericResultsMarker : IDamlType
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier(RichTypesPackageId, "RichTypes", "GenericResults"), DamlTypeKind.Template, "rich-types");
    }

    private sealed class OlderGenericResults : IDamlType, IHasChoices<OlderGenericResults>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("older-version-package", "RichTypes", "GenericResults"), DamlTypeKind.Template, "rich-types");

        public static IReadOnlyList<IChoice> Choices { get; } =
        [
            new Choice<OlderGenericResults, DamlUnit, DamlValue>
            {
                Name = new ChoiceName("ReturnOutcome"),
                Consuming = false,
                ArgumentEncoder = _ => DamlUnit.Instance,
                ArgumentDecoder = _ => DamlUnit.Instance,
                ResultDecoder = value => value,
                ArgumentJsonReader = DamlLfJsonDecoders.ReadUnit,
                ResultJsonReader = (_, _) => DamlVariant.Create(
                    "Win", DamlRecord.Create(DamlField.Create("prize", new DamlNumeric(0m)), DamlField.Create("tier", new DamlText("older-version-tier")))),
            },
        ];
    }

    internal static async Task<string> ReturnOutcomeWhileAnOlderVersionIsRegistered()
    {
        GeneratedTypeReaders.ForChoices<OlderGenericResults>();
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, ReturnOutcomeTransaction);
        using var factory = new StubHttpClientFactory(transport);
        var client = new RestLedgerClient(factory);
        var command = ExerciseCommand.For(
            new ContractId<GenericResultsMarker>("00results"), new ChoiceName("ReturnOutcome"), DamlUnit.Instance);

        var outcome = await client.TryExerciseAsync<DamlValue>(command, new Party("party::alice"));

        return $"first call: {DescribeVariantOutcome(outcome)}";
    }

    private static string DescribeVariantOutcome(ExerciseOutcome<DamlValue> outcome) =>
        outcome switch
        {
            ExerciseOutcome<DamlValue>.One { Result: DamlVariant { Value: DamlRecord record } variant } =>
                $"One({variant.Constructor}, prize={((DamlNumeric)record.GetRequiredField("prize")).Value}, tier={((DamlText)record.GetRequiredField("tier")).Value})",
            ExerciseOutcome<DamlValue>.CommittedUndecodable undecodable =>
                $"CommittedUndecodable({undecodable.SourceException?.Message})",
            _ => outcome.GetType().Name,
        };

    internal static async Task<string> ReturnOutcomeThroughGenericResults()
    {
        var first = await ExerciseReturnOutcomeAsync();
        return $"first call: {DescribeOutcome(first)}";
    }

    internal static async Task<string> DescribeThroughTheHoldingInterface()
    {
        var loadedBefore = ConformanceIsLoaded();
        var first = await ExerciseDescribeAsync();
        return $"conformance loaded before: {loadedBefore}; first call: {Describe(first)}";
    }

    internal static async Task<string> DescribeAfterTheHostRunsTheBindingModuleConstructor()
    {
        var first = await ExerciseDescribeAsync();
        var context = AssemblyLoadContext.GetLoadContext(typeof(UntouchedBindingScenarios).Assembly)!;
        var binding = context.LoadFromAssemblyPath(
            Path.Combine(AppContext.BaseDirectory, ConformanceAssemblyName + ".dll"));
        RuntimeHelpers.RunModuleConstructor(binding.ManifestModule.ModuleHandle);
        var second = await ExerciseDescribeAsync();
        return $"first call: {Describe(first)}; after RunModuleConstructor: {Describe(second)}";
    }

    private static bool ConformanceIsLoaded() =>
        AssemblyLoadContext.GetLoadContext(typeof(UntouchedBindingScenarios).Assembly)!
            .Assemblies.Any(assembly => assembly.GetName().Name == ConformanceAssemblyName);

    private static async Task<ExerciseOutcome<Outcome>> ExerciseReturnOutcomeAsync()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, ReturnOutcomeTransaction);
        using var factory = new StubHttpClientFactory(transport);
        var client = new RestLedgerClient(factory);
        var command = ExerciseCommand.For(
            new ContractId<GenericResultsMarker>("00results"), new ChoiceName("ReturnOutcome"), DamlUnit.Instance);

        return await client.TryExerciseAsync<Outcome>(command, new Party("party::alice"));
    }

    private static string DescribeOutcome(ExerciseOutcome<Outcome> outcome) =>
        outcome switch
        {
            ExerciseOutcome<Outcome>.One { Result: Outcome.Win win } => $"One(Win({win.Value.Prize}, {win.Value.Tier}))",
            ExerciseOutcome<Outcome>.One one => $"One({one.Result.Tag})",
            ExerciseOutcome<Outcome>.CommittedUndecodable undecodable =>
                $"CommittedUndecodable({undecodable.SourceException?.Message})",
            _ => outcome.GetType().Name,
        };

    private static async Task<ExerciseOutcome<DamlValue>> ExerciseDescribeAsync()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, DescribeTransaction);
        using var factory = new StubHttpClientFactory(transport);
        var client = new RestLedgerClient(factory);
        var command = ExerciseCommand.For(
            new ContractId<HoldingMarker>("00asset"), new ChoiceName("Describe"), DamlUnit.Instance);

        return await client.TryExerciseAsync<DamlValue>(command, new Party("party::alice"));
    }

    private static string Describe(ExerciseOutcome<DamlValue> outcome) =>
        outcome switch
        {
            ExerciseOutcome<DamlValue>.One { Result: DamlUndecodedJson carried } => $"One(carried {carried.LfJson})",
            ExerciseOutcome<DamlValue>.One one => $"One({((DamlText)one.Result).Value})",
            ExerciseOutcome<DamlValue>.CommittedUndecodable undecodable =>
                $"CommittedUndecodable({undecodable.SourceException?.Message})",
            _ => outcome.GetType().Name,
        };

    private const string DescribeTransaction = $$"""
        {
          "transaction": {
            "updateId": "upd-1",
            "offset": "1",
            "events": [
              {
                "ExercisedEvent": {
                  "offset": "1",
                  "nodeId": 0,
                  "contractId": "00asset",
                  "templateId": {"packageId": "{{RichTypesPackageId}}", "moduleName": "RichTypes", "entityName": "Asset"},
                  "interfaceId": {"packageId": "{{RichTypesPackageId}}", "moduleName": "RichTypes", "entityName": "Holding"},
                  "choice": "Describe",
                  "choiceArgument": {"prefix": "audit"},
                  "actingParties": ["party::alice"],
                  "consuming": false,
                  "witnessParties": ["party::alice"],
                  "exerciseResult": "audit: 12.5"
                }
              }
            ]
          }
        }
        """;

    private const string ReturnOutcomeTransaction = $$$"""
        {
          "transaction": {
            "updateId": "upd-1",
            "offset": "1",
            "events": [
              {
                "ExercisedEvent": {
                  "offset": "1",
                  "nodeId": 0,
                  "contractId": "00results",
                  "templateId": {"packageId": "{{{RichTypesPackageId}}}", "moduleName": "RichTypes", "entityName": "GenericResults"},
                  "choice": "ReturnOutcome",
                  "choiceArgument": {"wantWin": true},
                  "actingParties": ["party::alice"],
                  "consuming": false,
                  "witnessParties": ["party::alice"],
                  "exerciseResult": {"tag": "Win", "value": {"prize": "12.5", "tier": "gold"}}
                }
              }
            ]
          }
        }
        """;
}
