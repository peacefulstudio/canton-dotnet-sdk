// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Telemetry;
using Canton.Ledger.Kernel.Wire;
using Daml.Ledger.Abstractions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Interactive = Com.Daml.Ledger.Api.V2.Interactive;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client;

internal sealed partial class LedgerClient
{
    /// <inheritdoc />
    /// <remarks>
    /// Priced over the interactive submission service's <c>PrepareSubmission</c>. The per-call
    /// <paramref name="timeout"/> overrides <see cref="LedgerClientOptions.Timeout"/>; when both are
    /// <see langword="null"/> the call carries no deadline. The estimated total is tagged onto the
    /// call's activity as <c>canton.traffic_cost_bytes</c>.
    /// </remarks>
    /// <exception cref="LedgerOperationException">
    /// The participant rejected or failed the request, or answered with a cost estimate that cannot be
    /// read: a cost above <see cref="long.MaxValue"/> (over nine exabytes for one transaction, so a corrupt
    /// or hostile response rather than an expensive submission) or an out-of-range estimation timestamp.
    /// The latter two carry <c>UndecodableBody</c> and a <see cref="MalformedResponseException"/> naming the
    /// offending value.
    /// </exception>
    public Task<TrafficCostEstimate?> EstimateTrafficCostAsync(
        RuntimeCommands.CommandsSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var request = BuildPrepareSubmissionRequest(submission, estimateTrafficCost: true);

        return _invoker.ExecuteTracedAsync<LedgerClient, TrafficCostEstimate?>(
            LedgerCallKind.Read,
            LedgerCallInvoker.Source,
            Interactive.InteractiveSubmissionService.Descriptor,
            "PrepareSubmission",
            async (activity, token) =>
            {
                var response = await _invoker.InvokeAsync(
                    (headers, deadline, callToken) => _interactiveSubmissionService.PrepareSubmissionAsync(
                        request, headers, deadline, callToken),
                    token,
                    timeout).ConfigureAwait(false);

                var estimate = ProjectTrafficCostEstimate(response.CostEstimation);
                if (estimate is not null)
                {
                    activity?.SetTag(LedgerActivityTagNames.CantonTrafficCostBytes, estimate.TotalCost);
                }

                return estimate;
            },
            cancellationToken);
    }

    internal static TrafficCostEstimate? ProjectTrafficCostEstimate(Interactive.CostEstimation? estimation) =>
        estimation is null
            ? null
            : new TrafficCostEstimate(
                MalformedResponse.Decoding(estimation.EstimationTimestamp, timestamp => timestamp?.ToDateTimeOffset()),
                ToSignedCost(estimation.ConfirmationRequestTrafficCostEstimation, "confirmation request"),
                ToSignedCost(estimation.ConfirmationResponseTrafficCostEstimation, "confirmation response"),
                ToSignedCost(estimation.TotalTrafficCostEstimation, "total"));

    private static long ToSignedCost(ulong reportedCost, string component) =>
        reportedCost <= long.MaxValue
            ? (long)reportedCost
            : throw MalformedResponse.WithDetail(
                $"the participant reports a {component} traffic cost of {reportedCost} bytes, which exceeds the supported maximum of {long.MaxValue}.");

    private Interactive.PrepareSubmissionRequest BuildPrepareSubmissionRequest(
        RuntimeCommands.CommandsSubmission submission,
        bool estimateTrafficCost)
    {
        var commands = _commandBuilder.BuildCommands(submission);
        var request = new Interactive.PrepareSubmissionRequest
        {
            UserId = commands.UserId,
            CommandId = commands.CommandId,
            SynchronizerId = commands.SynchronizerId,
        };

        if (submission.MinLedgerTime is { } minLedgerTime)
        {
            request.MinLedgerTime = ToWireMinLedgerTime(minLedgerTime);
        }

        if (estimateTrafficCost)
        {
            request.EstimateTrafficCost = new Interactive.CostEstimationHints();
        }

        request.ActAs.AddRange(commands.ActAs);
        request.ReadAs.AddRange(commands.ReadAs);
        request.Commands.AddRange(commands.Commands_);
        request.DisclosedContracts.AddRange(commands.DisclosedContracts);

        return request;
    }
}
