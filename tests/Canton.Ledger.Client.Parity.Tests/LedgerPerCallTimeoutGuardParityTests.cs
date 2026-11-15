// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Rest.Client;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Proof that no public member taking a per-call <c>timeout</c> escapes the caller-error rule. The
/// members are read off each transport's client by reflection, so a member added later is covered
/// without a new row, and each one is called with a zero or negative timeout and an otherwise
/// empty argument list: the timeout check must come first, throw
/// <see cref="ArgumentOutOfRangeException"/> for <c>timeout</c> from the call itself, and leave no
/// task behind.
/// </summary>
public sealed class LedgerPerCallTimeoutGuardParityTests
{
    private const string Grpc = "gRPC";
    private const string Rest = "REST";
    private const string TimeoutParameter = "timeout";

    public static TheoryData<string, string, TimeSpan> Rows()
    {
        var rows = new TheoryData<string, string, TimeSpan>();
        foreach (var transport in new[] { Grpc, Rest })
        {
            foreach (var member in TimeoutTakingMembers(ClientTypeFor(transport)))
            {
                rows.Add(transport, member.ToString()!, TimeSpan.Zero);
                rows.Add(transport, member.ToString()!, TimeSpan.FromMilliseconds(-5));
            }
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void TimeoutTakingMember_throws_for_a_degenerate_timeout_at_the_call(
        string transport, string memberSignature, TimeSpan timeout)
    {
        using var scope = ClientScope.For(transport);
        var member = TimeoutTakingMembers(scope.Client.GetType()).Single(m => m.ToString() == memberSignature);
        var callOnClosedMember = CloseOverTestTypes(member);

        var act = () =>
        {
            try
            {
                _ = callOnClosedMember.Invoke(scope.Client, ArgumentsWithTimeout(callOnClosedMember, timeout));
            }
            catch (TargetInvocationException thrown)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(thrown.InnerException!).Throw();
            }
        };

        act.Should().ThrowExactly<ArgumentOutOfRangeException>(
                "{0} on {1} must reject the timeout before a task exists", memberSignature, transport)
            .Which.ParamName.Should().Be(TimeoutParameter);
    }

    [Theory]
    [InlineData(Grpc, 29)]
    [InlineData(Rest, 29)]
    public void TimeoutTakingMembers_are_the_ones_each_transport_publishes(string transport, int expectedCount)
    {
        TimeoutTakingMembers(ClientTypeFor(transport)).Should().HaveCount(expectedCount);
    }

    [Theory]
    [InlineData(Grpc)]
    [InlineData(Rest)]
    public void TimeoutTakingMembers_never_return_a_stream_whose_failure_would_wait_for_the_first_move(
        string transport)
    {
        TimeoutTakingMembers(ClientTypeFor(transport))
            .Where(m => m.ReturnType.IsGenericType
                && m.ReturnType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData(Grpc)]
    [InlineData(Rest)]
    public void ICantonLedgerClient_abstract_members_taking_a_timeout_are_all_public_on_the_transport(string transport)
    {
        var clientType = ClientTypeFor(transport);
        var published = TimeoutTakingMembers(clientType).ToHashSet();

        var interfaceTargets = typeof(ICantonLedgerClient).GetInterfaces().Append(typeof(ICantonLedgerClient))
            .SelectMany(contract => contract.GetMethods().Select(method => (contract, method)))
            .Where(pair => pair.method.IsAbstract && TakesTimeout(pair.method))
            .Select(pair => InterfaceTarget(clientType, pair.contract, pair.method))
            .ToList();

        interfaceTargets.Should().OnlyContain(target => published.Contains(target));
    }

    private static MethodInfo InterfaceTarget(Type clientType, Type contract, MethodInfo method)
    {
        var map = clientType.GetInterfaceMap(contract);
        var index = Array.IndexOf(map.InterfaceMethods, method);
        return map.TargetMethods[index];
    }

    private static IEnumerable<MethodInfo> TimeoutTakingMembers(Type clientType) =>
        clientType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(TakesTimeout);

    private static bool TakesTimeout(MethodInfo method) =>
        method.GetParameters().Any(parameter =>
            parameter.Name == TimeoutParameter && parameter.ParameterType == typeof(TimeSpan?));

    private static MethodInfo CloseOverTestTypes(MethodInfo member)
    {
        if (!member.IsGenericMethodDefinition)
        {
            return member;
        }

        var arity = member.GetGenericArguments().Length;
        IEnumerable<Type[]> candidates = arity switch
        {
            1 => [[typeof(Marker)], [typeof(string)]],
            2 => [[typeof(IHolding), typeof(HoldingView)]],
            _ => [],
        };

        foreach (var arguments in candidates)
        {
            try
            {
                return member.MakeGenericMethod(arguments);
            }
            catch (ArgumentException)
            {
            }
        }

        throw new InvalidOperationException($"No test type satisfies the generic constraints of {member}.");
    }

    private static object?[] ArgumentsWithTimeout(MethodInfo member, TimeSpan timeout) =>
        member.GetParameters()
            .Select(parameter => parameter.Name == TimeoutParameter
                ? timeout
                : parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null)
            .ToArray();

    private static Type ClientTypeFor(string transport)
    {
        using var scope = ClientScope.For(transport);
        return scope.Client.GetType();
    }

    private sealed class ClientScope(ICantonLedgerClient client, IDisposable owned) : IDisposable
    {
        public ICantonLedgerClient Client { get; } = client;

        public static ClientScope For(string transport) => transport switch
        {
            Grpc => ForGrpc(),
            Rest => ForRest(),
            _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, "Unknown transport."),
        };

        private static ClientScope ForGrpc()
        {
            var services = new ServiceCollection()
                .AddLedgerClient(options => options.GrpcAddress = "http://127.0.0.1:1")
                .BuildServiceProvider();
            return new ClientScope(services.GetRequiredService<ICantonLedgerClient>(), services);
        }

        private static ClientScope ForRest()
        {
            var services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
            var client = new RestLedgerClient(services.GetRequiredService<IHttpClientFactory>(), options: null);
            return new ClientScope(client, services);
        }

        public void Dispose() => owned.Dispose();
    }
}
