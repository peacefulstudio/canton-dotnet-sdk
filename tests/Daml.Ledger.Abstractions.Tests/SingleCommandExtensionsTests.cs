// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime;
using Daml.Runtime.Streams;
using AwesomeAssertions;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

/// <summary>
/// Verifies <see cref="SingleCommandExtensions"/>: the shared single-command submission
/// path that generated exercisers and the hand-written write-path extensions both use.
/// </summary>
public class SingleCommandExtensionsTests
{
    private static readonly ExerciseCommand SampleCommand = new(
        new Identifier("pkg", "Module", "Template"),
        ContractId: new ContractId<SampleTemplate>("cid-1"),
        Choice: new ChoiceName("DoIt"),
        ChoiceArgument: new DamlRecord(null, []));

    private static readonly SubmitterInfo Alice = new Party("alice");

    private static readonly TransactionResult TransactionCreatingOneSampleTemplate = new(
        UpdateId: "update-id",
        CompletionOffset: LedgerOffset.Begin,
        CreatedContracts: [CreatedOf("cid-created")],
        ArchivedContractIds: [],
        CommandId: new CommandId("cmd-id"));

    private static CreatedContract CreatedOf(string contractId) =>
        new(
            EventId: $"evt-{contractId}",
            ContractId: contractId,
            TemplateId: SampleTemplate.TemplateId,
            Payload: DamlRecord.Create(),
            WitnessParties: [new Party("alice")],
            Signatories: [new Party("alice")],
            Observers: []);

