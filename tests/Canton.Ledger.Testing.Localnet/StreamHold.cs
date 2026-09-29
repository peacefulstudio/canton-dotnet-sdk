// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Testing.Localnet;

/// <summary>
/// The shared side of a <see cref="LedgerUserRightsGate"/>, held for as long as one stream
/// authorized as its Ledger user is open.
/// </summary>
public sealed class StreamHold : IDisposable
{
    private readonly LedgerUserRightsGate _gate;
    private readonly string _userId;
    private readonly LedgerUserRightsGate.FlowStreams _flow;
    private int _released;

    internal StreamHold(LedgerUserRightsGate gate, string userId, LedgerUserRightsGate.FlowStreams flow)
    {
        _gate = gate;
        _userId = userId;
        _flow = flow;
    }

    /// <summary>
    /// Releases the shared side, admitting any rights change the stream held back. Dispose it once
    /// the stream has closed; a second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            _gate.CloseStream(_userId, _flow);
        }
    }
}
