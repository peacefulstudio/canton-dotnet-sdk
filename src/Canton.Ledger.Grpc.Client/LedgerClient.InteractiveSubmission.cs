// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Telemetry;
using Canton.Ledger.Kernel.Wire;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Interactive = Com.Daml.Ledger.Api.V2.Interactive;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client;

internal sealed partial class LedgerClient
{
    /// <inheritdoc />
    public Task<PreparedSubmission> PrepareSubmissionAsync(
        RuntimeCommands.CommandsSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var request = BuildPrepareSubmissionRequest(submission, estimateTrafficCost: false);

        return _invoker.InvokeTracedAsync<LedgerClient, Interactive.PrepareSubmissionResponse, PreparedSubmission>(
            LedgerCallKind.Read,
            LedgerCallInvoker.Source,
            Interactive.InteractiveSubmissionService.Descriptor,
            "PrepareSubmission",
            (headers, deadline, token) => _interactiveSubmissionService.PrepareSubmissionAsync(
                request, headers, deadline, token),
            ProjectPreparedSubmission,
            cancellationToken,
            timeout: timeout);
    }

    /// <inheritdoc />
    public Task ExecuteSubmissionAsync(
        SignedSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var request = BuildExecuteSubmissionRequest(submission);

        return _invoker.InvokeTracedAsync<LedgerClient, Interactive.ExecuteSubmissionResponse>(
            LedgerCallKind.AcceptedOnlyWrite,
            LedgerCallInvoker.Source,
            Interactive.InteractiveSubmissionService.Descriptor,
            "ExecuteSubmission",
            (headers, deadline, token) => _interactiveSubmissionService.ExecuteSubmissionAsync(
                request, headers, deadline, token),
            cancellationToken,
            activity => activity?.SetTag(LedgerActivityTagNames.CantonSubmissionId, submission.SubmissionId),
            timeout,
            replayable: false);
    }

    /// <inheritdoc />
    public Task<ExecutedSubmission> ExecuteSubmissionAndWaitAsync(
        SignedSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var request = BuildExecuteSubmissionAndWaitRequest(submission);

        return _invoker.InvokeTracedAsync<LedgerClient, Interactive.ExecuteSubmissionAndWaitResponse, ExecutedSubmission>(
            LedgerCallKind.EffectAppliedWrite,
            LedgerCallInvoker.Source,
            Interactive.InteractiveSubmissionService.Descriptor,
            "ExecuteSubmissionAndWait",
            (headers, deadline, token) => _interactiveSubmissionService.ExecuteSubmissionAndWaitAsync(
                request, headers, deadline, token),
            response => new ExecutedSubmission(response.UpdateId, LedgerWireConversions.ToLedgerOffset(response.CompletionOffset)),
            cancellationToken,
            configureActivity: activity => activity?.SetTag(LedgerActivityTagNames.CantonSubmissionId, submission.SubmissionId),
            timeout: timeout,
            replayable: false);
    }

