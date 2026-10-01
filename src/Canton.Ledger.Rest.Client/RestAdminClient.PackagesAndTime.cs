// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Telemetry;
using Daml.Ledger.Abstractions;

namespace Canton.Ledger.Rest.Client;

internal sealed partial class RestAdminClient
{
    private const string TimeUnsupported =
        "The participant's TimeService is not available over the JSON Ledger API: it serves no route for GetTime or SetTime. Use the gRPC IAdminClient registered by AddAdminClient.";

    public Task<IReadOnlyList<string>> ListPackagesAsync(CancellationToken cancellationToken = default) =>
        TracedAsync(nameof(ListPackagesAsync), () => _calls.SendAsync<Raw.ListPackagesResponse, IReadOnlyList<string>>(
            Read(PackagesPath, "package ids"),
            response => (response.PackageIds ?? []).ToList(),
            timeout: null,
            cancellationToken));

    public Task<PackageStatus> GetPackageStatusAsync(string packageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        return TracedAsync(
            nameof(GetPackageStatusAsync),
            () => _calls.SendAsync<Raw.GetPackageStatusResponse, PackageStatus>(
                Read($"{PackagesPath}/{Uri.EscapeDataString(packageId)}/status", "package status"),
                response => response.PackageStatus switch
                {
                    null or Raw.GetPackageStatusResponsePackageStatus.PACKAGE_STATUS_UNSPECIFIED => PackageStatus.Unspecified,
                    Raw.GetPackageStatusResponsePackageStatus.PACKAGE_STATUS_REGISTERED => PackageStatus.Registered,
                    _ => PackageStatus.Unrecognized,
                },
                timeout: null,
                cancellationToken),
            (LedgerActivityTagNames.DamlPackageId, packageId));
    }

    public Task<DateTimeOffset> GetTimeAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<DateTimeOffset>(new NotSupportedException(TimeUnsupported));

    public Task SetTimeAsync(DateTimeOffset currentTime, DateTimeOffset newTime, CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException(TimeUnsupported));
}
