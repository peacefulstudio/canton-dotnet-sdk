// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.Intermediate.Model;

/// <summary>
/// Represents a Daml type.
/// </summary>
public abstract record DamlType
{
    /// <summary>
    /// Dispatches this node to the arm of <paramref name="visitor"/> that handles its
    /// concrete type — each node reaches exactly its own arm, never a shared default, so a
    /// new node added to the algebra without extending a visitor fails the build at that
    /// visitor instead of silently dispatching to a fallback at runtime.
    /// </summary>
    /// <typeparam name="TResult">The value the chosen arm produces.</typeparam>
    /// <param name="visitor">The visitor whose matching arm handles this node.</param>
    public abstract TResult Accept<TResult>(IDamlTypeVisitor<TResult> visitor);
}

/// <summary>
/// A primitive Daml type.
/// </summary>
public sealed record DamlPrimitiveType(DamlPrimitive Primitive) : DamlType
{
    /// <inheritdoc />
    public override TResult Accept<TResult>(IDamlTypeVisitor<TResult> visitor) => visitor.VisitPrimitive(this);
}

/// <summary>
/// Enumeration of Daml primitive types.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "Members name Daml-LF builtin types; Int64 is the spelling the Daml-LF specification and the intermediate protobuf use.")]
public enum DamlPrimitive
{
    /// <summary>Daml <c>()</c> — the empty value.</summary>
    Unit,

    /// <summary>Daml <c>Bool</c>.</summary>
    Bool,

    /// <summary>Daml <c>Int</c> — 64-bit signed integer.</summary>
    Int64,

    /// <summary>Daml <c>Numeric n</c> — fixed-scale decimal.</summary>
    Numeric,

    /// <summary>Daml <c>Text</c> — a string.</summary>
    Text,

    /// <summary>Daml <c>Date</c> — a calendar date without time.</summary>
    Date,

    /// <summary>Daml <c>Time</c> — a timestamp with microsecond precision.</summary>
    Timestamp,

    /// <summary>Daml <c>Party</c> — a ledger party identifier.</summary>
    Party,

    /// <summary>Daml <c>ContractId a</c> — takes the template type as an argument.</summary>
    ContractId,

    /// <summary>Daml <c>[a]</c> — takes the element type as an argument.</summary>
    List,

    /// <summary>Daml <c>Optional a</c> — takes the element type as an argument.</summary>
    Optional,

    /// <summary>Daml <c>TextMap a</c> — string-keyed map, takes the value type as an argument.</summary>
    TextMap,

    /// <summary>Daml <c>GenMap k v</c> — takes the key and value types as arguments.</summary>
    GenMap,

    /// <summary>
    /// Daml <c>a -&gt; b</c> — the function type. A structural type-former that is legal in
    /// type signatures but never a serializable data-field type; see <see cref="DamlPrimitiveCatalog"/>.
    /// </summary>
    Arrow,

    /// <summary>
    /// Daml <c>Update a</c> — the contract-update monad. Legal in type signatures (interface
    /// method return types are typically <c>Update X</c>) but never a serializable data-field
    /// type; see <see cref="DamlPrimitiveCatalog"/>.
    /// </summary>
    Update,

    /// <summary>
    /// Daml <c>TypeRep</c> — a runtime type representation. Signature-only structural
    /// builtin; see <see cref="DamlPrimitiveCatalog"/>.
    /// </summary>
    TypeRep,

    /// <summary>
    /// Daml <c>Any</c> — the existential builtin. Signature-only structural builtin; see
    /// <see cref="DamlPrimitiveCatalog"/>.
    /// </summary>
    Any,

    /// <summary>
    /// Daml <c>AnyException</c> — the catch-all exception context builtin. Signature-only
    /// structural builtin; see <see cref="DamlPrimitiveCatalog"/>.
    /// </summary>
    AnyException,

    /// <summary>
    /// Daml <c>FailureCategory</c> — the exception failure classification builtin. Characterized
    /// on the splice fixtures as signature-only (interned pools and value-definition signatures,
    /// never a serializable data position); see <see cref="DamlPrimitiveCatalog"/>.
    /// </summary>
    FailureCategory
}

/// <summary>
/// A reference to a user-defined type.
/// </summary>
public sealed record DamlTypeRef(string PackageId, string Module, string Name) : DamlType
{
    /// <inheritdoc />
    public override TResult Accept<TResult>(IDamlTypeVisitor<TResult> visitor) => visitor.VisitTypeRef(this);
}