    [Fact]
    public async Task TrySubmitSingleAsync_uses_the_supplied_command_id()
    {
        var writer = new CapturingWriter();

        await writer.TrySubmitSingleAsync(
            SampleCommand, Alice, commandId: new CommandId("caller-supplied"),
            cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.CommandId.Should().Be(new CommandId("caller-supplied"));
    }

    [Fact]
    public async Task TrySubmitSingleAsync_mints_a_command_id_when_none_is_supplied()
    {
        var writer = new CapturingWriter();

        await writer.TrySubmitSingleAsync(
            SampleCommand, Alice, cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.CommandId.Should().NotBeNull(
            "the submission must carry a client-assigned command id rather than leaving command_id unset");
    }

    [Fact]
    public async Task TrySubmitSingleAsync_mints_a_distinct_command_id_per_call()
    {
        var writer = new CapturingWriter();

        await writer.TrySubmitSingleAsync(SampleCommand, Alice, cancellationToken: TestContext.Current.CancellationToken);
        var first = writer.LastSubmission!.CommandId;
        await writer.TrySubmitSingleAsync(SampleCommand, Alice, cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.CommandId.Should().NotBe(first);
    }

    [Fact]
    public async Task TrySubmitSingleAsync_treats_an_empty_workflow_id_as_absent()
    {
        var writer = new CapturingWriter();

        await writer.TrySubmitSingleAsync(
            SampleCommand, Alice, workflowId: string.Empty,
            cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.WorkflowId.Should().BeNull(
            "workflow_id is a correlation key and an empty one correlates nothing");
    }

    [Fact]
    public async Task TrySubmitSingleAsync_treats_a_null_workflow_id_as_absent()
    {
        var writer = new CapturingWriter();

        await writer.TrySubmitSingleAsync(
            SampleCommand, Alice, workflowId: null,
            cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.WorkflowId.Should().BeNull();
    }

    [Fact]
    public async Task TrySubmitSingleAsync_carries_a_non_empty_workflow_id()
    {
        var writer = new CapturingWriter();

        await writer.TrySubmitSingleAsync(
            SampleCommand, Alice, workflowId: "wf-1",
            cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.WorkflowId.Should().Be(new WorkflowId("wf-1"));
    }

    [Fact]
    public async Task TrySubmitSingleAsync_forwards_the_timeout()
    {
        var writer = new CapturingWriter();

        await writer.TrySubmitSingleAsync(
            SampleCommand, Alice, timeout: TimeSpan.FromSeconds(3),
            cancellationToken: TestContext.Current.CancellationToken);

        writer.LastTimeout.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task TrySubmitSingleAsync_carries_the_single_command()
    {
        var writer = new CapturingWriter();

        await writer.TrySubmitSingleAsync(
            SampleCommand, Alice, cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.Commands.Should().ContainSingle().Which.Should().Be(SampleCommand);
    }

    [Fact]
    public async Task TrySubmitSingleAsync_throws_when_the_writer_is_null()
    {
        ILedgerWriter writer = null!;

        Func<Task> act = () => writer.TrySubmitSingleAsync(
            SampleCommand, Alice, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task TrySubmitSingleAsync_throws_when_the_command_is_null()
    {
        var writer = new CapturingWriter();

        Func<Task> act = () => writer.TrySubmitSingleAsync(
            null!, Alice, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task TrySubmitSingleAsync_forwards_the_cancellation_token()
    {
        var writer = new CapturingWriter();
        using var cts = new CancellationTokenSource();

        await writer.TrySubmitSingleAsync(SampleCommand, Alice, cancellationToken: cts.Token);

        writer.LastCancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task TrySubmitSingleAsync_rejects_a_default_command_id_rather_than_submitting_it()
    {
        var writer = new CapturingWriter();

        Func<Task> act = () => writer.TrySubmitSingleAsync(
            SampleCommand, Alice, commandId: default(CommandId), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
        writer.LastSubmission.Should().BeNull();
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_mints_a_command_id_so_a_retry_can_deduplicate()
    {
        var writer = new CapturingWriter();

        await writer.TryCreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, Alice, cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.CommandId.Should().NotBeNull();
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_uses_the_supplied_command_id()
    {
        var writer = new CapturingWriter();

        await writer.TryCreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, Alice, commandId: new CommandId("caller-supplied"),
            cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.CommandId.Should().Be(new CommandId("caller-supplied"));
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_re_wraps_a_writer_level_Many_over_the_created_contract_id()
    {
        var writer = new CapturingWriter(new ExerciseOutcome<TransactionResult>.Many(["cid-1", "cid-2"]));

        var outcome = await writer.TryCreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, Alice, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<SampleTemplate>>.Many>()
            .Which.ContractIds.Should().Equal("cid-1", "cid-2");
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_yields_Many_when_the_transaction_created_more_than_one()
    {
        var writer = new CapturingWriter(new ExerciseOutcome<TransactionResult>.One(
            TransactionCreatingOneSampleTemplate with
            {
                CreatedContracts = [CreatedOf("cid-created"), CreatedOf("cid-created-2")],
            }));

        var outcome = await writer.TryCreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, Alice, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<SampleTemplate>>.Many>()
            .Which.ContractIds.Should().Equal("cid-created", "cid-created-2");
    }

    [Theory]
    [InlineData("TryCreateManyByExerciseAsync", "Party")]
    [InlineData("TryCreateManyByExerciseAsync", "SubmitterInfo")]
    [InlineData("CreateOneByExerciseAsync", "Party")]
    [InlineData("CreateOneByExerciseAsync", "SubmitterInfo")]
    [InlineData("CreateManyByExerciseAsync", "Party")]
    [InlineData("CreateManyByExerciseAsync", "SubmitterInfo")]
    public async Task CreateByExercise_uses_the_supplied_command_id(string method, string submitterShape)
    {
        var writer = new CapturingWriter(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreatingOneSampleTemplate));

        await InvokeCreateByExercise(
            method, submitterShape, writer, new CommandId("caller-supplied"), TestContext.Current.CancellationToken);

        writer.LastSubmission.Should().NotBeNull("the CreateByExercise path must reach TrySubmitAndWaitForTransactionAsync for the command id to be checkable");
        writer.LastSubmission!.CommandId.Should().Be(
            new CommandId("caller-supplied"),
            "an overload that drops the caller's command id mints a fresh one instead, so a retry of a lost-but-accepted submission re-executes rather than deduplicating");
    }

    private static Task InvokeCreateByExercise(
        string method,
        string submitterShape,
        ILedgerWriter writer,
        CommandId commandId,
        CancellationToken cancellationToken)
    {
        var actAs = new Party("alice");
        SubmitterInfo submitter = actAs;

        return (method, submitterShape) switch
        {
            ("TryCreateManyByExerciseAsync", "Party") => writer.TryCreateManyByExerciseAsync<SampleTemplate>(
                SampleCommand, actAs, commandId: commandId, cancellationToken: cancellationToken),
            ("TryCreateManyByExerciseAsync", "SubmitterInfo") => writer.TryCreateManyByExerciseAsync<SampleTemplate>(
                SampleCommand, submitter, commandId: commandId, cancellationToken: cancellationToken),
            ("CreateOneByExerciseAsync", "Party") => writer.CreateOneByExerciseAsync<SampleTemplate>(
                SampleCommand, actAs, commandId: commandId, cancellationToken: cancellationToken),
            ("CreateOneByExerciseAsync", "SubmitterInfo") => writer.CreateOneByExerciseAsync<SampleTemplate>(
                SampleCommand, submitter, commandId: commandId, cancellationToken: cancellationToken),
            ("CreateManyByExerciseAsync", "Party") => writer.CreateManyByExerciseAsync<SampleTemplate>(
                SampleCommand, actAs, commandId: commandId, cancellationToken: cancellationToken),
            ("CreateManyByExerciseAsync", "SubmitterInfo") => writer.CreateManyByExerciseAsync<SampleTemplate>(
                SampleCommand, submitter, commandId: commandId, cancellationToken: cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method + " / " + submitterShape),
        };
    }

    private static readonly DisclosedContract HoldingDisclosure = new(
        "cid-holding", new Identifier("pkg", "Module", "Holding"), new byte[] { 1, 2, 3 });

    private static readonly SynchronizerId GlobalSynchronizer = new("global-domain::1220abcd");

    private static Func<CommandsSubmission, CommandsSubmission> AddEveryOptionalPart =>
        s => s.WithDisclosedContracts(HoldingDisclosure)
            .WithDeduplicationPeriod(new DeduplicationPeriod.Duration(TimeSpan.FromMinutes(5)))
            .WithSynchronizerId(GlobalSynchronizer)
            .WithMinLedgerTime(new MinLedgerTime.Relative(TimeSpan.FromSeconds(10)));

    private static void AssertEveryOptionalPartReachedTheWriter(CapturingWriter writer)
    {
        var submission = writer.LastSubmission!;
        submission.DisclosedContracts.Should().Equal(HoldingDisclosure);
        submission.DeduplicationPeriod.Should().Be(new DeduplicationPeriod.Duration(TimeSpan.FromMinutes(5)));
        submission.SynchronizerId.Should().Be(new SynchronizerId("global-domain::1220abcd"));
        submission.MinLedgerTime.Should().Be(new MinLedgerTime.Relative(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task TrySubmitSingleAsync_delivers_the_parts_added_by_configure_to_the_writer()
    {
        var writer = new CapturingWriter();

        await writer.TrySubmitSingleAsync(
            SampleCommand, Alice, configure: AddEveryOptionalPart,
            cancellationToken: TestContext.Current.CancellationToken);

        AssertEveryOptionalPartReachedTheWriter(writer);
    }

    [Fact]
    public async Task TrySubmitSingleAsync_without_configure_submits_no_optional_parts()
    {
        var writer = new CapturingWriter();

        await writer.TrySubmitSingleAsync(
            SampleCommand, Alice, cancellationToken: TestContext.Current.CancellationToken);

        var submission = writer.LastSubmission!;
        submission.DisclosedContracts.Should().BeNull();
        submission.DeduplicationPeriod.Should().BeNull();
        submission.SynchronizerId.Should().BeNull();
        submission.MinLedgerTime.Should().BeNull();
    }

    [Fact]
    public async Task TrySubmitSingleAsync_hands_configure_the_submission_already_carrying_the_command_id_and_workflow_id()
    {
        var writer = new CapturingWriter();
        CommandsSubmission? seen = null;

        await writer.TrySubmitSingleAsync(
            SampleCommand, Alice, workflowId: "wf-1", commandId: new CommandId("caller-supplied"),
            configure: s => seen = s,
            cancellationToken: TestContext.Current.CancellationToken);

        seen!.CommandId.Should().Be(new CommandId("caller-supplied"));
        seen.WorkflowId.Should().Be(new WorkflowId("wf-1"));
        seen.Commands.Should().ContainSingle().Which.Should().Be(SampleCommand);
    }

    [Fact]
    public async Task TrySubmitSingleAsync_submits_what_configure_returns()
    {
        var writer = new CapturingWriter();

        await writer.TrySubmitSingleAsync(
            SampleCommand, Alice, configure: s => s.WithCommandId(new CommandId("chosen-by-configure")),
            cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.CommandId.Should().Be(new CommandId("chosen-by-configure"));
    }

    [Fact]
    public async Task TrySubmitSingleAsync_rejects_a_configure_that_replaces_the_command_and_submits_nothing()
    {
        var writer = new CapturingWriter();
        var otherCommand = SampleCommand with { Choice = new ChoiceName("SomethingElse") };

        Func<Task> act = () => writer.TrySubmitSingleAsync(
            SampleCommand, Alice, configure: s => s with { Commands = [otherCommand] },
            cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*command*");
        writer.LastSubmission.Should().BeNull();
    }

    [Fact]
    public async Task TrySubmitSingleAsync_rejects_a_configure_that_adds_a_second_command_and_submits_nothing()
    {
        var writer = new CapturingWriter();

        Func<Task> act = () => writer.TrySubmitSingleAsync(
            SampleCommand, Alice, configure: s => s with { Commands = [SampleCommand, SampleCommand] },
            cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*command*");
        writer.LastSubmission.Should().BeNull();
    }

    [Fact]
    public async Task TrySubmitSingleAsync_rejects_a_configure_that_returns_null_and_submits_nothing()
    {
        var writer = new CapturingWriter();

        Func<Task> act = () => writer.TrySubmitSingleAsync(
            SampleCommand, Alice, configure: _ => null!,
            cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*null*");
        writer.LastSubmission.Should().BeNull();
    }

    [Fact]
    public async Task TrySubmitSingleAsync_still_dispatches_the_submitter_when_configure_sets_other_act_as_parties()
    {
        var writer = new CapturingWriter();

        await writer.TrySubmitSingleAsync(
            SampleCommand, Alice, configure: s => s.WithActAs(new Party("mallory")),
            cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmitter.Should().Be(Alice);
    }

    [Theory]
    [InlineData("TryCreateOneByExerciseAsync")]
    [InlineData("TryCreateManyByExerciseAsync")]
    [InlineData("CreateOneByExerciseAsync")]
    [InlineData("CreateManyByExerciseAsync")]
    [InlineData("ExerciseAsync")]
    public async Task Sibling_submit_extensions_deliver_the_parts_added_by_configure_to_the_writer(string method)
    {
        var writer = new CapturingWriter(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreatingOneSampleTemplate));
        var configure = AddEveryOptionalPart;
        var cancellationToken = TestContext.Current.CancellationToken;

        await (method switch
        {
            "TryCreateOneByExerciseAsync" => writer.TryCreateOneByExerciseAsync<SampleTemplate>(
                SampleCommand, Alice, configure: configure, cancellationToken: cancellationToken),
            "TryCreateManyByExerciseAsync" => writer.TryCreateManyByExerciseAsync<SampleTemplate>(
                SampleCommand, Alice, configure: configure, cancellationToken: cancellationToken),
            "CreateOneByExerciseAsync" => writer.CreateOneByExerciseAsync<SampleTemplate>(
                SampleCommand, Alice, configure: configure, cancellationToken: cancellationToken),
            "CreateManyByExerciseAsync" => writer.CreateManyByExerciseAsync<SampleTemplate>(
                SampleCommand, Alice, configure: configure, cancellationToken: cancellationToken),
            "ExerciseAsync" => writer.ExerciseAsync(
                SampleCommand, Alice, configure: configure, cancellationToken: cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
        });

        AssertEveryOptionalPartReachedTheWriter(writer);
    }

    [Fact]
    public async Task TryCreateAsync_with_configure_submits_a_create_command_carrying_the_parts_added_by_configure()
    {
        var writer = new CapturingWriter(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreatingOneSampleTemplate));

        var outcome = await SingleCommandExtensions.TryCreateAsync(
            writer, new SampleTemplate(), Alice, configure: AddEveryOptionalPart,
            cancellationToken: TestContext.Current.CancellationToken);

        AssertEveryOptionalPartReachedTheWriter(writer);
        writer.LastSubmission!.Commands.Should().ContainSingle().Which.Should().BeOfType<CreateCommand>();
        writer.LastSubmission.WorkflowId.Should().Be(new WorkflowId("create-sampletemplate"));
        outcome.Should().BeOfType<ExerciseOutcome<ContractId<SampleTemplate>>.One>()
            .Which.Result.Should().Be(new ContractId<SampleTemplate>("cid-created"));
        writer.TryCreateAsyncCalls.Should().Be(0);
    }

    [Fact]
    public async Task TryCreateAsync_with_configure_carries_the_supplied_workflow_id_and_command_id()
    {
        var writer = new CapturingWriter(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreatingOneSampleTemplate));

        await SingleCommandExtensions.TryCreateAsync(
            writer, new SampleTemplate(), Alice, workflowId: "wf-1", commandId: new CommandId("caller-supplied"),
            timeout: TimeSpan.FromSeconds(3), configure: AddEveryOptionalPart,
            cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.WorkflowId.Should().Be(new WorkflowId("wf-1"));
        writer.LastSubmission.CommandId.Should().Be(new CommandId("caller-supplied"));
        writer.LastTimeout.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task TryCreateAsync_with_a_null_configure_delegates_to_the_writers_own_create()
    {
        var writer = new CapturingWriter();

        await SingleCommandExtensions.TryCreateAsync(
            writer, new SampleTemplate(), Alice, configure: null,
            cancellationToken: TestContext.Current.CancellationToken);

        writer.TryCreateAsyncCalls.Should().Be(1);
        writer.LastSubmission.Should().BeNull();
    }

    [Fact]
    public async Task ExerciseAsync_forwards_the_command_id_from_an_implicitly_converted_Party_for_a_void_choice()
    {
        var writer = new CapturingWriter();

        await writer.ExerciseAsync(
            SampleCommand, new Party("alice"), commandId: new CommandId("caller-supplied"),
            cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.CommandId.Should().Be(new CommandId("caller-supplied"));
    }

    [Fact]
    public async Task ExerciseAsync_mints_a_command_id_for_a_void_choice()
    {
        var writer = new CapturingWriter();

        await writer.ExerciseAsync(
            SampleCommand, Alice, cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.CommandId.Should().NotBeNull();
    }

    private sealed class CapturingWriter : ILedgerWriter
    {
        private static readonly TransactionResult SampleTransaction = new(
            "update-id", LedgerOffset.Begin, [], [], new CommandId("cmd-id"));

        private readonly ExerciseOutcome<TransactionResult> _outcome;

        public CapturingWriter()
            : this(new ExerciseOutcome<TransactionResult>.One(SampleTransaction))
        {
        }

        public CapturingWriter(ExerciseOutcome<TransactionResult> outcome) => _outcome = outcome;

        public CommandsSubmission? LastSubmission { get; private set; }

        public SubmitterInfo? LastSubmitter { get; private set; }

        public int TryCreateAsyncCalls { get; private set; }

        public TimeSpan? LastTimeout { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
            ExerciseCommand command,
            SubmitterInfo submitter,
            string? workflowId = null,
            CommandId? commandId = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<ExerciseOutcome<TResult>>(new ExerciseOutcome<TResult>.One(default(TResult)!));

        public Task<SubmitAndWaitResult> SubmitAndWaitAsync(
            CommandsSubmission submission,
            SubmitterInfo submitter,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new SubmitAndWaitResult(new CommandId("cmd-id"), "update-id", LedgerOffset.Begin));

        public Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
            CommandsSubmission submission,
            SubmitterInfo submitter,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            LastSubmission = submission;
            LastSubmitter = submitter;
            LastTimeout = timeout;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(_outcome);
        }

        public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
            TTemplate payload,
            SubmitterInfo submitter,
            string? workflowId = null,
            CommandId? commandId = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            where TTemplate : ITemplate
        {
            TryCreateAsyncCalls++;
            return Task.FromResult<ExerciseOutcome<ContractId<TTemplate>>>(
                new ExerciseOutcome<ContractId<TTemplate>>.None());
        }
    }
}