    /// <inheritdoc />
    public Task<TransactionResult> ExecuteSubmissionAndWaitForTransactionAsync(
        SignedSubmission submission,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var request = BuildExecuteSubmissionAndWaitForTransactionRequest(submission, submitter);

        return _invoker.InvokeTracedAsync<LedgerClient, Interactive.ExecuteSubmissionAndWaitForTransactionResponse, TransactionResult>(
            LedgerCallKind.EffectAppliedWrite,
            LedgerCallInvoker.Source,
            Interactive.InteractiveSubmissionService.Descriptor,
            "ExecuteSubmissionAndWaitForTransaction",
            (headers, deadline, token) => _interactiveSubmissionService.ExecuteSubmissionAndWaitForTransactionAsync(
                request, headers, deadline, token),
            response => ProjectExecutedTransaction(response, submission.SubmissionId),
            cancellationToken,
            configureActivity: activity =>
            {
                activity?.SetTag(LedgerActivityTagNames.CantonSubmissionId, submission.SubmissionId);
                activity.SetSubmitterTags(submitter, _options);
            },
            timeout: timeout,
            replayable: false);
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

        var request = new Interactive.GetPreferredPackagesRequest
        {
            SynchronizerId = synchronizerId?.Value ?? string.Empty,
            VettingValidAt = vettingValidAt is { } validAt ? Timestamp.FromDateTimeOffset(validAt) : null,
        };
        request.PackageVettingRequirements.AddRange(requirements.Select(ToWireRequirement));

        return _invoker.InvokeTracedAsync<LedgerClient, Interactive.GetPreferredPackagesResponse, PreferredPackages>(
            LedgerCallKind.Read,
            LedgerCallInvoker.Source,
            Interactive.InteractiveSubmissionService.Descriptor,
            "GetPreferredPackages",
            (headers, deadline, token) => _interactiveSubmissionService.GetPreferredPackagesAsync(
                request, headers, deadline, token),
            response => new PreferredPackages(
                response.PackageReferences.Select(GrpcExternalSigningMapper.FromWire).ToList(),
                RequireSynchronizer(response.SynchronizerId)),
            cancellationToken,
            timeout: timeout);
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

        var request = new Interactive.GetPreferredPackageVersionRequest
        {
            PackageName = packageName,
            SynchronizerId = synchronizerId?.Value ?? string.Empty,
            VettingValidAt = vettingValidAt is { } validAt ? Timestamp.FromDateTimeOffset(validAt) : null,
        };
        request.Parties.AddRange(parties.Select(party => party.Value));

        return _invoker.InvokeTracedAsync<LedgerClient, Interactive.GetPreferredPackageVersionResponse, PackagePreference?>(
            LedgerCallKind.Read,
            LedgerCallInvoker.Source,
            Interactive.InteractiveSubmissionService.Descriptor,
            "GetPreferredPackageVersion",
            (headers, deadline, token) => _interactiveSubmissionService.GetPreferredPackageVersionAsync(
                request, headers, deadline, token),
            response => response.PackagePreference is { } preference
                ? new PackagePreference(
                    GrpcExternalSigningMapper.FromWire(
                        preference.PackageReference
                        ?? throw MalformedResponse.MissingRequiredField("the package preference has no package_reference")),
                    RequireSynchronizer(preference.SynchronizerId))
                : null,
            cancellationToken,
            timeout: timeout);
    }

    private static PreparedSubmission ProjectPreparedSubmission(Interactive.PrepareSubmissionResponse response) =>
        new(
            (response.PreparedTransaction
                ?? throw MalformedResponse.MissingRequiredField("the participant prepared a submission without a prepared transaction"))
            .ToByteArray(),
            response.PreparedTransactionHash.Memory,
            (HashingSchemeVersion)(int)response.HashingSchemeVersion,
            response.HasHashingDetails ? response.HashingDetails : null,
            ProjectTrafficCostEstimate(response.CostEstimation));

    private static SynchronizerId RequireSynchronizer(string wireValue) =>
        SynchronizerId.FromWire(wireValue)
        ?? throw MalformedResponse.MissingRequiredField("the participant returned a package preference without a synchronizer id");

    private static Interactive.PackageVettingRequirement ToWireRequirement(PackageVettingRequirement requirement)
    {
        var wire = new Interactive.PackageVettingRequirement { PackageName = requirement.PackageName };
        wire.Parties.AddRange(requirement.Parties.Select(party => party.Value));
        return wire;
    }

    private Interactive.ExecuteSubmissionRequest BuildExecuteSubmissionRequest(SignedSubmission submission)
    {
        var execution = ToExecution(submission);
        var request = new Interactive.ExecuteSubmissionRequest
        {
            PreparedTransaction = execution.PreparedTransaction,
            PartySignatures = execution.PartySignatures,
            SubmissionId = submission.SubmissionId,
            UserId = execution.UserId,
            HashingSchemeVersion = execution.HashingSchemeVersion,
            MinLedgerTime = execution.MinLedgerTime,
        };

        if (execution.DeduplicationOffset is { } offset) request.DeduplicationOffset = offset;
        else if (execution.DeduplicationDuration is { } duration) request.DeduplicationDuration = duration;

        return request;
    }

    private Interactive.ExecuteSubmissionAndWaitRequest BuildExecuteSubmissionAndWaitRequest(SignedSubmission submission)
    {
        var execution = ToExecution(submission);
        var request = new Interactive.ExecuteSubmissionAndWaitRequest
        {
            PreparedTransaction = execution.PreparedTransaction,
            PartySignatures = execution.PartySignatures,
            SubmissionId = submission.SubmissionId,
            UserId = execution.UserId,
            HashingSchemeVersion = execution.HashingSchemeVersion,
            MinLedgerTime = execution.MinLedgerTime,
        };

        if (execution.DeduplicationOffset is { } offset) request.DeduplicationOffset = offset;
        else if (execution.DeduplicationDuration is { } duration) request.DeduplicationDuration = duration;

        return request;
    }

