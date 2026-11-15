// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using AwesomeAssertions;
using Daml.Runtime;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

/// <summary>
/// Proof that the fake applies the caller-error rule of the real clients to every public member
/// taking a per-call <c>timeout</c>, so a consumer test cannot pass with a timeout production
/// rejects. The members are read off <see cref="FakeLedgerClient"/> by reflection, so one added
/// later is covered without a new row.
/// </summary>
public class FakeLedgerClientPerCallTimeoutGuardTests
{
    private const string TimeoutParameter = "timeout";

    public static TheoryData<string, TimeSpan> Rows()
    {
        var rows = new TheoryData<string, TimeSpan>();
        foreach (var member in TimeoutTakingMembers())
        {
            rows.Add(member.ToString()!, TimeSpan.Zero);
            rows.Add(member.ToString()!, TimeSpan.FromMilliseconds(-5));
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void TimeoutTakingMember_throws_for_a_degenerate_timeout_at_the_call(string memberSignature, TimeSpan timeout)
    {
        var member = CloseOverTestTypes(TimeoutTakingMembers().Single(candidate => candidate.ToString() == memberSignature));
        var client = FakeLedgerClient.Create().Build();

        Action act = () => InvokeWithoutAwaiting(member, client, timeout);

        act.Should().ThrowExactly<ArgumentOutOfRangeException>(
                "{0} must reject the timeout before a task exists", memberSignature)
            .Which.ParamName.Should().Be(TimeoutParameter);
    }

    [Fact]
    public void TimeoutTakingMembers_are_the_twenty_nine_the_fake_publishes()
    {
        TimeoutTakingMembers().Should().HaveCount(29);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(30000)]
    public async Task GetLedgerEndAsync_accepts_the_infinite_or_a_positive_timeout(int milliseconds)
    {
        var client = FakeLedgerClient.Create().WithLedgerEnd(LedgerOffset.At(7)).Build();

        var end = await client.GetLedgerEndAsync(
            TimeSpan.FromMilliseconds(milliseconds), TestContext.Current.CancellationToken);

        end.Should().Be(LedgerOffset.At(7));
    }

    private static IEnumerable<MethodInfo> TimeoutTakingMembers() =>
        typeof(FakeLedgerClient)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetParameters().Any(parameter =>
                parameter.Name == TimeoutParameter && parameter.ParameterType == typeof(TimeSpan?)));

    private static MethodInfo CloseOverTestTypes(MethodInfo member)
    {
        if (!member.IsGenericMethodDefinition)
        {
            return member;
        }

        var arity = member.GetGenericArguments().Length;
        IEnumerable<Type[]> candidates = arity switch
        {
            1 => [[typeof(DemoAsset)], [typeof(string)]],
            2 => [[typeof(IDemoHoldingView), typeof(DemoHoldingView)]],
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

    private static void InvokeWithoutAwaiting(MethodInfo member, FakeLedgerClient client, TimeSpan timeout)
    {
        var arguments = member.GetParameters()
            .Select(parameter => parameter.Name == TimeoutParameter
                ? timeout
                : parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null)
            .ToArray();
        try
        {
            _ = member.Invoke(client, arguments);
        }
        catch (TargetInvocationException thrown)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(thrown.InnerException!).Throw();
        }
    }
}
