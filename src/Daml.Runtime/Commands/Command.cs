// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Commands;

/// <summary>
/// Base interface for all ledger commands.
/// </summary>
/// <remarks>
/// Commands are written, never read back. A command rebuilt for a retry must be resubmitted with the
/// original <see cref="CommandsSubmission.CommandId"/>, so the participant deduplicates it.
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
    DamlRecord CreateArguments) : ICommand
{
    /// <inheritdoc />
    public string CommandType => "Create";

    /// <summary>
    /// Creates a CreateCommand for the specified template.
    /// </summary>
    public static CreateCommand For<T>(T template) where T : ITemplate =>
        new(T.TemplateId, template.ToRecord());
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
    DamlValue ChoiceArgument) : ICommand
{
    /// <inheritdoc />
    public string CommandType => "Exercise";

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
        return new(TOwner.DamlTypeId.Identifier, contractId, choice, argument);
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
    DamlValue ChoiceArgument) : ICommand
{
    /// <inheritdoc />
    public string CommandType => "ExerciseByKey";
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
    DamlValue ChoiceArgument) : ICommand
{
    /// <inheritdoc />
    public string CommandType => "CreateAndExercise";

    /// <summary>
    /// Creates a CreateAndExerciseCommand for the specified template.
    /// </summary>
    public static CreateAndExerciseCommand For<T>(
        T template,
        ChoiceName choice,
        DamlValue argument) where T : ITemplate =>
        new(T.TemplateId, template.ToRecord(), choice, argument);
}
