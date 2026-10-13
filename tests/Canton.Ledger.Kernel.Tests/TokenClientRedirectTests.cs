// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public sealed class TokenClientRedirectTests
{
    private static ServiceProvider BuildProvider(string tokenEndpoint)
    {
        var services = new ServiceCollection();
        services.AddCantonAuth(options =>
        {
            options.ClientId = "my-client";
            options.ClientSecret = "my-secret";
            options.TokenEndpoint = new Uri(tokenEndpoint);
            options.AllowInsecureTokenEndpoint = true;
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddCantonAuth_primary_handler_does_not_follow_redirects()
    {
        using var provider = BuildProvider("http://127.0.0.1:1/oauth/token");
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler("CantonAuth");
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler!;

        handler.Should().BeOfType<SocketsHttpHandler>().Which.AllowAutoRedirect.Should().BeFalse();
    }

    [Fact]
    public async Task GetTokenAsync_surfaces_a_redirect_as_a_failed_request_without_resending_the_client_secret()
    {
        using var servers = new RedirectingLoopbackServers();
        await using var provider = BuildProvider($"{servers.RedirectorAddress}/oauth/token");
        var tokenProvider = provider.GetRequiredService<ITokenProvider>();

        var act = () => tokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

        var failure = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        failure.StatusCode.Should().Be(HttpStatusCode.TemporaryRedirect);
        servers.TargetRequestBodies.Should().BeEmpty();
    }
}
