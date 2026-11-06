// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Data;

namespace Splice.Api.Token.Registry.Client;

/// <summary>
/// Maps the disclosed contracts a registry returns in a choice context onto the
/// transport-neutral <see cref="DisclosedContract"/> a ledger submission carries. Every registry
/// namespace generates its own wire type for the same schema, so each gets an overload.
/// </summary>
public static class DisclosedContractMapping
{
    /// <summary>Maps a disclosed contract of the allocation instruction V2 API.</summary>
    /// <param name="wire">The disclosed contract as the registry returned it.</param>
    /// <returns>The disclosed contract to attach to a submission.</returns>
    /// <exception cref="FormatException">The <c>createdEventBlob</c> is empty or not base64.</exception>
    public static DisclosedContract ToDisclosedContract(this AllocationInstructionV2.DisclosedContract wire) =>
        Map(wire.ContractId, wire.TemplateId, wire.CreatedEventBlob, wire.SynchronizerId);

    /// <summary>Maps a disclosed contract of the allocation V2 API.</summary>
    /// <param name="wire">The disclosed contract as the registry returned it.</param>
    /// <returns>The disclosed contract to attach to a submission.</returns>
    /// <exception cref="FormatException">The <c>createdEventBlob</c> is empty or not base64.</exception>
    public static DisclosedContract ToDisclosedContract(this AllocationV2.DisclosedContract wire) =>
        Map(wire.ContractId, wire.TemplateId, wire.CreatedEventBlob, wire.SynchronizerId);

    private static DisclosedContract Map(string contractId, string templateId, string createdEventBlob, string? synchronizerId)
    {
        var blob = Convert.FromBase64String(createdEventBlob);
        if (blob.Length == 0)
        {
            throw new FormatException("The disclosed contract has an empty createdEventBlob.");
        }

        return new DisclosedContract(contractId, Identifier.Parse(templateId), blob)
        {
            SynchronizerId = SynchronizerId.FromWire(synchronizerId),
        };
    }
}
