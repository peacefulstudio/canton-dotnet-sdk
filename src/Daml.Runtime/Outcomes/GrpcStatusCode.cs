// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Outcomes;

/// <summary>
/// The gRPC status codes, mirrored member for member and value for value from
/// <c>Grpc.Core.StatusCode</c> so <see cref="TransportStatus.Grpc"/> can carry one without
/// <c>Daml.Runtime</c> taking a gRPC dependency. A gRPC consumer converts with a plain cast in
/// either direction.
/// </summary>
public enum GrpcStatusCode
{
    /// <summary>Not an error; returned on success.</summary>
    OK = 0,

    /// <summary>The operation was cancelled, typically by the caller.</summary>
    Cancelled = 1,

    /// <summary>Unknown error.</summary>
    Unknown = 2,

    /// <summary>The client specified an invalid argument.</summary>
    InvalidArgument = 3,

    /// <summary>The deadline expired before the operation could complete.</summary>
    DeadlineExceeded = 4,

    /// <summary>Some requested entity was not found.</summary>
    NotFound = 5,

    /// <summary>The entity a client attempted to create already exists.</summary>
    AlreadyExists = 6,

    /// <summary>The caller does not have permission to execute the operation.</summary>
    PermissionDenied = 7,

    /// <summary>Some resource has been exhausted.</summary>
    ResourceExhausted = 8,

    /// <summary>The system is not in a state required for the operation's execution.</summary>
    FailedPrecondition = 9,

    /// <summary>The operation was aborted, typically due to a concurrency issue.</summary>
    Aborted = 10,

    /// <summary>The operation was attempted past the valid range.</summary>
    OutOfRange = 11,

    /// <summary>The operation is not implemented or not supported.</summary>
    Unimplemented = 12,

    /// <summary>Internal error.</summary>
    Internal = 13,

    /// <summary>The service is currently unavailable.</summary>
    Unavailable = 14,

    /// <summary>Unrecoverable data loss or corruption.</summary>
    DataLoss = 15,

    /// <summary>The request does not have valid authentication credentials.</summary>
    Unauthenticated = 16,
}
