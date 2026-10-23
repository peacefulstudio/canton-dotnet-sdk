// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Data;

internal static class DamlHash
{
    internal static int Int64(long value) => HashCode.Combine((int)value, (int)(value >> 32));

    internal static int Decimal(decimal value)
    {
        if (value == 0m)
        {
            return 0;
        }

        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        var mantissa = new UInt128((uint)bits[2], ((ulong)(uint)bits[1] << 32) | (uint)bits[0]);
        var scale = value.Scale;
        while (scale > 0 && mantissa % 10 == 0)
        {
            mantissa /= 10;
            scale--;
        }

        var low = (ulong)mantissa;
        return HashCode.Combine((int)low, (int)(low >> 32), (int)(mantissa >> 64), scale, value < 0m);
    }
}
