// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Abstractions;

/// <summary>
/// The status of a package on the participant, as returned by
/// <c>IAdminClient.GetPackageStatusAsync</c>.
/// </summary>
public enum PackageStatus
{
    /// <summary>The participant did not specify a status.</summary>
    Unspecified,

    /// <summary>The package is registered on the participant.</summary>
    Registered,

    /// <summary>A status reported by the participant that this SDK version does not recognise.</summary>
    Unrecognized,
}
