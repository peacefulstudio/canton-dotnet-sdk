// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Stdlib;

/// <summary>
/// Runtime backing for the Daml stdlib type <c>DA.Types.Either</c>.
/// </summary>
/// <remarks>
/// Through <see cref="System.Text.Json"/> it travels as its own concrete arm's object with a
/// <c>"$case"</c> discriminator — <c>{"$case":"Left","Value":"e"}</c> or
/// <c>{"$case":"Right","Value":1}</c> — rather than the <see cref="DamlVariant"/> the Daml-LF
/// encoding uses, because that path is a CLR round-trip contract rather than a wire one (ADR
/// 0028): a declared-abstract <see cref="Either{TL, TR}"/> slot writes nothing of either arm and
/// cannot be read back at all without a converter. It names
/// <see cref="EitherJsonConverterFactory"/> in a <see cref="JsonConverterAttribute"/>, so it
/// converts on bare <see cref="JsonSerializerOptions"/> with no registration.
/// </remarks>
/// <typeparam name="TL">Type carried by the <see cref="Left"/> constructor.</typeparam>
/// <typeparam name="TR">Type carried by the <see cref="Right"/> constructor.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "The static factory is the wire-decoding entry point for this Daml stdlib shape; generated code calls it as Either<...>.FromValue, mirroring the Daml constructor it decodes.")]
[JsonConverter(typeof(EitherJsonConverterFactory))]
public abstract record Either<TL, TR>
    where TL : notnull
    where TR : notnull
{
    private Either()
    {
    }

    /// <summary>
    /// The <c>Left</c> constructor of <c>DA.Types.Either</c>.
    /// </summary>
    /// <param name="Value">The carried left value.</param>
    public sealed record Left(TL Value) : Either<TL, TR>
    {
        /// <inheritdoc />
        public override int GetHashCode() =>
            HashCode.Combine(base.GetHashCode(), DamlEqualityComparer<TL>.Default.GetHashCode(Value));
    }

    /// <summary>
    /// The <c>Right</c> constructor of <c>DA.Types.Either</c>.
    /// </summary>
    /// <param name="Value">The carried right value.</param>
    public sealed record Right(TR Value) : Either<TL, TR>
    {
        /// <inheritdoc />
        public override int GetHashCode() =>
            HashCode.Combine(base.GetHashCode(), DamlEqualityComparer<TR>.Default.GetHashCode(Value));
    }

    /// <summary>
    /// Converts this value to its <see cref="DamlValue"/> wire representation.
    /// </summary>
    /// <param name="convertLeft">Converter for the left value.</param>
    /// <param name="convertRight">Converter for the right value.</param>
    /// <returns>A <see cref="DamlVariant"/> wire value.</returns>
    public DamlValue ToValue(Func<TL, DamlValue> convertLeft, Func<TR, DamlValue> convertRight)
    {
        ArgumentNullException.ThrowIfNull(convertLeft);
        ArgumentNullException.ThrowIfNull(convertRight);
        return this switch
        {
            Left left => DamlVariant.Create("Left", convertLeft(left.Value)),
            Right right => DamlVariant.Create("Right", convertRight(right.Value)),
            _ => throw new InvalidOperationException($"Unknown Either constructor: {GetType().Name}"),
        };
    }

    /// <summary>
    /// Reconstructs an <see cref="Either{TL,TR}"/> from its <see cref="DamlValue"/> wire representation.
    /// </summary>
    /// <param name="value">The wire value, expected to be a <see cref="DamlVariant"/>.</param>
    /// <param name="convertLeft">Converter for the left value.</param>
    /// <param name="convertRight">Converter for the right value.</param>
    /// <returns>The reconstructed value.</returns>
    public static Either<TL, TR> FromValue(
        DamlValue value,
        Func<DamlValue, TL> convertLeft,
        Func<DamlValue, TR> convertRight)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(convertLeft);
        ArgumentNullException.ThrowIfNull(convertRight);
        var variant = value.As<DamlVariant>();
        return variant.Constructor switch
        {
            "Left" => new Left(convertLeft(variant.Value)),
            "Right" => new Right(convertRight(variant.Value)),
            _ => throw new InvalidOperationException($"Unknown Either constructor: {variant.Constructor}"),
        };
    }
}

