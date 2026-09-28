// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Contracts;

namespace Daml.Runtime.Serialization;

/// <summary>
/// Supplies the <see cref="System.Text.Json"/> converter for any closed
/// <see cref="ContractId{T}"/>. PQS rows and the JSON Ledger API encode a contract id as a raw
/// JSON string; without this factory the default object contract writes
/// <c>{"Value":"..."}</c> and cannot read a bare string back, so every consumer ends up
/// hand-rolling the same reflective factory.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ContractId{T}"/> carries this factory as a
/// <see cref="JsonConverterAttribute"/>, and is sealed, so every contract id — a generated
/// payload field, a <see cref="Contract{T}.Id"/>, or a property on a consumer-authored DTO —
/// converts with no registration. Registering it anyway, directly or through
/// <see cref="DamlJsonConverters.AddDamlConverters"/>, keeps the
/// <see cref="JsonSerializerOptions"/> converter list self-describing.
/// </para>
/// <para>
/// <b>AOT / trimming incompatibility:</b> <see cref="CreateConverter"/> uses
/// <see cref="Activator.CreateInstance(Type)"/> and <see cref="Type.MakeGenericType"/> to
/// instantiate the closed converter at runtime. These reflection-based calls are not
/// compatible with Native AOT compilation or aggressive IL trimming and will produce
/// <see cref="System.NotSupportedException"/> in those environments. Use a source-generated
/// <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> instead when targeting AOT.
/// </para>
/// </remarks>
[RequiresUnreferencedCode("ContractIdJsonConverterFactory uses MakeGenericType and Activator.CreateInstance, which are not trimming-safe.")]
[RequiresDynamicCode("ContractIdJsonConverterFactory uses MakeGenericType at runtime, which requires dynamic code generation.")]
public sealed class ContractIdJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert is { IsGenericType: true }
        && typeToConvert.GetGenericTypeDefinition() == typeof(ContractId<>);

    /// <inheritdoc/>
    /// <exception cref="ArgumentException">
    /// <paramref name="typeToConvert"/> is not a closed <see cref="ContractId{T}"/>.
    /// </exception>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (!CanConvert(typeToConvert))
        {
            throw new ArgumentException(
                $"'{typeToConvert}' is not a closed {typeof(ContractId<>).Name}.",
                nameof(typeToConvert));
        }

        return (JsonConverter)Activator.CreateInstance(
            typeof(ContractIdJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()))!;
    }
}

internal sealed class ContractIdJsonConverter<T> : JsonConverter<ContractId<T>>
    where T : IDamlType
{
    private static readonly string TypeName = $"ContractId<{typeof(T).Name}>";

    /// <remarks>
    /// The reference-type member of the identity-converter family cannot police null the way
    /// <see cref="OpaqueStringIdJsonConverter{TId}"/> does. <c>ContractId&lt;T&gt;</c> and
    /// <c>ContractId&lt;T&gt;?</c> — the C# rendering of <c>Optional (ContractId T)</c> — are the
    /// same runtime type, so a converter handed the null token has no way to tell a required
    /// field from an optional one and would have to reject both. The family reaches its shared
    /// posture instead through <see cref="JsonSerializerOptions.RespectNullableAnnotations"/>,
    /// which <see cref="DamlJsonConverters.AddDamlConverters"/> enables.
    /// </remarks>
    public override bool HandleNull => false;

    public override ContractId<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected string token for {TypeName}, got {reader.TokenType}.");
        }

        return ParseChecked(reader.GetString());
    }

    public override void Write(Utf8JsonWriter writer, ContractId<T> value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);

    /// <summary>
    /// Reads a <see cref="ContractId{T}"/> used as a JSON object's property name — the
    /// shape a <c>Map (ContractId T) v</c> field takes, e.g. <c>{"00abc":1}</c>. Property names
    /// are always JSON strings, so the token-type guard <see cref="Read"/> needs does not apply
    /// here; the blank/parse checks are otherwise identical.
    /// </summary>
    /// <inheritdoc/>
    public override ContractId<T> ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ParseChecked(reader.GetString());

    /// <summary>
    /// Writes a <see cref="ContractId{T}"/> used as a JSON object's property name. See
    /// <see cref="ReadAsPropertyName"/>.
    /// </summary>
    /// <inheritdoc/>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, ContractId<T> value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value.Value);

    private static ContractId<T> ParseChecked(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new JsonException(
                $"Invalid contract id for {TypeName}: contract ids must be non-null and non-whitespace.")
            : new ContractId<T>(value);
}
