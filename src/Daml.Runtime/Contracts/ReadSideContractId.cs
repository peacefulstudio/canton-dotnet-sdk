// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Contracts;

internal static class ReadSideContractId
{
    internal static string Require(string? value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    internal static EquatableArray<string> RequireAll(EquatableArray<string> values, string parameterName)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(values[index]))
            {
                throw new ArgumentException(
                    $"Element {index} is null, empty or whitespace; a contract id is none of those.",
                    parameterName);
            }
        }

        return values;
    }
}
