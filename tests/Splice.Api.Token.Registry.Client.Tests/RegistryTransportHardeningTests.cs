// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Splice.Api.Token.Registry.Client.TokenMetadataV1;
using Xunit;

namespace Splice.Api.Token.Registry.Client.Tests;

public class RegistryTransportHardeningTests
{
    [Fact]
    public async Task AddSpliceTokenRegistryClients_surfaces_a_redirect_instead_of_following_it()
    {
        using var registry = new LoopbackRegistry(context =>
        {
            context.Response.StatusCode = (int)HttpStatusCode.Found;
            context.Response.RedirectLocation = "/elsewhere";
        });
        var services = new ServiceCollection();
        services.AddSpliceTokenRegistryClients(registry.BaseAddress);
        using var provider = services.BuildServiceProvider();
        var api = provider.GetRequiredService<ITokenMetadataV1Api>();

        var failure = await api.Invoking(a => a.GetRegistryInfo(TestContext.Current.CancellationToken))
            .Should().ThrowAsync<Refit.ApiException>();

        failure.Which.StatusCode.Should().Be(HttpStatusCode.Found);
        registry.RequestedPaths.Should().Equal("/registry/metadata/v1/info");
    }

    [Fact]
    public async Task AddSpliceTokenRegistryClients_keeps_refusing_redirects_when_the_host_configures_the_sockets_handler()
    {
        using var registry = new LoopbackRegistry(context =>
        {
            context.Response.StatusCode = (int)HttpStatusCode.Found;
            context.Response.RedirectLocation = "/elsewhere";
        });
        var services = new ServiceCollection();
        services.AddSpliceTokenRegistryClients(registry.BaseAddress)
            .UseSocketsHttpHandler((handler, _) => handler.MaxConnectionsPerServer = 4);
        using var provider = services.BuildServiceProvider();
        var api = provider.GetRequiredService<ITokenMetadataV1Api>();

        var failure = await api.Invoking(a => a.GetRegistryInfo(TestContext.Current.CancellationToken))
            .Should().ThrowAsync<Refit.ApiException>();

        failure.Which.StatusCode.Should().Be(HttpStatusCode.Found);
        registry.RequestedPaths.Should().Equal("/registry/metadata/v1/info");
    }

    [Fact]
    public void AddSpliceTokenRegistryClients_bounds_every_registry_call_to_thirty_seconds()
    {
        var services = new ServiceCollection();
        services.AddSpliceTokenRegistryClients(new Uri("https://registry.example/"));
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("Splice.Api.Token.Registry.Client");

        client.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void AddSpliceTokenRegistryClients_lets_the_host_replace_the_timeout()
    {
        var services = new ServiceCollection();
        services.AddSpliceTokenRegistryClients(new Uri("https://registry.example/"))
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5));
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("Splice.Api.Token.Registry.Client");

        client.Timeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    private const int SixteenMebibytes = 16 * 1024 * 1024;

    public static TheoryData<bool> WithAndWithoutContentLength => [true, false];

    [Theory]
    [MemberData(nameof(WithAndWithoutContentLength))]
    public async Task AddSpliceTokenRegistryClients_refuses_a_response_body_above_sixteen_mebibytes(bool declaresContentLength)
    {
        var api = ResolveOver<ITokenMetadataV1Api>(() => JsonBody(RegistryInfoPaddedTo(SixteenMebibytes + 1), declaresContentLength));

        var failure = await api.Invoking(a => a.GetRegistryInfo(TestContext.Current.CancellationToken))
            .Should().ThrowAsync<Refit.ApiRequestException>();

        failure.WithInnerException<HttpRequestException>().WithMessage("*16777216*");
    }

    [Theory]
    [MemberData(nameof(WithAndWithoutContentLength))]
    public async Task AddSpliceTokenRegistryClients_reads_a_response_body_of_exactly_sixteen_mebibytes(bool declaresContentLength)
    {
        var api = ResolveOver<ITokenMetadataV1Api>(() => JsonBody(RegistryInfoPaddedTo(SixteenMebibytes), declaresContentLength));

        var info = await api.GetRegistryInfo(TestContext.Current.CancellationToken);

        info.AdminId.Should().Be("DSO::1220");
    }

    private static TApi ResolveOver<TApi>(Func<HttpContent> content) where TApi : notnull
    {
        var services = new ServiceCollection();
        services.AddSpliceTokenRegistryClients(new Uri("https://registry.example/"))
            .ConfigurePrimaryHttpMessageHandler(() => new CannedResponseHandler(content));
        return services.BuildServiceProvider().GetRequiredService<TApi>();
    }

    private static byte[] RegistryInfoPaddedTo(int length)
    {
        var json = Encoding.UTF8.GetBytes("""{"adminId":"DSO::1220","supportedApis":{}}""");
        var body = new byte[length];
        Array.Fill(body, (byte)' ');
        json.CopyTo(body, 0);
        return body;
    }

    private static HttpContent JsonBody(byte[] body, bool declaresContentLength)
    {
        HttpContent content = declaresContentLength
            ? new ByteArrayContent(body)
            : new StreamContent(new UnseekableStream(body));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    private sealed class UnseekableStream(byte[] body) : MemoryStream(body)
    {
        public override bool CanSeek => false;
    }
}
