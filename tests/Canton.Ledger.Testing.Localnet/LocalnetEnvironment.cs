// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Testing.Localnet;

/// <summary>
/// The names of the environment variables that carry the a-validator-1 LocalNet endpoints, and the
/// default participant address, so a rename of that contract is one edit.
/// </summary>
internal static class LocalnetEnvironment
{
    /// <summary>The participant's gRPC Ledger API address.</summary>
    internal const string GrpcUrl = "CANTON_LOCALNET_A_VALIDATOR_1_GRPC_URL";

    /// <summary>The gRPC Ledger API address LocalNet publishes when <see cref="GrpcUrl"/> is unset.</summary>
    internal const string DefaultGrpcUrl = "http://localhost:11901";

    /// <summary>The PQS PostgreSQL connection string.</summary>
    internal const string PqsConnectionString = "CANTON_LOCALNET_A_VALIDATOR_1_PQS_CONNECTION_STRING";

    /// <summary>The Splice validator admin API base URL.</summary>
    internal const string ValidatorApiUrl = "CANTON_LOCALNET_A_VALIDATOR_1_VALIDATOR_API_URL";
}
