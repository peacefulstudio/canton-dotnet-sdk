// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime;

/// <summary>
/// Canonical <c>Daml.Runtime.*</c> namespace string literals, shared with the C# emitter
/// (<c>Daml.Codegen.CSharp</c>) via <c>InternalsVisibleTo</c> so it can emit references to them.
/// </summary>
internal static class RuntimeNamespaces
{
    /// <summary>The <c>Daml.Runtime.Data</c> namespace.</summary>
    public const string Data = "Daml.Runtime.Data";

    /// <summary>The <c>Daml.Runtime.Contracts</c> namespace.</summary>
    public const string Contracts = "Daml.Runtime.Contracts";

    /// <summary>The <c>Daml.Runtime.Commands</c> namespace.</summary>
    public const string Commands = "Daml.Runtime.Commands";

    /// <summary>The <c>Daml.Runtime.Stdlib</c> namespace.</summary>
    public const string Stdlib = "Daml.Runtime.Stdlib";

    /// <summary>The <c>Daml.Runtime.Outcomes</c> namespace.</summary>
    public const string Outcomes = "Daml.Runtime.Outcomes";

    /// <summary>The <c>Daml.Runtime.Serialization</c> namespace.</summary>
    public const string Serialization = "Daml.Runtime.Serialization";
}
