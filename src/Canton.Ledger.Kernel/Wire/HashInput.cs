// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Canton.Ledger.Kernel.Wire;

internal sealed class HashInput
{
    private readonly ArrayBufferWriter<byte> _written = new();

    internal ReadOnlySpan<byte> Written => _written.WrittenSpan;

    internal byte[] Sha256() => SHA256.HashData(_written.WrittenSpan);

    internal HashInput Byte(byte value)
    {
        _written.GetSpan(1)[0] = value;
        _written.Advance(1);
        return this;
    }

    internal HashInput Bool(bool value) => Byte(value ? (byte)1 : (byte)0);

    internal HashInput Int(int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(_written.GetSpan(sizeof(int)), value);
        _written.Advance(sizeof(int));
        return this;
    }

    internal HashInput Long(long value)
    {
        BinaryPrimitives.WriteInt64BigEndian(_written.GetSpan(sizeof(long)), value);
        _written.Advance(sizeof(long));
        return this;
    }

    internal HashInput Raw(ReadOnlySpan<byte> bytes)
    {
        _written.Write(bytes);
        return this;
    }

    internal HashInput Bytes(ReadOnlySpan<byte> bytes) => Int(bytes.Length).Raw(bytes);

    internal HashInput String(string value) => Bytes(Encoding.UTF8.GetBytes(value));

    internal HashInput HexString(string value, string description)
    {
        try
        {
            return Bytes(Convert.FromHexString(value));
        }
        catch (FormatException notHex)
        {
            throw MalformedResponse.WithDetail(
                $"the prepared transaction carries {description} '{value}', which is not a hexadecimal string", notHex);
        }
    }

    internal HashInput Optional<T>(T? value, Action<HashInput, T> encode)
        where T : class
    {
        if (value is null) return Byte(0);
        Byte(1);
        encode(this, value);
        return this;
    }

    internal HashInput OptionalLong(bool present, long value) =>
        present ? Byte(1).Long(value) : Byte(0);

    internal HashInput Repeated<T>(IReadOnlyCollection<T> values, Action<HashInput, T> encode)
    {
        Int(values.Count);
        foreach (var value in values)
        {
            encode(this, value);
        }

        return this;
    }
}
