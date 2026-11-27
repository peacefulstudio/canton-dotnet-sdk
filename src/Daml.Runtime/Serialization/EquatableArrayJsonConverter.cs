// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Contracts;

namespace Daml.Runtime.Serialization;

/// <summary>
/// Supplies the <see cref="System.Text.Json"/> converter for any closed
/// <see cref="EquatableArray{T}"/> — the type every list member of the event and stream
/// records carries. Without it those records are write-only: the struct implements
/// <see cref="IList{T}"/>, so the built-in collection converter writes a JSON array happily,
/// but it cannot read one back — the collection reports itself read-only, so the converter
/// has nothing to instantiate and populate — and fails with
/// <see cref="NotSupportedException"/> on every array, the empty one included.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EquatableArray{T}"/> carries this factory as a
/// <see cref="JsonConverterAttribute"/>, the way <see cref="Daml.Runtime.Contracts.ContractId{T}"/>
/// carries its own, so a record with a list member reads and writes on bare
/// <see cref="JsonSerializerOptions"/> with no registration. What registration still adds is the
/// half no converter can supply — a list the payload omits never reaches a converter at all, and
/// only <see cref="DamlJsonConverters.AddDamlConverters"/> refuses it rather than reading it as
/// the empty array.
/// </para>
/// <para>
/// The wire shape is the plain JSON array, and the elements convert through the
/// <see cref="JsonSerializerOptions"/> the caller supplied, so an
/// <c>EquatableArray&lt;Party&gt;</c> reads and writes as
/// <c>["Alice::1220ab", "Bob::1220cd"]</c> rather than as a list of objects.
/// <c>default</c> — which the type defines as the empty array — writes <c>[]</c>, and
/// <c>[]</c> reads back as <see cref="EquatableArray{T}.Empty"/>, so the value survives a
/// round trip whether or not it was ever populated.
/// </para>
/// <para>
/// <b>AOT / trimming incompatibility:</b> <see cref="CreateConverter"/> uses
/// <see cref="Activator.CreateInstance(Type)"/> and <see cref="Type.MakeGenericType"/> to
/// instantiate the closed converter at runtime. These reflection-based calls are not
/// compatible with Native AOT compilation or aggressive IL trimming and will produce
/// <see cref="NotSupportedException"/> in those environments.
/// </para>
/// <para>
/// A source-generated <see cref="JsonSerializerContext"/> does not route around this. Because
/// <see cref="EquatableArray{T}"/> carries the factory as an attribute, the generator emits the
/// factory for a member of that type rather than collection metadata, and the consumer build
/// reports <c>IL2026</c> and <c>IL3050</c>. The converter reads and writes its elements through
/// the reflection-based <see cref="JsonSerializer"/> overloads, which need a
/// <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo"/> for the element type the
/// context no longer registers: writing throws a <see cref="JsonException"/> naming that element
/// type until the context declares <c>[JsonSerializable(typeof(T))]</c> for it.
/// </para>
/// </remarks>
[RequiresUnreferencedCode("EquatableArrayJsonConverterFactory uses MakeGenericType and Activator.CreateInstance, which are not trimming-safe.")]
[RequiresDynamicCode("EquatableArrayJsonConverterFactory uses MakeGenericType at runtime, which requires dynamic code generation.")]
public sealed class EquatableArrayJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert) => IsClosedEquatableArray(typeToConvert);

    internal static bool IsClosedEquatableArray(Type type) =>
        ClosedGenericType.IsOf(type, typeof(EquatableArray<>));

    /// <inheritdoc/>
    /// <exception cref="ArgumentException">
    /// <paramref name="typeToConvert"/> is not a closed <see cref="EquatableArray{T}"/>.
    /// </exception>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (!CanConvert(typeToConvert))
        {
            throw new ArgumentException(
                $"'{typeToConvert}' is not a closed {nameof(EquatableArray)}<T>.",
                nameof(typeToConvert));
        }

        return (JsonConverter)Activator.CreateInstance(
            typeof(EquatableArrayJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
    }
}

internal sealed class EquatableArrayJsonConverter<T>() : JsonArrayConverter<EquatableArray<T>, T>(
    $"{nameof(EquatableArray)}<{DiscriminatedUnionJson.Describe(typeof(T))}>")
{
    /// <remarks>
    /// The struct has no <c>null</c> state to represent one with, so reading the null token is
    /// the only way to refuse it rather than let it become an empty array. That keeps the
    /// converter on the posture the identity converters already hold — a JSON null is rejected
    /// wherever the declared type forbids one, and read as absent wherever it permits one:
    /// a slot declared <c>EquatableArray&lt;T&gt;?</c> short-circuits null before reaching here.
    /// </remarks>
    public override bool HandleNull => true;

    public override EquatableArray<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            throw new JsonException(
                $"{TypeName} cannot be null; write [] for an empty array, or declare the slot as {TypeName}? to accept an absent one.");
        }

        return ReadArray(ref reader, options);
    }

    public override void Write(Utf8JsonWriter writer, EquatableArray<T> value, JsonSerializerOptions options) =>
        WriteArray(writer, value, options);

    protected override EquatableArray<T> Create(List<T> elements) =>
        EquatableArray.Create(CollectionsMarshal.AsSpan(elements));
}
