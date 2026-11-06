// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Outcomes;

/// <summary>
/// Closed set of Canton's documented gRPC error categories.
/// Mirrors the categories listed in the
/// <see href="https://docs.canton.network/appdev/reference/error-codes">Canton error-codes reference</see>.
///
/// Canton transports the numeric category id (for example <c>"8"</c>) in
/// <c>ErrorInfo.metadata["category"]</c>; values that don't match any known category map to
/// <see cref="Unknown"/>.
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
    /// Security alert — detected malicious or faulty behaviour from a participant
    /// (<c>SecurityAlert</c>). Canton redacts most security alerts, so they reach the client as a
    /// bare <c>INVALID_ARGUMENT</c> with no category and classify as <see cref="Unknown"/>; only the
    /// unredacted variant (<c>UnredactedSecurityAlert</c>, which shares id 5) arrives as category 5.
    /// </summary>
    SecurityAlert,

    /// <summary>
    /// Authentication interceptor rejected the credentials
    /// (<c>AuthInterceptorInvalidAuthenticationCredentials</c>).
    /// </summary>
    AuthInterceptorInvalidAuthenticationCredentials,

    /// <summary>
    /// Authorization checks failed for the requested operation
    /// (<c>InsufficientPermission</c>). Canton redacts it as well; the clients recover it from gRPC
    /// <c>PermissionDenied</c> or HTTP 403.
    /// </summary>
    InsufficientPermission,

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
