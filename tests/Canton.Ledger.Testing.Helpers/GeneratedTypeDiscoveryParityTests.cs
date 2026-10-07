// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using System.Text.Json;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// Pins, on every transport, how a hand-built exercise finds the generated type that decodes its
/// choice result now that only the generated type registry discovers types: a hand-written template
/// that never registers cannot disturb a generated template's decode, and a binding that registers
/// nothing is not found. The wire building stays per-transport.
/// </summary>
public abstract class GeneratedTypeDiscoveryParityTests
{
    /// <summary>The result every exercise in this suite answers with, in its decoded form.</summary>
    protected static readonly OptionalTails EchoedTails =
        new((Party)"party::alice", null, new TrailingNote("inner", null), null);

    /// <summary>Daml-LF JSON text of <see cref="EchoedTails"/>, as the JSON Ledger API sends it.</summary>
    protected const string EchoedTailsLfJson =
        """{"owner": "party::alice", "midNote": null, "inner": {"text": "inner", "remark": null}, "tailNote": null}""";

    /// <summary>
    /// Exercises <paramref name="choice"/> on a contract of <paramref name="templateId"/> through this
    /// transport's hand-built <c>TryExerciseAsync</c>, answering <see cref="EchoedTails"/>.
    /// </summary>
    protected abstract Task<ExerciseOutcome<OptionalTails>> ExerciseAnsweringEchoedTailsAsync(
        RuntimeIdentifier templateId, ChoiceName choice);

    [Fact]
    public async Task A_loaded_hand_written_template_without_a_record_leaves_a_generated_exercise_at_One()
    {
        RuntimeHelpers.RunClassConstructor(typeof(HandWrittenTemplateWithoutRecord).TypeHandle);

        var outcome = await ExerciseAnsweringEchoedTailsAsync(
            new RuntimeIdentifier(
                "e72ec259be271bedbcb0d33f19a47008de832963a569e5a04f1ed7b8efb434e9", "RichTypes", "OptionalTails"),
            new ChoiceName("EchoOptionalTails"));

        outcome.Should().BeOfType<ExerciseOutcome<OptionalTails>.One>().Subject.Result.Should().Be(EchoedTails);
    }

    [Fact]
    public async Task A_loaded_binding_that_registers_nothing_still_decodes_to_One_through_the_result_type()
    {
        RuntimeHelpers.RunClassConstructor(typeof(PreRegistryTemplate).TypeHandle);

        var outcome = await ExerciseAnsweringEchoedTailsAsync(PreRegistryTemplate.TemplateId, new ChoiceName("Echo"));

        outcome.Should().BeOfType<ExerciseOutcome<OptionalTails>.One>().Subject.Result.Should().Be(EchoedTails);
    }

    /// <summary>A hand-written template that declares no <see cref="IDamlRecord{TSelf}"/> reader.</summary>
    public sealed record HandWrittenTemplateWithoutRecord : ITemplate
    {
        /// <inheritdoc />
        public static RuntimeIdentifier TemplateId { get; } = new("hand-written-pkg", "HandWritten", "Template");

        /// <inheritdoc />
        public static string PackageId => "hand-written-pkg";

        /// <inheritdoc />
        public static string PackageName => "hand-written";

        /// <inheritdoc />
        public static Version PackageVersion { get; } = new(0, 1, 0);

        /// <inheritdoc />
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        /// <inheritdoc />
        public DamlRecord ToRecord() => new(TemplateId, []);
    }

    /// <summary>A complete template and choice descriptor that no code ever registers.</summary>
    public sealed record PreRegistryTemplate : ITemplate, IDamlRecord<PreRegistryTemplate>
    {
        /// <inheritdoc />
        public static RuntimeIdentifier TemplateId { get; } = new("pre-registry-pkg", "PreRegistry", "Template");

        /// <inheritdoc />
        public static string PackageId => "pre-registry-pkg";

        /// <inheritdoc />
        public static string PackageName => "pre-registry";

        /// <inheritdoc />
        public static Version PackageVersion { get; } = new(0, 1, 0);

        /// <inheritdoc />
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        /// <summary>The choice that answers the echoed tails.</summary>
        public static Choice<PreRegistryTemplate, DamlUnit, OptionalTails> ChoiceEcho { get; } = new()
        {
            Name = new ChoiceName("Echo"),
            Consuming = false,
            ArgumentEncoder = _ => DamlUnit.Instance,
            ArgumentDecoder = _ => DamlUnit.Instance,
            ResultDecoder = value => OptionalTails.FromRecord(value.As<DamlRecord>()),
            ArgumentJsonReader = DamlLfJsonDecoders.ReadUnit,
            ResultJsonReader = OptionalTails.__ReadDamlLfJson,
        };

        /// <inheritdoc />
        public DamlRecord ToRecord() => new(TemplateId, []);

        /// <inheritdoc />
        public static PreRegistryTemplate FromRecord(DamlRecord record) => new();

        /// <inheritdoc />
        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            new(TemplateId, []);
    }
}
