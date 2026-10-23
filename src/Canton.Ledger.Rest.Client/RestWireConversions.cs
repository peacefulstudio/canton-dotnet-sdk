// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Canton.Ledger.Kernel.Wire;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using WireIdentifier = Canton.Ledger.Rest.Client.Raw.Identifier;

namespace Canton.Ledger.Rest.Client;

internal static class RestWireConversions
{
    public static EquatableArray<Party> ToPartyList(IEnumerable<string>? wireParties) =>
        MalformedResponse.Decoding(wireParties, ToParties);

    private static EquatableArray<Party> ToParties(IEnumerable<string>? wireParties)
    {
        if (wireParties is null) return [];
        var result = new List<Party>();
        foreach (var party in wireParties)
        {
            result.Add((Party)party);
        }
        return EquatableArray.Create(result);
    }

    public static ContractId<T> ToContractId<T>(string wireContractId)
        where T : IDamlType =>
        MalformedResponse.Decoding(wireContractId, id => new ContractId<T>(id));

    public static RuntimeIdentifier ToRuntimeIdentifier(WireIdentifier identifier) =>
        new(identifier.PackageId, identifier.ModuleName, identifier.EntityName);

    public static WireIdentifier ToPackageNameReference(DamlTypeDescriptor descriptor)
    {
        var referenced = descriptor.Identifier.WithPackageNameReference(descriptor.PackageName);
        return new WireIdentifier
        {
            PackageId = referenced.PackageId,
            ModuleName = referenced.ModuleName,
            EntityName = referenced.EntityName,
        };
    }

    internal static string? ToKeyHash(string? contractKeyHash) =>
        string.IsNullOrEmpty(contractKeyHash) ? null : contractKeyHash;

    public static long ParseOffset(string? wireOffset) => ParseNonNegativeInt64(wireOffset, "offset");

    public static long ParseReassignmentCounter(string? wireCounter) =>
        ParseNonNegativeInt64(wireCounter, "reassignment counter");

    public static long ParsePaidTrafficCost(string? wireCost) =>
        ParseNonNegativeInt64(wireCost, "paid traffic cost");

    public static long? ToPaidTrafficCost(string? wireCost) =>
        string.IsNullOrEmpty(wireCost) ? null : ParsePaidTrafficCost(wireCost);

    public static bool TryParseOffset(string? wireOffset, out long offset) =>
        long.TryParse(wireOffset, NumberStyles.None, CultureInfo.InvariantCulture, out offset);

    private static long ParseNonNegativeInt64(string? wireValue, string fieldName) =>
        long.TryParse(wireValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException($"Cannot parse wire {fieldName} '{wireValue}' as a non-negative integer.");
}
