// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;
using WireCommand = Canton.Ledger.Rest.Client.Raw.Command;
using WireCostEstimation = Canton.Ledger.Rest.Client.Raw.CostEstimation;
using WireDeduplicationPeriod = Canton.Ledger.Rest.Client.Raw.DeduplicationPeriod;
using WireDisclosedContract = Canton.Ledger.Rest.Client.Raw.DisclosedContract;
using WireTransactionFormat = Canton.Ledger.Rest.Client.Raw.TransactionFormat;

namespace Canton.Ledger.Rest.Client;

internal sealed record ServedPrepareSubmissionRequest(
    [property: JsonPropertyName("userId")] string? UserId,
    [property: JsonPropertyName("commandId")] string CommandId,
    [property: JsonPropertyName("commands")] ICollection<WireCommand> Commands,
    [property: JsonPropertyName("minLedgerTime")] ServedMinLedgerTime? MinLedgerTime,
    [property: JsonPropertyName("actAs")] ICollection<string> ActAs,
    [property: JsonPropertyName("readAs")] ICollection<string> ReadAs,
    [property: JsonPropertyName("disclosedContracts")] ICollection<WireDisclosedContract>? DisclosedContracts,
    [property: JsonPropertyName("synchronizerId")] string SynchronizerId,
    [property: JsonPropertyName("packageIdSelectionPreference")] ICollection<string> PackageIdSelectionPreference);

internal sealed record ServedPrepareSubmissionResponse(
    [property: JsonPropertyName("preparedTransaction")] string? PreparedTransaction,
    [property: JsonPropertyName("preparedTransactionHash")] string? PreparedTransactionHash,
    [property: JsonPropertyName("hashingSchemeVersion")] string? HashingSchemeVersion,
    [property: JsonPropertyName("hashingDetails")] string? HashingDetails,
    [property: JsonPropertyName("costEstimation")] WireCostEstimation? CostEstimation);

internal sealed record ServedExecuteSubmissionRequest(
    [property: JsonPropertyName("preparedTransaction")] string PreparedTransaction,
    [property: JsonPropertyName("partySignatures")] ServedPartySignatures PartySignatures,
    [property: JsonPropertyName("deduplicationPeriod")] WireDeduplicationPeriod DeduplicationPeriod,
    [property: JsonPropertyName("submissionId")] string SubmissionId,
    [property: JsonPropertyName("userId")] string? UserId,
    [property: JsonPropertyName("hashingSchemeVersion")] string HashingSchemeVersion,
    [property: JsonPropertyName("minLedgerTime")] ServedMinLedgerTime? MinLedgerTime,
    [property: JsonPropertyName("transactionFormat")] WireTransactionFormat? TransactionFormat);

internal sealed record ServedPartySignatures(
    [property: JsonPropertyName("signatures")] IReadOnlyList<ServedSinglePartySignatures> Signatures);

internal sealed record ServedSinglePartySignatures(
    [property: JsonPropertyName("party")] string Party,
    [property: JsonPropertyName("signatures")] IReadOnlyList<ServedSignature> Signatures);

internal sealed record ServedSignature(
    [property: JsonPropertyName("format")] string Format,
    [property: JsonPropertyName("signature")] string Signature,
    [property: JsonPropertyName("signedBy")] string SignedBy,
    [property: JsonPropertyName("signingAlgorithmSpec")] string SigningAlgorithmSpec);

internal sealed record ServedSigningPublicKey(
    [property: JsonPropertyName("format")] string Format,
    [property: JsonPropertyName("keyData")] string KeyData,
    [property: JsonPropertyName("keySpec")] string KeySpec);

internal sealed record ServedMinLedgerTime(
    [property: JsonPropertyName("time")] ServedMinLedgerTimeArm Time);

internal sealed record ServedMinLedgerTimeArm(
    [property: JsonPropertyName("MinLedgerTimeAbs")] ServedValue<DateTimeOffset>? Absolute,
    [property: JsonPropertyName("MinLedgerTimeRel")] ServedValue<ServedDuration>? Relative);

internal sealed record ServedValue<T>([property: JsonPropertyName("value")] T Value);

internal sealed record ServedDuration(
    [property: JsonPropertyName("seconds")] long Seconds,
    [property: JsonPropertyName("nanos")] int Nanos);

internal sealed record ServedGenerateExternalPartyTopologyRequest(
    [property: JsonPropertyName("synchronizer")] string Synchronizer,
    [property: JsonPropertyName("partyHint")] string PartyHint,
    [property: JsonPropertyName("publicKey")] ServedSigningPublicKey PublicKey,
    [property: JsonPropertyName("localParticipantObservationOnly")] bool? LocalParticipantObservationOnly,
    [property: JsonPropertyName("otherConfirmingParticipantUids")] IReadOnlyList<string>? OtherConfirmingParticipantUids,
    [property: JsonPropertyName("confirmationThreshold")] int? ConfirmationThreshold,
    [property: JsonPropertyName("observingParticipantUids")] IReadOnlyList<string>? ObservingParticipantUids);

internal sealed record ServedAllocateExternalPartyRequest(
    [property: JsonPropertyName("synchronizer")] string Synchronizer,
    [property: JsonPropertyName("onboardingTransactions")] IReadOnlyList<ServedSignedTransaction> OnboardingTransactions,
    [property: JsonPropertyName("multiHashSignatures")] IReadOnlyList<ServedSignature>? MultiHashSignatures,
    [property: JsonPropertyName("identityProviderId")] string? IdentityProviderId,
    [property: JsonPropertyName("waitForAllocation")] bool? WaitForAllocation,
    [property: JsonPropertyName("userId")] string? UserId);

internal sealed record ServedSignedTransaction(
    [property: JsonPropertyName("transaction")] string Transaction,
    [property: JsonPropertyName("signatures")] IReadOnlyList<ServedSignature>? Signatures);
