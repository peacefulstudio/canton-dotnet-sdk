// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Commands;

/// <summary>
/// Represents a submission of commands to the ledger.
/// </summary>
/// <remarks>
/// Every collection-typed member is copied at construction and on <c>init</c> — so through
/// <c>with</c> expressions and the <c>With…</c> builders too — because a submission carries
/// authorization data and must not change under a caller that retains the array it passed.
/// Equality is structural over those copies, so two submissions built from equal contents
/// compare equal however they were allocated.
/// </remarks>
/// <param name="Commands">The commands to submit.</param>
/// <param name="WorkflowId">Optional workflow identifier for correlation.</param>
/// <param name="CommandId">Unique command identifier for deduplication.</param>
/// <param name="ActAs">Parties to act as when submitting.</param>
/// <param name="ReadAs">Parties whose contracts are visible.</param>
/// <param name="SynchronizerId">Optional synchronizer to pin the submission to.</param>
/// <param name="DisclosedContracts">
/// Optional contracts explicitly disclosed alongside this submission, for parties
/// that don't natively see them. <see langword="null"/> preserves today's behaviour
/// (no explicit disclosure).
/// </param>
/// <param name="MinLedgerTime">
/// Optional earliest ledger effective time for this submission — the participant must not
/// commit it before the bound. <see langword="null"/> preserves today's behaviour (no bound:
/// the participant assigns the ledger time itself).
/// </param>
public sealed record CommandsSubmission(
    IReadOnlyList<ICommand> Commands,
    WorkflowId? WorkflowId = null,
    CommandId? CommandId = null,
    IReadOnlyList<Party>? ActAs = null,
    IReadOnlyList<Party>? ReadAs = null,
    SynchronizerId? SynchronizerId = null,
    IReadOnlyList<DisclosedContract>? DisclosedContracts = null,
    MinLedgerTime? MinLedgerTime = null)
{
    private readonly IReadOnlyList<ICommand> _commands =
        EventCollections.Copy(Commands, nameof(Commands));

    private readonly IReadOnlyList<Party>? _actAs = CopiedOrNull(ActAs, nameof(ActAs));

    private readonly IReadOnlyList<Party>? _readAs = CopiedOrNull(ReadAs, nameof(ReadAs));

    private readonly IReadOnlyList<DisclosedContract>? _disclosedContracts =
        CopiedOrNull(DisclosedContracts, nameof(DisclosedContracts));

    private readonly DeduplicationPeriod? _deduplicationPeriod;

    /// <summary>
    /// The commands to submit. Copied at construction and on <c>init</c>, so a caller that
    /// retains the list it supplied cannot change the submission, its equality or its hash
    /// code afterwards.
    /// </summary>
    /// <exception cref="ArgumentNullException">The supplied list is <c>null</c>.</exception>
    public IReadOnlyList<ICommand> Commands
    {
        get => _commands;
        init => _commands = EventCollections.Copy(value, nameof(Commands));
    }

    /// <summary>
    /// Parties to act as when submitting, or <c>null</c> when the submission carries none —
    /// which is not the same as carrying an empty set. Copied on the same terms as
    /// <see cref="Commands"/>.
    /// </summary>
    public IReadOnlyList<Party>? ActAs
    {
        get => _actAs;
        init => _actAs = CopiedOrNull(value, nameof(ActAs));
    }

    /// <summary>
    /// Parties whose contracts are visible, or <c>null</c> when the submission carries none.
    /// Copied on the same terms as <see cref="Commands"/>.
    /// </summary>
    public IReadOnlyList<Party>? ReadAs
    {
        get => _readAs;
        init => _readAs = CopiedOrNull(value, nameof(ReadAs));
    }

    /// <summary>
    /// Contracts explicitly disclosed alongside this submission, or <c>null</c> when none
    /// are. Copied on the same terms as <see cref="Commands"/>; each
    /// <see cref="DisclosedContract"/> already owns its own payload bytes.
    /// </summary>
    public IReadOnlyList<DisclosedContract>? DisclosedContracts
    {
        get => _disclosedContracts;
        init => _disclosedContracts = CopiedOrNull(value, nameof(DisclosedContracts));
    }

    /// <summary>
    /// The period over which the participant deduplicates this submission against earlier ones
    /// carrying the same change id (<see cref="CommandId"/>, <see cref="ActAs"/> and user id),
    /// projected onto the <c>Commands.deduplication_period</c> oneof — or <see langword="null"/>
    /// to leave the oneof unset, so the participant applies its configured maximum
    /// deduplication duration.
    /// </summary>
    /// <remarks>
    /// Unset by default: no submission derives a period from a retry policy's horizon. A caller
    /// that retries for longer than the participant's default window sets one explicitly.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The period is a <see cref="Commands.DeduplicationPeriod.Duration"/> whose length is
    /// negative, which the Ledger API rejects.
    /// </exception>
    public DeduplicationPeriod? DeduplicationPeriod
    {
        get => _deduplicationPeriod;
        init => _deduplicationPeriod = NonNegative(value, nameof(DeduplicationPeriod));
    }

    /// <summary>
    /// Creates a submission with a single command.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1720:Identifier contains type name",
        Justification = "Single and Multiple name the command cardinality of the submission, paired as a factory vocabulary; neither refers to System.Single.")]
    public static CommandsSubmission Single(ICommand command, Party? actAs = null) =>
        new([command], ActAs: actAs is not null ? [actAs.Value] : null);

    /// <summary>
    /// Creates a submission with multiple commands.
    /// </summary>
    public static CommandsSubmission Multiple(params ICommand[] commands) =>
        new(commands);

    /// <summary>
    /// Adds a workflow ID to this submission.
    /// </summary>
    public CommandsSubmission WithWorkflowId(WorkflowId workflowId) =>
        this with { WorkflowId = workflowId };

    /// <summary>
    /// Adds a workflow ID to this submission when <paramref name="workflowId"/> carries one,
    /// and returns the submission unchanged when it is <c>null</c>, empty or whitespace-only.
    /// A blank workflow id is treated as absent rather than stored, because <c>workflow_id</c>
    /// is a correlation key and a blank one correlates nothing — which is as true of a
    /// whitespace-only value as it is of an empty one.
    /// </summary>
    /// <remarks>
    /// Only this convenience overload decides that blank means absent.
    /// <see cref="WorkflowId"/> itself stays permissive — its constructor accepts empty and
    /// whitespace because the Ledger API puts no non-empty constraint on <c>workflow_id</c> —
    /// so a caller who genuinely wants to send a blank one still can, by saying
    /// <c>WithWorkflowId(new WorkflowId(" "))</c> explicitly.
    /// </remarks>
    /// <param name="workflowId">The workflow id, or <c>null</c>/blank to leave the submission unchanged.</param>
    public CommandsSubmission WithOptionalWorkflowId(string? workflowId) =>
        string.IsNullOrWhiteSpace(workflowId) ? this : WithWorkflowId(new WorkflowId(workflowId));

    /// <summary>
    /// Adds a command ID to this submission.
    /// </summary>
    public CommandsSubmission WithCommandId(CommandId commandId) =>
        this with { CommandId = commandId };

    /// <summary>
    /// Adds a synchronizer ID to this submission.
    /// </summary>
    public CommandsSubmission WithSynchronizerId(SynchronizerId synchronizerId) =>
        this with { SynchronizerId = synchronizerId };

    /// <summary>
    /// Sets the parties to act as.
    /// </summary>
    public CommandsSubmission WithActAs(params Party[] parties) =>
        this with { ActAs = parties };

    /// <summary>
    /// Sets the parties to read as.
    /// </summary>
    public CommandsSubmission WithReadAs(params Party[] parties) =>
        this with { ReadAs = parties };

    /// <summary>
    /// Sets the contracts to explicitly disclose alongside this submission.
    /// Passing no contracts, <see langword="null"/>, or an empty array clears
    /// the field back to <see langword="null"/>.
    /// </summary>
    public CommandsSubmission WithDisclosedContracts(params DisclosedContract[]? disclosedContracts) =>
        this with { DisclosedContracts = disclosedContracts is { Length: > 0 } ? disclosedContracts : null };

    /// <summary>
    /// Sets the earliest ledger effective time this submission may be assigned. Passing
    /// <see langword="null"/> clears the bound, leaving the ledger time to the participant.
    /// </summary>
    /// <param name="minLedgerTime">
    /// The bound — <see cref="Commands.MinLedgerTime.Absolute"/> or
    /// <see cref="Commands.MinLedgerTime.Relative"/> — or <see langword="null"/> to impose none.
    /// </param>
    public CommandsSubmission WithMinLedgerTime(MinLedgerTime? minLedgerTime) =>
        this with { MinLedgerTime = minLedgerTime };

    /// <summary>
    /// Sets the period over which the participant deduplicates this submission. Passing
    /// <see langword="null"/> clears it, leaving the participant's configured maximum
    /// deduplication duration in force.
    /// </summary>
    /// <param name="deduplicationPeriod">
    /// The period — <see cref="Commands.DeduplicationPeriod.Duration"/> or
    /// <see cref="Commands.DeduplicationPeriod.Offset"/> — or <see langword="null"/> for the
    /// participant default.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="deduplicationPeriod"/> is a <see cref="Commands.DeduplicationPeriod.Duration"/>
    /// whose length is negative.
    /// </exception>
    public CommandsSubmission WithDeduplicationPeriod(DeduplicationPeriod? deduplicationPeriod) =>
        this with { DeduplicationPeriod = deduplicationPeriod };

    /// <summary>
    /// Applies a <see cref="SubmitterInfo"/> — sets both <see cref="ActAs"/> and
    /// <see cref="ReadAs"/> from the submitter's party sets in a single call. The
    /// preferred way for code-generated and library callers to project a typed
    /// submitter onto a submission; preserves the property that the wire format
    /// reflects exactly the parties carried by <paramref name="submitter"/>.
    /// </summary>
    /// <remarks>
    /// Both projections are overwritten, so a submitter carrying no <c>readAs</c> parties clears
    /// any <see cref="ReadAs"/> already on the submission.
    /// </remarks>
    public CommandsSubmission WithSubmitter(SubmitterInfo submitter)
    {
        var withActAs = this with { ActAs = [.. submitter.ActAs] };
        return submitter.ReadAs.Count == 0
            ? withActAs with { ReadAs = null }
            : withActAs with { ReadAs = [.. submitter.ReadAs] };
    }

    /// <summary>
    /// Compares two submissions field-by-field, comparing <see cref="Commands"/>,
    /// <see cref="ActAs"/>, <see cref="ReadAs"/> and <see cref="DisclosedContracts"/> element
    /// by element rather than by list identity — each element then using its own equality, as
    /// <see cref="Contracts.TransactionResult"/> and <see cref="DisclosedContract"/> already do.
    /// </summary>
    /// <remarks>
    /// The record-synthesized equality compares the backing <see cref="IReadOnlyList{T}"/>
    /// members by reference. Left alone it would narrow to near-identity once the members are
    /// copied at every entry point, because no two submissions could then share a list
    /// instance; comparing the contents instead keeps two submissions built from the same
    /// values equal however they were allocated. An absent list stays distinct from an empty
    /// one, so a submission carrying no <c>act_as</c> parties never compares equal to one
    /// carrying an empty set.
    /// </remarks>
    /// <param name="other">The submission to compare against.</param>
    /// <returns><c>true</c> when both describe the same submission.</returns>
    public bool Equals(CommandsSubmission? other) =>
        other is not null
        && WorkflowId == other.WorkflowId
        && CommandId == other.CommandId
        && SynchronizerId == other.SynchronizerId
        && Equals(MinLedgerTime, other.MinLedgerTime)
        && Equals(DeduplicationPeriod, other.DeduplicationPeriod)
        && Commands.SequenceEqual(other.Commands)
        && ContentsEqual(ActAs, other.ActAs)
        && ContentsEqual(ReadAs, other.ReadAs)
        && ContentsEqual(DisclosedContracts, other.DisclosedContracts);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(WorkflowId);
        hash.Add(CommandId);
        hash.Add(SynchronizerId);
        hash.Add(MinLedgerTime);
        hash.Add(DeduplicationPeriod);
        hash.Add(ContentsHash(Commands));
        hash.Add(ContentsHash(ActAs));
        hash.Add(ContentsHash(ReadAs));
        hash.Add(ContentsHash(DisclosedContracts));
        return hash.ToHashCode();
    }

    private static DeduplicationPeriod? NonNegative(DeduplicationPeriod? period, string parameterName) =>
        period is DeduplicationPeriod.Duration { Length: var length } && length < TimeSpan.Zero
            ? throw new ArgumentOutOfRangeException(
                parameterName,
                length,
                "A deduplication duration must be non-negative.")
            : period;

    private static IReadOnlyList<T>? CopiedOrNull<T>(IReadOnlyList<T>? values, string parameterName) =>
        values is null ? null : EventCollections.Copy(values, parameterName);

    private static bool ContentsEqual<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right) =>
        left is null ? right is null : right is not null && left.SequenceEqual(right);

    private static int ContentsHash<T>(IReadOnlyList<T>? values)
    {
        if (values is null)
        {
            return 0;
        }

        var hash = new HashCode();
        hash.Add(values.Count);
        foreach (var value in values)
        {
            hash.Add(value);
        }
        return hash.ToHashCode();
    }
}
