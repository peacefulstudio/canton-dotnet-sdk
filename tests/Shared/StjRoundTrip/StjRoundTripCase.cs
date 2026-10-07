// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Testing.StjRoundTrip;

/// <summary>
/// The <see cref="System.Text.Json"/> options a sample is round-tripped under: the default
/// options, which are the contract, and the posture a host that builds its own options gets
/// from <c>AddDamlConverters()</c>.
/// </summary>
internal enum StjOptionsLeg
{
    Default,
    AddDamlConverters,
}

/// <summary>
/// One sample round-tripped as <paramref name="DeclaredType"/>, under a deterministic and unique
/// <paramref name="Id"/>.
/// </summary>
internal sealed record StjRoundTripCase(string Id, Type DeclaredType, object Sample);

/// <summary>
/// Raised when a case does not read back as itself, or when a known-broken case does.
/// </summary>
internal sealed class StjRoundTripFailure : Exception
{
    public StjRoundTripFailure(string message)
        : base(message)
    {
    }

    public StjRoundTripFailure(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
