// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Data;

/// <summary>
/// Marks a generated property with the original Daml field name it was derived from,
/// preserving the on-ledger name after C# member-name normalization. Emitted by
/// canton-dotnet-sdk on every record, template, and interface-view property so that
/// downstream tooling (such as the PQS field DSL) can map a C# property back to its
/// Daml field.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class DamlFieldAttribute : System.Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DamlFieldAttribute"/> class.
    /// </summary>
    /// <param name="name">The original Daml field name.</param>
    public DamlFieldAttribute(string name) => Name = name;

    /// <summary>
    /// Gets the original Daml field name.
    /// </summary>
    public string Name { get; }
}
