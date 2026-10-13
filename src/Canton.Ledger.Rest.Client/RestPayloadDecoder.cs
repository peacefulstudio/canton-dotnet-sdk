// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Daml.Runtime.Contracts;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using WireCreatedEvent = Canton.Ledger.Rest.Client.Raw.CreatedEvent;
using WireExercisedEvent = Canton.Ledger.Rest.Client.Raw.ExercisedEvent;
using WireRecord = Canton.Ledger.Rest.Client.Raw.Record;
using WireValue = Canton.Ledger.Rest.Client.Raw.Value;

namespace Canton.Ledger.Rest.Client;

internal sealed record ExercisePayloads(DamlValue Argument, DamlValue Result);

internal sealed record CreatePayload(DamlRecord Arguments, DamlUndecodedJson? Undecoded);

/// <summary>
/// Decodes the Daml payloads of a JSON Ledger API event — a create argument, a contract key, a choice
/// argument and exercise result — against the generated types the generated type registry holds for the
/// event's template or interface. A payload whose type is not registered, or is registered by more than one
/// generated type, is carried as the <see cref="DamlUndecodedJson"/> the participant sent: nothing is inferred
/// from the JSON alone, and a later decode through the generated type's own reader recovers the value.
/// A template that is not registered under the event's exact package id is found by the event's package name,
/// module and entity; an event with no package name is never found that way. A payload that does not fit a type found
/// that way is carried as the <see cref="DamlUndecodedJson"/> the participant sent, tagged with its package id, instead
/// of failing the read. An interface is found by its exact package id only.
/// A key that arrives for a registered template declaring none is still refused with
/// <see cref="TemplateTypeRequiredException"/>.
/// </summary>
internal static class RestPayloadDecoder
{
    public static CreatePayload CreatePayloadOf(WireCreatedEvent created, RuntimeIdentifier templateId)
    {
        var argument = RequireCreateArgument(created);
        return GeneratedTypeReaders.FindRecordReader(templateId, created.PackageName) switch
        {
            RegistryLookup<DamlLfElementReader>.Resolved template => DecodeOrUndecode(
                template.ViaPackageName,
                () => new CreatePayload(
                    RestValueDecoder.ToDamlRecord(argument, template.Value, template.DeclaringType.Name),
                    Undecoded: null),
                () => UndecodedCreatePayload(argument, templateId, templateId.PackageId)),
            _ => UndecodedCreatePayload(argument, templateId, packageId: null),
        };
    }

    private static CreatePayload UndecodedCreatePayload(WireRecord argument, RuntimeIdentifier templateId, string? packageId) =>
        new(new DamlRecord(templateId, []), RestValueDecoder.ToUndecodedJson(argument, packageId));

    private static TDecoded DecodeOrUndecode<TDecoded>(bool viaPackageName, Func<TDecoded> decode, Func<TDecoded> undecoded)
    {
        if (!viaPackageName)
        {
            return decode();
        }

        try
        {
            return decode();
        }
        catch (MalformedResponseException)
        {
            return undecoded();
        }
    }

    public static WireRecord RequireCreateArgument(WireCreatedEvent created) =>
        created.CreateArgument
        ?? throw MalformedResponse.MissingRequiredField(
            $"CreatedEvent for contract '{created.ContractId}' has no createArgument");

    public static ContractKey? ContractKeyOf(WireCreatedEvent created, RuntimeIdentifier templateId)
    {
        if (created.ContractKey is null || RestValueDecoder.IsJsonNull(created.ContractKey))
        {
            return null;
        }

        return new ContractKey(KeyValueOf(created.ContractKey, templateId, created.PackageName), templateId)
        {
            KeyHash = RestWireConversions.ToKeyHash(created.ContractKeyHash),
        };
    }

    private static DamlValue KeyValueOf(WireValue contractKey, RuntimeIdentifier templateId, string? packageName)
    {
        if (GeneratedTypeReaders.FindRecordReader(templateId, packageName) is not RegistryLookup<DamlLfElementReader>.Resolved template)
        {
            return RestValueDecoder.ToUndecodedJson(contractKey);
        }

        return GeneratedTypeReaders.FindKeyDescriptor(templateId, packageName) switch
        {
            RegistryLookup<IKeyDescriptor>.Resolved keyDescriptor => DecodeOrUndecode<DamlValue>(
                keyDescriptor.ViaPackageName,
                () => RestValueDecoder.ToDamlValue(
                    contractKey, keyDescriptor.Value.ReadKeyJson, $"{template.DeclaringType.Name}.key"),
                () => RestValueDecoder.ToUndecodedJson(contractKey, templateId.PackageId)),
            RegistryLookup<IKeyDescriptor>.Ambiguous => RestValueDecoder.ToUndecodedJson(contractKey),
            _ => throw TemplateTypeRequiredException.ForKeylessTemplate(
                Display(templateId), template.DeclaringType.FullName ?? template.DeclaringType.Name),
        };
    }

    public static ExercisePayloads ExercisePayloadsOf(WireExercisedEvent exercised, RuntimeIdentifier templateId)
    {
        var choiceName = exercised.Choice
            ?? throw MalformedResponse.MissingRequiredField(
                $"ExercisedEvent for contract '{exercised.ContractId}' has no choice");
        var choiceArgument = exercised.ChoiceArgument
            ?? throw MalformedResponse.MissingRequiredField(
                $"ExercisedEvent for contract '{exercised.ContractId}' has no choiceArgument");
        var exerciseResult = exercised.ExerciseResult
            ?? throw MalformedResponse.MissingRequiredField(
                $"ExercisedEvent for contract '{exercised.ContractId}' has no exerciseResult");

        var ownerId = exercised.InterfaceId is { } interfaceId
            ? RestWireConversions.ToRuntimeIdentifier(interfaceId)
            : templateId;
        if (string.IsNullOrWhiteSpace(choiceName))
        {
            throw new TemplateTypeRequiredException(Display(ownerId), choiceName);
        }

        var ownerPackageName = exercised.InterfaceId is null ? exercised.PackageName : null;
        return GeneratedTypeReaders.FindChoice(ownerId, new ChoiceName(choiceName), ownerPackageName) switch
        {
            RegistryLookup<IChoice>.Resolved choice => DecodeOrUndecode(
                choice.ViaPackageName,
                () => new ExercisePayloads(
                    RestValueDecoder.ToDamlValue(choiceArgument, choice.Value.ReadArgumentJson, $"{choiceName}.argument"),
                    RestValueDecoder.ToDamlValue(exerciseResult, choice.Value.ReadResultJson, $"{choiceName}.result")),
                () => new ExercisePayloads(
                    RestValueDecoder.ToUndecodedJson(choiceArgument, ownerId.PackageId),
                    RestValueDecoder.ToUndecodedJson(exerciseResult, ownerId.PackageId))),
            _ => new ExercisePayloads(
                RestValueDecoder.ToUndecodedJson(choiceArgument),
                RestValueDecoder.ToUndecodedJson(exerciseResult)),
        };
    }

    private static string Display(RuntimeIdentifier identifier) =>
        $"{identifier.PackageId}:{identifier.ModuleName}:{identifier.EntityName}";
}
