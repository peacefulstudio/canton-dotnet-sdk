// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Commands;

internal interface IAddressedByPackageName
{
    ICommand AddressedByPackageName();
}

/// <summary>
/// Base interface for all ledger commands.
/// </summary>
/// <remarks>
/// Commands are written, never read back. Before a command is rebuilt for a retry after an unknown outcome,
/// look its <see cref="CommandsSubmission.CommandId"/> up on the completion stream.
/// </remarks>
public interface ICommand
{
    /// <summary>
    /// Gets the command type name for debugging/logging.
    /// </summary>
    string CommandType { get; }
}

/// <summary>
/// Command to create a new contract.
/// </summary>
/// <param name="TemplateId">The template identifier.</param>
/// <param name="CreateArguments">The template arguments.</param>
public sealed record CreateCommand(
    Identifier TemplateId,
    DamlRecord CreateArguments) : ICommand, IAddressedByPackageName
{
    /// <inheritdoc />
    public string CommandType => "Create";

    /// <summary>
    /// The name of the package that defines <see cref="TemplateId"/>. A submission puts the
    /// template on the wire as <c>#&lt;package-name&gt;:Module:Entity</c> when this is set, so the
    /// participant resolves the version it has vetted. Set by <see cref="For{T}"/>; <see langword="null"/> on a command
    /// built by hand, which goes on the wire as <see cref="TemplateId"/> is.
    /// </summary>
    public string? PackageName { get; init; }

    ICommand IAddressedByPackageName.AddressedByPackageName() =>
        PackageName is null ? this : this with { TemplateId = TemplateId.AddressedByPackageName(PackageName) };

    /// <summary>
    /// Creates a CreateCommand for the specified template.
    /// </summary>
    public static CreateCommand For<T>(T template) where T : ITemplate =>
        new(T.TemplateId, template.ToRecord()) { PackageName = T.PackageName };
}

/// <summary>
/// Command to exercise a choice on a contract.
/// </summary>
/// <param name="TemplateId">
/// The identifier carried in the ledger API's <c>template_id</c> field. Per
/// Canton 3.x semantics this slot also carries the *interface* id when the
/// choice being exercised is an interface choice — see the ledger-API
/// <c>commands.proto</c> note: "To exercise a choice on an interface, specify
/// the interface identifier in the template_id field."
/// </param>
/// <param name="ContractId">The contract ID to exercise on.</param>
/// <param name="Choice">The choice name.</param>
/// <param name="ChoiceArgument">The choice arguments.</param>
public sealed record ExerciseCommand(
    Identifier TemplateId,
    ContractId ContractId,
    ChoiceName Choice,
    DamlValue ChoiceArgument) : ICommand, IAddressedByPackageName
{
    /// <inheritdoc />
    public string CommandType => "Exercise";

    /// <summary>
    /// The name of the package that defines the template <see cref="TemplateId"/> names, which a
    /// result projection compares in place of the package id so an exercise that ran on an upgraded
    /// version of the package still resolves, and which a submission puts on the wire as
    /// <c>#&lt;package-name&gt;:Module:Entity</c>. Set by <see cref="For{TOwner}"/> for a template; <see langword="null"/>
    /// for an interface, which is not upgradeable and so is always addressed by its exact package id,
    /// and on a command built by hand, which a projection then matches on the exact package id.
    /// </summary>
    public string? PackageName { get; init; }

    ICommand IAddressedByPackageName.AddressedByPackageName() =>
        PackageName is null ? this : this with { TemplateId = TemplateId.AddressedByPackageName(PackageName) };

    /// <summary>
    /// Creates an ExerciseCommand for a specific contract and choice. Works uniformly for
    /// a concrete template or a Daml interface marker: the identifier that travels on the
    /// wire — a template id or an interface id — comes from
    /// <typeparamref name="TOwner"/>'s own <see cref="IDamlType.DamlTypeId"/>, matching the
    /// ledger API's <c>template_id</c> field, which also accepts interface ids for
    /// interface choices.
    /// </summary>
    /// <typeparam name="TOwner">The choice owner: a concrete template or interface marker.</typeparam>
    public static ExerciseCommand For<TOwner>(
        ContractId<TOwner> contractId,
        ChoiceName choice,
        DamlValue argument) where TOwner : IDamlType
    {
        ArgumentNullException.ThrowIfNull(contractId);
        var owner = TOwner.DamlTypeId;
        return new(owner.Identifier, contractId, choice, argument)
        {
            PackageName = owner.Kind == DamlTypeKind.Template ? owner.PackageName : null,
        };
    }

    /// <summary>
    /// Creates an ExerciseCommand for an interface choice. The interface id is
    /// what travels on the wire — the ledger API's <c>template_id</c> field
    /// also accepts interface ids for interface choices.
    /// </summary>
    /// <typeparam name="TInterface">The Daml interface marker type.</typeparam>
    [Obsolete($"Use {nameof(For)}<TOwner> instead — it resolves the same identifier for both templates and interface markers.")]
    public static ExerciseCommand ForInterface<TInterface>(
        ContractId<TInterface> contractId,
        ChoiceName choice,
        DamlValue argument) where TInterface : IDamlInterface
    {
        ArgumentNullException.ThrowIfNull(contractId);
        return new(TInterface.InterfaceId, contractId, choice, argument);
    }
}

