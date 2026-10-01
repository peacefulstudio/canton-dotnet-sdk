// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json.Nodes;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using RuntimeCommands = Daml.Runtime.Commands;
using WireCostEstimationHints = Canton.Ledger.Rest.Client.Raw.CostEstimationHints;

namespace Canton.Ledger.Rest.Client;

internal sealed partial class RestLedgerClient
{
    private const string ExecuteSubmissionPath = "/v2/interactive-submission/execute";
    private const string ExecuteSubmissionAndWaitPath = "/v2/interactive-submission/executeAndWait";
    private const string ExecuteSubmissionAndWaitForTransactionPath =
        "/v2/interactive-submission/executeAndWaitForTransaction";
    private const string PreferredPackagesPath = "/v2/interactive-submission/preferred-packages";
    private const string PreferredPackageVersionPath = "/v2/interactive-submission/preferred-package-version";

    /// <inheritdoc />
    /// <remarks>
    /// Sent as <c>POST /v2/interactive-submission/prepare</c>. The participant serves the prepared
    /// transaction as the base64 of its serialized protobuf bytes, which are handed back unchanged.
    /// A hashing scheme version this SDK version does not name fails the call rather than being
    /// guessed, because the JSON Ledger API carries it by name only.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="submission"/> is <see langword="null"/>.</exception>
    public Task<PreparedSubmission> PrepareSubmissionAsync(
        RuntimeCommands.CommandsSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        return _calls.SendAsync<ServedPrepareSubmissionResponse, PreparedSubmission>(
            new RestCall(
                HttpMethod.Post, PrepareSubmissionPath, BuildPrepareSubmissionRequest(submission, estimateTrafficCost: null),
                MissingBody("prepared submission"), MalformedBody("prepared submission")),
            ProjectPreparedSubmission,
            timeout,
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>Sent as <c>POST /v2/interactive-submission/execute</c>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="submission"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// A signature or hashing scheme version carries a value this SDK version has no JSON Ledger API name for.
    /// </exception>
    public Task ExecuteSubmissionAsync(
        SignedSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        return _calls.SendAsync(
            Execute(ExecuteSubmissionPath, BuildExecuteSubmissionRequest(submission, transactionFormat: null), "executed submission"),
            IgnoreBodyAsync,
            timeout,
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>Sent as <c>POST /v2/interactive-submission/executeAndWait</c>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="submission"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// A signature or hashing scheme version carries a value this SDK version has no JSON Ledger API name for.
    /// </exception>
    public Task<ExecutedSubmission> ExecuteSubmissionAndWaitAsync(
        SignedSubmission submission,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        return _calls.SendAsync<Raw.ExecuteSubmissionAndWaitResponse, ExecutedSubmission>(
            Execute(
                ExecuteSubmissionAndWaitPath,
                BuildExecuteSubmissionRequest(submission, transactionFormat: null),
                "executed submission"),
            ProjectExecutedSubmission,
            timeout,
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Sent as <c>POST /v2/interactive-submission/executeAndWaitForTransaction</c>, asking for the
    /// ledger-effects shape filtered to <paramref name="submitter"/>'s parties, as
    /// <see cref="GetUpdateByIdAsync"/> does.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="submission"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// A signature or hashing scheme version carries a value this SDK version has no JSON Ledger API name for.
    /// </exception>
    public Task<TransactionResult> ExecuteSubmissionAndWaitForTransactionAsync(
        SignedSubmission submission,
        RuntimeCommands.SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        return _calls.SendAsync<Raw.ExecuteSubmissionAndWaitForTransactionResponse, TransactionResult>(
            Execute(
                ExecuteSubmissionAndWaitForTransactionPath,
                BuildExecuteSubmissionRequest(submission, RestSubscribeRequestBuilder.BuildTransactionFormat(submitter)),
                "executed transaction"),
            body => RestTransactionResultProjector.Project(
                body.Transaction ?? throw MalformedResponse.MissingRequiredField("The response carries no transaction")),
            timeout,
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>Sent as <c>POST /v2/interactive-submission/preferred-packages</c>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="requirements"/> is <see langword="null"/>.</exception>
    public Task<PreferredPackages> GetPreferredPackagesAsync(
        IEnumerable<PackageVettingRequirement> requirements,
        SynchronizerId? synchronizerId = null,
        DateTimeOffset? vettingValidAt = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        var request = new Raw.GetPreferredPackagesRequest
        {
            PackageVettingRequirements =
            [
                .. requirements.Select(requirement => new Raw.PackageVettingRequirement
                {
                    Parties = [.. requirement.Parties.Select(party => party.Value)],
                    PackageName = requirement.PackageName,
                }),
            ],
            VettingValidAt = vettingValidAt,
        };
        if (synchronizerId is { } synchronizer)
        {
            request.SynchronizerId = synchronizer.Value;
        }

        return _calls.SendAsync<Raw.GetPreferredPackagesResponse, PreferredPackages>(
            new RestCall(
                HttpMethod.Post, PreferredPackagesPath, request,
                MissingBody("preferred packages"), MalformedBody("preferred packages")),
            body => new PreferredPackages(
                [.. (body.PackageReferences ?? []).Select(ToPackageReference)],
                ToSynchronizerId(body.SynchronizerId)),
            timeout,
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Sent as <c>GET /v2/interactive-submission/preferred-package-version</c> with the query
    /// parameter names the participant reads — <c>package-name</c>, <c>synchronizer-id</c> and
    /// <c>vetting_valid_at</c> — rather than the ones its specification derives, which it rejects or
    /// silently discards.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="parties"/> or <paramref name="packageName"/> is <see langword="null"/>.
    /// </exception>
    public Task<PackagePreference?> GetPreferredPackageVersionAsync(
        IEnumerable<Party> parties,
        string packageName,
        SynchronizerId? synchronizerId = null,
        DateTimeOffset? vettingValidAt = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parties);
        ArgumentNullException.ThrowIfNull(packageName);

        return _calls.SendAsync<Raw.GetPreferredPackageVersionResponse, PackagePreference?>(
            new RestCall(
                HttpMethod.Get, PreferredPackageVersionQuery(parties, packageName, synchronizerId, vettingValidAt),
                Body: null, MissingBody("preferred package version"), MalformedBody("preferred package version")),
            body => body.PackagePreference is { } preference
                ? new PackagePreference(
                    ToPackageReference(preference.PackageReference
                        ?? throw MalformedResponse.MissingRequiredField("The package preference carries no package reference")),
                    ToSynchronizerId(preference.SynchronizerId))
                : null,
            timeout,
            cancellationToken);
    }

    private ServedPrepareSubmissionRequest BuildPrepareSubmissionRequest(
        RuntimeCommands.CommandsSubmission submission,
        WireCostEstimationHints? estimateTrafficCost)
    {
        var commands = RestCommandBuilder.BuildCommands(submission, _userId);
        return new ServedPrepareSubmissionRequest(
            commands.UserId,
            commands.CommandId,
            commands.CommandList,
            submission.MinLedgerTime is { } bound ? RestExternalSigningConversions.ToServed(bound) : null,
            commands.ActAs,
            commands.ReadAs,
            commands.DisclosedContracts is { Count: > 0 } disclosedContracts ? disclosedContracts : null,
            commands.SynchronizerId ?? string.Empty,
            [],
            estimateTrafficCost);
    }

    private ServedExecuteSubmissionRequest BuildExecuteSubmissionRequest(
        SignedSubmission submission,
        Raw.TransactionFormat? transactionFormat) =>
        new(
            Convert.ToBase64String(submission.Prepared.PreparedTransaction.Span),
            RestExternalSigningConversions.ToServed(submission.PartySignatures, nameof(submission)),
            RestCommandBuilder.ToWireDeduplicationPeriod(submission.DeduplicationPeriod) ?? UnsetDeduplicationPeriod(),
            submission.SubmissionId,
            _userId,
            ServedEnums.HashingSchemeVersions.NameOf(submission.Prepared.HashingSchemeVersion, nameof(submission)),
            submission.MinLedgerTime is { } bound ? RestExternalSigningConversions.ToServed(bound) : null,
            transactionFormat);

    private static PreparedSubmission ProjectPreparedSubmission(ServedPrepareSubmissionResponse body) =>
        new(
            RestExternalSigningConversions.FromBase64(body.PreparedTransaction, "preparedTransaction"),
            RestExternalSigningConversions.FromBase64(body.PreparedTransactionHash, "preparedTransactionHash"),
            ServedEnums.HashingSchemeVersions.ValueOf(body.HashingSchemeVersion),
            string.IsNullOrEmpty(body.HashingDetails) ? null : body.HashingDetails,
            ProjectTrafficCostEstimate(body.CostEstimation));

    private static Raw.DeduplicationPeriod UnsetDeduplicationPeriod() =>
        new() { AdditionalProperties = { ["Empty"] = new JsonObject() } };

    private static ExecutedSubmission ProjectExecutedSubmission(Raw.ExecuteSubmissionAndWaitResponse body) =>
        RestWireConversions.TryParseOffset(body.CompletionOffset, out var completionOffset)
            ? new ExecutedSubmission(
                body.UpdateId ?? throw MalformedResponse.MissingRequiredField("The response carries no updateId"),
                LedgerOffset.At(completionOffset))
            : throw MalformedResponse.WithDetail(
                $"The completion offset '{body.CompletionOffset}' is missing or not a non-negative integer.");

    private static PackageReference ToPackageReference(Raw.PackageReference reference) =>
        new(reference.PackageId, reference.PackageName, reference.PackageVersion);

    private static SynchronizerId ToSynchronizerId(string? synchronizerId) =>
        string.IsNullOrEmpty(synchronizerId)
            ? throw MalformedResponse.MissingRequiredField("The response carries no synchronizerId")
            : new SynchronizerId(synchronizerId);

    private static string PreferredPackageVersionQuery(
        IEnumerable<Party> parties,
        string packageName,
        SynchronizerId? synchronizerId,
        DateTimeOffset? vettingValidAt)
    {
        var query = parties.Select(party => $"parties={Uri.EscapeDataString(party.Value)}").ToList();
        query.Add($"package-name={Uri.EscapeDataString(packageName)}");
        if (synchronizerId is { } synchronizer)
        {
            query.Add($"synchronizer-id={Uri.EscapeDataString(synchronizer.Value)}");
        }
        if (vettingValidAt is { } validAt)
        {
            query.Add($"vetting_valid_at={Uri.EscapeDataString(validAt.ToString("O", CultureInfo.InvariantCulture))}");
        }

        return $"{PreferredPackageVersionPath}?{string.Join('&', query)}";
    }

    private static RestCall Execute(string path, ServedExecuteSubmissionRequest body, string subject) =>
        new(HttpMethod.Post, path, body, MissingBody(subject), MalformedBody(subject), Replayable: false);

    private static string MissingBody(string subject) =>
        $"Server returned a successful response but no body was present for the {subject}.";

    private static string MalformedBody(string subject) =>
        $"Server returned a malformed {subject} response body: ";

    private static Task<bool> IgnoreBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        Task.FromResult(true);
}
