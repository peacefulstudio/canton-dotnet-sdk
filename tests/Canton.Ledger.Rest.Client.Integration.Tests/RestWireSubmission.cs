// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Canton.Ledger.Grpc.Client.Integration.Tests;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Integration.Tests;

internal static class RestWireSubmission
{
    internal const string SubmitAndWaitPath = "/v2/commands/submit-and-wait";

    internal static async Task<Party> NewOwnerAsync(
        RestLiveLane lane, string partyHint, CancellationToken cancellationToken)
    {
        var darOutcome = await lane.Fixture.UploadDarAsync(RichTypesDar.Path, cancellationToken);
        Assert.True(
            darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
            $"Unexpected DAR upload outcome: {darOutcome}");

        var party = await lane.Fixture.AllocatePartyAsync(partyHint, cancellationToken: cancellationToken);
        await lane.GrantActAsAsync(party.PartyId, cancellationToken);
        return new Party(party.PartyId);
    }

    internal static JsonObject AdaptedBodyOf(ICommand command, Party owner) =>
        BodyOf(RestCommandBuilder.BuildCommands(CommandsSubmission.Single(command, owner), userId: null));

    internal static JsonObject BodyOf(Raw.Commands commands) =>
        (JsonObject)JsonNode.Parse(JsonSerializer.Serialize(commands, RestRefitSettings.SerializerOptions))!;

    internal static async Task<WireOutcome> SubmitAsync(
        HttpClient wireClient, JsonObject body, CancellationToken cancellationToken)
    {
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await wireClient.PostAsync(SubmitAndWaitPath, content, cancellationToken);
        return new WireOutcome(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    internal sealed record WireOutcome(HttpStatusCode Status, string Body)
    {
        internal bool Accepted => Status == HttpStatusCode.OK;

        internal bool Refused => Status == HttpStatusCode.BadRequest;

        internal string Describe() => $"{(int)Status} {Body}";
    }
}
