// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Stdlib;

/// <summary>
/// Daml stdlib type <c>DA.NonEmpty.Types.NonEmpty a</c> — a list guaranteed to contain
/// at least one element.
/// </summary>
/// <remarks>
/// <para>
/// On the wire the type is a record with fields <c>hd : a</c> (the head) and
/// <c>tl : [a]</c> (the rest of the list). Iterating <see cref="All"/> yields
/// <see cref="Hd"/> followed by every element of <see cref="Tl"/>, so consumers
/// that just want the values can ignore the split.
/// </para>
/// <para>
/// The C# codegen emits the type with a concrete CLR generic argument
/// (e.g. <c>NonEmpty&lt;Party&gt;</c>) which is not in general <see cref="IDamlRecord"/>.
/// Round-tripping therefore goes through caller-supplied converters that bridge the
/// generic CLR type to <see cref="DamlValue"/>; the codegen knows the concrete
/// element type at the call site and inlines the appropriate conversion lambdas.
/// </para>
/// <para>
/// <see cref="System.Text.Json"/> serialization is a separate, CLR-only round-trip contract
/// carried by <see cref="NonEmptyJsonConverterFactory"/>: a null <see cref="Hd"/> or tail element
/// reached through <see cref="JsonSerializer.Deserialize{TValue}(string, JsonSerializerOptions?)"/>
/// surfaces as a <see cref="JsonException"/> naming the offending slot, on bare
/// <see cref="JsonSerializerOptions"/> as well as under
/// <see cref="Serialization.DamlJsonConverters.AddDamlConverters"/>, mirroring
/// <see cref="Set{T}"/>'s <see cref="SetJsonConverter{T}"/>.
/// </para>
/// </remarks>
/// <typeparam name="T">Element type.</typeparam>
[SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "The static factory is the wire-decoding entry point for this Daml stdlib shape; generated code calls it as NonEmpty<...>.FromRecord, mirroring the Daml constructor it decodes.")]
[JsonConverter(typeof(NonEmptyJsonConverterFactory))]
public sealed record NonEmpty<T>(T Hd, IReadOnlyList<T> Tl)
    where T : notnull
{
    private readonly T _hd = EventCollections.RejectNull(Hd, nameof(Hd), nameof(NonEmpty<T>));

    private readonly IReadOnlyList<T> _tl =
        EventCollections.CopyRejectingNullElements(Tl, nameof(Tl), nameof(NonEmpty<T>));

    /// <summary>
    /// The first element. Guarded at construction and on <c>init</c> against a
    /// <see langword="null"/> of a reference element type, matching <see cref="Tl"/>.
    /// </summary>
    public T Hd
    {
        get => _hd;
        init => _hd = EventCollections.RejectNull(value, nameof(Hd), nameof(NonEmpty<T>));
    }

    /// <summary>
    /// The elements after <see cref="Hd"/>. Copied at construction and on <c>init</c>, so a
    /// producer that retains the list it supplied cannot change this value's equality or
    /// hash code afterwards.
    /// </summary>
    public IReadOnlyList<T> Tl
    {
        get => _tl;
        init => _tl = EventCollections.CopyRejectingNullElements(value, nameof(Tl), nameof(NonEmpty<T>));
    }

    /// <summary>
    /// All elements: <see cref="Hd"/> first, followed by every element of <see cref="Tl"/>.
    /// </summary>
    public IEnumerable<T> All
    {
        get
        {
            yield return Hd;
            foreach (var item in Tl)
            {
                yield return item;
            }
        }
    }

    /// <summary>
    /// Converts this non-empty list to its Ledger API record representation.
    /// </summary>
    public DamlRecord ToRecord(Func<T, DamlValue> convertElement)
    {
        ArgumentNullException.ThrowIfNull(convertElement);
        var tail = Tl.Select(element => (DamlValue)convertElement(element)).ToList();
        return DamlRecord.Create(
            DamlField.Create("hd", convertElement(Hd)),
            DamlField.Create("tl", new DamlList(tail)));
    }

    /// <summary>
    /// Reconstructs a non-empty list from its Ledger API record representation.
    /// </summary>
    public static NonEmpty<T> FromRecord(DamlRecord record, Func<DamlValue, T> convertElement)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(convertElement);
        var hd = convertElement(record.GetRequiredField("hd"));
        var tl = record.GetRequiredField("tl").As<DamlList>().Values
            .Select(convertElement)
            .ToList();
        return new NonEmpty<T>(hd, tl);
    }

    /// <summary>
    /// Compares two non-empty lists by head and by tail content, in order. The
    /// record-synthesized equality compares the backing <see cref="IReadOnlyList{T}"/>
    /// by reference — a footgun for a value type — so we override it with structural
    /// comparison.
    /// </summary>
    public bool Equals(NonEmpty<T>? other) =>
        other is not null
        && EqualityComparer<T>.Default.Equals(Hd, other.Hd)
        && Tl.SequenceEqual(other.Tl);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Hd);
        hash.Add(Tl.Count);
        foreach (var item in Tl)
        {
            hash.Add(item);
        }
        return hash.ToHashCode();
    }
}

