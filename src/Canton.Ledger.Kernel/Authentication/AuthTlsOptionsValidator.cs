// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Authentication.TokenGeneration;
using Canton.Ledger.Kernel.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Canton.Ledger.Kernel.Authentication;

/// <summary>
/// Fails the default-named client options when they configure TLS while client-credentials
/// authentication is registered with an unconfigured <see cref="ClientCredentialsOptions.Tls"/>,
/// because the token endpoint would then silently ignore the client's TLS material.
/// </summary>
internal sealed class AuthTlsOptionsValidator<TOptions>(
    IServiceProvider serviceProvider,
    IOptions<ClientCredentialsOptions> authOptions,
    Func<TOptions, TlsOptions> tlsOf,
    string optionsName) : IValidateOptions<TOptions>
    where TOptions : class
{
    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        if (name != Options.DefaultName)
            return ValidateOptionsResult.Skip;

        if (serviceProvider.GetService<ClientCredentialsRegistration>() is null)
            return ValidateOptionsResult.Success;

        return tlsOf(options).IsConfigured
            && !authOptions.Value.Tls.IsConfigured
                ? ValidateOptionsResult.Fail(
                    $"{optionsName}.Tls is configured while client-credentials authentication "
                    + "uses an unconfigured ClientCredentialsOptions.Tls. Configure TLS separately "
                    + "for the token endpoint.")
                : ValidateOptionsResult.Success;
    }
}

internal static class AuthTlsOptionsValidationExtensions
{
    internal static void AddAuthTlsValidation<TOptions>(
        this IServiceCollection services,
        Func<TOptions, TlsOptions> tlsOf,
        string optionsName)
        where TOptions : class =>
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TOptions>, AuthTlsOptionsValidator<TOptions>>(
                serviceProvider => new AuthTlsOptionsValidator<TOptions>(
                    serviceProvider,
                    serviceProvider.GetRequiredService<IOptions<ClientCredentialsOptions>>(),
                    tlsOf,
                    optionsName)));
}
