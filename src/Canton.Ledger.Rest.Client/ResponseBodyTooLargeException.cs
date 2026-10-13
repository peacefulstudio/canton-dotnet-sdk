// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Rest.Client;

internal sealed class ResponseBodyTooLargeException(long maxResponseBodyBytes)
    : Exception(
        $"The response body is larger than the configured {nameof(RestLedgerClientOptions.MaxResponseBodyBytes)} "
        + $"of {maxResponseBodyBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)} bytes.");
