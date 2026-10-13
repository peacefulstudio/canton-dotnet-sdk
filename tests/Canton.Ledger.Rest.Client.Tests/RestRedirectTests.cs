// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestRedirectTests
{
    private static ServiceProvider BuildProvider(string httpAddress)
    {
        var services = new ServiceCollection();
        services.AddRestLedgerClient(options => options.HttpAddress = httpAddress);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddRestLedgerClient_primary_handler_does_not_follow_redirects()
    {
        using var provider = BuildProvider("https://ledger.example:7575");
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(ServiceCollectionExtensions.HttpClientName);
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler!;

        handler.Should().BeOfType<SocketsHttpHandler>().Which.AllowAutoRedirect.Should().BeFalse();
    }

    [Fact]
    public async Task GetLedgerEndAsync_reports_a_redirect_as_a_rejection_without_following_it()
    {
        using var servers = new RedirectingLoopbackServers();
        await using var provider = BuildProvider(servers.RedirectorAddress);
        var reader = provider.GetRequiredService<ILedgerReader>();

        var act = () => reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var failure = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        failure.Status.Should().Be(new TransportStatus.Http(HttpStatusCode.TemporaryRedirect));
        servers.TargetRequestBodies.Should().BeEmpty();
    }
}
