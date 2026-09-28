// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Canton.Ledger.Abstractions;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

/// <summary>
/// Mirrors over <c>AdminClient</c> the surface gate <see cref="ICantonLedgerClientTests"/> keeps over
/// <c>LedgerClient</c>: a capability Canton serves belongs on <see cref="IAdminClient"/>, and the gate is
/// what stops one from being added to the concrete class instead. <c>AdminClient</c> is internal, so a
/// public member that <see cref="IAdminClient"/> does not declare is not merely awkward to reach — it is
/// reachable by nobody at all, carried and maintained for no caller.
/// </summary>
public class AdminClientSurfaceTests
{
    /// <summary>
    /// Signatures exempt from the gate because they are gRPC transport plumbing rather than an
    /// admin capability: raw stub construction for callers who need to bypass the typed surface.
    /// </summary>
    private static readonly string[] NotAdminCapabilitiesButGrpcTransportPlumbing =
    [
        "CreateCallInvoker()",
    ];

    [Fact]
    public void Every_public_AdminClient_member_is_declared_on_IAdminClient_or_named_in_a_concrete_only_allowlist()
    {
        var declaredOnTheInterface = AdminClientMembersImplementingIAdminClient();
        var exempt = ConcreteOnlyAllowlist();

        var unreachable = string.Join(", ", PublicAdminClientMembers()
            .Where(member => !declaredOnTheInterface.Contains(member))
            .Select(Describe)
            .Where(signature => !exempt.Contains(signature))
            .Order());

        unreachable.Should().BeEmpty(
            "a public AdminClient member IAdminClient does not declare is reachable only by downcasting past "
            + "the DI-registered abstraction, so either declare it on the interface and implement it for real, "
            + "or add its signature to the concrete-only allowlist at the top of this file so the exemption is a "
            + "decision a reviewer can see");
    }

    [Fact]
    public void Every_concrete_only_allowlist_entry_still_matches_a_public_AdminClient_member()
    {
        var declared = PublicAdminClientMembers().Select(Describe).ToHashSet(StringComparer.Ordinal);

        var stale = string.Join(", ", ConcreteOnlyAllowlist().Except(declared).Order());

        stale.Should().BeEmpty(
            "an allowlist entry that matches no AdminClient member exempts nothing and hides the fact that the "
            + "member it was written for has been renamed, promoted, or removed");
    }

    private static HashSet<string> ConcreteOnlyAllowlist() =>
    [
        .. NotAdminCapabilitiesButGrpcTransportPlumbing,
    ];

    private static MethodInfo[] PublicAdminClientMembers() =>
        typeof(AdminClient).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    private static HashSet<MethodInfo> AdminClientMembersImplementingIAdminClient() =>
    [
        .. new[] { typeof(IAdminClient) }
            .Concat(typeof(IAdminClient).GetInterfaces())
            .SelectMany(contract => typeof(AdminClient).GetInterfaceMap(contract).TargetMethods),
    ];

    private static string Describe(MethodInfo member)
    {
        var typeArguments = member.IsGenericMethodDefinition
            ? $"<{string.Join(", ", member.GetGenericArguments().Select(argument => argument.Name))}>"
            : string.Empty;
        var parameters = string.Join(", ", member.GetParameters().Select(p => Describe(p.ParameterType)));
        return $"{member.Name}{typeArguments}({parameters})";
    }

    private static string Describe(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
            return $"{Describe(underlying)}?";

        if (!type.IsGenericType)
            return type.Name;

        var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Describe))}>";
    }
}
