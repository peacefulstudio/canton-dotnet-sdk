// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Resilience;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Data;
using Microsoft.Extensions.Options;
using Xunit;
using PartyDetails = Canton.Ledger.Abstractions.PartyDetails;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestAdminClientUpdateTests : IDisposable
{
    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    private RestAdminClient ClientWith(HttpMessageHandler handler)
    {
        var factory = new StubHttpClientFactory(handler);
        _factories.Add(factory);
        return new RestAdminClient(factory);
    }

    private RestAdminClient RetryingClientWith(RecordingHttpHandler transport)
    {
        var retryHandler = new RestRetryHandler(Options.Create(new RestLedgerClientOptions
        {
            HttpAddress = "http://localhost:7575",
            Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 3, Delay = TimeSpan.Zero },
        }))
        {
            InnerHandler = transport,
        };
        return ClientWith(retryHandler);
    }

    [Fact]
    public async Task UpdateUserAsync_patches_the_user_with_an_object_field_mask_and_maps_the_updated_user()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, """{"user": {"id": "alice", "primaryParty": "alice::1220"}}""");
        IAdminClient client = ClientWith(transport);

        var details = await client.UpdateUserAsync(
            "alice",
            new UserUpdate { IsDeactivated = true },
            cancellationToken: TestContext.Current.CancellationToken);

        details.Should().Be(new UserDetails("alice", new Party("alice::1220")));
        transport.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/users/alice");
        transport.LastRequestBody.Should().Contain("\"paths\":[\"is_deactivated\"]");
        transport.LastRequestBody.Should().Contain("\"unknownFields\"");
        transport.LastRequestBody.Should().Contain("\"isDeactivated\":true");
    }

    [Fact]
    public async Task UpdateUserAsync_is_never_replayed_by_the_opt_in_retry_pipeline()
    {
        var transport = new RecordingHttpHandler()
            .WithTransportException(new HttpRequestException("connection reset after the request was sent"));
        IAdminClient client = RetryingClientWith(transport);

        var act = () => client.UpdateUserAsync(
            "alice", new UserUpdate { IsDeactivated = true }, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task DeleteUserAsync_deletes_v2_users_by_id_and_is_never_replayed()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{}");
        IAdminClient client = ClientWith(transport);

        await client.DeleteUserAsync("alice", cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/users/alice");
    }

    [Fact]
    public async Task DeleteUserAsync_with_an_identity_provider_id_is_not_supported_and_sends_nothing()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{}");
        IAdminClient client = ClientWith(transport);

        var act = () => client.DeleteUserAsync("alice", "idp-1", TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("identity-provider-scoped user delete");
        transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteUserAsync_without_an_identity_provider_id_sends_no_query_parameter()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{}");
        IAdminClient client = ClientWith(transport);

        await client.DeleteUserAsync("alice", null, TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.Query.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteUserAsync_is_never_replayed_by_the_opt_in_retry_pipeline()
    {
        var transport = new RecordingHttpHandler()
            .WithTransportException(new HttpRequestException("connection reset after the request was sent"));
        IAdminClient client = RetryingClientWith(transport);

        var act = () => client.DeleteUserAsync("alice", cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task UpdateUserIdentityProviderIdAsync_patches_the_user_identity_provider_id_route()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{}");
        IAdminClient client = ClientWith(transport);

        await client.UpdateUserIdentityProviderIdAsync(
            "alice", null, "idp-2", TestContext.Current.CancellationToken);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/users/alice/identity-provider-id");
        transport.LastRequestBody.Should().Contain("\"targetIdentityProviderId\":\"idp-2\"");
    }

    [Fact]
    public async Task UpdatePartyDetailsAsync_patches_the_party_with_the_local_metadata_annotations_mask()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, """{"partyDetails": {"party": "alice::1220", "isLocal": true}}""");
        IAdminClient client = ClientWith(transport);

        var details = await client.UpdatePartyDetailsAsync(
            new Party("alice::1220"),
            new PartyUpdate { Annotations = new Dictionary<string, string> { ["owner"] = "ops" } },
            cancellationToken: TestContext.Current.CancellationToken);

        details.Should().Be(new PartyDetails(new Party("alice::1220"), IsLocal: true));
        transport.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/parties/alice%3A%3A1220");
        transport.LastRequestBody.Should().Contain("\"paths\":[\"local_metadata.annotations\"]");
        transport.LastRequestBody.Should().Contain("\"owner\":\"ops\"");
    }

    [Fact]
    public async Task UpdatePartyIdentityProviderIdAsync_is_not_served_by_the_JSON_API()
    {
        IAdminClient client = ClientWith(new RecordingHttpHandler());

        var act = () => client.UpdatePartyIdentityProviderIdAsync(
            new Party("alice::1220"), null, "idp-2", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task GetCommandStatusAsync_is_not_served_by_the_JSON_API()
    {
        IAdminClient client = ClientWith(new RecordingHttpHandler());

        var act = () => client.GetCommandStatusAsync(cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
