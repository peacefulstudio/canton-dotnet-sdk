// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime.Data;
using Google.Protobuf;
using LfTransaction = Com.Digitalasset.Daml.Lf.Transaction;

namespace Canton.Ledger.Kernel.Wire;

/// <summary>
/// Decodes the <c>createdEventBlob</c> a participant, or a Token Standard registry, hands out for a
/// disclosed contract.
/// </summary>
public static class CreatedEventBlob
{
    private const long UnknownCreationTime = long.MinValue;
    private const long TicksPerMicrosecond = 10;

    private static readonly string[] SupportedSerializationVersions = ["2.1", "2", "3"];

    /// <summary>
    /// Reads the contract a <c>createdEventBlob</c> discloses: its template, signatories, observers, key
    /// maintainers and create argument.
    /// </summary>
    /// <param name="createdEventBlob">The blob, exactly as the Ledger API's <c>created_event_blob</c> carries it.</param>
    /// <returns>The decoded contract.</returns>
    /// <exception cref="MalformedResponseException">
    /// The bytes are not a Daml-LF contract instance, the serialization version is not one this SDK reads
    /// (<c>2.1</c>, <c>2</c> or <c>3</c>), or the contract lacks its id, template, package name or signatories.
    /// </exception>
    public static DecodedCreatedEventBlob Decode(ReadOnlyMemory<byte> createdEventBlob)
    {
        try
        {
            var versioned = LfTransaction.Versioned.Parser.ParseFrom(createdEventBlob.Span);
            RequireSupportedVersion(versioned.Version);
            var contract = LfTransaction.FatContractInstance.Parser.ParseFrom(versioned.Payload);
            return Decoded(versioned.Version, contract);
        }
        catch (InvalidProtocolBufferException unreadable)
        {
            throw new MalformedResponseException(
                "the createdEventBlob is not a readable Daml-LF contract instance.", unreadable);
        }
    }

    private static void RequireSupportedVersion(string version)
    {
        if (!SupportedSerializationVersions.Contains(version, StringComparer.Ordinal))
        {
            throw new MalformedResponseException(
                $"the createdEventBlob is serialization version '{version}', which this SDK does not read; "
                + $"it reads {string.Join(", ", SupportedSerializationVersions.Select(v => $"'{v}'"))}.");
        }
    }

    private static DecodedCreatedEventBlob Decoded(string version, LfTransaction.FatContractInstance contract)
    {
        var keyMaintainers = contract.ContractKeyWithMaintainers?.Maintainers.ToHashSet(StringComparer.Ordinal)
            ?? [];
        var signatories = contract.NonMaintainerSignatories.Concat(keyMaintainers).ToHashSet(StringComparer.Ordinal);
        if (signatories.Count == 0)
        {
            throw new MalformedResponseException("the createdEventBlob's contract instance names no signatory.");
        }

        return new DecodedCreatedEventBlob(
            version,
            ContractIdOf(contract),
            RequirePackageName(contract),
            TemplateIdOf(contract),
            signatories,
            contract.NonSignatoryStakeholders.ToHashSet(StringComparer.Ordinal),
            keyMaintainers,
            CreatedAtOf(contract.CreatedAt),
            contract.CreateArg.ToByteArray(),
            ContractKeyOf(contract),
            contract.ContractKeyWithMaintainers?.Hash.ToByteArray() ?? []);
    }

    private static byte[]? ContractKeyOf(LfTransaction.FatContractInstance contract) =>
        contract.ContractKeyWithMaintainers?.Key.ToByteArray();

    private static string ContractIdOf(LfTransaction.FatContractInstance contract) =>
        contract.ContractId.IsEmpty
            ? throw new MalformedResponseException("the createdEventBlob's contract instance has no contract id.")
            : Convert.ToHexStringLower(contract.ContractId.Span);

    private static string RequirePackageName(LfTransaction.FatContractInstance contract) =>
        contract.PackageName.Length == 0
            ? throw new MalformedResponseException("the createdEventBlob's contract instance has no package name.")
            : contract.PackageName;

    private static Identifier TemplateIdOf(LfTransaction.FatContractInstance contract)
    {
        var template = contract.TemplateId;
        return template is { PackageId.Length: > 0, ModuleName.Count: > 0, Name.Count: > 0 }
            ? new Identifier(template.PackageId, string.Join('.', template.ModuleName), string.Join('.', template.Name))
            : throw new MalformedResponseException("the createdEventBlob's contract instance has no complete template id.");
    }

    private static DateTimeOffset? CreatedAtOf(long microsecondsSinceEpoch)
    {
        if (microsecondsSinceEpoch == UnknownCreationTime)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.UnixEpoch.AddTicks(checked(microsecondsSinceEpoch * TicksPerMicrosecond));
        }
        catch (Exception outOfRange) when (outOfRange is OverflowException or ArgumentOutOfRangeException)
        {
            throw new MalformedResponseException(
                $"the createdEventBlob's creation time of {microsecondsSinceEpoch} microseconds is out of range.",
                outOfRange);
        }
    }
}
