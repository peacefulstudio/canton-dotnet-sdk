// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Outcomes;

/// <summary>
/// What a submission did, so the code that logs, traces or throws on its
/// <see cref="ExerciseOutcome{T}"/> reads it off <see cref="ExerciseOutcome{T}.Operation"/> instead of
/// restating it. A single-command submission is a <see cref="Command"/>: the template (or, for an
/// interface choice, the interface) it addressed and the choice it exercised, or <c>Create</c>. A
/// submission of several commands is a <see cref="Batch"/> of them, in submission order.
/// </summary>
/// <remarks>
/// Through <see cref="System.Text.Json"/> a <see cref="Command"/> travels as
/// <c>{"TemplateId":{…},"Name":"Accept"}</c> and a <see cref="Batch"/> as <c>{"Commands":[…]}</c>;
/// the reader tells them apart by the <c>Commands</c> member. <see cref="Label"/> is not written.
/// </remarks>
[JsonConverter(typeof(LedgerOperationJsonConverter))]
public abstract record LedgerOperation
{
    private const string CreateName = "Create";

    private protected LedgerOperation() { }

    /// <summary>
    /// The operation as text: <c>{Template}.{Name}</c> for a <see cref="Command"/>, e.g.
    /// <c>ConnectionRequest.Accept</c>; for a <see cref="Batch"/>, each distinct command label in the
    /// order it first appears, followed by <c>×N</c> when it appears more than once, e.g.
    /// <c>Connection.Disconnect ×2, InviteLink.Withdraw ×3</c>. Entity names alone, without module or package.
    /// </summary>
    [JsonIgnore]
    public abstract string Label { get; }

    /// <inheritdoc/>
    public sealed override string ToString() => Label;

    /// <summary>One command: the template or interface it addressed and the operation it ran on it.</summary>
    /// <param name="TemplateId">The template or interface the command addressed, as the command named it.</param>
    /// <param name="Name">The exercised choice's name, or <c>Create</c> for a create.</param>
    public sealed record Command(Identifier TemplateId, string Name) : LedgerOperation
    {
        /// <inheritdoc/>
        [JsonIgnore]
        public override string Label => $"{TemplateId.EntityName}.{Name}";
    }

    /// <summary>The commands of one atomic submission, in submission order.</summary>
    /// <param name="Commands">The submission's commands, in submission order; at least one.</param>
    /// <exception cref="ArgumentException"><paramref name="Commands"/> is empty, at construction or
    /// through a <c>with</c> expression.</exception>
    public sealed record Batch(EquatableArray<Command> Commands) : LedgerOperation
    {
        private readonly EquatableArray<Command> _commands = AtLeastOne(Commands, nameof(Commands));

        /// <summary>The submission's commands, in submission order; never empty.</summary>
        public EquatableArray<Command> Commands
        {
            get => _commands;
            init => _commands = AtLeastOne(value, nameof(Commands));
        }

        /// <inheritdoc/>
        [JsonIgnore]
        public override string Label =>
            string.Join(
                ", ",
                Commands
                    .GroupBy(command => command.Label, StringComparer.Ordinal)
                    .Select(group => group.Count() == 1 ? group.Key : $"{group.Key} ×{group.Count()}"));

        private static EquatableArray<Command> AtLeastOne(EquatableArray<Command> commands, string parameterName) =>
            commands.Count > 0
                ? commands
                : throw new ArgumentException("A batch names at least one command.", parameterName);
    }

    internal static Command? Of(ICommand command) => command switch
    {
        CreateCommand create => new(create.TemplateId, CreateName),
        ExerciseCommand exercise => new(exercise.TemplateId, exercise.Choice.Value),
        ExerciseByKeyCommand byKey => new(byKey.TemplateId, byKey.Choice.Value),
        CreateAndExerciseCommand createAndExercise => new(createAndExercise.TemplateId, createAndExercise.Choice.Value),
        _ => null,
    };

    internal static Command Creating(Identifier templateId) => new(templateId, CreateName);
}

internal sealed class LedgerOperationJsonConverter : JsonConverter<LedgerOperation>
{
    private const string CommandsMember = nameof(LedgerOperation.Batch.Commands);

    public override LedgerOperation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"Expected object token for {nameof(LedgerOperation)}, got {root.ValueKind}.");
        }

        LedgerOperation? read = NamesCommands(root, options)
            ? root.Deserialize<LedgerOperation.Batch>(options)
            : root.Deserialize<LedgerOperation.Command>(options);
        return read ?? throw new JsonException($"{nameof(LedgerOperation)} deserialized to null.");
    }

    public override void Write(Utf8JsonWriter writer, LedgerOperation value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case LedgerOperation.Command command:
                JsonSerializer.Serialize(writer, command, options);
                break;
            case LedgerOperation.Batch batch:
                JsonSerializer.Serialize(writer, batch, options);
                break;
            default:
                throw new JsonException($"Unhandled {nameof(LedgerOperation)} case {value.GetType().Name}.");
        }
    }

    private static bool NamesCommands(JsonElement root, JsonSerializerOptions options)
    {
        var member = options.PropertyNamingPolicy?.ConvertName(CommandsMember) ?? CommandsMember;
        var comparison = options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return root.EnumerateObject().Any(property => string.Equals(property.Name, member, comparison));
    }
}
