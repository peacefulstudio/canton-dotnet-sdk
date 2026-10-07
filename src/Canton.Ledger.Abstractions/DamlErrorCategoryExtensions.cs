// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using Daml.Runtime.Outcomes;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// Canonical transport-status mapping for <see cref="DamlErrorCategory"/>.
/// Each Canton error category prescribes the gRPC status the participant returns it
/// under; this class ships the HTTP side of that table (the standard
/// <c>google.rpc.Code</c>-to-HTTP mapping applied to the category's gRPC status), so
/// services that re-expose ledger errors over HTTP no longer hand-maintain the table.
/// The gRPC side lives in <c>Canton.Ledger.Grpc.Client</c> as
/// <c>ToGrpcStatusCode()</c>, keeping this package free of transport dependencies.
/// </summary>
public static class DamlErrorCategoryExtensions
{
    /// <summary>
    /// Maps the category to the HTTP status code a JSON-facing service should return
    /// for it. <see cref="DamlErrorCategory.Unknown"/> maps to
    /// <see cref="HttpStatusCode.InternalServerError"/>;
    /// <see cref="DamlErrorCategory.MaliciousOrFaultyBehaviour"/> maps to
    /// <see cref="HttpStatusCode.BadRequest"/> (Canton reports a security alert as gRPC
    /// <c>INVALID_ARGUMENT</c> with the details redacted, which its JSON API serves as 400);
    /// <see cref="DamlErrorCategory.BackgroundProcessDegradationWarning"/> is a
    /// log-only category never expected on a request path and also maps to
    /// <see cref="HttpStatusCode.InternalServerError"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is not a defined <see cref="DamlErrorCategory"/>.
    /// </exception>
    public static HttpStatusCode ToHttpStatusCode(this DamlErrorCategory category) =>
        category switch
        {
            DamlErrorCategory.Unknown => HttpStatusCode.InternalServerError,
            DamlErrorCategory.TransientServerFailure => HttpStatusCode.ServiceUnavailable,
            DamlErrorCategory.ContentionOnSharedResources => HttpStatusCode.Conflict,
            DamlErrorCategory.DeadlineExceededRequestStateUnknown => HttpStatusCode.GatewayTimeout,
            DamlErrorCategory.SystemInternalAssumptionViolated => HttpStatusCode.InternalServerError,
            DamlErrorCategory.MaliciousOrFaultyBehaviour => HttpStatusCode.BadRequest,
            DamlErrorCategory.AuthInterceptorInvalidAuthenticationCredentials => HttpStatusCode.Unauthorized,
            DamlErrorCategory.AuthorizationChecksFailed => HttpStatusCode.Forbidden,
            DamlErrorCategory.InvalidIndependentOfSystemState => HttpStatusCode.BadRequest,
            DamlErrorCategory.InvalidGivenCurrentSystemStateOther => HttpStatusCode.BadRequest,
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists => HttpStatusCode.Conflict,
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing => HttpStatusCode.NotFound,
            DamlErrorCategory.InvalidGivenCurrentSystemStateSeekAfterEnd => HttpStatusCode.BadRequest,
            DamlErrorCategory.BackgroundProcessDegradationWarning => HttpStatusCode.InternalServerError,
            DamlErrorCategory.InternalUnsupportedOperation => HttpStatusCode.NotImplemented,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };
}
