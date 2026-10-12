// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Globalization;
using Canton.Ledger.Kernel.Wire;

namespace Canton.Ledger.Grpc.Client;

internal sealed class WireEnumMap<TPublic, TWire>
    where TPublic : struct, Enum
    where TWire : struct, Enum
{
    private readonly string _wireEnum;
    private readonly FrozenDictionary<TPublic, TWire> _wireByPublic;
    private readonly FrozenDictionary<TWire, TPublic> _publicByWire;

    public WireEnumMap(string wireEnum, IReadOnlyDictionary<TPublic, TWire> wireByPublic)
    {
        _wireEnum = wireEnum;
        _wireByPublic = wireByPublic.ToFrozenDictionary();
        _publicByWire = wireByPublic.ToFrozenDictionary(entry => entry.Value, entry => entry.Key);
    }

    public TWire ToWire(TPublic value, string paramName) =>
        _wireByPublic.TryGetValue(value, out var wire)
            ? wire
            : throw new ArgumentException(
                $"{typeof(TPublic).Name} value {Convert.ToInt64(value, CultureInfo.InvariantCulture)} has no "
                + $"{_wireEnum} value this SDK version knows.",
                paramName);

    public TPublic FromWire(TWire wire) =>
        _publicByWire.TryGetValue(wire, out var value)
            ? value
            : throw MalformedResponse.WithDetail(
                $"The participant sent {_wireEnum} value {Convert.ToInt64(wire, CultureInfo.InvariantCulture)}, "
                + "which this SDK version does not know.");
}
