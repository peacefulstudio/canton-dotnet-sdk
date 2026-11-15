// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

/// <summary>
/// Proof that no public writer helper taking a per-call <c>timeout</c> reports a degenerate one
/// through a task. The helpers are read off the assembly by reflection, so one added later is
/// covered without a new row, and each is called against a writer that never completes: only a
/// guard in the helper itself can throw, from the call, with <c>timeout</c> as the parameter.
/// </summary>
public class WriterHelperTimeoutGuardTests
{
    private const string TimeoutParameter = "timeout";

    private static readonly Party Alice = new("alice");

    private static readonly ILedgerWriter NeverCompletingWriter = new NeverCompletingLedgerWriter();

    public static TheoryData<string, TimeSpan> Rows()
    {
        var rows = new TheoryData<string, TimeSpan>();
        foreach (var helper in TimeoutTakingHelpers())
        {
            rows.Add(Describe(helper), TimeSpan.Zero);
            rows.Add(Describe(helper), TimeSpan.FromMilliseconds(-5));
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void WriterHelper_throws_for_a_degenerate_timeout_at_the_call(string helperName, TimeSpan timeout)
    {
        var helper = CloseOverTestType(TimeoutTakingHelpers().Single(candidate => Describe(candidate) == helperName));

        Action act = () => InvokeWithoutAwaiting(helper, timeout);

        act.Should().ThrowExactly<ArgumentOutOfRangeException>(
                "{0} must reject the timeout before a task exists", helperName)
            .Which.ParamName.Should().Be(TimeoutParameter);
    }

    [Fact]
    public void TimeoutTakingHelpers_are_the_nine_the_assembly_publishes()
    {
        TimeoutTakingHelpers().Select(Describe).Should().BeEquivalentTo(
        [
            "CommandBatchExtensions.TrySubmitBatchAsync",
            "CreateByExercise.CreateManyByExerciseAsync",
            "CreateByExercise.CreateOneByExerciseAsync",
            "CreateByExercise.TryCreateManyByExerciseAsync",
            "CreateByExercise.TryCreateOneByExerciseAsync",
            "SingleCommandExtensions.TryCreateAsync",
            "SingleCommandExtensions.TrySubmitSingleAsync",
            "ThrowingExercise.ExerciseAsync(ExerciseCommand,SubmitterInfo,String,Nullable`1,Nullable`1,CancellationToken)",
            "ThrowingExercise.ExerciseAsync(ExerciseCommand,SubmitterInfo,String,Nullable`1,Nullable`1,Func`2,CancellationToken)",
        ]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void TrySubmitSingleAsync_with_valid_arguments_throws_for_a_degenerate_timeout_at_the_call(int milliseconds)
    {
        var command = CreateCommand.For(new Marker(Alice));

        Action act = () => _ = NeverCompletingWriter.TrySubmitSingleAsync(
            command, Alice, timeout: TimeSpan.FromMilliseconds(milliseconds), cancellationToken: TestContext.Current.CancellationToken);

        act.Should().ThrowExactly<ArgumentOutOfRangeException>().Which.ParamName.Should().Be(TimeoutParameter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void TrySubmitBatchAsync_with_valid_arguments_throws_for_a_degenerate_timeout_at_the_call(int milliseconds)
    {
        var batch = new CommandBatch();
        batch.AddCreate(new Marker(Alice));

        Action act = () => _ = NeverCompletingWriter.TrySubmitBatchAsync(
            batch, Alice, timeout: TimeSpan.FromMilliseconds(milliseconds), cancellationToken: TestContext.Current.CancellationToken);

        act.Should().ThrowExactly<ArgumentOutOfRangeException>().Which.ParamName.Should().Be(TimeoutParameter);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(0, true)]
    [InlineData(-5, true)]
    public void TryCreateAsync_with_valid_arguments_throws_for_a_degenerate_timeout_at_the_call(
        int milliseconds, bool withConfigureHook)
    {
        Func<CommandsSubmission, CommandsSubmission>? configure = withConfigureHook ? submission => submission : null;

        Action act = () => _ = SingleCommandExtensions.TryCreateAsync(
            NeverCompletingWriter, new Marker(Alice), Alice, timeout: TimeSpan.FromMilliseconds(milliseconds),
            configure: configure, cancellationToken: TestContext.Current.CancellationToken);

        act.Should().ThrowExactly<ArgumentOutOfRangeException>().Which.ParamName.Should().Be(TimeoutParameter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void GeneratedTryCreateAsync_throws_for_a_degenerate_timeout_at_the_call(int milliseconds)
    {
        var payload = new Marker(Alice);

        Action act = () => _ = NeverCompletingWriter.TryCreateAsync(
            payload, timeout: TimeSpan.FromMilliseconds(milliseconds), cancellationToken: TestContext.Current.CancellationToken);

        act.Should().ThrowExactly<ArgumentOutOfRangeException>().Which.ParamName.Should().Be(TimeoutParameter);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(30000)]
    public void GeneratedTryCreateAsync_returns_a_task_for_the_infinite_or_a_positive_timeout(int milliseconds)
    {
        var payload = new Marker(Alice);

        var pending = NeverCompletingWriter.TryCreateAsync(
            payload, timeout: TimeSpan.FromMilliseconds(milliseconds), cancellationToken: TestContext.Current.CancellationToken);

        pending.IsCompleted.Should().BeFalse();
    }

    private static IEnumerable<MethodInfo> TimeoutTakingHelpers() =>
        typeof(ILedgerWriter).Assembly.GetExportedTypes()
            .Where(type => type.IsAbstract && type.IsSealed && type != typeof(StreamerSnapshot))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => method.GetParameters().Any(parameter =>
                parameter.Name == TimeoutParameter && parameter.ParameterType == typeof(TimeSpan?)));

    private static string Describe(MethodInfo method)
    {
        var overloads = method.DeclaringType!
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Count(other => other.Name == method.Name
                && other.GetParameters().Any(parameter => parameter.Name == TimeoutParameter));
        var name = $"{method.DeclaringType!.Name}.{method.Name}";
        return overloads == 1
            ? name
            : $"{name}({string.Join(',', method.GetParameters().Skip(1).Select(parameter => parameter.ParameterType.Name))})";
    }

    private static MethodInfo CloseOverTestType(MethodInfo helper) =>
        helper.IsGenericMethodDefinition ? helper.MakeGenericMethod(typeof(Marker)) : helper;

    private static void InvokeWithoutAwaiting(MethodInfo helper, TimeSpan timeout)
    {
        var arguments = helper.GetParameters()
            .Select(parameter =>
                parameter.Name == TimeoutParameter ? timeout
                : parameter.ParameterType == typeof(ILedgerWriter) ? NeverCompletingWriter
                : parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType)
                : null)
            .ToArray();
        try
        {
            _ = helper.Invoke(null, arguments);
        }
        catch (TargetInvocationException thrown)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(thrown.InnerException!).Throw();
        }
    }

    private sealed class NeverCompletingLedgerWriter : ILedgerWriter
    {
        public Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
            ExerciseCommand command, SubmitterInfo submitter, string? workflowId = null, CommandId? commandId = null,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            new TaskCompletionSource<ExerciseOutcome<TResult>>().Task;

        public Task<SubmitAndWaitResult> SubmitAndWaitAsync(
            CommandsSubmission submission, SubmitterInfo submitter, TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            new TaskCompletionSource<SubmitAndWaitResult>().Task;

        public Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
            CommandsSubmission submission, SubmitterInfo submitter, TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            new TaskCompletionSource<ExerciseOutcome<TransactionResult>>().Task;

        public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
            TTemplate payload, SubmitterInfo submitter, string? workflowId = null, CommandId? commandId = null,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default)
            where TTemplate : ITemplate =>
            new TaskCompletionSource<ExerciseOutcome<ContractId<TTemplate>>>().Task;
    }
}
