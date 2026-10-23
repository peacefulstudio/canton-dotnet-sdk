// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Authentication.TokenGeneration;
using Canton.Ledger.Kernel.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public class AuthTlsOptionsValidatorTests
{
    private static readonly TlsOptions ConfiguredTls = new() { ClientCertificatePemPath = "client.pem" };

    [Fact]
    public void Validate_fails_with_the_options_name_when_options_tls_is_configured_and_auth_tls_is_not()
    {
        var validator = ValidatorFor(registerClientCredentials: true, authTls: new TlsOptions());

        var result = validator.Validate(Options.DefaultName, new SampleOptions { Tls = ConfiguredTls });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Be(
            "SampleOptions.Tls is configured while client-credentials authentication "
            + "uses an unconfigured ClientCredentialsOptions.Tls. Configure TLS separately "
            + "for the token endpoint.");
    }

    [Fact]
    public void Validate_succeeds_when_both_tls_are_configured()
    {
        var validator = ValidatorFor(registerClientCredentials: true, authTls: ConfiguredTls);

        var result = validator.Validate(Options.DefaultName, new SampleOptions { Tls = ConfiguredTls });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_succeeds_when_options_tls_is_unconfigured()
    {
        var validator = ValidatorFor(registerClientCredentials: true, authTls: new TlsOptions());

        var result = validator.Validate(Options.DefaultName, new SampleOptions());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_succeeds_when_client_credentials_authentication_is_not_registered()
    {
        var validator = ValidatorFor(registerClientCredentials: false, authTls: new TlsOptions());

        var result = validator.Validate(Options.DefaultName, new SampleOptions { Tls = ConfiguredTls });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_skips_named_options()
    {
        var validator = ValidatorFor(registerClientCredentials: true, authTls: new TlsOptions());

        var result = validator.Validate("other", new SampleOptions { Tls = ConfiguredTls });

        result.Skipped.Should().BeTrue();
    }

    [Fact]
    public void AddAuthTlsValidation_registers_one_validator_however_often_it_is_called()
    {
        var services = new ServiceCollection();

        services.AddAuthTlsValidation<SampleOptions>(options => options.Tls, "SampleOptions");
        services.AddAuthTlsValidation<SampleOptions>(options => options.Tls, "SampleOptions");

        services.Count(descriptor => descriptor.ServiceType == typeof(IValidateOptions<SampleOptions>))
            .Should().Be(1);
    }

    private static IValidateOptions<SampleOptions> ValidatorFor(bool registerClientCredentials, TlsOptions authTls)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<ClientCredentialsOptions>>(
            Options.Create(new ClientCredentialsOptions { ClientId = "client", ClientSecret = "secret", Tls = authTls }));
        if (registerClientCredentials)
            services.AddSingleton(new ClientCredentialsRegistration());
        services.AddAuthTlsValidation<SampleOptions>(options => options.Tls, "SampleOptions");

        return services.BuildServiceProvider().GetRequiredService<IValidateOptions<SampleOptions>>();
    }

    private sealed class SampleOptions
    {
        public TlsOptions Tls { get; init; } = new();
    }
}
