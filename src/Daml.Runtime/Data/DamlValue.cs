// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Data;

/// <summary>
/// Marker interface for all Daml values that can be serialized to/from the Ledger API.
/// </summary>
/// <remarks>
/// Carries no members. Record-shaped values implement <see cref="IDamlRecord"/> and
/// variant-shaped values implement <see cref="IDamlVariant"/>; both extend this marker
/// so generic helpers can constrain on the broader type without dispatching on shape.
/// </remarks>
public interface IDamlValue
{
}

/// <summary>
/// A Daml value whose Ledger API representation is a record.
/// </summary>
public interface IDamlRecord : IDamlValue
{
    /// <summary>
    /// Converts this value to its Ledger API record representation.
    /// </summary>
    DamlRecord ToRecord();
}

/// <summary>
/// A Daml value whose Ledger API representation is a record and whose concrete type
/// <typeparamref name="TSelf"/> can be reconstructed from that representation.
/// </summary>
/// <remarks>
/// The factory is a static abstract, so it is called through a type parameter constrained
/// to this interface — <c>static T Materialize&lt;T&gt;(DamlRecord record)
/// where T : IDamlRecord&lt;T&gt; =&gt; T.FromRecord(record);</c> — never on an instance.
/// </remarks>
/// <typeparam name="TSelf">The implementing type itself; the self-referential constraint
/// lets the factory return the concrete type rather than this interface.</typeparam>
public interface IDamlRecord<TSelf> : IDamlRecord
    where TSelf : IDamlRecord<TSelf>
{
    /// <summary>
    /// Creates a <typeparamref name="TSelf"/> instance from its Ledger API record
    /// representation. Implementations throw when a required field is missing.
    /// </summary>
    static abstract TSelf FromRecord(DamlRecord record);

    /// <summary>
    /// Decodes a Daml-LF JSON record directly into a <see cref="DamlRecord"/> for
    /// <typeparamref name="TSelf"/>, without going through reflection.
    /// </summary>
    /// <remarks>
    /// Emitted by the code generator for generated types; not for hand-authored calls.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [SuppressMessage(
        "Naming", "CA1707:Identifiers should not contain underscores",
        Justification = "The double-underscore prefix is the emitted-plumbing naming convention shared "
            + "by every __ReadDamlLfJson member; it marks a compiler-dispatched member no hand-written "
            + "call site should name, the same intent EditorBrowsable(Never) signals.")]
    static abstract DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context);
}

/// <summary>
/// A Daml value whose Ledger API representation is a variant.
/// </summary>
public interface IDamlVariant : IDamlValue
{
    /// <summary>
    /// Converts this value to its Ledger API variant representation.
    /// </summary>
    DamlVariant ToVariant();
}

/// <summary>
/// A Daml value whose Ledger API representation is a variant and whose concrete arm can be
/// decoded directly from Daml-LF JSON.
/// </summary>
/// <typeparam name="TSelf">The implementing type itself; the self-referential constraint
/// lets the reader dispatch to the concrete type's own decoding logic.</typeparam>
public interface IDamlVariant<TSelf> : IDamlVariant
    where TSelf : IDamlVariant<TSelf>
{
    /// <summary>
    /// Decodes a Daml-LF JSON variant directly into a <see cref="DamlVariant"/> for
    /// <typeparamref name="TSelf"/>, without going through reflection.
    /// </summary>
    /// <remarks>
    /// Emitted by the code generator for generated types; not for hand-authored calls.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [SuppressMessage(
        "Naming", "CA1707:Identifiers should not contain underscores",
        Justification = "The double-underscore prefix is the emitted-plumbing naming convention shared "
            + "by every __ReadDamlLfJson member; it marks a compiler-dispatched member no hand-written "
            + "call site should name, the same intent EditorBrowsable(Never) signals.")]
    static abstract DamlVariant __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context);
}

/// <summary>
/// Base class for all Daml primitive and composite values.
/// </summary>
/// <remarks>
/// A value declared as <see cref="DamlValue"/> serializes with <see cref="System.Text.Json"/> as its
/// concrete arm's own JSON object with a <c>"$case"</c> member naming the arm first, and reads back
/// as that arm, on default options and with no registration. This is a CLR round-trip contract, not
/// the Daml-LF wire encoding; read a participant or PQS payload with <see cref="DamlLfJsonReader"/>.
/// </remarks>
[JsonConverter(typeof(DamlValueClrJsonConverter))]
public abstract record DamlValue
{
    /// <summary>
    /// Attempts to cast this value to the specified type.
    /// </summary>
    public T As<T>() where T : DamlValue =>
        this as T ?? throw new InvalidCastException($"Cannot cast {GetType().Name} to {typeof(T).Name}");

    /// <summary>
    /// Attempts to get the value as the specified primitive type.
    /// </summary>
    public bool TryGet<T>(out T? result) where T : DamlValue
    {
        if (this is T typed)
        {
            result = typed;
            return true;
        }
        result = default;
        return false;
    }
}
