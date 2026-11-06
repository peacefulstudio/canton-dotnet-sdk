// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Streams;
using Canton.Ledger.Kernel.Wire;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using WireCreatedEvent = Canton.Ledger.Rest.Client.Raw.CreatedEvent;

namespace Canton.Ledger.Rest.Client;

internal static partial class RestContractStreamProjector
{
    public static CreatedContract<T> ProjectCreatedContract<T>(WireCreatedEvent? created)
        where T : ITemplate, IDamlRecord<T>
    {
        if (created is null)
        {
            throw MalformedResponse.MissingRequiredField("the contract-by-id response has no createdEvent");
        }

        RestProjectionCore.RequireTemplateId(created.TemplateId, nameof(Raw.CreatedEvent), created.ContractId);
        if (!RestMarkerMatcher<T>.MatchesCreated(created) || !RestProjectionCore.TryResolvePayload<T, T>(created, out var payload))
        {
            throw new ProjectionRefusalException(
                $"Contract '{created.ContractId}' is a {created.TemplateId.ModuleName}.{created.TemplateId.EntityName}, "
                + $"which cannot be read as {typeof(T).Name}.");
        }

        return new CreatedContract<T>(
            RestWireConversions.ToContractId<T>(created.ContractId),
            payload,
            RestProjectionCore.ContractKeyOf(created),
            RestWireConversions.ToPartyList(created.WitnessParties));
    }

    public static ContractLifecycle<T> ProjectContractLifecycle<T>(Raw.GetEventsByContractIdResponse response)
        where T : ITemplate, IDamlRecord<T>
    {
        var (created, archived) = RestProjectionCore.ProjectContractLifecycle<ContractArms<T>, ContractStreamEvent<T>, T, T>(response);
        return new ContractLifecycle<T>(created as ContractStreamEvent<T>.Created, archived as ContractStreamEvent<T>.Archived);
    }
}
