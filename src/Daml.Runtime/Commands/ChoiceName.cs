// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Commands;

/// <summary>
/// Name of a Daml choice carried on an <see cref="ExerciseCommand"/>,
/// <see cref="ExerciseByKeyCommand"/>, or <see cref="CreateAndExerciseCommand"/>, and
/// projected onto the Ledger API <c>choice</c> field.
/// </summary>
/// <remarks>
/// Conversions to and from <see cref="string"/> are both explicit, so a choice name can
/// never be silently mistaken for an arbitrary string (or vice versa) — in particular it
/// cannot be transposed with the adjacent contract-id string at a command-construction
/// site; use <see cref="Value"/> or <see cref="ToString"/> for logging and interpolation.
/// </remarks>
[JsonConverter(typeof(ChoiceNameJsonConverter))]
public readonly record struct ChoiceName
{
    private readonly string? _value;

    /// <summary>The verbatim choice name string.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when accessed on a default-initialized value.
    /// </exception>
    public string Value =>
        _value ?? throw new InvalidOperationException("Cannot access Value of a default (uninitialized) ChoiceName.");

    /// <summary>Constructs a <see cref="ChoiceName"/> from a non-empty string.</summary>
    /// <param name="value">The choice name; stored verbatim (non-null, non-whitespace).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null, empty, or whitespace.</exception>
    public ChoiceName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        _value = value;
    }

    /// <summary>Extracts the choice name; explicit so it is never silently used as arbitrary text.</summary>
    public static explicit operator string(ChoiceName name) =>
        name._value ?? throw new InvalidOperationException("Cannot convert a default (uninitialized) ChoiceName to string.");

    /// <summary>Parses a choice name; explicit so arbitrary strings never silently become choice names.</summary>
    public static explicit operator ChoiceName(string value) => new(value);

    /// <remarks>
    /// Returns a sentinel — not a throw — for <c>default(ChoiceName)</c>: logging
    /// frameworks may invoke <c>ToString</c> on a captured value during exception
    /// handling, and a throw here would mask the original exception.
    /// </remarks>
    public override string ToString() => _value ?? "<uninitialized ChoiceName>";
}

/// <summary>
/// System.Text.Json converter for <see cref="ChoiceName"/>. Serializes as a plain JSON string,
/// the shape the Ledger API's <c>choice</c> field carries, so a <see cref="ChoiceName"/> member
/// reads back with the choice it named. A whole <see cref="ExerciseCommand"/> is
/// written, never read back — see <see cref="ICommand"/>.
/// </summary>
internal sealed class ChoiceNameJsonConverter : OpaqueStringIdJsonConverter<ChoiceName>
{
    /// <inheritdoc/>
    protected override ChoiceName Parse(string id) => new(id);

    /// <inheritdoc/>
    protected override string Format(ChoiceName value) => value.Value;
}
