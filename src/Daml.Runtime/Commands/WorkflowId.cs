// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Commands;

/// <summary>
/// Correlation identifier carried on a <see cref="CommandsSubmission"/> and projected
/// onto the Ledger API <c>workflow_id</c> field.
/// </summary>
/// <remarks>
/// Conversions to and from <see cref="string"/> are both explicit, so a workflow id can
/// never be silently mistaken for an arbitrary string (or vice versa) — in particular it
/// cannot be transposed with a <see cref="CommandId"/> at a call site; use
/// <see cref="Value"/> or <see cref="ToString"/> for logging and interpolation.
/// </remarks>
[JsonConverter(typeof(WorkflowIdJsonConverter))]
public readonly record struct WorkflowId
{
    private readonly string? _value;

    /// <summary>The verbatim workflow id string.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when accessed on a default-initialized value.
    /// </exception>
    public string Value =>
        _value ?? throw new InvalidOperationException("Cannot access Value of a default (uninitialized) WorkflowId.");

    /// <summary>Constructs a <see cref="WorkflowId"/> from a non-null string.</summary>
    /// <param name="value">
    /// The workflow id; stored verbatim. Empty and whitespace values are accepted because the
    /// Ledger API treats <c>workflow_id</c> as optional with no non-empty constraint.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
    public WorkflowId(string value)
    {
        ArgumentNullException.ThrowIfNull(value, nameof(value));
        _value = value;
    }

    /// <summary>Extracts the workflow id; explicit so it is never silently used as arbitrary text.</summary>
    public static explicit operator string(WorkflowId id) =>
        id._value ?? throw new InvalidOperationException("Cannot convert a default (uninitialized) WorkflowId to string.");

    /// <summary>Parses a workflow id; explicit so arbitrary strings never silently become workflow ids.</summary>
    public static explicit operator WorkflowId(string value) => new(value);

    /// <remarks>
    /// Returns a sentinel — not a throw — for <c>default(WorkflowId)</c>: logging
    /// frameworks may invoke <c>ToString</c> on a captured value during exception
    /// handling, and a throw here would mask the original exception.
    /// </remarks>
    public override string ToString() => _value ?? "<uninitialized WorkflowId>";
}

/// <summary>
/// System.Text.Json converter for <see cref="WorkflowId"/>. Serializes as a plain JSON string,
/// the shape the Ledger API's <c>workflow_id</c> field carries, so a <see cref="WorkflowId"/>
/// member reads back with the id it correlates on. A whole <see cref="CommandsSubmission"/>
/// is written, never read back — see <see cref="ICommand"/>.
/// </summary>
internal sealed class WorkflowIdJsonConverter : OpaqueStringIdJsonConverter<WorkflowId>
{
    /// <inheritdoc/>
    protected override bool PermitsBlank => true;

    /// <inheritdoc/>
    protected override WorkflowId Parse(string id) => new(id);

    /// <inheritdoc/>
    protected override string Format(WorkflowId value) => value.Value;
}
