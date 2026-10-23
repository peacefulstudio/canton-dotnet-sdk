// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;

namespace Canton.Ledger.Pqs.Client;

/// <summary>
/// Configuration options for the PQS client.
/// </summary>
/// <remarks>
/// <para>
/// Payloads decode through the generated Daml-LF JSON reader, strictly. PQS documents Daml
/// <c>Numeric</c> and <c>Int64</c> as JSON strings by default, and
/// <c>--target-encoding-numericasstring</c> / <c>--target-encoding-int64asstring</c> change that
/// default; a bare JSON number where an <c>Int64</c> or <c>Numeric</c> is expected is refused.
/// </para>
/// <para>
/// PQS stores nullable fields as JSON nulls by default. A payload that omits the key of a Daml
/// <c>Optional</c> field, as <c>--target-encoding-excludenulls</c> does, decodes that field as
/// <c>None</c>; a payload that omits any other field's key fails to decode with an error naming the
/// field. A row over 16 MiB, 100,000 JSON nodes or nesting depth 128 fails to decode
/// like any other undecodable row.
/// </para>
/// </remarks>
public sealed class PqsClientOptions
{
    /// <summary>
    /// PostgreSQL connection string for the PQS database.
    /// </summary>
    [Required]
    public required string ConnectionString { get; set; }

    /// <summary>
    /// Whether the client's spans and logs carry what a failed query's exception says: the
    /// exception message in the status of the failed span, and the exception itself in the
    /// <c>PQS query failed</c> error log and in the unhealthy health check's log line and result. Off by
    /// default, so anyone with read access to the tracing backend, the logs or the health endpoint
    /// cannot read the contract ids, party ids or values that a PostgreSQL error message, its
    /// <c>Detail</c> or a decode failure can echo. Off, a failed span's status carries the exception
    /// type name, such as <c>PostgresException</c>, and every logged exception, including the one on the
    /// health check result, is recorded as a redaction wrapper whose <c>exception.type</c> is the
    /// wrapper, whose message is the original type's full name and whose stack is the original's.
    /// Alerting that keys on the original <c>exception.type</c> needs this option on.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>LedgerClientOptions.EmitPartyAndContractSpanTags</c> on the gRPC client. Opt in
    /// only where the tracing backend and the logs are trusted with the same data as the ledger
    /// and the PQS database themselves: the messages are exported verbatim.
    /// </remarks>
    public bool EmitPartyAndContractSpanTags { get; set; }
}
