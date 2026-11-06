// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Rest.Client.Raw;
using Refit;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Integration.Tests;

public static partial class LedgerApiVersionSkewGuard
{
    internal static async Task AssertConformableAsync(
        IVersionServiceApi versionApi,
        CancellationToken cancellationToken)
    {
        string? reportedVersion;
        try
        {
            reportedVersion = (await versionApi.GetLedgerApiVersion(cancellationToken)).Version;
        }
        catch (ApiException failure)
        {
            Assert.Fail(VersionProbeFailureMessage(failure.StatusCode, failure.Content));
            return;
        }

        switch (Classify(reportedVersion))
        {
            case Verdict.Supported:
                return;
            case Verdict.Unsupported:
                Assert.Fail(UnsupportedVersionFailureMessage(reportedVersion));
                return;
            default:
                Assert.Fail(UnparseableVersionFailureMessage(reportedVersion));
                return;
        }
    }
}
