// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;

namespace Daml.Codegen.Tooling;

/// <summary>
/// Read access to the stdlib disposition inventory for the internal tooling and its tests, which
/// the emitter library keeps out of its public surface.
/// </summary>
public static class StdlibRuntimeInventoryAccess
{
    /// <summary>The stdlib types hand-written into <c>Daml.Runtime</c>.</summary>
    public static IReadOnlySet<(string Module, string Name)> HandMapped => StdlibRuntimeInventory.HandMapped;

    /// <summary>The stdlib types generated into <c>Daml.Runtime</c>.</summary>
    public static IReadOnlySet<(string Module, string Name)> Generated => StdlibRuntimeInventory.Generated;

    /// <summary>The stdlib types excluded from the runtime, keyed to the reason.</summary>
    public static IReadOnlyDictionary<(string Module, string Name), string> Excluded => StdlibRuntimeInventory.Excluded;
}