/// <summary>
/// Supplies the <see cref="System.Text.Json"/> converter for any closed <see cref="NonEmpty{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>AOT / trimming incompatibility:</b> <see cref="CreateConverter"/> uses
/// <see cref="Activator.CreateInstance(Type)"/> and <see cref="Type.MakeGenericType"/> to
/// instantiate the closed converter at runtime. These reflection-based calls are not compatible
/// with Native AOT compilation or aggressive IL trimming and will produce
/// <see cref="NotSupportedException"/> in those environments — the cost
/// <see cref="MapJsonConverterFactory"/> and <see cref="SetJsonConverterFactory"/> already carry,
/// accepted so that the attribute reaches a consumer who never registers the converters.
/// </para>
/// <para>
/// A source-generated <see cref="JsonSerializerContext"/> does not route around this. Because
/// <see cref="NonEmpty{T}"/> carries the factory as an attribute, the generator emits the factory
/// for a member of that type rather than collection metadata, and the consumer build reports
/// <c>IL2026</c> and <c>IL3050</c>. The converter reads and writes elements through the
/// reflection-based <see cref="JsonSerializer"/> overloads, which need a
/// <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo"/> for <c>T</c> the context no
/// longer registers.
/// </para>
/// </remarks>
[RequiresUnreferencedCode("NonEmptyJsonConverterFactory uses MakeGenericType and Activator.CreateInstance, which are not trimming-safe.")]
[RequiresDynamicCode("NonEmptyJsonConverterFactory uses MakeGenericType at runtime, which requires dynamic code generation.")]
internal sealed class NonEmptyJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => IsClosedNonEmpty(typeToConvert);

    private static bool IsClosedNonEmpty(Type type) =>
        type is { IsConstructedGenericType: true, ContainsGenericParameters: false }
        && type.GetGenericTypeDefinition() == typeof(NonEmpty<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(NonEmptyJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()))!;
}

