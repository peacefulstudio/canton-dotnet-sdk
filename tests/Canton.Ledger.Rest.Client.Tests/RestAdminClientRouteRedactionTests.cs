// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Telemetry;
using Canton.Ledger.Testing.Helpers;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestAdminClientRouteRedactionTests : IDisposable
{
    private readonly RecordingHttpHandler _transport = new();
    private readonly List<IDisposable> _owned = [];

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
    }

    private IAdminClient Client()
    {
        var handler = new RestActivityHandler { InnerHandler = _transport };
        _owned.Add(handler);
        var factory = new AddressedHttpClientFactory(handler, new Uri("http://localhost:7575"));
        _owned.Add(factory);
        return new RestAdminClient(factory);
    }

    private static Activity HttpSpanOf(ActivityCapture capture) =>
        capture.Activities.Should().ContainSingle(a => a.GetTagItem(RestActivityHandler.UrlFull) != null).Subject;

    [Fact]
    public async Task GetPartiesAsync_records_the_party_route_template_in_place_of_the_party_id()
    {
        _transport.WithResponse(HttpStatusCode.OK, """{"partyDetails": [{"party": "alice::1220ab", "isLocal": true}]}""");
        var client = Client();
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.RestLedgerClient);

        await client.GetPartiesAsync([new Party("alice::1220ab")], TestContext.Current.CancellationToken);

        var span = HttpSpanOf(capture);
        span.GetTagItem(RestActivityHandler.UrlFull).Should().Be("http://localhost:7575/v2/parties/{party}");
        span.GetTagItem(RestActivityHandler.UrlTemplate).Should().Be("/v2/parties/{party}");
    }

    [Fact]
    public async Task GetUserAsync_records_the_user_route_template_in_place_of_the_user_id()
    {
        _transport.WithResponse(HttpStatusCode.OK, """{"user": {"id": "carol@example", "primaryParty": "alice::1220ab"}}""");
        var client = Client();
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.RestLedgerClient);

        await client.GetUserAsync("carol@example", TestContext.Current.CancellationToken);

        var span = HttpSpanOf(capture);
        span.GetTagItem(RestActivityHandler.UrlFull).Should().Be("http://localhost:7575/v2/users/{userId}");
        span.GetTagItem(RestActivityHandler.UrlTemplate).Should().Be("/v2/users/{userId}");
    }

    [Fact]
    public async Task GrantUserRightsAsync_records_the_user_rights_route_template_in_place_of_the_user_id()
    {
        _transport.WithResponse(HttpStatusCode.OK, """{"newlyGrantedRights": []}""");
        var client = Client();
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.RestLedgerClient);

        await client.GrantUserRightsAsync("carol@example", [new UserRight.ParticipantAdmin()], TestContext.Current.CancellationToken);

        var span = HttpSpanOf(capture);
        span.GetTagItem(RestActivityHandler.UrlFull).Should().Be("http://localhost:7575/v2/users/{userId}/rights");
        span.GetTagItem(RestActivityHandler.UrlTemplate).Should().Be("/v2/users/{userId}/rights");
    }

    [Fact]
    public async Task ListUserRightsAsync_records_the_user_rights_route_template_in_place_of_the_user_id()
    {
        _transport.WithResponse(HttpStatusCode.OK, """{"rights": []}""");
        var client = Client();
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.RestLedgerClient);

        await client.ListUserRightsAsync("carol@example", TestContext.Current.CancellationToken);

        var span = HttpSpanOf(capture);
        span.GetTagItem(RestActivityHandler.UrlFull).Should().Be("http://localhost:7575/v2/users/{userId}/rights");
        span.GetTagItem(RestActivityHandler.UrlTemplate).Should().Be("/v2/users/{userId}/rights");
    }

    [Fact]
    public async Task UpdatePartyDetailsAsync_records_the_party_route_template_in_place_of_the_party_id()
    {
        _transport.WithResponse(HttpStatusCode.OK, """{"partyDetails": {"party": "alice::1220ab", "isLocal": true}}""");
        var client = Client();
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.RestLedgerClient);

        await client.UpdatePartyDetailsAsync(
            new Party("alice::1220ab"),
            new PartyUpdate { Annotations = new Dictionary<string, string> { ["owner"] = "ops" } },
            cancellationToken: TestContext.Current.CancellationToken);

        var span = HttpSpanOf(capture);
        span.GetTagItem(RestActivityHandler.UrlFull).Should().Be("http://localhost:7575/v2/parties/{party}");
        span.GetTagItem(RestActivityHandler.UrlTemplate).Should().Be("/v2/parties/{party}");
    }

    [Fact]
    public async Task DeleteUserAsync_records_the_user_route_template_in_place_of_the_user_id()
    {
        var client = Client();
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.RestLedgerClient);

        await client.DeleteUserAsync("carol@example", cancellationToken: TestContext.Current.CancellationToken);

        HttpSpanOf(capture).GetTagItem(RestActivityHandler.UrlFull).Should().Be("http://localhost:7575/v2/users/{userId}");
    }

    private sealed class AddressedHttpClientFactory(HttpMessageHandler handler, Uri baseAddress) : IHttpClientFactory, IDisposable
    {
        private HttpClient? _client;

        public HttpClient CreateClient(string name) =>
            _client ??= new HttpClient(handler, disposeHandler: false) { BaseAddress = baseAddress };

        public void Dispose() => _client?.Dispose();
    }
}
