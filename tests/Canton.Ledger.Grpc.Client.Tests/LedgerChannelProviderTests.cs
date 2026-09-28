// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client.Raw;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class LedgerChannelProviderTests
{
    private const string GrpcAddress = "https://localhost:5001";

    private static readonly Dictionary<string, Func<IServiceProvider, CallInvoker>> CallInvokerOfEachClient = new()
    {
        ["LedgerClient"] = provider => ((LedgerClient)provider.GetRequiredService<ICantonLedgerClient>()).CreateCallInvoker(),
        ["AdminClient"] = provider => ((AdminClient)provider.GetRequiredService<IAdminClient>()).CreateCallInvoker(),
        ["GrpcCallInvokerFactory"] = provider => provider.GetRequiredService<IGrpcCallInvokerFactory>().CreateCallInvoker(),
    };

    public static TheoryData<string> ClientNames => new(CallInvokerOfEachClient.Keys);

    private static ServiceProvider BuildProviderWithEveryClient()
    {
        var services = new ServiceCollection();
        services.AddLedgerClient(options => options.GrpcAddress = GrpcAddress);
        services.AddAdminClient(options => options.GrpcAddress = GrpcAddress);
        services.AddLedgerRawGrpc(options => options.GrpcAddress = GrpcAddress);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Registering_every_client_registers_one_singleton_channel_provider()
    {
        var services = new ServiceCollection();

        services.AddLedgerClient(options => options.GrpcAddress = GrpcAddress);
        services.AddAdminClient(options => options.GrpcAddress = GrpcAddress);
        services.AddLedgerRawGrpc(options => options.GrpcAddress = GrpcAddress);

        services.Should().ContainSingle(d => d.ServiceType == typeof(LedgerChannelProvider))
            .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Theory]
    [MemberData(nameof(ClientNames))]
    public void Client_runs_on_the_channel_of_its_registration(string clientName)
    {
        using var provider = BuildProviderWithEveryClient();
        var createCallInvoker = () => CallInvokerOfEachClient[clientName](provider);
        createCallInvoker().Should().NotBeNull();

        provider.GetRequiredService<LedgerChannelProvider>().Dispose();

        createCallInvoker.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Disposing_the_container_releases_the_admin_client_channel()
    {
        var services = new ServiceCollection();
        services.AddAdminClient(options => options.GrpcAddress = GrpcAddress);
        var provider = services.BuildServiceProvider();
        var adminClient = (AdminClient)provider.GetRequiredService<IAdminClient>();

        provider.Dispose();

        var act = () => adminClient.CreateCallInvoker();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void IAdminClient_leaves_disposal_to_the_container()
    {
        typeof(IAdminClient).IsAssignableTo(typeof(IDisposable)).Should().BeFalse();
        typeof(AdminClient).IsAssignableTo(typeof(IDisposable)).Should().BeFalse();
    }

    [Theory]
    [InlineData("Canton.Ledger.Grpc.Client.LedgerClient")]
    [InlineData("Canton.Ledger.Grpc.Client.AdminClient")]
    [InlineData("Canton.Ledger.Grpc.Client.Raw.GrpcCallInvokerFactory")]
    [InlineData("Canton.Ledger.Grpc.Client.LedgerChannelProvider")]
    public void Channel_consumer_is_not_exported_so_only_the_container_constructs_it(string typeName)
    {
        var assembly = typeof(LedgerChannelProvider).Assembly;
        assembly.GetType(typeName, throwOnError: true);

        assembly.GetExportedTypes().Select(type => type.FullName).Should().NotContain(typeName);
    }

    [Fact]
    public void No_exported_member_takes_or_hands_out_a_GrpcChannel()
    {
        const BindingFlags DeclaredPublicMembers =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var channelMembers = typeof(LedgerChannelProvider).Assembly.GetExportedTypes()
            .SelectMany(type => type.GetMembers(DeclaredPublicMembers))
            .Where(member => SignatureTypes(member).Contains(typeof(GrpcChannel)))
            .Select(member => $"{member.DeclaringType!.FullName}.{member.Name}");

        channelMembers.Should().BeEmpty();
    }

    private static IEnumerable<Type> SignatureTypes(MemberInfo member) => member switch
    {
        MethodInfo method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType),
        ConstructorInfo constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType),
        PropertyInfo property => [property.PropertyType],
        FieldInfo field => [field.FieldType],
        _ => [],
    };
}
