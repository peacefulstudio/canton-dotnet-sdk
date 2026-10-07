// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Testing;

public sealed partial class FakeLedgerClient
{
    /// <inheritdoc />
    public Task<PreparedSubmission> PrepareSubmissionAsync(
        RuntimeCommands.CommandsSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        return Answering(() => _interactive.PreparedSubmission ?? throw StagingMissing(
            "prepared submission", nameof(PrepareSubmissionAsync), "WithPreparedSubmission"));
    }

    /// <inheritdoc />
    public Task ExecuteSubmissionAsync(
        SignedSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSigned(submission);
        return ThrowingCallFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ExecutedSubmission> ExecuteSubmissionAndWaitAsync(
        SignedSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSigned(submission);
        return Answering(() =>
        {
            var executed = _interactive.ExecutedSubmission ?? throw StagingMissing(
                "executed submission", nameof(ExecuteSubmissionAndWaitAsync), "WithExecutedSubmission");
            Interlocked.Increment(ref _committedWrites);
            return executed;
        });
    }

    /// <inheritdoc />
    public Task<TransactionResult> ExecuteSubmissionAndWaitForTransactionAsync(
        SignedSubmission submission,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSigned(submission);
        return Answering(() =>
        {
            var transaction = _interactive.ExecutedTransaction ?? throw StagingMissing(
                "executed transaction", nameof(ExecuteSubmissionAndWaitForTransactionAsync), "WithExecutedTransaction");
            Interlocked.Increment(ref _committedWrites);
            return transaction;
        });
    }

    /// <inheritdoc />
    public Task<PreferredPackages> GetPreferredPackagesAsync(
        IEnumerable<PackageVettingRequirement> requirements,
        SynchronizerId? synchronizerId = null,
        DateTimeOffset? vettingValidAt = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        return Answering(() => _interactive.PreferredPackages ?? throw StagingMissing(
            "preferred packages", nameof(GetPreferredPackagesAsync), "WithPreferredPackages"));
    }

    /// <inheritdoc />
    public Task<PackagePreference?> GetPreferredPackageVersionAsync(
        IEnumerable<Party> parties,
        string packageName,
        SynchronizerId? synchronizerId = null,
        DateTimeOffset? vettingValidAt = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parties);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        return Answering(() => _interactive.PackagePreference is { } staged
            ? staged.Preference
            : throw StagingMissing(
                "package preference", nameof(GetPreferredPackageVersionAsync), "WithPackagePreference"));
    }

    private static void ValidateSigned(SignedSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentException.ThrowIfNullOrWhiteSpace(submission.SubmissionId);
    }
}

/// <summary>
/// The interactive-submission answers a <see cref="FakeLedgerClient"/> replays: the prepared
/// submission, the two execute-and-wait results, and the package-preference answers.
/// </summary>
internal sealed record FakeInteractiveSurface(
    PreparedSubmission? PreparedSubmission,
    ExecutedSubmission? ExecutedSubmission,
    TransactionResult? ExecutedTransaction,
    PreferredPackages? PreferredPackages,
    StagedPackagePreference? PackagePreference);

/// <summary>
/// A staged package-preference answer, boxed so that staging "no package satisfies the
/// requirements" is distinguishable from staging nothing at all.
/// </summary>
/// <param name="Preference">The preference to reply with, or <see langword="null"/> when no package satisfies.</param>
internal sealed record StagedPackagePreference(PackagePreference? Preference);
