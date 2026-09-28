// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Commands;

/// <summary>
/// Non-generic facet of <see cref="Choice{TOwner, TArg, TResult}"/>, letting
/// <see cref="Contracts.IHasChoices{TSelf}.Choices"/> enumerate a type's choices
/// without knowing each choice's argument and result types at the call site.
/// </summary>
public interface IChoice
{
    /// <summary>
    /// Gets the choice name.
    /// </summary>
    ChoiceName Name { get; }

    /// <summary>
    /// Gets whether this choice is consuming.
    /// </summary>
    bool Consuming { get; }

    /// <summary>
    /// Gets the CLR type of the choice argument.
    /// </summary>
    Type ArgumentType { get; }

    /// <summary>
    /// Gets the CLR type of the choice result.
    /// </summary>
    Type ResultType { get; }

    /// <summary>
    /// Encodes a boxed argument into its wire <see cref="DamlValue"/>.
    /// </summary>
    /// <param name="argument">
    /// An instance of the choice's <see cref="ArgumentType"/>, boxed if that type is a
    /// value type, or <see langword="null"/> when the argument type can itself legitimately
    /// carry <see langword="null"/> (for example a Daml <c>Optional</c> in its <c>None</c> state).
    /// </param>
    DamlValue EncodeArgument(object? argument);

    /// <summary>
    /// Decodes a wire <see cref="DamlValue"/> into a boxed argument.
    /// </summary>
    /// <returns>
    /// An instance of the choice's <see cref="ArgumentType"/>, boxed if that type is a
    /// value type, or <see langword="null"/> when the decoded argument is itself
    /// legitimately <see langword="null"/> (for example a Daml <c>Optional</c> in its
    /// <c>None</c> state).
    /// </returns>
    object? DecodeArgument(DamlValue value);

    /// <summary>
    /// Decodes a wire <see cref="DamlValue"/> into a boxed result.
    /// </summary>
    /// <returns>
    /// An instance of the choice's <see cref="ResultType"/>, boxed if that type is a
    /// value type, or <see langword="null"/> when the decoded result is itself legitimately
    /// <see langword="null"/> (for example a Daml <c>Optional</c> in its <c>None</c> state).
    /// </returns>
    object? DecodeResult(DamlValue value);

    /// <summary>
    /// Decodes Daml-LF JSON into a boxed argument.
    /// </summary>
    /// <returns>
    /// An instance of the choice's <see cref="ArgumentType"/>, boxed if that type is a
    /// value type, or <see langword="null"/> when the decoded argument is itself
    /// legitimately <see langword="null"/> (for example a Daml <c>Optional</c> in its
    /// <c>None</c> state).
    /// </returns>
    object? DecodeArgumentJson(JsonElement json, DamlLfJsonDecodeContext context);

    /// <summary>
    /// Decodes Daml-LF JSON into a boxed result.
    /// </summary>
    /// <returns>
    /// An instance of the choice's <see cref="ResultType"/>, boxed if that type is a
    /// value type, or <see langword="null"/> when the decoded result is itself legitimately
    /// <see langword="null"/> (for example a Daml <c>Optional</c> in its <c>None</c> state).
    /// </returns>
    object? DecodeResultJson(JsonElement json, DamlLfJsonDecodeContext context);

    /// <summary>Reads the argument's Daml-LF JSON into its wire value.</summary>
    DamlValue ReadArgumentJson(JsonElement json, DamlLfJsonDecodeContext context);

    /// <summary>Reads the result's Daml-LF JSON into its wire value.</summary>
    DamlValue ReadResultJson(JsonElement json, DamlLfJsonDecodeContext context);
}

/// <summary>
/// Choice metadata for generated choice types.
/// </summary>
/// <typeparam name="TOwner">
/// The choice owner: a concrete template or a Daml interface marker. Both templates and
/// interface markers emit their own <c>Choice{X}</c> statics of this type, resolving the
/// choice's owner from <see cref="IDamlType.DamlTypeId"/> the same way regardless of
/// which kind of type declared the choice.
/// </typeparam>
/// <typeparam name="TArg">The choice argument type.</typeparam>
/// <typeparam name="TResult">The choice result type.</typeparam>
public sealed record Choice<TOwner, TArg, TResult> : IChoice
    where TOwner : IDamlType
{
    /// <summary>
    /// Gets the choice name.
    /// </summary>
    public required ChoiceName Name { get; init; }

    /// <summary>
    /// Gets whether this choice is consuming.
    /// </summary>
    public required bool Consuming { get; init; }

    /// <summary>
    /// Gets the function to convert the argument to a DamlValue.
    /// </summary>
    public required Func<TArg, DamlValue> ArgumentEncoder { get; init; }

    /// <summary>
    /// Gets the function to decode the argument from a DamlValue.
    /// </summary>
    public required Func<DamlValue, TArg> ArgumentDecoder { get; init; }

    /// <summary>
    /// Gets the function to decode the result from a DamlValue.
    /// </summary>
    public required Func<DamlValue, TResult> ResultDecoder { get; init; }

    /// <summary>
    /// Gets the function reading the argument's Daml-LF JSON into its wire value.
    /// </summary>
    public required Func<JsonElement, DamlLfJsonDecodeContext, DamlValue> ArgumentJsonReader { get; init; }

    /// <summary>
    /// Gets the function reading the result's Daml-LF JSON into its wire value.
    /// </summary>
    public required Func<JsonElement, DamlLfJsonDecodeContext, DamlValue> ResultJsonReader { get; init; }

    /// <summary>
    /// Gets the function to decode the argument from Daml-LF JSON.
    /// </summary>
    public Func<JsonElement, DamlLfJsonDecodeContext, TArg> ArgumentJsonDecoder =>
        (json, context) => ArgumentDecoder(ArgumentJsonReader(json, context));

    /// <summary>
    /// Gets the function to decode the result from Daml-LF JSON.
    /// </summary>
    public Func<JsonElement, DamlLfJsonDecodeContext, TResult> ResultJsonDecoder =>
        (json, context) => ResultDecoder(ResultJsonReader(json, context));

    Type IChoice.ArgumentType => typeof(TArg);

    Type IChoice.ResultType => typeof(TResult);

    DamlValue IChoice.EncodeArgument(object? argument) => ArgumentEncoder((TArg)argument!);

    object? IChoice.DecodeArgument(DamlValue value) => ArgumentDecoder(value);

    object? IChoice.DecodeResult(DamlValue value) => ResultDecoder(value);

    object? IChoice.DecodeArgumentJson(JsonElement json, DamlLfJsonDecodeContext context) => ArgumentDecoder(ArgumentJsonReader(json, context));

    object? IChoice.DecodeResultJson(JsonElement json, DamlLfJsonDecodeContext context) => ResultDecoder(ResultJsonReader(json, context));

    DamlValue IChoice.ReadArgumentJson(JsonElement json, DamlLfJsonDecodeContext context) => ArgumentJsonReader(json, context);

    DamlValue IChoice.ReadResultJson(JsonElement json, DamlLfJsonDecodeContext context) => ResultJsonReader(json, context);
}