internal sealed class NonEmptyJsonConverter<T> : JsonConverter<NonEmpty<T>>
    where T : notnull
{
    private static readonly string TypeName = $"{nameof(NonEmpty<object>)}<{Describe(typeof(T))}>";

    /// <remarks>
    /// The posture <see cref="SetJsonConverter{T}"/> already holds: a null token is read as the
    /// absent value the declared type already permits, and refused by
    /// <see cref="JsonSerializerOptions.RespectNullableAnnotations"/> where it does not — which only
    /// <see cref="Serialization.DamlJsonConverters.AddDamlConverters"/> sets, so on bare options a
    /// null in a non-nullable slot binds as null rather than being refused.
    /// </remarks>
    public override bool HandleNull => false;

    public override NonEmpty<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected object token for {TypeName}, got {reader.TokenType}.");
        }

        var hdName = PropertyName(nameof(NonEmpty<T>.Hd), options);
        var tlName = PropertyName(nameof(NonEmpty<T>.Tl), options);
        var allName = PropertyName(nameof(NonEmpty<T>.All), options);
        var comparison = options.PropertyNameCaseInsensitive
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        T hd = default!;
        var hdSeen = false;
        IReadOnlyList<T>? tl = null;
        while (true)
        {
            if (!reader.Read())
            {
                throw new JsonException($"Unexpected end of JSON while reading {TypeName}.");
            }

            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            var propertyName = reader.GetString();
            reader.Read();

            if (string.Equals(propertyName, hdName, comparison))
            {
                hd = ReadHd(ref reader, options);
                hdSeen = true;
            }
            else if (string.Equals(propertyName, tlName, comparison))
            {
                tl = ReadTl(ref reader, options);
            }
            else if (string.Equals(propertyName, allName, comparison))
            {
                reader.Skip();
            }
            else if (options.UnmappedMemberHandling == JsonUnmappedMemberHandling.Disallow)
            {
                throw new JsonException(
                    $"The JSON property '{propertyName}' could not be mapped to any .NET member contained in type '{TypeName}'.");
            }
            else
            {
                reader.Skip();
            }
        }

        if (!hdSeen && options.RespectRequiredConstructorParameters)
        {
            throw new JsonException($"{TypeName} requires '{hdName}'; the payload does not carry it.");
        }

        try
        {
            return new NonEmpty<T>(hd, tl!);
        }
        catch (ArgumentException ex)
        {
            throw new JsonException($"Invalid element in {TypeName}: {ex.Message}", ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, NonEmpty<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (!IsOmittedFromWrite(value.Hd, options))
        {
            writer.WritePropertyName(PropertyName(nameof(NonEmpty<T>.Hd), options));
            JsonSerializer.Serialize(writer, value.Hd, options);
        }

        writer.WritePropertyName(PropertyName(nameof(NonEmpty<T>.Tl), options));
        JsonSerializer.Serialize(writer, value.Tl, options);
        if (!options.IgnoreReadOnlyProperties)
        {
            writer.WritePropertyName(PropertyName(nameof(NonEmpty<T>.All), options));
            JsonSerializer.Serialize(writer, value.All, options);
        }

        writer.WriteEndObject();
    }

    private static T ReadHd(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(ref reader, options)!;
        }
        catch (Exception ex) when (ex is not JsonException)
        {
            throw new JsonException($"Cannot read {TypeName}.Hd: {ex.Message}", ex);
        }
    }

    private static IReadOnlyList<T>? ReadTl(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<T>>(ref reader, options);
        }
        catch (Exception ex) when (ex is not JsonException)
        {
            throw new JsonException($"Cannot read {TypeName}.Tl: {ex.Message}", ex);
        }
    }

    private static string PropertyName(string clrName, JsonSerializerOptions options) =>
        options.PropertyNamingPolicy?.ConvertName(clrName) ?? clrName;

    private static bool IsOmittedFromWrite<TProperty>(TProperty value, JsonSerializerOptions options) =>
        (options.DefaultIgnoreCondition == JsonIgnoreCondition.WhenWritingDefault
            && EqualityComparer<TProperty>.Default.Equals(value, default!))
        || (options.DefaultIgnoreCondition == JsonIgnoreCondition.WhenWritingNull && value is null);

    private static string Describe(Type type)
    {
        var arity = type.Name.IndexOf('`', StringComparison.Ordinal);
        var name = arity < 0 ? type.Name : type.Name[..arity];
        return type switch
        {
            { IsGenericType: true } =>
                $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Describe))}>",
            { DeclaringType: not null } => $"{type.DeclaringType.Name}.{name}",
            _ => name,
        };
    }
}
