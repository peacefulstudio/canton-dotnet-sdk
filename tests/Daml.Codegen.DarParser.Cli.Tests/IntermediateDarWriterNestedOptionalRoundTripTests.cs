// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp;
using Daml.Codegen.DarParser;
using Daml.Codegen.Intermediate;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Google.Protobuf;
using Xunit;
using PbBuiltin = Daml.Codegen.Intermediate.BuiltinType;

namespace Daml.Codegen.DarParser.Cli.Tests;

/// <summary>
/// Pins the writer's generic-application path over a nested-optional tree: a model whose
/// field type is a nested <c>Optional (Optional Text)</c> spelled as plain
/// <see cref="DamlTypeApp"/> trees writes through <see cref="IntermediateDarWriter"/>
/// with no node-specific arm, the written proto reparses byte-identically, and
/// <see cref="IntermediateDarReader.Read"/> reads it back structurally equal — both
/// optional levels survive, folded into their typed node at the reader boundary.
/// </summary>
public sealed class IntermediateDarWriterNestedOptionalRoundTripTests
{
    private static DamlType RawNestedOptionalTextApplication() => new DamlTypeApp(
        new DamlPrimitiveType(DamlPrimitive.Optional),
        [
            new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.Optional),
                [new DamlPrimitiveType(DamlPrimitive.Text)]),
        ]);

    private static DarModel ModelWithNestedOptionalField() =>
        new()
        {
            MainPackage = new DamlPackage
            {
                PackageId = "1111111111111111111111111111111111111111111111111111111111111111",
                Name = "pkg",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules =
                [
                    new DamlModule
                    {
                        Name = "M",
                        Templates = [],
                        DataTypes =
                        [
                            new DamlDataType
                            {
                                Name = "Corners",
                                TypeParams = [],
                                Definition = new DamlRecordDefinition(
                                    [new DamlFieldDefinition("maybeMaybeNote", RawNestedOptionalTextApplication())]),
                            },
                        ],
                        Interfaces = [],
                    },
                ],
                DependencyReferences = [],
            },
            Dependencies = [],
        };

    [Fact]
    public void IntermediateDarWriter_nested_optional_type_app_tree_round_trips_through_the_proto()
    {
        var model = ModelWithNestedOptionalField();

        var written = IntermediateDarWriter.Write(model);
        var bytes = written.ToByteArray();

        var reparsed = IntermediateDar.Parser.ParseFrom(bytes);
        reparsed.Should().Be(written,
            "the serialized .binpb must parse back into the exact same IntermediateDar message");

        var field = written.Main.Modules.Single().DataTypes.Single().Record.Fields.Single();
        field.Type.TypeApp.Function.Builtin.Should().Be(PbBuiltin.Optional,
            "the raw DamlTypeApp tree must write through as the generic TypeApp shape — the writer " +
            "has no nested-optional-specific arm, and must not need one");
        var inner = field.Type.TypeApp.Arguments.Should().ContainSingle().Subject;
        inner.TypeApp.Function.Builtin.Should().Be(PbBuiltin.Optional,
            "the nested second optional level writes at its own TypeApp level — the chain is never " +
            "collapsed by the write");
        inner.TypeApp.Arguments.Should().ContainSingle()
            .Which.Builtin.Should().Be(PbBuiltin.Text);

        var readBack = IntermediateDarReader.Read(reparsed);
        var readField = ((DamlRecordDefinition)readBack.MainPackage.Modules[0].DataTypes[0].Definition).Fields[0];
        readField.Type.Should().Be(
            new DamlOptionalType(new DamlOptionalType(new DamlPrimitiveType(DamlPrimitive.Text))),
            "the reader folds the written generic application into its typed node at the boundary, and " +
            "the read-back tree must be structurally equal to the tree that was written: two nested " +
            "optional levels around a Text leaf, no level dropped and none invented");

        IntermediateDarWriter.Write(readBack).ToByteArray().Should().Equal(bytes,
            "the typed-node spelling must write to the identical wire bytes as the raw application " +
            "spelled them — the round trip is a byte fixed point, so the writer cannot distinguish the " +
            "two spellings of the same nested optional");
    }
}
