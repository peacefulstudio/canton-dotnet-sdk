// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Testing.Localnet;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class CapabilityLane<TCapability>(
    TCapability capability, Func<ValueTask> disposeAsync, string? rightsGatedUserId = null)
    : IAsyncDisposable
{
    public TCapability Capability { get; } = capability;

    /// <summary>
    /// Holds the shared side of <see cref="LedgerUserRightsGate.Shared"/> for the Ledger user this lane
    /// streams as, so no rights change on that user lands while the stream is open. A lane without a
    /// participant has no user to hold and answers <see langword="null"/>.
    /// </summary>
    public ValueTask<StreamHold?> HoldStreamAsync(CancellationToken cancellationToken)
    {
        if (rightsGatedUserId is null)
        {
            return ValueTask.FromResult<StreamHold?>(null);
        }

        var admission = LedgerUserRightsGate.Shared.HoldStreamAsync(rightsGatedUserId, cancellationToken);
        return admission.IsCompletedSuccessfully
            ? ValueTask.FromResult<StreamHold?>(admission.Result)
            : AwaitAdmissionAsync(admission);
    }

    public ValueTask DisposeAsync() => disposeAsync();

    private static async ValueTask<StreamHold?> AwaitAdmissionAsync(ValueTask<StreamHold> admission) =>
        await admission.ConfigureAwait(false);
}
