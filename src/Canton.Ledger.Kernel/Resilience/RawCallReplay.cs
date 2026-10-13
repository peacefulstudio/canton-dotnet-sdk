// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Kernel.Resilience;

/// <summary>
/// Opts raw calls into the opt-in retry pipeline. A raw call is one made through the gRPC
/// <c>CreateCallInvoker()</c> escape hatch or the JSON Ledger API raw tier
/// (<c>AddRestLedgerRawApis</c>). The SDK cannot tell whether such a call is idempotent, so it is
/// sent exactly once even when <c>Retry.Enabled</c> is set; a transient failure surfaces to the
/// caller. A caller that knows the call is safe to repeat wraps it in <see cref="Allow"/>.
/// </summary>
/// <remarks>
/// The scope flows with the logical call context, so it covers the calls started inside it, and
/// the call must be started before the scope is disposed (await it inside the <c>using</c>).
/// Typed client methods are unaffected: each decides for itself whether it can be replayed.
/// </remarks>
public static class RawCallReplay
{
    private static readonly AsyncLocal<bool> Allowed = new();

    /// <summary>
    /// Lets raw calls started inside the returned scope be re-sent after a transient failure when
    /// <c>Retry.Enabled</c> is set. Dispose the scope to restore the previous setting.
    /// </summary>
    /// <returns>The scope; dispose it when the idempotent calls are done.</returns>
    public static IDisposable Allow()
    {
        var previous = Allowed.Value;
        Allowed.Value = true;
        return new Scope(previous);
    }

    internal static bool IsAllowed => Allowed.Value;

    private sealed class Scope(bool previous) : IDisposable
    {
        public void Dispose() => Allowed.Value = previous;
    }
}
