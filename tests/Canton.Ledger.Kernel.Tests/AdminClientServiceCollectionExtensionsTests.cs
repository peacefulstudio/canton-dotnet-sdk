// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public class AdminClientServiceCollectionExtensionsTests
{
    private readonly IAdminClient _fallback = Substitute.For<IAdminClient>();
    private readonly IAdminClient _dedicated = Substitute.For<IAdminClient>();
    private readonly IAdminClient _consumerOwn = Substitute.For<IAdminClient>();

    [Fact]
    public void TryAddFallbackAdminClient_registers_a_singleton_when_none_is_registered()
    {
        var services = new ServiceCollection();

        services.TryAddFallbackAdminClient(_ => _fallback);

        services.Should().ContainSingle(d => d.ServiceType == typeof(IAdminClient))
            .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IAdminClient>().Should().BeSameAs(_fallback);
    }

    [Fact]
    public void TryAddFallbackAdminClient_keeps_an_admin_client_registered_before_it()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_consumerOwn);

        services.TryAddFallbackAdminClient(_ => _fallback);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IAdminClient>().Should().BeSameAs(_consumerOwn);
    }

    [Fact]
    public void TryAddAdminClientOverFallback_replaces_a_fallback_registered_before_it()
    {
        var services = new ServiceCollection();
        services.TryAddFallbackAdminClient(_ => _fallback);

        services.TryAddAdminClientOverFallback(_ => _dedicated);

        services.Should().ContainSingle(d => d.ServiceType == typeof(IAdminClient))
            .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IAdminClient>().Should().BeSameAs(_dedicated);
    }

    [Fact]
    public void TryAddAdminClientOverFallback_is_kept_by_a_fallback_registered_after_it()
    {
        var services = new ServiceCollection();
        services.TryAddAdminClientOverFallback(_ => _dedicated);

        services.TryAddFallbackAdminClient(_ => _fallback);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IAdminClient>().Should().BeSameAs(_dedicated);
    }

    [Fact]
    public void TryAddAdminClientOverFallback_keeps_a_consumer_admin_client_registered_before_it()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_consumerOwn);

        services.TryAddAdminClientOverFallback(_ => _dedicated);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IAdminClient>().Should().BeSameAs(_consumerOwn);
    }

    [Fact]
    public void TryAddAdminClientOverFallback_keeps_an_earlier_one_of_its_own_kind()
    {
        var services = new ServiceCollection();
        services.TryAddAdminClientOverFallback(_ => _dedicated);

        services.TryAddAdminClientOverFallback(_ => _consumerOwn);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IAdminClient>().Should().BeSameAs(_dedicated);
    }

    [Fact]
    public void TryAddAdminClientOverFallback_leaves_a_keyed_admin_client_in_place()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton("secondary", _consumerOwn);
        services.TryAddFallbackAdminClient(_ => _fallback);

        services.TryAddAdminClientOverFallback(_ => _dedicated);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredKeyedService<IAdminClient>("secondary").Should().BeSameAs(_consumerOwn);
        provider.GetRequiredService<IAdminClient>().Should().BeSameAs(_dedicated);
    }
}
