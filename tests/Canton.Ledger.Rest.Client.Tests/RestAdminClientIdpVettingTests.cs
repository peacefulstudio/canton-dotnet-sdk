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

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestAdminClientIdpVettingTests : IDisposable
{
    private const string ConfigJson =
        """{"identityProviderConfig": {"identityProviderId": "idp-1", "isDeactivated": false, "issuer": "https://issuer.invalid", "jwksUrl": "https://issuer.invalid/jwks", "audience": "aud"}}""";

    private static readonly IdentityProviderConfig Config =
        new("idp-1", false, "https://issuer.invalid", "https://issuer.invalid/jwks", "aud");

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
    public async Task CreateIdentityProviderConfigAsync_posts_to_v2_idps_and_maps_the_stored_config()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, ConfigJson);
        IAdminClient client = ClientWith(transport);

        var created = await client.CreateIdentityProviderConfigAsync(Config, TestContext.Current.CancellationToken);

        created.Should().Be(Config);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/idps");
        transport.LastRequestBody.Should().Contain("\"identityProviderId\":\"idp-1\"");
        transport.LastRequestBody.Should().Contain("\"jwksUrl\":\"https://issuer.invalid/jwks\"");
    }

    [Fact]
    public async Task CreateIdentityProviderConfigAsync_is_never_replayed_by_the_opt_in_retry_pipeline()
    {
        var transport = new RecordingHttpHandler()
            .WithTransportException(new HttpRequestException("connection reset after the request was sent"));
        IAdminClient client = RetryingClientWith(transport);

        var act = () => client.CreateIdentityProviderConfigAsync(Config, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task GetIdentityProviderConfigAsync_reads_v2_idps_by_id()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, ConfigJson);
        IAdminClient client = ClientWith(transport);

        var read = await client.GetIdentityProviderConfigAsync("idp-1", TestContext.Current.CancellationToken);

        read.Should().Be(Config);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/idps/idp-1");
    }

    [Fact]
    public async Task GetIdentityProviderConfigAsync_returns_null_on_404()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.NotFound, "{}");
        IAdminClient client = ClientWith(transport);

        var read = await client.GetIdentityProviderConfigAsync("idp-1", TestContext.Current.CancellationToken);

        read.Should().BeNull();
    }

    [Fact]
    public async Task ListIdentityProviderConfigsAsync_maps_every_config()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """{"identityProviderConfigs": [{"identityProviderId": "idp-1", "isDeactivated": false, "issuer": "https://issuer.invalid", "jwksUrl": "https://issuer.invalid/jwks", "audience": "aud"}]}""");
        IAdminClient client = ClientWith(transport);

        var listed = await client.ListIdentityProviderConfigsAsync(TestContext.Current.CancellationToken);

        listed.Should().Equal(Config);
        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/idps");
    }

    [Fact]
    public async Task UpdateIdentityProviderConfigAsync_patches_with_an_object_field_mask()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, ConfigJson);
        IAdminClient client = ClientWith(transport);

        var updated = await client.UpdateIdentityProviderConfigAsync(
            "idp-1",
            new IdentityProviderConfigUpdate { Issuer = "https://issuer.invalid" },
            TestContext.Current.CancellationToken);

        updated.Should().Be(Config);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/idps/idp-1");
        transport.LastRequestBody.Should().Contain("\"paths\":[\"issuer\"]");
        transport.LastRequestBody.Should().Contain("\"unknownFields\"");
    }

    [Fact]
    public async Task DeleteIdentityProviderConfigAsync_deletes_v2_idps_by_id_and_is_never_replayed()
    {
        var transport = new RecordingHttpHandler()
            .WithTransportException(new HttpRequestException("connection reset after the request was sent"));
        IAdminClient client = RetryingClientWith(transport);

        var act = () => client.DeleteIdentityProviderConfigAsync("idp-1", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
        transport.Requests.Should().ContainSingle();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/idps/idp-1");
    }

    [Fact]
    public async Task UpdateVettedPackagesAsync_posts_the_changes_and_options_and_maps_the_result()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """{"newVettedPackages": {"packages": [{"packageId": "pkg-1", "packageName": "name", "packageVersion": "1.0.0"}], "participantId": "participant::1220", "synchronizerId": "sync::1220", "topologySerial": 3}}""");
        IAdminClient client = ClientWith(transport);

        var result = await client.UpdateVettedPackagesAsync(
            [new VettedPackagesChange.Vet([new PackageSelector("pkg-1")]), new VettedPackagesChange.Unvet([new PackageSelector("pkg-2")])],
            dryRun: true,
            synchronizerId: new SynchronizerId("sync::1220"),
            expectedTopologySerial: ExpectedTopologySerial.At(7),
            safetyOverrides: VettingOverrides.AllowVetIncompatibleUpgrades,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Past.Should().BeNull();
        result.New!.Packages.Should().Equal(new VettedPackageEntry("pkg-1", "name", "1.0.0", null, null));
        result.New.TopologySerial.Should().Be(3u);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/package-vetting/update");
        transport.LastRequestBody.Should().Contain("\"dryRun\":true");
        transport.LastRequestBody.Should().Contain("\"synchronizerId\":\"sync::1220\"");
        transport.LastRequestBody.Should().Contain("\"serial\":{\"Prior\":{\"value\":7}}");
        transport.LastRequestBody.Should().Contain("UPDATE_VETTED_PACKAGES_FORCE_FLAG_ALLOW_VET_INCOMPATIBLE_UPGRADES");
        transport.LastRequestBody.Should().Contain("\"Vet\":{\"value\":{\"packages\"");
        transport.LastRequestBody.Should().Contain("\"Unvet\":{\"value\":{\"packages\"");
    }

    [Fact]
    public async Task UpdateVettedPackagesAsync_is_never_replayed_by_the_opt_in_retry_pipeline()
    {
        var transport = new RecordingHttpHandler()
            .WithTransportException(new HttpRequestException("connection reset after the request was sent"));
        IAdminClient client = RetryingClientWith(transport);

        var act = () => client.UpdateVettedPackagesAsync([], cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task PruneAsync_is_not_served_by_the_JSON_API()
    {
        IAdminClient client = ClientWith(new RecordingHttpHandler());

        var act = () => client.PruneAsync(1, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
