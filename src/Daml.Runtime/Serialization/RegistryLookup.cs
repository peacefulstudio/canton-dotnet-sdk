// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Serialization;

internal abstract record RegistryLookup<TValue>
{
    private RegistryLookup()
    {
    }

    internal sealed record Resolved(TValue Value, Type DeclaringType) : RegistryLookup<TValue>;

    internal sealed record Missing : RegistryLookup<TValue>;

    internal sealed record Ambiguous(IReadOnlyList<string> Candidates) : RegistryLookup<TValue>;
}
