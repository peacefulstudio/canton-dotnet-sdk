// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;

namespace Canton.Ledger.AspNetCore;

/// <summary>Registers the ledger failure mapping with the ASP.NET Core service collection.</summary>
public static class LedgerProblemServiceCollectionExtensions
{
    /// <summary>
    /// Answers a <see cref="Daml.Ledger.Abstractions.LedgerOperationException"/> escaping an endpoint
    /// with an RFC 9457 <c>application/problem+json</c> response, for minimal APIs and MVC
    /// controllers alike. The host must also call <c>UseExceptionHandler()</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure <see cref="LedgerProblemOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCantonLedgerProblemDetails(
        this IServiceCollection services,
        Action<LedgerProblemOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails();
        services.AddExceptionHandler<LedgerOperationExceptionHandler>();
        var options = services.AddOptions<LedgerProblemOptions>();
        if (configure is not null)
            options.Configure(configure);
        return services;
    }
}
