// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// Hand-registered bindings whose choice descriptors decode to values no <c>FromDamlValue</c> reading of
/// the wire result could produce, so a parity row can tell which decoder answered. Each binding sits on
/// its own package, module and entity, so registering them disturbs no generated binding.
/// </summary>
public static class ChoiceResultParityBindings
{
    /// <summary>The choice every binding in this class declares.</summary>
    public static readonly ChoiceName Pick = new("Pick");

    /// <summary>What the template of the interface-versus-template pair decodes a result to.</summary>
    public const string DecodedByTemplate = "via-template";

    /// <summary>What the interface of the interface-versus-template pair decodes a result to.</summary>
    public const string DecodedByInterface = "via-interface";

    /// <summary>What a <see cref="DescriptorDecodedTemplate"/> descriptor decodes a result to.</summary>
    public const string DecodedByDescriptor = "from-descriptor";

    /// <summary>Owner party of the record the first version's descriptor decodes to.</summary>
    public const string DecodedByVersionOne = "via-v1";

    /// <summary>Owner party of the record the second version's descriptor decodes to.</summary>
    public const string DecodedByVersionTwo = "via-v2";

    /// <summary>Owner party of the record either duplicate's descriptor decodes to.</summary>
    public const string DecodedByDuplicate = "via-duplicate";

    /// <summary>An identifier no binding registers.</summary>
    public static RuntimeIdentifier Unbound { get; } = new("parity-pkg-unbound", "Parity", "Unbound");

    /// <summary>A package id of the versioned binding that no registered version carries.</summary>
    public const string UnregisteredVersionPackageId = "parity-pkg-v3";

    /// <summary>The first registered version of the versioned binding.</summary>
    public static RuntimeIdentifier VersionOne => VersionOneTemplate.DamlTypeId.Identifier;

    /// <summary>The second registered version of the versioned binding.</summary>
    public static RuntimeIdentifier VersionTwo => VersionTwoTemplate.DamlTypeId.Identifier;

    /// <summary>The identifier two distinct declaring types both register.</summary>
    public static RuntimeIdentifier Duplicated => DuplicateOneTemplate.DamlTypeId.Identifier;

    /// <summary>The template of the interface-versus-template pair.</summary>
    public static RuntimeIdentifier OrderedTemplate => OrderedTemplateBinding.DamlTypeId.Identifier;

    /// <summary>The interface of the interface-versus-template pair.</summary>
    public static RuntimeIdentifier OrderedInterface => OrderedInterfaceBinding.DamlTypeId.Identifier;

    /// <summary>The template whose descriptor reads its result as an <c>Int64</c>.</summary>
    public static RuntimeIdentifier Int64Decoded => Int64DecodedTemplate.DamlTypeId.Identifier;

    /// <summary>The template whose descriptor decodes to <see cref="DecodedByDescriptor"/>.</summary>
    public static RuntimeIdentifier DescriptorDecoded => DescriptorDecodedTemplate.DamlTypeId.Identifier;

    /// <summary>The record a version or duplicate descriptor decodes to, named by <paramref name="owner"/>.</summary>
    public static OptionalTails DecodedRecord(string owner) =>
        new((Party)owner, null, new TrailingNote("inner", null), null);

    /// <summary>Registers every binding of this class into the process-wide registry.</summary>
    public static void RegisterAll()
    {
        GeneratedTypeReaders.ForChoices<DescriptorDecodedTemplate>();
        GeneratedTypeReaders.ForChoices<Int64DecodedTemplate>();
        GeneratedTypeReaders.ForChoices<OrderedTemplateBinding>();
        GeneratedTypeReaders.ForChoices<OrderedInterfaceBinding>();
        GeneratedTypeReaders.ForChoices<VersionOneTemplate>();
        GeneratedTypeReaders.ForChoices<VersionTwoTemplate>();
        GeneratedTypeReaders.ForChoices<DuplicateOneTemplate>();
        GeneratedTypeReaders.ForChoices<DuplicateTwoTemplate>();
    }

    private static Choice<TOwner, DamlUnit, TResult> PickReturning<TOwner, TResult>(
        Func<DamlValue, TResult> resultDecoder,
        Func<System.Text.Json.JsonElement, DamlLfJsonDecodeContext, DamlValue> resultJsonReader)
        where TOwner : IDamlType =>
        new()
        {
            Name = Pick,
            Consuming = false,
            ArgumentEncoder = _ => DamlUnit.Instance,
            ArgumentDecoder = _ => DamlUnit.Instance,
            ResultDecoder = resultDecoder,
            ArgumentJsonReader = DamlLfJsonDecoders.ReadUnit,
            ResultJsonReader = resultJsonReader,
        };

    private static Choice<TOwner, DamlUnit, OptionalTails> PickRecordDecodedBy<TOwner>(string owner)
        where TOwner : IDamlType =>
        PickReturning<TOwner, OptionalTails>(_ => DecodedRecord(owner), OptionalTails.__ReadDamlLfJson);

