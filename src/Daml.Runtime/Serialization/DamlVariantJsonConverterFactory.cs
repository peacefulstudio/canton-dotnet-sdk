// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daml.Runtime.Serialization;

/// <summary>
/// A <see cref="System.Text.Json"/> converter factory for generated Daml variants: the abstract
/// record the codegen emits for a Daml variant, and the sealed nested record it emits for each of
/// that variant's constructors.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="System.Text.Json"/> writes against the declared type, so without a converter a
/// variant declared as its abstract base wrote only the base's own members and dropped the arm's
/// payload, and a read refused the abstract type outright. The codegen names this factory in a
/// <see cref="JsonConverterAttribute"/> on every generated variant — an attribute on
/// <see cref="Daml.Runtime.Data.IDamlVariant"/> would not reach it, because
/// <see cref="System.Text.Json"/> does not honor one placed on an interface — so a generated
/// variant converts on bare <see cref="JsonSerializerOptions"/> with no registration.
/// </para>
/// <para>
/// Each variant is written as its concrete arm's own JSON object with a <c>"$case"</c>
/// discriminator naming the arm's C# type inserted first — <c>{"$case":"Win","Value":{…},"Tag":"Win"}</c>,
/// or <c>{"$case":"Pending","Tag":"Pending"}</c> for a constructor without arguments — the same
/// shape <see cref="Daml.Runtime.Stdlib.Optional{T}"/> and <see cref="Daml.Runtime.Stdlib.Either{TL, TR}"/>
/// take. This is a CLR round-trip contract, not the Daml-LF wire encoding: a payload from the
/// ledger or PQS is read with <see cref="DamlLfJsonReader"/>, and <c>ToVariant</c> with
/// <see cref="DamlJsonSerializer"/> writes the Daml-LF <c>{"tag":…,"value":…}</c> form.
/// A <see cref="JsonSerializerOptions.ReferenceHandler"/> is refused with a
/// <see cref="JsonException"/>, as it is for those unions.
/// </para>
/// <para>
/// <see cref="CanConvert"/> also matches the arm types directly, so a variable statically typed
/// as the arm still gets the discriminated shape once this factory is registered, e.g. via
/// <see cref="DamlJsonConverters.AddDamlConverters"/>; the attribute on the variant alone does
/// not reach the arm, since <see cref="JsonConverterAttribute"/> is not inherited.
/// </para>
/// <para>
/// <b>AOT / trimming incompatibility:</b> <see cref="CreateConverter"/> discovers the arms by
/// reflection and instantiates the closed converter with <see cref="Type.MakeGenericType"/> and
/// <see cref="Activator.CreateInstance(Type)"/>, the same cost
/// <see cref="ContractIdJsonConverterFactory"/> carries.
/// </para>
/// </remarks>
[RequiresUnreferencedCode("DamlVariantJsonConverterFactory discovers variant arms by reflection and uses MakeGenericType and Activator.CreateInstance, which are not trimming-safe.")]
[RequiresDynamicCode("DamlVariantJsonConverterFactory uses MakeGenericType at runtime, which requires dynamic code generation.")]
public sealed class DamlVariantJsonConverterFactory : JsonConverterFactory, IDiscriminatedUnionJsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return IsGeneratedVariant(typeToConvert) || IsArmOfGeneratedVariant(typeToConvert);
    }

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        var variantType = IsGeneratedVariant(typeToConvert) ? typeToConvert : typeToConvert.BaseType!;
        return (JsonConverter)Activator.CreateInstance(typeof(DamlVariantJsonConverter<>).MakeGenericType(variantType))!;
    }

    private static bool IsGeneratedVariant(Type type) =>
        type is { IsAbstract: true, ContainsGenericParameters: false }
        && type.GetCustomAttribute<JsonConverterAttribute>(inherit: false)?.ConverterType == typeof(DamlVariantJsonConverterFactory);

    private static bool IsArmOfGeneratedVariant(Type type) =>
        type is { IsSealed: true, BaseType: { } baseType } && IsGeneratedVariant(baseType);
}

[RequiresUnreferencedCode("DamlVariantJsonConverter discovers variant arms by reflection.")]
internal sealed class DamlVariantJsonConverter<TVariant> : JsonConverter<TVariant>
    where TVariant : class
{
    private static readonly string TypeName = DiscriminatedUnionJson.Describe(typeof(TVariant));

    private static readonly IReadOnlyDictionary<string, Type> Cases =
        ArmsOf(typeof(TVariant)).ToDictionary(arm => arm.Name, StringComparer.Ordinal);

    public override TVariant Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DiscriminatedUnionJson.Read<TVariant>(ref reader, options, Cases, TypeName);

    public override void Write(Utf8JsonWriter writer, TVariant value, JsonSerializerOptions options) =>
        DiscriminatedUnionJson.Write(writer, value, options, TypeName);

    private static IEnumerable<Type> ArmsOf(Type variantType)
    {
        var declaringType = variantType.IsGenericType ? variantType.GetGenericTypeDefinition() : variantType;
        return declaringType
            .GetNestedTypes(BindingFlags.Public)
            .Select(nested => nested.IsGenericTypeDefinition ? nested.MakeGenericType(variantType.GetGenericArguments()) : nested)
            .Where(nested => nested.IsSealed && nested.BaseType == variantType);
    }
}