/// <summary>
/// Command to exercise a choice on a contract identified by its key.
/// </summary>
/// <param name="TemplateId">The template identifier.</param>
/// <param name="ContractKey">The contract key.</param>
/// <param name="Choice">The choice name.</param>
/// <param name="ChoiceArgument">The choice arguments.</param>
public sealed record ExerciseByKeyCommand(
    Identifier TemplateId,
    DamlValue ContractKey,
    ChoiceName Choice,
    DamlValue ChoiceArgument) : ICommand, IAddressedByPackageName
{
    /// <inheritdoc />
    public string CommandType => "ExerciseByKey";

    /// <summary>
    /// The name of the package that defines <see cref="TemplateId"/>. A submission puts the
    /// template on the wire as <c>#&lt;package-name&gt;:Module:Entity</c> when this is set. Set by the
    /// generated <c>&lt;Choice&gt;ByKeyCommand</c> builders; <see langword="null"/> on a command built
    /// by hand, which goes on the wire as <see cref="TemplateId"/> is.
    /// </summary>
    public string? PackageName { get; init; }

    ICommand IAddressedByPackageName.AddressedByPackageName() =>
        PackageName is null ? this : this with { TemplateId = TemplateId.AddressedByPackageName(PackageName) };
}

/// <summary>
/// Command to create a contract and immediately exercise a choice on it.
/// </summary>
/// <param name="TemplateId">The template identifier.</param>
/// <param name="CreateArguments">The template arguments.</param>
/// <param name="Choice">The choice name.</param>
/// <param name="ChoiceArgument">The choice arguments.</param>
public sealed record CreateAndExerciseCommand(
    Identifier TemplateId,
    DamlRecord CreateArguments,
    ChoiceName Choice,
    DamlValue ChoiceArgument) : ICommand, IAddressedByPackageName
{
    /// <inheritdoc />
    public string CommandType => "CreateAndExercise";

    /// <summary>
    /// The name of the package that defines <see cref="TemplateId"/>. A submission puts the
    /// template on the wire as <c>#&lt;package-name&gt;:Module:Entity</c> when this is set. Set by
    /// <see cref="For{T}"/>; <see langword="null"/> on a command built by hand, which goes on the
    /// wire as <see cref="TemplateId"/> is.
    /// </summary>
    public string? PackageName { get; init; }

    ICommand IAddressedByPackageName.AddressedByPackageName() =>
        PackageName is null ? this : this with { TemplateId = TemplateId.AddressedByPackageName(PackageName) };

    /// <summary>
    /// Creates a CreateAndExerciseCommand for the specified template.
    /// </summary>
    public static CreateAndExerciseCommand For<T>(
        T template,
        ChoiceName choice,
        DamlValue argument) where T : ITemplate =>
        new(T.TemplateId, template.ToRecord(), choice, argument) { PackageName = T.PackageName };
}
