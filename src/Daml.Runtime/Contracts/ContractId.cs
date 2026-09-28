// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Non-generic, erased contract id: a validated ledger contract-id string with no
/// static template witness. The sealed <see cref="ContractId{T}"/> is its only subtype;
/// the base is abstract on purpose, so a typeless contract id can never be fabricated.
/// </summary>
/// <remarks>
/// Construction is guarded — <see cref="ArgumentException.ThrowIfNullOrWhiteSpace"/>
/// runs in the protected constructor, so every <see cref="ContractId{T}"/> carries a
/// non-empty value. The value still projects onto the Ledger API <c>contract_id</c>
/// string via <see cref="Value"/>.
/// </remarks>
public abstract record ContractId
{
    /// <summary>The verbatim ledger contract-id string (non-null, non-whitespace).</summary>
    public string Value { get; }

    /// <summary>Constructs a contract id from a non-empty string.</summary>
    /// <param name="value">The ledger contract-id string; stored verbatim.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null, empty, or whitespace.</exception>
    protected ContractId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        Value = value;
    }

    /// <summary>Returns the verbatim contract-id string.</summary>
    public sealed override string ToString() => Value;
}

/// <summary>
/// Represents a contract ID that references a contract of type T on the ledger.
/// </summary>
/// <typeparam name="T">
/// The Daml template or interface this contract ID refers to. The constraint is the
/// shared marker <see cref="IDamlType"/>, satisfied by both <see cref="ITemplate"/>
/// (concrete templates) and <see cref="IDamlInterface"/> (codegen-emitted interface
/// markers). Daml allows <c>ContractId I</c> where <c>I</c> is an interface — e.g.
/// <c>ContractId Holding</c> across the Splice token standard — and that flows here as
/// <c>ContractId&lt;IHolding&gt;</c>.
/// </typeparam>
/// <remarks>
/// Serializes as the bare ledger contract-id string via
/// <see cref="ContractIdJsonConverterFactory"/>, matching how PQS rows and JSON Ledger
/// API payloads encode a contract id.
/// </remarks>
[JsonConverter(typeof(ContractIdJsonConverterFactory))]
public sealed record ContractId<T> : ContractId where T : IDamlType
{
    /// <summary>Constructs a typed contract id from a non-empty string.</summary>
    /// <param name="value">The ledger contract-id string; stored verbatim.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null, empty, or whitespace.</exception>
    public ContractId(string value) : base(value)
    {
    }

    /// <summary>Extracts the contract-id string; explicit so a contract id is never silently used as text.</summary>
    public static explicit operator string(ContractId<T> id) => id.Value;

    /// <summary>Parses and validates a contract-id string; explicit so arbitrary strings never silently become contract ids.</summary>
    public static explicit operator ContractId<T>(string value) => new(value);

    /// <summary>
    /// Converts to a <see cref="DamlContractId"/> for serialization, including the
    /// static type identifier of <typeparamref name="T"/>:
    /// <see cref="ITemplate.TemplateId"/> for templates, or
    /// <see cref="IDamlInterface.InterfaceId"/> for interface markers.
    /// </summary>
    /// <remarks>
    /// Resolved at compile time via <see cref="IDamlType.DamlTypeId"/>. Errors from the
    /// underlying static getter (e.g. an interface placeholder that throws on
    /// <c>DamlTypeId</c> access) propagate to the caller — that is the intended failure mode.
    /// </remarks>
    public DamlContractId ToDamlValue() => new(Value, T.DamlTypeId.Identifier);
}

/// <summary>
/// Represents a contract ID as a Daml value.
/// </summary>
public sealed record DamlContractId(string Value, Identifier? TemplateId = null) : DamlValue
{
    /// <summary>
    /// Produces a validated <see cref="ContractId{T}"/> from this raw wire carrier.
    /// <see cref="DamlContractId"/> itself carries the contract-id string unvalidated
    /// (by design, the erased/wire contract id stays an unvalidated string; validation
    /// happens at the typed <see cref="ContractId{T}"/> boundary); this
    /// method — together with the typed <see cref="ContractId{T}"/> constructors and
    /// casts — is the validation boundary for raw wire values.
    /// </summary>
    /// <typeparam name="T">The Daml template or interface witness for the typed id.</typeparam>
    /// <exception cref="System.ArgumentException">
    /// Thrown when the carried <see cref="Value"/> is null, empty, or whitespace.
    /// </exception>
    public ContractId<T> ToTyped<T>() where T : IDamlType => new(Value);

    /// <summary>Returns the carried (unvalidated) contract-id string.</summary>
    public override string ToString() => Value;
}

/// <summary>
/// Coercion helpers between concrete-template and interface-typed contract ids.
/// </summary>
/// <remarks>
/// Mirrors Daml's <c>toInterfaceContractId @I cid</c> at the C# type level. The
/// underlying ledger contract id string is unchanged — only the static type
/// witness changes, so the result can be used with helpers constrained on
/// <see cref="IDamlInterface"/> (e.g. interface choice exercisers, the
/// interface-view dispatch in <c>tx.Single&lt;I&gt;()</c>). The
/// <see cref="IImplements{TInterface}"/> constraint at the call site enforces
/// that the concrete template actually implements the interface — codegen
/// emits the marker on every <c>template T implements I where ...</c>.
/// </remarks>
public static class ContractIdInterfaceCoercion
{
    /// <summary>
    /// Reinterprets a template-typed contract id as an interface-typed one.
    /// The runtime contract id string is preserved.
    /// </summary>
    /// <typeparam name="TConcrete">The concrete template type.</typeparam>
    /// <typeparam name="TInterface">The interface marker type.</typeparam>
    public static ContractId<TInterface> ToInterfaceContractId<TConcrete, TInterface>(
        this ContractId<TConcrete> id)
        where TConcrete : ITemplate, IImplements<TInterface>
        where TInterface : IDamlInterface
    {
        ArgumentNullException.ThrowIfNull(id);
        return new ContractId<TInterface>(id.Value);
    }
}
