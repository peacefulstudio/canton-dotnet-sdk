// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Outcomes;

/// <summary>
/// Closed set of Canton's documented gRPC error categories.
/// Mirrors the categories listed in the Canton error-codes reference —
/// search for "error categories" on https://docs.digitalasset.com.
///
/// Canton transports the category as a string in <c>ErrorInfo.metadata["category"]</c>;
/// values that don't match any known category map to <see cref="Unknown"/>.
/// </summary>
public enum DamlErrorCategory
{
    /// <summary>
    /// Trailers were missing or unparseable, or the value in
    /// <c>ErrorInfo.metadata["category"]</c> didn't match any known category.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Transient failure — retrying may succeed (<c>TransientServerFailure</c>).
    /// </summary>
    TransientServerFailure,

    /// <summary>
    /// Contention on a shared resource — retry with backoff (<c>ContentionOnSharedResources</c>).
    /// </summary>
    ContentionOnSharedResources,

    /// <summary>
    /// Deadline exceeded; outcome of the request is unknown
    /// (<c>DeadlineExceededRequestStateUnknown</c>).
    /// </summary>
    DeadlineExceededRequestStateUnknown,

    /// <summary>
    /// Canton internal invariant violated — operator action required
    /// (<c>SystemInternalAssumptionViolated</c>).
    /// </summary>
    SystemInternalAssumptionViolated,

    /// <summary>
    /// Detected malicious or faulty behaviour from a participant
    /// (<c>MaliciousOrFaultyBehaviour</c>).
    /// </summary>
    MaliciousOrFaultyBehaviour,

    /// <summary>
    /// Authentication interceptor rejected the credentials
    /// (<c>AuthInterceptorInvalidAuthenticationCredentials</c>).
    /// </summary>
    AuthInterceptorInvalidAuthenticationCredentials,

    /// <summary>
    /// Authorization checks failed for the requested operation
    /// (<c>AuthorizationChecksFailed</c>).
    /// </summary>
    AuthorizationChecksFailed,

    /// <summary>
    /// Request is invalid independent of system state — usually a client bug
    /// (<c>InvalidIndependentOfSystemState</c>).
    /// </summary>
    InvalidIndependentOfSystemState,

    /// <summary>
    /// Request is invalid given the current system state, other reason
    /// (<c>InvalidGivenCurrentSystemStateOther</c>).
    /// </summary>
    InvalidGivenCurrentSystemStateOther,

    /// <summary>
    /// Request invalid because the resource already exists
    /// (<c>InvalidGivenCurrentSystemStateResourceExists</c>).
    /// </summary>
    InvalidGivenCurrentSystemStateResourceExists,

    /// <summary>
    /// Request invalid because the resource is missing
    /// (<c>InvalidGivenCurrentSystemStateResourceMissing</c>).
    /// </summary>
    InvalidGivenCurrentSystemStateResourceMissing,

    /// <summary>
    /// Request invalid because a resource was requested with a parameter beyond the current upper
    /// bound of the system's state, such as an offset past the ledger end. Canton names this
    /// category <c>InvalidGivenCurrentSystemStateSeekAfterEnd</c> and returns it under gRPC
    /// <c>OUT_OF_RANGE</c>.
    /// </summary>
    InvalidGivenCurrentSystemStateSeekAfterEnd,

    /// <summary>
    /// Background process degradation warning — non-fatal
    /// (<c>BackgroundProcessDegradationWarning</c>).
    /// </summary>
    BackgroundProcessDegradationWarning,

    /// <summary>
    /// The requested operation is not supported by the current configuration
    /// (<c>InternalUnsupportedOperation</c>).
    /// </summary>
    InternalUnsupportedOperation,
}
