// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Splice.Api.Token.Registry.Client.Tests;

public class CancelledTokenTests
{
    public static TheoryData<Type, string> RegistryMethods
    {
        get
        {
            var data = new TheoryData<Type, string>();
            foreach (var registryInterface in ServiceCollectionExtensionsTests.RegistryInterfaces.Select(row => row.Data))
                foreach (var method in registryInterface.GetMethods())
                    data.Add(registryInterface, method.Name);
            return data;
        }
    }

    [Fact]
    public void RegistryMethods_covers_every_method_of_the_seven_registry_interfaces()
    {
        RegistryMethods.Should().HaveCount(21);
    }

    [Theory]
    [MemberData(nameof(RegistryMethods))]
    public async Task RegistryMethod_hands_a_cancelled_token_to_the_transport_and_throws_operation_cancelled(
        Type registryInterface, string methodName)
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var services = new ServiceCollection();
        services.AddSpliceTokenRegistryClients(new Uri("https://registry.example/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        var api = provider.GetRequiredService(registryInterface);
        var method = registryInterface.GetMethod(methodName)!;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var call = (Task)method.Invoke(api, method.GetParameters().Select(p => ArgumentFor(p, cancelled.Token)).ToArray())!;

        await call.Invoking(c => c).Should().ThrowAsync<OperationCanceledException>();
        handler.TransportSawCancellation.Should().BeTrue();
    }

    private static object? ArgumentFor(ParameterInfo parameter, CancellationToken cancelled) => parameter.ParameterType switch
    {
        var type when type == typeof(CancellationToken) => cancelled,
        var type when type == typeof(string) => "id-1",
        _ when parameter.HasDefaultValue => parameter.DefaultValue,
        var type => RuntimeHelpers.GetUninitializedObject(type),
    };
}
