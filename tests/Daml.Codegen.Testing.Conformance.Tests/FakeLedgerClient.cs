// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions.Testing.Conformance;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;

namespace Daml.Codegen.Testing.Conformance.Tests;

internal sealed class FakeLedgerClient : NotSupportedLedgerClient
{
    private readonly Func<CommandsSubmission, ExerciseOutcome<TransactionResult>> _transaction;
    private readonly Func<object, ExerciseOutcome<object>>? _create;

    public CommandsSubmission? LastSubmission { get; private set; }

    public SubmitterInfo? LastCreateSubmitter { get; private set; }

    public FakeLedgerClient(
        Func<CommandsSubmission, ExerciseOutcome<TransactionResult>>? transaction = null,
        Func<object, ExerciseOutcome<object>>? create = null)
    {
        _transaction = transaction
            ?? (_ => new ExerciseOutcome<TransactionResult>.InfraError(new TransportStatus.UndecodableBody(), "unset"));
        _create = create;
    }

    public override Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
        CommandsSubmission submission,
        SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveSubmission = submission.WithSubmitter(submitter);
        LastSubmission = effectiveSubmission;
        return Task.FromResult(_transaction(effectiveSubmission));
    }

    public override Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload,
        SubmitterInfo submitter,
        string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        LastCreateSubmitter = submitter;
        var projected = _create is null
            ? new ExerciseOutcome<ContractId<TTemplate>>.InfraError(new TransportStatus.UndecodableBody(), "unset")
            : Project<TTemplate>(_create(payload!));
        return Task.FromResult(projected);
    }

    private static ExerciseOutcome<ContractId<TTemplate>> Project<TTemplate>(ExerciseOutcome<object> outcome)
        where TTemplate : ITemplate =>
        outcome switch
        {
            ExerciseOutcome<object>.One one =>
                new ExerciseOutcome<ContractId<TTemplate>>.One(new ContractId<TTemplate>((string)one.Result)),
            ExerciseOutcome<object>.DamlError e =>
                new ExerciseOutcome<ContractId<TTemplate>>.DamlError(e.Category, e.ErrorId, e.Message, e.Metadata),
            ExerciseOutcome<object>.InfraError e =>
                new ExerciseOutcome<ContractId<TTemplate>>.InfraError(e.Status, e.Message, e.Category, e.SourceException),
            _ => new ExerciseOutcome<ContractId<TTemplate>>.None(),
        };
}
