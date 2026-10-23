// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using AwesomeAssertions;
using Canton.Ledger.Kernel.Telemetry;
using Refit;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestActivityHandlerPathRedactionTests : IDisposable
{
    private readonly List<Activity> _activities = [];
    private readonly ActivityListener _listener;
    private readonly RecordingHttpHandler _transport = new();
    private readonly string _ownHost = $"{Guid.NewGuid():N}.ledger.example";
    private readonly HttpClient _client;
    private readonly IProbeApi _api;

    public RestActivityHandlerPathRedactionTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == LedgerActivitySource.NameFor<RestLedgerClient>(),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (CapturesOwnRequest(activity)) _activities.Add(activity);
            },
        };
        ActivitySource.AddActivityListener(_listener);

        var handler = new RestActivityHandler { InnerHandler = _transport };
        _client = new HttpClient(handler) { BaseAddress = new Uri($"http://{_ownHost}:7575") };
        _api = RestService.For<IProbeApi>(_client);
    }

    private bool CapturesOwnRequest(Activity activity) =>
        activity.GetTagItem(RestActivityHandler.UrlFull) is string url
        && url.StartsWith($"http://{_ownHost}:7575", StringComparison.Ordinal);

    public void Dispose()
    {
        _listener.Dispose();
        _client.Dispose();
    }

    public interface IProbeApi
    {
        [Get("/v2/parties/{party}")]
        Task<HttpResponseMessage> GetParty(string party);

        [Get("/v2/users/{userId}/rights")]
        Task<HttpResponseMessage> GetUserRights(string userId);

        [Get("/v2/version")]
        Task<HttpResponseMessage> GetVersion();

        [Get("/v2/state/connected-synchronizers")]
        Task<HttpResponseMessage> GetConnectedSynchronizers(string party);
    }

    [Fact]
    public async Task SendAsync_replaces_a_party_path_parameter_with_its_template_in_url_full()
    {
        await _api.GetParty("alice::1220abcdef");

        var activity = _activities.Should().ContainSingle().Subject;
        activity.GetTagItem(RestActivityHandler.UrlFull).Should().Be($"http://{_ownHost}:7575/v2/parties/{{party}}");
        activity.GetTagItem(RestActivityHandler.UrlTemplate).Should().Be("/v2/parties/{party}");
    }

    [Fact]
    public async Task SendAsync_replaces_a_user_path_parameter_with_its_template_in_url_full()
    {
        await _api.GetUserRights("operator-user-42");

        var activity = _activities.Should().ContainSingle().Subject;
        activity.GetTagItem(RestActivityHandler.UrlFull).Should().Be($"http://{_ownHost}:7575/v2/users/{{userId}}/rights");
        activity.GetTagItem(RestActivityHandler.UrlTemplate).Should().Be("/v2/users/{userId}/rights");
    }

    [Fact]
    public async Task SendAsync_keeps_a_parameterless_path_and_names_it_as_its_own_template()
    {
        await _api.GetVersion();

        var activity = _activities.Should().ContainSingle().Subject;
        activity.GetTagItem(RestActivityHandler.UrlFull).Should().Be($"http://{_ownHost}:7575/v2/version");
        activity.GetTagItem(RestActivityHandler.UrlTemplate).Should().Be("/v2/version");
    }

    [Fact]
    public async Task SendAsync_keeps_a_query_parameter_out_of_url_full_and_url_template()
    {
        await _api.GetConnectedSynchronizers("alice::1220abcdef");

        var activity = _activities.Should().ContainSingle().Subject;
        activity.GetTagItem(RestActivityHandler.UrlFull).Should().Be($"http://{_ownHost}:7575/v2/state/connected-synchronizers");
        activity.GetTagItem(RestActivityHandler.UrlTemplate).Should().Be("/v2/state/connected-synchronizers");
    }

    [Fact]
    public async Task SendAsync_keeps_a_base_address_path_prefix_in_url_full_and_url_template()
    {
        using var prefixedClient = new HttpClient(new RestActivityHandler { InnerHandler = _transport })
        {
            BaseAddress = new Uri($"http://{_ownHost}:7575/json-api"),
        };

        await RestService.For<IProbeApi>(prefixedClient).GetParty("alice::1220abcdef");

        _transport.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/json-api/v2/parties/alice%3A%3A1220abcdef");
        var activity = _activities.Should().ContainSingle().Subject;
        activity.GetTagItem(RestActivityHandler.UrlFull).Should().Be($"http://{_ownHost}:7575/json-api/v2/parties/{{party}}");
        activity.GetTagItem(RestActivityHandler.UrlTemplate).Should().Be("/json-api/v2/parties/{party}");
    }

    [Fact]
    public async Task SendAsync_falls_back_to_the_template_when_it_has_more_segments_than_the_request_path()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/v2/users", UriKind.Relative));
        request.Options.Set(
            new HttpRequestOptionsKey<string>(HttpRequestMessageOptions.RelativePathTemplate),
            "/v2/users/{userId}/rights");

        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        var activity = _activities.Should().ContainSingle().Subject;
        activity.GetTagItem(RestActivityHandler.UrlFull).Should().Be($"http://{_ownHost}:7575/v2/users/{{userId}}/rights");
        activity.GetTagItem(RestActivityHandler.UrlTemplate).Should().Be("/v2/users/{userId}/rights");
    }

    [Fact]
    public async Task SendAsync_emits_no_url_template_for_a_request_that_did_not_come_from_a_refit_method()
    {
        await _client.GetAsync(new Uri("/v2/version", UriKind.Relative), TestContext.Current.CancellationToken);

        var activity = _activities.Should().ContainSingle().Subject;
        activity.GetTagItem(RestActivityHandler.UrlTemplate).Should().BeNull();
        activity.GetTagItem(RestActivityHandler.UrlFull).Should().Be($"http://{_ownHost}:7575/v2/version");
    }
}
