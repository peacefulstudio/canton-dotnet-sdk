// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;

namespace Canton.Ledger.Testing;

public sealed partial class FakeAdminClient
{
    private readonly object _timeGate = new();
    private DateTimeOffset? _currentTime;

    internal FakePackagesAndTime PackagesAndTime
    {
        get => _packagesAndTime;
        init
        {
            _packagesAndTime = value;
            _currentTime = value.Time;
        }
    }

    private readonly FakePackagesAndTime _packagesAndTime = new(null, new Dictionary<string, PackageStatus>(), null);

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListPackagesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(_packagesAndTime.PackageIds ?? throw StagingMissing(
            "package ids", "them", "WithPackageIds(...)"));

    /// <inheritdoc />
    public Task<PackageStatus> GetPackageStatusAsync(string packageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        return Task.FromResult(_packagesAndTime.PackageStatuses.TryGetValue(packageId, out var status)
            ? status
            : throw StagingMissing(
                "package status", "one", $"WithPackageStatus(\"{packageId}\", ...)", $" for package id '{packageId}'"));
    }

    /// <inheritdoc />
    public Task<DateTimeOffset> GetTimeAsync(CancellationToken cancellationToken = default)
    {
        lock (_timeGate)
        {
            return Task.FromResult(_currentTime ?? throw StagingMissing("time", "it", "WithTime(...)"));
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Mirrors a static-time participant: the call succeeds only when <paramref name="currentTime"/>
    /// equals the staged time and <paramref name="newTime"/> is later; otherwise it throws
    /// <see cref="LedgerOperationException"/> and the time stays where it was.
    /// </remarks>
    public Task SetTimeAsync(DateTimeOffset currentTime, DateTimeOffset newTime, CancellationToken cancellationToken = default)
    {
        lock (_timeGate)
        {
            var staged = _currentTime ?? throw StagingMissing("time", "it", "WithTime(...)");
            if (staged != currentTime)
            {
                throw new LedgerOperationException(
                    $"SetTime rejected: the current time is {staged:O}, not the {currentTime:O} the caller expected.");
            }

            if (newTime <= staged)
            {
                throw new LedgerOperationException(
                    $"SetTime rejected: the new time {newTime:O} is not after the current time {staged:O}.");
            }

            _currentTime = newTime;
            return Task.CompletedTask;
        }
    }
}

internal sealed record FakePackagesAndTime(
    IReadOnlyList<string>? PackageIds,
    IReadOnlyDictionary<string, PackageStatus> PackageStatuses,
    DateTimeOffset? Time);
