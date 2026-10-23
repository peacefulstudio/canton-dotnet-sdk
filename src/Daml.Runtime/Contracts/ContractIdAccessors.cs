// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Checked bridges from the string contract id on a raw read-side record to a typed
/// <see cref="ContractId{T}"/>.
/// </summary>
public static class ContractIdAccessors
{
    /// <summary>Returns the contract id of a created contract as a <see cref="ContractId{T}"/>.</summary>
    /// <typeparam name="T">The Daml template or interface the id is expected to address.</typeparam>
    /// <param name="created">The created contract.</param>
    /// <remarks>
    /// A template <typeparamref name="T"/> must match the record's template by package name, module and entity. An
    /// interface <typeparamref name="T"/> is checked only against the interface views present on the record, which
    /// are the views the transaction filter requested (<c>include_interface_view</c>). A contract that implements
    /// <typeparamref name="T"/> therefore still throws when that view was not requested: request the view, or use the
    /// template type.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The record does not match <typeparamref name="T"/>; the message
    /// names the record's template id and <typeparamref name="T"/>'s id.</exception>
    public static ContractId<T> ContractIdAs<T>(this CreatedContract created) where T : IDamlType
    {
        ArgumentNullException.ThrowIfNull(created);
        return Checked<T>(created.ContractId, created.TemplateId, created.PackageName, created.InterfaceIds);
    }

    /// <summary>Returns the contract id of a created tree event as a <see cref="ContractId{T}"/>.</summary>
    /// <typeparam name="T">The Daml template or interface the id is expected to address.</typeparam>
    /// <param name="created">The created tree event.</param>
    /// <remarks>
    /// A template <typeparamref name="T"/> must match the record's template by package name, module and entity. An
    /// interface <typeparamref name="T"/> is checked only against the interface views present on the record, which
    /// are the views the transaction filter requested (<c>include_interface_view</c>). A contract that implements
    /// <typeparamref name="T"/> therefore still throws when that view was not requested: request the view, or use the
    /// template type.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The record does not match <typeparamref name="T"/>; the message
    /// names the record's template id and <typeparamref name="T"/>'s id.</exception>
    public static ContractId<T> ContractIdAs<T>(this TreeEvent.Created created) where T : IDamlType
    {
        ArgumentNullException.ThrowIfNull(created);
        return Checked<T>(created.ContractId, created.TemplateId, created.PackageName, created.InterfaceIds);
    }

    /// <summary>Returns the contract id of an exercised tree event as a <see cref="ContractId{T}"/>.</summary>
    /// <typeparam name="T">The Daml template or interface the id is expected to address.</typeparam>
    /// <param name="exercised">The exercised tree event.</param>
    /// <remarks>
    /// A template <typeparamref name="T"/> must match the record's template by package name, module and entity. An
    /// interface <typeparamref name="T"/> is checked only against the <c>InterfaceId</c> of a choice exercised
    /// through that interface, so a contract that implements <typeparamref name="T"/> still throws when the choice
    /// was not exercised through it: use the template type.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The record does not match <typeparamref name="T"/>; the message
    /// names the record's template id and <typeparamref name="T"/>'s id.</exception>
    public static ContractId<T> ContractIdAs<T>(this TreeEvent.Exercised exercised) where T : IDamlType
    {
        ArgumentNullException.ThrowIfNull(exercised);
        return Checked<T>(
            exercised.ContractId, exercised.TemplateId, exercised.PackageName, ExercisedThrough(exercised.InterfaceId));
    }

    /// <summary>Returns the contract id of an exercised event as a <see cref="ContractId{T}"/>.</summary>
    /// <typeparam name="T">The Daml template or interface the id is expected to address.</typeparam>
    /// <param name="exercised">The exercised event.</param>
    /// <remarks>
    /// A template <typeparamref name="T"/> must match the record's template by package name, module and entity. An
    /// interface <typeparamref name="T"/> is checked only against the <c>InterfaceId</c> of a choice exercised
    /// through that interface, so a contract that implements <typeparamref name="T"/> still throws when the choice
    /// was not exercised through it: use the template type.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The record does not match <typeparamref name="T"/>; the message
    /// names the record's template id and <typeparamref name="T"/>'s id.</exception>
    public static ContractId<T> ContractIdAs<T>(this ExercisedEvent exercised) where T : IDamlType
    {
        ArgumentNullException.ThrowIfNull(exercised);
        return Checked<T>(
            exercised.ContractId, exercised.TemplateId, exercised.PackageName, ExercisedThrough(exercised.InterfaceId));
    }

    private static Identifier[] ExercisedThrough(Identifier? interfaceId) =>
        interfaceId is { } id ? [id] : [];

    private static ContractId<T> Checked<T>(
        string contractId,
        Identifier templateId,
        string? packageName,
        IReadOnlyCollection<Identifier> interfaceIds)
        where T : IDamlType
    {
        var marker = T.DamlTypeId;
        var matches = marker.Kind == DamlTypeKind.Interface
            ? interfaceIds.Any(interfaceId => MarkerMatch.Interface(marker, interfaceId))
            : MarkerMatch.Template(marker, packageName, templateId);
        if (!matches)
        {
            throw new InvalidOperationException(Mismatch(contractId, templateId, interfaceIds, marker));
        }

        return new ContractId<T>(contractId);
    }

    private static string Mismatch(
        string contractId,
        Identifier templateId,
        IReadOnlyCollection<Identifier> interfaceIds,
        DamlTypeDescriptor marker) =>
        marker.Kind == DamlTypeKind.Interface
            ? $"Contract '{contractId}' has template {templateId} and recorded interfaces [{string.Join(", ", interfaceIds)}], "
                + $"which do not include interface {marker.Identifier}."
            : $"Contract '{contractId}' has template {templateId}, which is not {marker.Identifier}.";
}
