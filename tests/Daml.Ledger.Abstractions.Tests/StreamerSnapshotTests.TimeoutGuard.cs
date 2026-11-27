// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using AwesomeAssertions;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

public sealed partial class StreamerSnapshotTests
{
    private const string TimeoutParameter = "timeout";

    public static TheoryData<string, long> RejectedTimeoutRows()
    {
        var rows = new TheoryData<string, long>();
        foreach (var helper in TimeoutTakingSnapshotHelpers())
        {
            rows.Add(helper.ToString()!, -50_000L);
            rows.Add(helper.ToString()!, -300_000_000L);
            rows.Add(helper.ToString()!, 51_840_000_000_000L);
        }

        return rows;
    }

    public static TheoryData<string, long> AcceptedTimeoutRows()
    {
        var rows = new TheoryData<string, long>();
        foreach (var helper in TimeoutTakingSnapshotHelpers())
        {
            rows.Add(helper.ToString()!, 0L);
            rows.Add(helper.ToString()!, -10_000L);
            rows.Add(helper.ToString()!, 300_000_000L);
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(RejectedTimeoutRows))]
    public void SnapshotHelper_throws_for_a_timeout_the_drain_cannot_honour_at_the_call(string helperSignature, long ticks)
    {
        var helper = FindSnapshotHelper(helperSignature);

        Action act = () => _ = InvokeSnapshotHelperWithoutAwaiting(helper, TimeSpan.FromTicks(ticks));

        act.Should().ThrowExactly<ArgumentOutOfRangeException>(
                "{0} must reject the timeout before a task exists", helperSignature)
            .Which.ParamName.Should().Be(TimeoutParameter);
    }

    [Theory]
    [MemberData(nameof(AcceptedTimeoutRows))]
    public void SnapshotHelper_returns_a_task_for_zero_the_infinite_or_a_positive_timeout(string helperSignature, long ticks)
    {
        var helper = FindSnapshotHelper(helperSignature);

        var pending = InvokeSnapshotHelperWithoutAwaiting(helper, TimeSpan.FromTicks(ticks));

        pending.Should().BeAssignableTo<Task>();
    }

    [Fact]
    public void TimeoutTakingHelpers_are_the_four_snapshot_overloads()
    {
        TimeoutTakingSnapshotHelpers().Select(helper => helper.Name).Should().BeEquivalentTo(
        [
            "SnapshotAsync",
            "SnapshotAsync",
            "SnapshotActiveAsync",
            "SnapshotActiveAsync",
        ]);
    }

    private static IEnumerable<MethodInfo> TimeoutTakingSnapshotHelpers() =>
        typeof(StreamerSnapshot)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.GetParameters().Any(parameter =>
                parameter.Name == TimeoutParameter && parameter.ParameterType == typeof(TimeSpan?)));

    private static MethodInfo FindSnapshotHelper(string helperSignature)
    {
        var helper = TimeoutTakingSnapshotHelpers().Single(candidate => candidate.ToString() == helperSignature);
        return helper.GetGenericArguments().Length == 1
            ? helper.MakeGenericMethod(typeof(Probe))
            : helper.MakeGenericMethod(typeof(Probe), typeof(Party));
    }

    private static object? InvokeSnapshotHelperWithoutAwaiting(MethodInfo helper, TimeSpan timeout)
    {
        var arguments = helper.GetParameters()
            .Select(parameter =>
                parameter.Name == TimeoutParameter ? timeout
                : parameter.ParameterType == typeof(ILedgerStreamer) ? new FakeStreamer()
                : parameter.ParameterType == typeof(SubmitterInfo) ? Alice
                : parameter.ParameterType.Name.StartsWith("KeyDescriptor", StringComparison.Ordinal) ? Probe.Key
                : parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType)
                : null)
            .ToArray();
        try
        {
            return helper.Invoke(null, arguments);
        }
        catch (TargetInvocationException thrown)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(thrown.InnerException!).Throw();
            throw;
        }
    }
}
