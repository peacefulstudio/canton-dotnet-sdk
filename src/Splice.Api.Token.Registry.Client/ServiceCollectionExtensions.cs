// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Refit;
using Splice.Api.Token.Registry.Client.AllocationInstructionV1;
using Splice.Api.Token.Registry.Client.AllocationInstructionV2;
using Splice.Api.Token.Registry.Client.AllocationV1;
using Splice.Api.Token.Registry.Client.AllocationV2;
using Splice.Api.Token.Registry.Client.TokenMetadataV1;
using Splice.Api.Token.Registry.Client.TransferInstructionV1;
using Splice.Api.Token.Registry.Client.TransferInstructionV2;

namespace Splice.Api.Token.Registry.Client;

/// <summary>
/// Registers the Token Standard registry interfaces with the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The name of the <see cref="HttpClient"/> every registry interface is built over. Hosts can
    /// customize it through <c>services.AddHttpClient(HttpClientName)</c>.
    /// </summary>
    public const string HttpClientName = "Splice.Api.Token.Registry.Client";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Registers the seven registry interfaces, each <see cref="ServiceLifetime.Transient"/>, over
    /// one named <see cref="HttpClient"/> whose base address is the registry's: the metadata,
    /// transfer instruction, allocation and allocation instruction APIs, V1 and V2 where the
    /// Splice release publishes both.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="registryBaseAddress">The base URL of the registry, for example
    /// <c>https://registry.example/</c>; the generated routes start with <c>/registry/</c>.</param>
    /// <returns>The builder of the named <see cref="HttpClient"/>, for adding handlers.</returns>
    /// <remarks>
    /// <para>
    /// The named client treats the registry as untrusted. A redirect is never followed: it surfaces
    /// as a <see cref="Refit.ApiException"/> carrying the <c>3xx</c> status. Every call is bounded to
    /// 30 seconds through <see cref="HttpClient.Timeout"/>. A response body above 16 MiB
    /// (16,777,216 bytes) fails with a <see cref="Refit.ApiRequestException"/> whose inner
    /// <see cref="HttpRequestException"/> names the limit, before it reaches the deserializer.
    /// </para>
    /// <para>
    /// Hosts customize the client through the returned builder, and their settings win:
    /// <c>ConfigureHttpClient</c> replaces the timeout, and
    /// <c>UseSocketsHttpHandler((handler, _) =&gt; handler.SslOptions = ...)</c> sets TLS options
    /// on the primary handler while keeping redirects refused. Supplying a primary handler of
    /// one's own with <c>ConfigurePrimaryHttpMessageHandler(Func&lt;HttpMessageHandler&gt;)</c>
    /// replaces the redirect setting too; the body limit stays, because it is not the primary handler.
    /// </para>
    /// </remarks>
    public static IHttpClientBuilder AddSpliceTokenRegistryClients(this IServiceCollection services, Uri registryBaseAddress)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registryBaseAddress);

        AddApi<ITokenMetadataV1Api>(services);
        AddApi<ITransferInstructionV1Api>(services);
        AddApi<ITransferInstructionV2Api>(services);
        AddApi<IAllocationV1Api>(services);
        AddApi<IAllocationV2Api>(services);
        AddApi<IAllocationInstructionV1Api>(services);
        AddApi<IAllocationInstructionV2Api>(services);

        AddRedirectRefusingPrimaryHandler(services);
        services.TryAddTransient<ResponseBodyLimitHandler>();

        return services.AddHttpClient(HttpClientName, client =>
            {
                client.BaseAddress = registryBaseAddress;
                client.Timeout = RequestTimeout;
            })
            .AddHttpMessageHandler<ResponseBodyLimitHandler>();
    }

    private static void AddRedirectRefusingPrimaryHandler(IServiceCollection services) =>
        services.AddOptions<HttpClientFactoryOptions>(HttpClientName)
            .PostConfigure(static factoryOptions =>
                factoryOptions.HttpMessageHandlerBuilderActions.Insert(0, builder =>
                {
                    var handler = builder.PrimaryHandler as SocketsHttpHandler
                        ?? new SocketsHttpHandler { PooledConnectionLifetime = factoryOptions.HandlerLifetime };
                    handler.AllowAutoRedirect = false;
                    builder.PrimaryHandler = handler;
                }));

    private static void AddApi<TApi>(IServiceCollection services) where TApi : class =>
        services.TryAddTransient(static sp => RestService.ForGenerated<TApi>(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            SpliceRegistryRefitSettings.Create()));
}
