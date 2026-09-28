// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// A Daml <c>Optional a</c> in a position C# nullable syntax cannot carry, emitted as the
/// runtime wrapper rather than as <c>t?</c>. Produced only by the representation pre-pass
/// (<see cref="OptionalRepresentation"/>), which is the sole owner of the rule deciding which
/// positions those are, and consumed only by the emitter itself. Private to the emitter on purpose:
/// the neutral Intermediate model is language-neutral, no producer ever creates this node,
/// and the Intermediate writer has never been able to serialize it.
/// </summary>
/// <param name="Argument">The type the optional carries.</param>
/// <param name="Encoding">The wire encoding this position requires.</param>
internal sealed record DamlWrappedOptional(DamlType Argument, OptionalEncoding Encoding) : DamlType
{
    /// <summary>
    /// Always throws: this wrapper is the emitter's own representation node, not a member
    /// of the public type algebra, so no <see cref="IDamlTypeVisitor{TResult}"/> arm exists
    /// for it — the representation pre-pass and the type mapper handle it structurally.
    /// </summary>
    public override TResult Accept<TResult>(IDamlTypeVisitor<TResult> visitor) =>
        throw new NotSupportedException(
            "DamlWrappedOptional is the emitter's own optional-representation node and is not "
            + "part of the public DamlType algebra; the representation pre-pass and the type "
            + "mapper handle it structurally.");
}

/// <summary>
/// The wire encoding a Daml <c>Optional</c> carries, which depends on its position rather
/// than on its C# representation.
/// </summary>
internal enum OptionalEncoding
{
    /// <summary>JSON <c>null</c> when absent, the bare value when present.</summary>
    Flat,

    /// <summary>
    /// JSON <c>[]</c> when absent, <c>[v]</c> when present — the form a participant requires
    /// at every level of an Optional chain nested two or more levels deep.
    /// </summary>
    NestedChain
}