/// <summary>
/// Supplies the <see cref="System.Text.Json"/> converter for any closed <see cref="Either{TL, TR}"/>,
/// including its <see cref="Either{TL, TR}.Left"/> and <see cref="Either{TL, TR}.Right"/> arms.
/// Without it the declared-abstract type writes and reads nothing of either arm — see
/// <see cref="Either{TL, TR}"/>'s remarks.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CanConvert"/> also matches the arm types directly, so that a caller whose variable is
/// statically typed as the concrete arm — <c>Either&lt;string, int&gt;.Left</c>, not
/// <c>Either&lt;string, int&gt;</c> — still gets the discriminated shape once this factory is
/// registered, e.g. via <see cref="DamlJsonConverters.AddDamlConverters"/>. That registration is
/// required for the arm case specifically: <see cref="JsonConverterAttribute"/> is not inherited by
/// <see cref="System.Text.Json"/>'s converter resolution, so the <see cref="JsonConverterAttribute"/>
/// on <see cref="Either{TL, TR}"/> alone leaves an arm-typed lookup on the default reflection-based
/// contract — the same limitation <see cref="DamlJsonConverters.AddDamlConverters"/>'s remarks
/// describe for a hand-written <see cref="Daml.Runtime.Contracts.ContractId{T}"/> derivation. Putting
/// the attribute on the arm types too would not lift that requirement: see
/// <see cref="DiscriminatedUnionJson.Write{TUnion}"/>'s remarks for why an arm can carry this
/// converter only through <see cref="JsonSerializerOptions.Converters"/>, never its own attribute.
/// </para>
/// <para>
/// <b>AOT / trimming incompatibility:</b> <see cref="CreateConverter"/> uses
/// <see cref="Activator.CreateInstance(Type)"/> and <see cref="Type.MakeGenericType"/> to
/// instantiate the closed converter at runtime — the same cost
/// <see cref="Daml.Runtime.Stdlib.SetJsonConverterFactory"/> already carries, accepted so the
/// attribute reaches a consumer who never registers the converters.
/// </para>
/// </remarks>
[System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("EitherJsonConverterFactory uses MakeGenericType and Activator.CreateInstance, which are not trimming-safe.")]
[System.Diagnostics.CodeAnalysis.RequiresDynamicCode("EitherJsonConverterFactory uses MakeGenericType at runtime, which requires dynamic code generation.")]
internal sealed class EitherJsonConverterFactory : JsonConverterFactory, IDiscriminatedUnionJsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        IsClosedEither(typeToConvert) || IsArmOfClosedEither(typeToConvert);

    private static bool IsClosedEither(Type type) =>
        type is { IsConstructedGenericType: true, ContainsGenericParameters: false }
        && type.GetGenericTypeDefinition() == typeof(Either<,>);

    private static bool IsArmOfClosedEither(Type type) =>
        type.BaseType is { } baseType && IsClosedEither(baseType);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var closedEitherType = IsClosedEither(typeToConvert) ? typeToConvert : typeToConvert.BaseType!;
        var typeArguments = closedEitherType.GetGenericArguments();
        return (JsonConverter)Activator.CreateInstance(
            typeof(EitherJsonConverter<,>).MakeGenericType(typeArguments[0], typeArguments[1]))!;
    }
}

internal sealed class EitherJsonConverter<TL, TR> : JsonConverter<Either<TL, TR>>
    where TL : notnull
    where TR : notnull
{
    private static readonly string TypeName =
        $"{nameof(Either<object, object>)}<{DiscriminatedUnionJson.Describe(typeof(TL))}, {DiscriminatedUnionJson.Describe(typeof(TR))}>";

    private static readonly IReadOnlyDictionary<string, Type> Cases = new Dictionary<string, Type>
    {
        [nameof(Either<TL, TR>.Left)] = typeof(Either<TL, TR>.Left),
        [nameof(Either<TL, TR>.Right)] = typeof(Either<TL, TR>.Right),
    };

    public override Either<TL, TR> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DiscriminatedUnionJson.Read<Either<TL, TR>>(ref reader, options, Cases, TypeName);

    public override void Write(Utf8JsonWriter writer, Either<TL, TR> value, JsonSerializerOptions options) =>
        DiscriminatedUnionJson.Write(writer, value, options, TypeName);
}