    private Interactive.ExecuteSubmissionAndWaitForTransactionRequest BuildExecuteSubmissionAndWaitForTransactionRequest(
        SignedSubmission submission,
        RuntimeCommands.SubmitterInfo submitter)
    {
        var execution = ToExecution(submission);
        var request = new Interactive.ExecuteSubmissionAndWaitForTransactionRequest
        {
            PreparedTransaction = execution.PreparedTransaction,
            PartySignatures = execution.PartySignatures,
            SubmissionId = submission.SubmissionId,
            UserId = execution.UserId,
            HashingSchemeVersion = execution.HashingSchemeVersion,
            MinLedgerTime = execution.MinLedgerTime,
            TransactionFormat = GrpcSubscribeRequestBuilder.BuildTransactionFormat(submitter),
        };

        if (execution.DeduplicationOffset is { } offset) request.DeduplicationOffset = offset;
        else if (execution.DeduplicationDuration is { } duration) request.DeduplicationDuration = duration;

        return request;
    }

    private Execution ToExecution(SignedSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(submission.Prepared);
        ArgumentNullException.ThrowIfNull(submission.PartySignatures);
        ArgumentException.ThrowIfNullOrWhiteSpace(submission.SubmissionId);

        var partySignatures = new Interactive.PartySignatures();
        partySignatures.Signatures.AddRange(submission.PartySignatures.Select(ToWireSinglePartySignatures));

        return new Execution(
            ParsePreparedTransaction(submission.Prepared.PreparedTransaction),
            partySignatures,
            _options.UserId ?? string.Empty,
            (Interactive.HashingSchemeVersion)(int)submission.Prepared.HashingSchemeVersion,
            submission.MinLedgerTime is { } minLedgerTime ? ToWireMinLedgerTime(minLedgerTime) : null,
            (submission.DeduplicationPeriod as RuntimeCommands.DeduplicationPeriod.Offset)?.Start.Value,
            (submission.DeduplicationPeriod as RuntimeCommands.DeduplicationPeriod.Duration) is { } duration
                ? Duration.FromTimeSpan(duration.Length)
                : null);
    }

    private static Interactive.PreparedTransaction ParsePreparedTransaction(ReadOnlyMemory<byte> serialized)
    {
        try
        {
            return Interactive.PreparedTransaction.Parser.ParseFrom(serialized.Span);
        }
        catch (InvalidProtocolBufferException malformed)
        {
            throw new ArgumentException(
                "The prepared transaction is not a serialized PreparedTransaction message.",
                nameof(serialized),
                malformed);
        }
    }

    private static Interactive.SinglePartySignatures ToWireSinglePartySignatures(PartySignatures partySignatures)
    {
        var wire = new Interactive.SinglePartySignatures { Party = partySignatures.Party.Value };
        wire.Signatures.AddRange(partySignatures.Signatures.Select(GrpcExternalSigningMapper.ToWire));
        return wire;
    }

    private static Interactive.MinLedgerTime ToWireMinLedgerTime(RuntimeCommands.MinLedgerTime bound) =>
        bound.Match(
            absolute: instant => new Interactive.MinLedgerTime { MinLedgerTimeAbs = Timestamp.FromDateTimeOffset(instant) },
            relative: delay => new Interactive.MinLedgerTime { MinLedgerTimeRel = Duration.FromTimeSpan(delay) });

    private static TransactionResult ProjectExecutedTransaction(
        Interactive.ExecuteSubmissionAndWaitForTransactionResponse response,
        string submissionId)
    {
        var transaction = response.Transaction
            ?? throw MalformedResponse.MissingRequiredField(
                "the ExecuteSubmissionAndWaitForTransaction response has no transaction");

        try
        {
            return GrpcTransactionResultProjector.Project(transaction);
        }
        catch (Exception decodeFailure) when (MalformedResponse.IsWireDecodeFailure(decodeFailure))
        {
            throw MalformedResponse.CouldNotDecodeTransaction($"submission {submissionId}", decodeFailure);
        }
    }

    private sealed record Execution(
        Interactive.PreparedTransaction PreparedTransaction,
        Interactive.PartySignatures PartySignatures,
        string UserId,
        Interactive.HashingSchemeVersion HashingSchemeVersion,
        Interactive.MinLedgerTime? MinLedgerTime,
        long? DeduplicationOffset,
        Duration? DeduplicationDuration);
}
