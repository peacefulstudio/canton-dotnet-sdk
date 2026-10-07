// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text.Json;
using Daml.Runtime.Contracts;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Data;

internal static class UndecodedJsonReader
{
    private static readonly MethodInfo ReadRecordMethod =
        typeof(DamlLfJsonDecoders).GetMethod(nameof(DamlLfJsonDecoders.ReadRecord), BindingFlags.Public | BindingFlags.Static, [typeof(JsonElement), typeof(DamlLfJsonDecodeContext)])!;

    internal static TResult ReadWith<TResult>(
        DamlUndecodedJson undecoded, string rootName, Func<JsonElement, DamlLfJsonDecodeContext, TResult> read)
    {
        var limits = DamlJsonSerializer.DefaultDeserializationLimits;
        DamlJsonSerializer.EnsureWithinInputLimit(undecoded.LfJson, limits);
        using var document = JsonDocument.Parse(undecoded.LfJson, DamlJsonSerializer.DocumentOptions);
        return read(document.RootElement, DamlLfJsonDecodeContext.Root(rootName, limits));
    }

    internal static DamlValue Read(DamlUndecodedJson undecoded, Type targetType) =>
        ReadWith(undecoded, targetType.Name, (json, context) => ReadAs(json, context, targetType));

    private static DamlValue ReadAs(JsonElement json, DamlLfJsonDecodeContext context, Type targetType)
    {
        if (json.ValueKind == JsonValueKind.Null)
            return DamlOptional.None;
        if (targetType == typeof(string))
            return DamlLfJsonDecoders.ReadText(json, context);
        if (targetType == typeof(long))
            return DamlLfJsonDecoders.ReadInt64(json, context);
        if (targetType == typeof(bool))
            return DamlLfJsonDecoders.ReadBool(json, context);
        if (targetType == typeof(decimal))
            return DamlLfJsonDecoders.ReadNumeric(json, context);
        if (targetType == typeof(DateOnly))
            return DamlLfJsonDecoders.ReadDate(json, context);
        if (targetType == typeof(DateTimeOffset))
            return DamlLfJsonDecoders.ReadTimestamp(json, context);
        if (targetType == typeof(Party))
            return DamlLfJsonDecoders.ReadParty(json, context);
        if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(ContractId<>))
            return DamlLfJsonDecoders.ReadContractId(json, context);
        if (IsGeneratedRecord(targetType))
            return (DamlRecord)ReadRecordMethod.MakeGenericMethod(targetType).Invoke(
                null, BindingFlags.DoNotWrapExceptions, binder: null, [json, context], culture: null)!;

        throw new NotSupportedException(
            $"Cannot decode carried Daml-LF JSON to {targetType}. " +
            $"Only a generated record, a scalar, a Party or a ContractId has a JSON reader to decode it with.");
    }

    private static bool IsGeneratedRecord(Type type) =>
        type.GetInterfaces().Any(contract =>
            contract.IsGenericType
            && contract.GetGenericTypeDefinition() == typeof(IDamlRecord<>)
            && contract.GetGenericArguments()[0] == type);
}
