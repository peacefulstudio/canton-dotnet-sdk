// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        return services.AddHttpClient(HttpClientName, client => client.BaseAddress = registryBaseAddress);
    }

    private static void AddApi<TApi>(IServiceCollection services) where TApi : class =>
        services.TryAddTransient(static sp => RestService.For<TApi>(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            SpliceRegistryRefitSettings.Create()));
}
