// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;

namespace Canton.Ledger.Kernel.Streams;

/// <summary>
/// An offset as a transport read it off the wire, before any rule decides where an event sits:
/// absent, a real offset, or malformed. Both transports read the wire into this one shape so the
/// rules in <see cref="StreamOffsets"/> see the same three cases whatever the encoding.
/// </summary>
internal readonly record struct WireOffset
{
    private readonly long value;
    private readonly string? malformedText;

    private WireOffset(long value, string? malformedText)
    {
        this.value = value;
        this.malformedText = malformedText;
    }

    public bool IsMalformed => malformedText is not null;

    public bool IsAbsent => !IsMalformed && value == 0;

    public long Value => value;

    public static WireOffset Absent => default;

    public static WireOffset FromProto(long wireOffset) =>
        wireOffset < 0
            ? new WireOffset(0, wireOffset.ToString(CultureInfo.InvariantCulture))
            : new WireOffset(wireOffset, malformedText: null);

    public static WireOffset FromText(string? wireOffset) =>
        wireOffset is null
            ? Absent
            : long.TryParse(wireOffset, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                ? new WireOffset(parsed, malformedText: null)
                : new WireOffset(0, wireOffset);

    public static WireOffset FromRequiredText(string? wireOffset) =>
        wireOffset is null ? new WireOffset(0, string.Empty) : FromText(wireOffset);

    public MalformedResponseException ToMalformedResponse(string fieldDescription) =>
        MalformedResponse.WithDetail($"{fieldDescription} '{malformedText}' is not a valid ledger offset");
}
