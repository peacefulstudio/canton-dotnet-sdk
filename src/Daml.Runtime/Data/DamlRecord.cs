// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using Daml.Runtime.Contracts;

namespace Daml.Runtime.Data;

/// <summary>
/// Represents a Daml record (product type) - a collection of named fields.
/// </summary>
/// <param name="RecordId">Optional identifier for the record type.</param>
/// <param name="Fields">The fields of the record.</param>
public sealed record DamlRecord(
    Identifier? RecordId,
    IReadOnlyList<DamlField> Fields) : DamlValue
{
    private readonly IReadOnlyList<DamlField> _fields =
        EventCollections.Copy(Fields, nameof(Fields));

    /// <summary>
    /// The fields of the record. Copied at construction and on <c>init</c>, so a caller that
    /// retains the list it supplied cannot change this value's equality or hash code
    /// afterwards.
    /// </summary>
    public IReadOnlyList<DamlField> Fields
    {
        get => _fields;
        init => _fields = EventCollections.Copy(value, nameof(Fields));
    }

    /// <summary>
    /// Creates a record with the specified fields.
    /// </summary>
    public static DamlRecord Create(params DamlField[] fields) =>
        new(null, fields);

    /// <summary>
    /// Creates a record with a type identifier and the specified fields.
    /// </summary>
    public static DamlRecord Create(Identifier recordId, params DamlField[] fields) =>
        new(recordId, fields);

    /// <summary>
    /// Gets a field value by name.
    /// </summary>
    public DamlValue? GetField(string name) =>
        Fields.FirstOrDefault(f => f.Label == name)?.Value;

    /// <summary>
    /// Gets a required field value by name, throwing if not found.
    /// </summary>
    public DamlValue GetRequiredField(string name) =>
        GetField(name) ?? throw MissingField(name);

    /// <summary>
    /// Gets a field whose Daml type is a flat <c>Optional</c>, reading an omitted field as
    /// <see cref="DamlOptional.None"/>. The Ledger API leaves out a record field whose value is a
    /// trailing <c>None</c>, and a payload written before an upgrade added the field has no such field.
    /// Called by generated <c>FromRecord</c> methods.
    /// </summary>
    /// <param name="name">The field label.</param>
    /// <returns>The field's value, or <see cref="DamlOptional.None"/> when the record has no such field.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public DamlValue GetOptionalField(string name) =>
        GetField(name) ?? DamlOptional.None;

    /// <summary>
    /// Gets a field whose Daml type is a nested <c>Optional (Optional a)</c> chain, reading an
    /// omitted field as <see cref="DamlOptionalChain.None"/>, for the reasons
    /// <see cref="GetOptionalField"/> gives. Called by generated <c>FromRecord</c> methods.
    /// </summary>
    /// <param name="name">The field label.</param>
    /// <returns>The field's value, or <see cref="DamlOptionalChain.None"/> when the record has no such field.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public DamlValue GetOptionalChainField(string name) =>
        GetField(name) ?? DamlOptionalChain.None;

    /// <summary>
    /// Gets and converts a field whose Daml type is a type parameter. A present field is converted by
    /// <paramref name="convert"/>. An omitted field is handed to <paramref name="convert"/> as
    /// <paramref name="absentReadsAs"/> when the call site gives one, which it does when the
    /// instantiation is an <c>Optional</c>, flat or nested: the Ledger API leaves out a field whose
    /// value is a trailing <c>None</c>, and <see cref="DamlOptional.None"/> reads as <c>None</c> at
    /// either level. An omitted field at an instantiation that is not an <c>Optional</c> is
    /// required, and the converter is not called. Called by generated <c>FromRecord</c> methods and
    /// the stdlib tuples.
    /// </summary>
    /// <typeparam name="T">The instantiation's C# type.</typeparam>
    /// <param name="name">The field label.</param>
    /// <param name="convert">The instantiation's converter.</param>
    /// <param name="absentReadsAs">
    /// What an omitted field reads as: <see cref="DamlOptional.None"/> when the instantiation is an
    /// <c>Optional</c>, or <see langword="null"/> when the field is required.
    /// </param>
    /// <returns>The converted field value.</returns>
    /// <exception cref="InvalidOperationException">
    /// The field is omitted and <paramref name="absentReadsAs"/> is <see langword="null"/>.
    /// </exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public T GetTypeParameterField<T>(string name, Func<DamlValue, T> convert, DamlValue? absentReadsAs)
    {
        ArgumentNullException.ThrowIfNull(convert);
        var value = GetField(name) ?? absentReadsAs ?? throw MissingField(name);
        return convert(value);
    }

    private static InvalidOperationException MissingField(string name) =>
        new($"Required field '{name}' not found in record.");

    /// <summary>
    /// Compares two records by <see cref="RecordId"/> and field-by-field content.
    /// The record-synthesized equality compares the backing
    /// <see cref="IReadOnlyList{T}"/> by reference — a footgun for a value type —
    /// so we override it with structural element comparison.
    /// </summary>
    public bool Equals(DamlRecord? other) =>
        other is not null
        && Equals(RecordId, other.RecordId)
        && Fields.SequenceEqual(other.Fields);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(RecordId);
        foreach (var field in Fields)
        {
            hash.Add(field);
        }
        return hash.ToHashCode();
    }
}

/// <summary>
/// Represents a single field within a Daml record.
/// </summary>
/// <param name="Label">The field name.</param>
/// <param name="Value">The field value.</param>
public sealed record DamlField(string Label, DamlValue Value)
{
    /// <summary>
    /// Creates a field with the specified label and value.
    /// </summary>
    public static DamlField Create(string label, DamlValue value) => new(label, value);
}
