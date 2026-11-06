// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Splice.Api.Token.Registry.Client.AllocationInstructionV1;
using Splice.Api.Token.Registry.Client.AllocationInstructionV2;
using Splice.Api.Token.Registry.Client.AllocationV1;
using Splice.Api.Token.Registry.Client.AllocationV2;
using Splice.Api.Token.Registry.Client.TokenMetadataV1;
using Splice.Api.Token.Registry.Client.TransferInstructionV1;
using Splice.Api.Token.Registry.Client.TransferInstructionV2;
using Xunit;

namespace Splice.Api.Token.Registry.Client.Tests;

public class ServiceCollectionExtensionsTests
{
    public static TheoryData<Type> RegistryInterfaces =>
    [
        typeof(ITokenMetadataV1Api),
        typeof(ITransferInstructionV1Api),
        typeof(ITransferInstructionV2Api),
        typeof(IAllocationV1Api),
        typeof(IAllocationV2Api),
        typeof(IAllocationInstructionV1Api),
        typeof(IAllocationInstructionV2Api),
    ];

    [Theory]
    [MemberData(nameof(RegistryInterfaces))]
    public void AddSpliceTokenRegistryClients_registers_every_registry_interface(Type registryInterface)
    {
        var services = new ServiceCollection();
        services.AddSpliceTokenRegistryClients(new Uri("https://registry.example/"));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService(registryInterface).Should().NotBeNull();
    }

    [Fact]
    public void AddSpliceTokenRegistryClients_names_the_http_client_after_the_package()
    {
        ServiceCollectionExtensions.HttpClientName.Should().Be("Splice.Api.Token.Registry.Client");
    }

    [Fact]
    public void AddSpliceTokenRegistryClients_builds_the_http_client_over_the_registry_base_address()
    {
        var services = new ServiceCollection();
        services.AddSpliceTokenRegistryClients(new Uri("https://registry.example/"));
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("Splice.Api.Token.Registry.Client");

        client.BaseAddress.Should().Be(new Uri("https://registry.example/"));
    }
}
