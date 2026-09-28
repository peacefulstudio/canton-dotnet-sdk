// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Canton.Ledger.Kernel.DependencyInjection;

/// <summary>
/// Decides which transport serves <see cref="IAdminClient"/> when a container registers more than one,
/// so a mixed-transport container resolves the same admin client whatever order its transports were
/// added in.
/// </summary>
internal static class AdminClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IAdminClient"/> as a singleton only as a fallback: it is skipped when an
    /// admin client is already registered, and a later
    /// <see cref="TryAddAdminClientOverFallback"/> replaces it.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="factory">Creates the fallback admin client.</param>
    /// <returns>The service collection for chaining.</returns>
    internal static IServiceCollection TryAddFallbackAdminClient(
        this IServiceCollection services,
        Func<IServiceProvider, IAdminClient> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);

        services.TryAddSingleton(new FallbackAdminClientFactory(factory).Create);

        return services;
    }

    /// <summary>
    /// Registers <see cref="IAdminClient"/> as a singleton, replacing a fallback registered by
    /// <see cref="TryAddFallbackAdminClient"/> and keeping any other admin client already registered.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="factory">Creates the admin client.</param>
    /// <returns>The service collection for chaining.</returns>
    internal static IServiceCollection TryAddAdminClientOverFallback(
        this IServiceCollection services,
        Func<IServiceProvider, IAdminClient> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);

        var fallbacks = services.Where(IsFallbackAdminClient).ToList();
        foreach (var fallback in fallbacks)
            services.Remove(fallback);

        services.TryAddSingleton(factory);

        return services;
    }

    private static bool IsFallbackAdminClient(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(IAdminClient)
        && !descriptor.IsKeyedService
        && descriptor.ImplementationFactory?.Target is FallbackAdminClientFactory;

    private sealed class FallbackAdminClientFactory(Func<IServiceProvider, IAdminClient> factory)
    {
        public IAdminClient Create(IServiceProvider provider) => factory(provider);
    }
}
