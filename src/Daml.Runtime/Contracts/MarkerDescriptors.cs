// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Contracts;

internal static class MarkerDescriptors
{
    internal static DamlTypeDescriptor Of<TMarker>()
        where TMarker : IDamlType
    {
        if (!typeof(IDamlInterface).IsAssignableFrom(typeof(TMarker))
            && !typeof(ITemplate).IsAssignableFrom(typeof(TMarker)))
        {
            throw new InvalidOperationException(
                $"{typeof(TMarker).FullName} implements IDamlType but is neither IDamlInterface nor ITemplate.");
        }

        return TMarker.DamlTypeId;
    }
}
