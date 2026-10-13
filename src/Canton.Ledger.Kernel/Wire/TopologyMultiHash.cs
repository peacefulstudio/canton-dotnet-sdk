// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Canton.Ledger.Kernel.Wire;

internal static class TopologyMultiHash
{
    private const int TopologyTransactionSignaturePurpose = 11;
    private const int MultiTopologyTransactionPurpose = 55;
    private static ReadOnlySpan<byte> Sha256MultihashPrefix => [0x12, 0x20];

    internal static byte[] TransactionHash(ReadOnlyMemory<byte> serializedTransaction) =>
        CantonSha256(TopologyTransactionSignaturePurpose, serializedTransaction.Span);

    internal static byte[] Of(IReadOnlyList<ReadOnlyMemory<byte>> serializedTransactions)
    {
        var sortedHashes = serializedTransactions
            .Select(TransactionHash)
            .Distinct(ByteSequenceComparer.Instance)
            .Order(ByteSequenceComparer.Instance)
            .ToList();

        using var combined = new MemoryStream();
        WriteInt32BigEndian(combined, sortedHashes.Count);
        foreach (var hash in sortedHashes)
        {
            WriteInt32BigEndian(combined, hash.Length);
            combined.Write(hash);
        }

        return CantonSha256(MultiTopologyTransactionPurpose, combined.ToArray());
    }

    private static byte[] CantonSha256(int purpose, ReadOnlySpan<byte> content)
    {
        var purposePrefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(purposePrefix, purpose);
        var digest = SHA256.HashData([.. purposePrefix, .. content]);
        return [.. Sha256MultihashPrefix, .. digest];
    }

    private static void WriteInt32BigEndian(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private sealed class ByteSequenceComparer : IComparer<byte[]>, IEqualityComparer<byte[]>
    {
        internal static readonly ByteSequenceComparer Instance = new();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);

        public bool Equals(byte[]? x, byte[]? y) => x.AsSpan().SequenceEqual(y);

        public int GetHashCode(byte[] obj) => Convert.ToHexString(obj).GetHashCode(StringComparison.Ordinal);
    }
}