/// <summary>
/// A type application (generic type with arguments).
/// </summary>
public sealed record DamlTypeApp(DamlType Base, IReadOnlyList<DamlType> Arguments) : DamlType
{
    /// <summary>
    /// Compares by value, including <see cref="Arguments"/> element by element. The
    /// compiler-synthesized record equality would compare that list by reference, which
    /// makes two independently-built but structurally identical type trees unequal.
    /// </summary>
    public bool Equals(DamlTypeApp? other) =>
        other is not null && Base.Equals(other.Base) && Arguments.SequenceEqual(other.Arguments);

    /// <inheritdoc cref="Equals(DamlTypeApp?)"/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Base);
        foreach (var argument in Arguments)
        {
            hash.Add(argument);
        }
        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override TResult Accept<TResult>(IDamlTypeVisitor<TResult> visitor) => visitor.VisitTypeApp(this);
}

/// <summary>
/// A type variable.
/// </summary>
public sealed record DamlTypeVar(string Name) : DamlType
{
    /// <inheritdoc />
    public override TResult Accept<TResult>(IDamlTypeVisitor<TResult> visitor) => visitor.VisitTypeVar(this);
}

/// <summary>
/// A Daml <c>[a]</c> — a list of <paramref name="Element"/> values. The typed node for the
/// wire's <c>TypeApp(List, [a])</c> application: the two spell the same Daml type, one as a
/// first-class model node, the other as a generic application of the <see cref="DamlPrimitive.List"/>
/// builtin.
/// </summary>
/// <param name="Element">The type of the list's elements.</param>
public sealed record DamlListType(DamlType Element) : DamlType
{
    /// <inheritdoc />
    public override TResult Accept<TResult>(IDamlTypeVisitor<TResult> visitor) => visitor.VisitList(this);
}

/// <summary>
/// A Daml <c>Optional a</c> — a value of <paramref name="Value"/> that may be absent. The
/// typed node for the wire's <c>TypeApp(Optional, [a])</c> application: the two spell the
/// same Daml type, one as a first-class model node, the other as a generic application of
/// the <see cref="DamlPrimitive.Optional"/> builtin.
/// </summary>
/// <param name="Value">The type the optional carries when present.</param>
public sealed record DamlOptionalType(DamlType Value) : DamlType
{
    /// <inheritdoc />
    public override TResult Accept<TResult>(IDamlTypeVisitor<TResult> visitor) => visitor.VisitOptional(this);
}

/// <summary>
/// A Daml <c>TextMap a</c> — a map keyed by text. The typed node for the wire's
/// <c>TypeApp(TextMap, [a])</c> application: the two spell the same Daml type, one as a
/// first-class model node, the other as a generic application of the
/// <see cref="DamlPrimitive.TextMap"/> builtin.
/// </summary>
/// <param name="Value">The type of the map's values.</param>
public sealed record DamlTextMapType(DamlType Value) : DamlType
{
    /// <inheritdoc />
    public override TResult Accept<TResult>(IDamlTypeVisitor<TResult> visitor) => visitor.VisitTextMap(this);
}

/// <summary>
/// A Daml <c>GenMap k v</c> — a map keyed by an arbitrary comparable type. The typed node
/// for the wire's <c>TypeApp(GenMap, [k, v])</c> application: the two spell the same Daml
/// type, one as a first-class model node, the other as a generic application of the
/// <see cref="DamlPrimitive.GenMap"/> builtin. Key and value are positional: swapping them
/// describes a different map.
/// </summary>
/// <param name="Key">The type of the map's keys.</param>
/// <param name="Value">The type of the map's values.</param>
public sealed record DamlGenMapType(DamlType Key, DamlType Value) : DamlType
{
    /// <inheritdoc />
    public override TResult Accept<TResult>(IDamlTypeVisitor<TResult> visitor) => visitor.VisitGenMap(this);
}

/// <summary>
/// A Daml <c>ContractId a</c> — a reference to a contract of template type
/// <paramref name="Payload"/>. The typed node for the wire's
/// <c>TypeApp(ContractId, [a])</c> application: the two spell the same Daml type, one as a
/// first-class model node, the other as a generic application of the
/// <see cref="DamlPrimitive.ContractId"/> builtin.
/// </summary>
/// <param name="Payload">The template type the contract id refers to.</param>
public sealed record DamlContractIdType(DamlType Payload) : DamlType
{
    /// <inheritdoc />
    public override TResult Accept<TResult>(IDamlTypeVisitor<TResult> visitor) => visitor.VisitContractId(this);
}
