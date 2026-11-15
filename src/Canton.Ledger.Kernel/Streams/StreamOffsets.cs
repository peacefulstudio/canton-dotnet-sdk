// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;

namespace Canton.Ledger.Kernel.Streams;

/// <summary>
/// The rules that decide the offset a stream event is reported at, shared by both transports so a
/// rule cannot drift between them. Only a transport's unset value counts as absent; a malformed
/// offset is never read as unset.
/// </summary>
internal static class StreamOffsets
{
    public static long EventOrUpdate(long? eventOffset, long updateOffset) =>
        eventOffset is null or 0 ? updateOffset : eventOffset.Value;

    public static bool TryRequired(WireOffset offset, out long value)
    {
        value = offset.Value;
        return !offset.IsMalformed;
    }

    public static bool TryAcsEntryResume(WireOffset unassignment, long snapshotResume, out long resume)
    {
        resume = unassignment.IsAbsent ? snapshotResume : unassignment.Value;
        return !unassignment.IsMalformed;
    }

    public static bool TryAcsCreatedResume(WireOffset created, long entryResume, out long resume)
    {
        resume = created.IsAbsent || created.IsMalformed ? entryResume : created.Value;
        return !created.IsMalformed;
    }

    public static LedgerOffset? AcsRefusalOffset(WireOffset unassignment, LedgerOffset? snapshotOffset) =>
        unassignment.IsAbsent || unassignment.IsMalformed ? snapshotOffset : LedgerOffset.At(unassignment.Value);
}