    /// <summary>A template whose choice's result type is <see cref="DamlText"/>, decoded to a fixed text.</summary>
    public sealed class DescriptorDecodedTemplate : IDamlType, IHasChoices<DescriptorDecodedTemplate>
    {
        /// <inheritdoc />
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new RuntimeIdentifier("parity-pkg-descriptor", "Parity", "DescriptorDecoded"), DamlTypeKind.Template, "parity");

        /// <inheritdoc />
        public static IReadOnlyList<IChoice> Choices { get; } =
        [
            PickReturning<DescriptorDecodedTemplate, DamlText>(
                _ => new DamlText(DecodedByDescriptor), DamlLfJsonDecoders.ReadText),
        ];
    }

    /// <summary>A template whose choice's result type is <see cref="long"/>, read from a <see cref="DamlInt64"/>.</summary>
    public sealed class Int64DecodedTemplate : IDamlType, IHasChoices<Int64DecodedTemplate>
    {
        /// <inheritdoc />
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new RuntimeIdentifier("parity-pkg-int64", "Parity", "Int64Decoded"), DamlTypeKind.Template, "parity");

        /// <inheritdoc />
        public static IReadOnlyList<IChoice> Choices { get; } =
        [
            PickReturning<Int64DecodedTemplate, long>(
                value => value.As<DamlInt64>().Value, DamlLfJsonDecoders.ReadInt64),
        ];
    }

    /// <summary>A template declaring <see cref="Pick"/> that decodes to <see cref="DecodedByTemplate"/>.</summary>
    public sealed class OrderedTemplateBinding : IDamlType, IHasChoices<OrderedTemplateBinding>
    {
        /// <inheritdoc />
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new RuntimeIdentifier("parity-pkg-ordered", "Parity", "OrderedTemplate"), DamlTypeKind.Template, "parity");

        /// <inheritdoc />
        public static IReadOnlyList<IChoice> Choices { get; } =
        [
            PickReturning<OrderedTemplateBinding, string>(_ => DecodedByTemplate, DamlLfJsonDecoders.ReadText),
        ];
    }

    /// <summary>An interface declaring <see cref="Pick"/> that decodes to <see cref="DecodedByInterface"/>.</summary>
    public sealed class OrderedInterfaceBinding : IDamlType, IHasChoices<OrderedInterfaceBinding>
    {
        /// <inheritdoc />
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new RuntimeIdentifier("parity-pkg-ordered", "Parity", "OrderedInterface"), DamlTypeKind.Interface, "parity");

        /// <inheritdoc />
        public static IReadOnlyList<IChoice> Choices { get; } =
        [
            PickReturning<OrderedInterfaceBinding, string>(_ => DecodedByInterface, DamlLfJsonDecoders.ReadText),
        ];
    }

    /// <summary>The first of two versions of one module and entity, each in its own package.</summary>
    public sealed class VersionOneTemplate : IDamlType, IHasChoices<VersionOneTemplate>
    {
        /// <inheritdoc />
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new RuntimeIdentifier("parity-pkg-v1", "Parity", "Versioned"), DamlTypeKind.Template, "parity");

        /// <inheritdoc />
        public static IReadOnlyList<IChoice> Choices { get; } = [PickRecordDecodedBy<VersionOneTemplate>(DecodedByVersionOne)];
    }

    /// <summary>The second of two versions of one module and entity, each in its own package.</summary>
    public sealed class VersionTwoTemplate : IDamlType, IHasChoices<VersionTwoTemplate>
    {
        /// <inheritdoc />
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new RuntimeIdentifier("parity-pkg-v2", "Parity", "Versioned"), DamlTypeKind.Template, "parity");

        /// <inheritdoc />
        public static IReadOnlyList<IChoice> Choices { get; } = [PickRecordDecodedBy<VersionTwoTemplate>(DecodedByVersionTwo)];
    }

    /// <summary>One of two declaring types registered for the same package, module and entity.</summary>
    public sealed class DuplicateOneTemplate : IDamlType, IHasChoices<DuplicateOneTemplate>
    {
        /// <inheritdoc />
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new RuntimeIdentifier("parity-pkg-dup", "Parity", "Duplicated"), DamlTypeKind.Template, "parity");

        /// <inheritdoc />
        public static IReadOnlyList<IChoice> Choices { get; } = [PickRecordDecodedBy<DuplicateOneTemplate>(DecodedByDuplicate)];
    }

    /// <summary>The other of two declaring types registered for the same package, module and entity.</summary>
    public sealed class DuplicateTwoTemplate : IDamlType, IHasChoices<DuplicateTwoTemplate>
    {
        /// <inheritdoc />
        public static DamlTypeDescriptor DamlTypeId { get; } = DuplicateOneTemplate.DamlTypeId;

        /// <inheritdoc />
        public static IReadOnlyList<IChoice> Choices { get; } = [PickRecordDecodedBy<DuplicateTwoTemplate>(DecodedByDuplicate)];
    }
}
