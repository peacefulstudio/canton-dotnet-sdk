// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.DarParser;
using Daml.Codegen.Intermediate;
using AwesomeAssertions;
using Google.Protobuf;
using Xunit;
using PbBuiltin = Daml.Codegen.Intermediate.BuiltinType;
using PbDataType = Daml.Codegen.Intermediate.DataType;
using PbField = Daml.Codegen.Intermediate.Field;
using PbModule = Daml.Codegen.Intermediate.IntermediateModule;
using PbPackage = Daml.Codegen.Intermediate.IntermediatePackage;
using PbRecord = Daml.Codegen.Intermediate.Record;
using PbType = Daml.Codegen.Intermediate.Type;

namespace Daml.Codegen.DarParser.Tests;

/// <summary>
/// Shape-targeted unit tests for <see cref="IntermediateDarWriter"/> over
/// hand-built model graphs — exercising the mapping branches and throw sites
/// the single round-trip fixture does not reach.
/// </summary>
public sealed class IntermediateDarWriterTests
{
    private static DarModel SingleDataType(DamlDataType dataType, string moduleName = "M") =>
        new()
        {
            MainPackage = Package([Module(moduleName, dataTypes: [dataType])]),
            Dependencies = [],
        };

    private static DamlPackage Package(IReadOnlyList<DamlModule> modules, string packageId = "1111111111111111111111111111111111111111111111111111111111111111") =>
        new()
        {
            PackageId = packageId,
            Name = "pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = modules,
            DependencyReferences = [],
        };

    private static DamlModule Module(
        string name,
        IReadOnlyList<DamlDataType>? dataTypes = null,
        IReadOnlyList<DamlTemplate>? templates = null,
        IReadOnlyList<DamlInterface>? interfaces = null) =>
        new()
        {
            Name = name,
            DataTypes = dataTypes ?? [],
            Templates = templates ?? [],
            Interfaces = interfaces ?? [],
        };

    private static DamlDataType Record(string name) =>
        new() { Name = name, Definition = new DamlRecordDefinition([]) };

    private static DarModel SingleInterface(DamlInterface @interface, string moduleName = "M") =>
        new()
        {
            MainPackage = Package([Module(moduleName, interfaces: [@interface])]),
            Dependencies = [],
        };

    private static DamlInterface Interface(
        string name,
        IReadOnlyList<DamlInterfaceMethod>? methods = null,
        IReadOnlyList<DamlChoice>? choices = null) =>
        new() { Name = name, Choices = choices ?? [], Methods = methods ?? [] };

    /// <summary>
    /// Every catalog row that carries a model identity — the
    /// <see cref="DamlPrimitiveDisposition.SupportedValue"/> and
    /// <see cref="DamlPrimitiveDisposition.SignatureOnly"/> rows, i.e. every builtin the
    /// <c>IntermediateDarReader</c> accepts — paired with the identical proto value it
    /// must write back to. The row set is keyed structurally on the row's disposition,
    /// matching the writer's own reverse-map filter.
    /// </summary>
    public static TheoryData<DamlPrimitive, BuiltinType> CatalogedPrimitiveRows()
    {
        var rows = new TheoryData<DamlPrimitive, BuiltinType>();
        foreach (var row in DamlPrimitiveCatalog.Rows
                     .Where(row => row.Disposition != DamlPrimitiveDisposition.Unsupported))
        {
            rows.Add(row.Primitive!.Value, row.Builtin);
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(CatalogedPrimitiveRows))]
    public void IntermediateDarWriter_cataloged_builtin_writes_back_to_its_identical_proto_value(
        DamlPrimitive primitive,
        BuiltinType expectedBuiltin)
    {
        var model = SingleDataType(new DamlDataType
        {
            Name = "RoundTrip",
            Definition = new DamlRecordDefinition(
                [new DamlFieldDefinition("position", new DamlPrimitiveType(primitive))]),
        });

        var proto = IntermediateDarWriter.Write(model);

        var field = proto.Main.Modules.Single().DataTypes.Single().Record.Fields.Single();
        field.Type.Builtin.Should().Be(expectedBuiltin,
            "the writer is the catalog's reverse lookup: every builtin the reader accepts must write back " +
            "to the identical proto BuiltinType value");
    }

    /// <summary>
    /// The five typed nodes, each paired with the canonical <c>TypeApp(builtin, args)</c>
    /// encoding it must unfold back to on the wire — the same encoding the reader folds
    /// in from, so a node the writer produces round-trips through the reader unchanged.
    /// </summary>
    public static TheoryData<DamlType, BuiltinType> TypedNodesWithTheirUnfoldedBuiltins() => new()
    {
        { new DamlListType(new DamlPrimitiveType(DamlPrimitive.Text)), PbBuiltin.List },
        { new DamlOptionalType(new DamlPrimitiveType(DamlPrimitive.Text)), PbBuiltin.Optional },
        { new DamlTextMapType(new DamlPrimitiveType(DamlPrimitive.Int64)), PbBuiltin.TextMap },
        { new DamlContractIdType(new DamlPrimitiveType(DamlPrimitive.Text)), PbBuiltin.ContractId },
        {
            new DamlGenMapType(new DamlPrimitiveType(DamlPrimitive.Party), new DamlPrimitiveType(DamlPrimitive.Int64)),
            PbBuiltin.GenMap
        },
    };

    [Theory]
    [MemberData(nameof(TypedNodesWithTheirUnfoldedBuiltins))]
    public void IntermediateDarWriter_typed_node_unfolds_to_the_canonical_type_app_encoding(
        DamlType node,
        BuiltinType expectedBuiltin)
    {
        var model = SingleDataType(new DamlDataType
        {
            Name = "Unfolded",
            Definition = new DamlRecordDefinition([new DamlFieldDefinition("position", node)]),
        });

        var proto = IntermediateDarWriter.Write(model);

        var field = proto.Main.Modules.Single().DataTypes.Single().Record.Fields.Single();
        field.Type.SortCase.Should().Be(PbType.SortOneofCase.TypeApp);
        field.Type.TypeApp.Function.Builtin.Should().Be(expectedBuiltin,
            "the typed node is the wire's TypeApp(builtin, args) application spelled as a first-class " +
            "model node; the writer unfolds it back to the identical canonical encoding");
        var expectedArity = DamlPrimitiveCatalog.Get(expectedBuiltin).Arity;
        field.Type.TypeApp.Arguments.Should().HaveCount(expectedArity);

        var reread = IntermediateDarReader.Read(proto);
        var rereadField = ((DamlRecordDefinition)reread.MainPackage.Modules[0].DataTypes[0].Definition).Fields[0];
        rereadField.Type.Should().Be(node,
            "what the writer unfolds, the reader folds back in: write→read reproduces the typed node");
    }

    /// <summary>
    /// The typed-node trees the byte-equality oracle pins: the five nodes over simple
    /// payloads plus nested combinations of them. Each tree and the legacy
    /// <see cref="DamlTypeApp"/> shape that spells the same Daml type are two spellings
    /// of one wire encoding — the writer must not be able to tell them apart.
    /// </summary>
    public static TheoryData<DamlType> TypedNodeTrees() => new()
    {
        new DamlListType(new DamlPrimitiveType(DamlPrimitive.Text)),
        new DamlOptionalType(new DamlPrimitiveType(DamlPrimitive.Text)),
        new DamlTextMapType(new DamlPrimitiveType(DamlPrimitive.Int64)),
        new DamlGenMapType(new DamlPrimitiveType(DamlPrimitive.Party), new DamlPrimitiveType(DamlPrimitive.Int64)),
        new DamlContractIdType(new DamlTypeRef("2222222222222222222222222222222222222222222222222222222222222222", "Mod", "T")),
        new DamlListType(new DamlOptionalType(new DamlTextMapType(new DamlPrimitiveType(DamlPrimitive.Int64)))),
        new DamlGenMapType(
            new DamlPrimitiveType(DamlPrimitive.Party),
            new DamlListType(new DamlOptionalType(new DamlPrimitiveType(DamlPrimitive.Int64)))),
        new DamlTextMapType(new DamlListType(new DamlContractIdType(new DamlTypeRef("2222222222222222222222222222222222222222222222222222222222222222", "Mod", "T")))),
        new DamlOptionalType(new DamlGenMapType(
            new DamlPrimitiveType(DamlPrimitive.Int64),
            new DamlPrimitiveType(DamlPrimitive.Text))),
        new DamlListType(new DamlListType(new DamlListType(new DamlPrimitiveType(DamlPrimitive.Party)))),
    };

    private static DamlType LegacyTypeAppShape(DamlType type) => type switch
    {
        DamlListType list => new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.List), [LegacyTypeAppShape(list.Element)]),
        DamlOptionalType optional => new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.Optional), [LegacyTypeAppShape(optional.Value)]),
        DamlTextMapType textMap => new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.TextMap), [LegacyTypeAppShape(textMap.Value)]),
        DamlGenMapType genMap => new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.GenMap),
            [LegacyTypeAppShape(genMap.Key), LegacyTypeAppShape(genMap.Value)]),
        DamlContractIdType contractId => new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.ContractId), [LegacyTypeAppShape(contractId.Payload)]),
        _ => type,
    };

    private static DarModel SingleRecordWithType(DamlType position) => SingleDataType(new DamlDataType
    {
        Name = "Unfolded",
        Definition = new DamlRecordDefinition([new DamlFieldDefinition("position", position)]),
    });

    private static PbType WrittenFieldType(DamlType position) =>
        IntermediateDarWriter.Write(SingleRecordWithType(position))
            .Main.Modules.Single().DataTypes.Single().Record.Fields.Single().Type;

    [Theory]
    [MemberData(nameof(TypedNodeTrees))]
    public void IntermediateDarWriter_typed_node_encodes_byte_identically_to_the_equivalent_hand_built_type_app(
        DamlType node)
    {
        var typedBytes = WrittenFieldType(node).ToByteArray();
        var legacyBytes = WrittenFieldType(LegacyTypeAppShape(node)).ToByteArray();

        typedBytes.Should().Equal(legacyBytes,
            "the typed node is the wire's TypeApp(builtin, args) application spelled as a first-class model " +
            "node; a correct unfolding encodes the written Type subtree exactly as the hand-built legacy " +
            "DamlTypeApp(DamlPrimitiveType(builtin), args) shape encodes it — down to the byte, so a flipped " +
            "GenMap key/value order or an extra Optional wrapper cannot pass");
    }

    [Theory]
    [MemberData(nameof(TypedNodeTrees))]
    public void IntermediateDarWriter_typed_node_write_read_write_cycle_is_a_byte_fixed_point(DamlType node)
    {
        var firstBytes = IntermediateDarWriter.Write(SingleRecordWithType(node)).ToByteArray();

        var reread = IntermediateDarReader.Read(IntermediateDar.Parser.ParseFrom(firstBytes));
        var rereadField = ((DamlRecordDefinition)reread.MainPackage.Modules[0].DataTypes[0].Definition).Fields[0];
        rereadField.Type.Should().Be(node,
            "what the writer unfolds, the reader folds back in: the re-read model re-normalizes to the same " +
            "typed node");

        var secondBytes = IntermediateDarWriter.Write(reread).ToByteArray();
        secondBytes.Should().Equal(firstBytes,
            "model → write → read → write is a fixed point: the second write reproduces the first write's " +
            "bytes exactly");
    }

    [Fact]
    public void IntermediateDarWriter_nested_typed_nodes_unfold_recursively()
    {
        var nested = new DamlListType(new DamlOptionalType(new DamlTextMapType(
            new DamlPrimitiveType(DamlPrimitive.Int64))));
        var model = SingleDataType(new DamlDataType
        {
            Name = "NestedUnfolded",
            Definition = new DamlRecordDefinition([new DamlFieldDefinition("tree", nested)]),
        });

        var proto = IntermediateDarWriter.Write(model);

        var field = proto.Main.Modules.Single().DataTypes.Single().Record.Fields.Single();
        field.Type.TypeApp.Function.Builtin.Should().Be(PbBuiltin.List);
        var optional = field.Type.TypeApp.Arguments.Should().ContainSingle().Subject;
        optional.TypeApp.Function.Builtin.Should().Be(PbBuiltin.Optional);
        var textMap = optional.TypeApp.Arguments.Should().ContainSingle().Subject;
        textMap.TypeApp.Function.Builtin.Should().Be(PbBuiltin.TextMap);
        textMap.TypeApp.Arguments.Should().ContainSingle()
            .Which.Builtin.Should().Be(PbBuiltin.Int64);

        var reread = IntermediateDarReader.Read(proto);
        var rereadField = ((DamlRecordDefinition)reread.MainPackage.Modules[0].DataTypes[0].Definition).Fields[0];
        rereadField.Type.Should().Be(nested);
    }

    [Fact]
    public void IntermediateDarWriter_every_reader_accepted_builtin_survives_read_write_read_round_trip()
    {
        var accepted = DamlPrimitiveCatalog.Rows
            .Where(row => row.Disposition != DamlPrimitiveDisposition.Unsupported)
            .OrderBy(row => row.Builtin)
            .ToList();
        accepted.Should().HaveCount(19,
            "the catalog accepts the 13 SupportedValue rows plus the 6 SignatureOnly rows — the composed " +
            "round trip must exercise every one of them");

        var record = new PbRecord();
        for (var i = 0; i < accepted.Count; i++)
        {
            record.Fields.Add(new PbField { Name = $"f{i}", Type = new PbType { Builtin = accepted[i].Builtin } });
        }
        var proto = new IntermediateDar
        {
            Main = new PbPackage
            {
                PackageId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                PackageName = "test",
                PackageVersion = "1.0.0",
                LanguageVersion = "2.1",
                Modules = { new PbModule { NameSegments = { "M" }, DataTypes =
                {
                    new PbDataType { Name = "RoundTrip", IsSerializable = false, Record = record },
                } } },
            },
        };

        var model = IntermediateDarReader.Read(proto);
        var modelFields = ((DamlRecordDefinition)model.MainPackage.Modules[0].DataTypes[0].Definition).Fields;

        var written = IntermediateDarWriter.Write(model);
        var writtenFields = written.Main.Modules.Single().DataTypes.Single().Record.Fields;

        for (var i = 0; i < accepted.Count; i++)
        {
            modelFields[i].Type.Should().Be(new DamlPrimitiveType(accepted[i].Primitive!.Value),
                $"{accepted[i].Builtin} must read into its real model identity");
            writtenFields[i].Type.Builtin.Should().Be(accepted[i].Builtin,
                $"{accepted[i].Builtin} must write back to the identical proto BuiltinType value");
        }

        var bytes = written.ToByteArray();
        IntermediateDarWriter.Write(model).ToByteArray().Should().Equal(bytes,
            "the writer must emit byte-stable proto output for the same model");
        var reparsed = IntermediateDar.Parser.ParseFrom(bytes);
        reparsed.Should().Be(written,
            "the serialized .binpb must parse back into the exact same IntermediateDar message");

        var reread = IntermediateDarReader.Read(reparsed);
        var rereadFields = ((DamlRecordDefinition)reread.MainPackage.Modules[0].DataTypes[0].Definition).Fields;
        for (var i = 0; i < accepted.Count; i++)
        {
            rereadFields[i].Type.Should().Be(modelFields[i].Type,
                $"read -> write -> read must preserve {accepted[i].Builtin}'s model identity");
        }
    }

    [Fact]
    public void IntermediateDarWriter_uncataloged_primitive_throws_not_supported()
    {
        var model = SingleDataType(new DamlDataType
        {
            Name = "BeyondCatalog",
            Definition = new DamlRecordDefinition(
                [new DamlFieldDefinition("position", new DamlPrimitiveType((DamlPrimitive)99))]),
        });

        var act = () => IntermediateDarWriter.Write(model);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*99*DamlPrimitiveCatalog*",
                "a DamlPrimitive with no catalog row must fail loudly, naming the primitive and pointing " +
                "at the catalog as the place to add the row");
    }

    [Fact]
    public void IntermediateDarWriter_interface_methods_round_trip_through_the_reader_exactly()
    {
        var wireOrderedMethodsWithMixedReturnTypes = new List<DamlInterfaceMethod>
        {
            new("holder", new DamlPrimitiveType(DamlPrimitive.Party)),
            new("lock", new DamlOptionalType(
                new DamlTypeRef("2222222222222222222222222222222222222222222222222222222222222222", "Splice.Api.Token.HoldingV1", "Lock"))),
            new("owner", new DamlTypeRef("2222222222222222222222222222222222222222222222222222222222222222", "Other.Module", "Owner")),
        };
        var model = SingleInterface(Interface("Holding", wireOrderedMethodsWithMixedReturnTypes));

        var bytes = IntermediateDarWriter.Write(model).ToByteArray();
        var reread = IntermediateDarReader.Read(IntermediateDar.Parser.ParseFrom(bytes));

        var iface = reread.MainPackage.Modules.Single().Interfaces.Single();
        iface.Methods.Should().Equal(wireOrderedMethodsWithMixedReturnTypes,
            "write -> read must preserve every interface method's name, return type and wire order exactly");

        IntermediateDarWriter.Write(model).ToByteArray().Should().Equal(bytes,
            "the writer must emit byte-stable proto output for the same model");
    }

    [Fact]
    public void IntermediateDarWriter_unsorted_interface_methods_are_written_in_ordinal_sorted_order()
    {
        var unsortedMethodsMixingNameCases = new List<DamlInterfaceMethod>
        {
            new("withdraw", new DamlPrimitiveType(DamlPrimitive.Party)),
            new("accept", new DamlPrimitiveType(DamlPrimitive.Text)),
            new("Merge", new DamlPrimitiveType(DamlPrimitive.Bool)),
        };
        var model = SingleInterface(Interface("Instruction", unsortedMethodsMixingNameCases));

        var firstBytes = IntermediateDarWriter.Write(model).ToByteArray();
        var written = IntermediateDar.Parser.ParseFrom(firstBytes);
        var iface = written.Main.Modules.Single().Interfaces.Single();
        iface.Methods.Select(m => m.Name).Should().Equal(
            ["Merge", "accept", "withdraw"],
            "the writer applies the same ordinal sort-on-write discipline as choices, matching the JVM " +
            "producer's sort-by-name");

        var reread = IntermediateDarReader.Read(written);
        var rereadMethods = reread.MainPackage.Modules.Single().Interfaces.Single().Methods;
        rereadMethods.Select(m => m.Name).Should().Equal(["Merge", "accept", "withdraw"],
            "write -> read of an unsorted list yields the ordinal-sorted order");
        rereadMethods.Select(m => m.ReturnType).Should().Equal(
            unsortedMethodsMixingNameCases.OrderBy(m => m.Name, StringComparer.Ordinal).Select(m => m.ReturnType),
            "each method keeps its own return type through the sort");

        IntermediateDarWriter.Write(model).ToByteArray().Should().Equal(firstBytes,
            "two writes of the same unsorted model must be byte-identical — the sort makes the output " +
            "independent of the input list's order");
    }

    [Fact]
    public void IntermediateDarWriter_signature_only_update_return_writes_back_with_real_identity()
    {
        var updateReturnTypeOverSelfPackageReference = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.Update),
            [new DamlTypeRef("3333333333333333333333333333333333333333333333333333333333333333", "Foo", "X")]);
        var model = SingleInterface(Interface("Instruction",
            [new("transferInstruction_acceptImpl", updateReturnTypeOverSelfPackageReference)]));

        var written = IntermediateDarWriter.Write(model);
        var protoMethod = written.Main.Modules.Single().Interfaces.Single().Methods.Single();
        protoMethod.Name.Should().Be("transferInstruction_acceptImpl");
        protoMethod.ReturnType.TypeApp.Function.Builtin.Should().Be(PbBuiltin.Update,
            "the signature-only Update builtin writes back through the catalog's reverse lookup, never as a Unit erasure");

        var bytes = written.ToByteArray();
        var reread = IntermediateDarReader.Read(IntermediateDar.Parser.ParseFrom(bytes));
        var rereadMethod = reread.MainPackage.Modules.Single().Interfaces.Single().Methods.Single();
        rereadMethod.ReturnType.Should().Be(updateReturnTypeOverSelfPackageReference,
            "the applied Update return type must re-read with its real identity, byte-for-byte the same model");
    }

    [Fact]
    public void IntermediateDarWriter_jvm_transfer_instruction_v2_round_trip_is_a_fixed_point()
    {
        var snapshotPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures", "Snapshots", "splice-api-token-transfer-instruction-v2", "intermediate.binpb");
        var jvmProducedWire = IntermediateDar.Parser.ParseFrom(File.ReadAllBytes(snapshotPath));

        var wireInstruction = WireMethods(jvmProducedWire, "TransferInstruction");
        var wireFactory = WireMethods(jvmProducedWire, "TransferFactory");
        wireInstruction.Should().HaveCount(6,
            "the TransferInstruction interface carries 6 methods on the wire (see the M3 reader fixture)");
        wireFactory.Should().HaveCount(3,
            "the TransferFactory interface carries 3 methods on the wire");

        var model = IntermediateDarReader.Read(jvmProducedWire);
        var writtenBytes = IntermediateDarWriter.Write(model).ToByteArray();
        var written = IntermediateDar.Parser.ParseFrom(writtenBytes);
        WireMethods(written, "TransferInstruction").Should().Equal(wireInstruction,
            "the JVM producer's pre-sorted wire order is preserved exactly — the writer's ordinal " +
            "sort-on-write re-emits it unchanged");
        WireMethods(written, "TransferFactory").Should().Equal(wireFactory);

        var reread = IntermediateDarReader.Read(written);
        var module = reread.MainPackage.Modules.Single(m => m.Name == "Splice.Api.Token.TransferInstructionV2");
        var firstInstruction = model.MainPackage.Modules
            .Single(m => m.Name == "Splice.Api.Token.TransferInstructionV2")
            .Interfaces.Single(i => i.Name == "TransferInstruction");
        var instruction = module.Interfaces.Single(i => i.Name == "TransferInstruction");
        instruction.Methods.Should().Equal(firstInstruction.Methods,
            "read -> write -> read of a JVM-produced intermediate is a fixed point for TransferInstruction");
        var factory = module.Interfaces.Single(i => i.Name == "TransferFactory");
        var firstFactory = model.MainPackage.Modules
            .Single(m => m.Name == "Splice.Api.Token.TransferInstructionV2")
            .Interfaces.Single(i => i.Name == "TransferFactory");
        factory.Methods.Should().Equal(firstFactory.Methods,
            "read -> write -> read of a JVM-produced intermediate is a fixed point for TransferFactory");

        IntermediateDarWriter.Write(reread).ToByteArray().Should().Equal(writtenBytes,
            "writing the re-read model reproduces the same bytes — the fixed point is stable, and " +
            "the write is deterministic on JVM-produced input");
    }

    private static IReadOnlyList<string> WireMethods(IntermediateDar proto, string interfaceName) =>
        proto.Main.Modules
            .Single(m => string.Join(".", m.NameSegments) == "Splice.Api.Token.TransferInstructionV2")
            .Interfaces.Single(i => i.Name == interfaceName)
            .Methods.Select(m => m.Name).ToList();

    [Fact]
    public void IntermediateDarWriter_implements_preserves_multi_segment_module_and_name_boundary()
    {
        var template = new DamlTemplate
        {
            Name = "Asset",
            Choices = [],
            Implements = [new DamlTypeRef("2222222222222222222222222222222222222222222222222222222222222222", "Splice.Api.Token.HoldingV1", "Holding")],
        };
        var model = new DarModel
        {
            MainPackage = Package([Module("M", templates: [template])]),
            Dependencies = [],
        };

        var proto = IntermediateDarWriter.Write(model);

        var implemented = proto.Main.Modules.Single().Templates.Single().Implements.Single();
        implemented.ModuleNameSegments.Should().Equal("Splice", "Api", "Token", "HoldingV1");
        implemented.NameSegments.Should().Equal("Holding");
        implemented.PackageId.Should().Be("2222222222222222222222222222222222222222222222222222222222222222");
    }

    [Fact]
    public void IntermediateDarWriter_numeric_type_var_is_emitted_as_nat()
    {
        var dataType = new DamlDataType
        {
            Name = "Wrapper",
            Definition = new DamlRecordDefinition([new DamlFieldDefinition("f", new DamlTypeVar("10"))]),
        };

        var proto = IntermediateDarWriter.Write(SingleDataType(dataType));

        var fieldType = proto.Main.Modules.Single().DataTypes.Single().Record.Fields.Single().Type;
        fieldType.Nat.Should().Be(10);
    }

    [Fact]
    public void IntermediateDarWriter_non_numeric_type_var_is_emitted_as_type_var()
    {
        var dataType = new DamlDataType
        {
            Name = "Wrapper",
            Definition = new DamlRecordDefinition([new DamlFieldDefinition("f", new DamlTypeVar("a"))]),
        };

        var proto = IntermediateDarWriter.Write(SingleDataType(dataType));

        var fieldType = proto.Main.Modules.Single().DataTypes.Single().Record.Fields.Single().Type;
        fieldType.TypeVar.Should().Be("a");
    }

    [Fact]
    public void IntermediateDarWriter_nullary_variant_constructor_is_emitted_as_unit()
    {
        var dataType = new DamlDataType
        {
            Name = "Choice",
            Definition = new DamlVariantDefinition([new DamlVariantConstructor("None", ArgumentType: null)]),
        };

        var proto = IntermediateDarWriter.Write(SingleDataType(dataType));

        var constructor = proto.Main.Modules.Single().DataTypes.Single().Variant.Constructors.Single();
        constructor.Type.Builtin.Should().Be(PbBuiltin.Unit);
    }

    [Fact]
    public void IntermediateDarWriter_two_part_version_is_padded_to_three_components()
    {
        var package = new DamlPackage
        {
            PackageId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            Name = "pkg",
            Version = new Version(1, 2),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = [],
        };
        var model = new DarModel { MainPackage = package, Dependencies = [] };

        var proto = IntermediateDarWriter.Write(model);

        proto.Main.PackageVersion.Should().Be("1.2.0");
    }

    [Fact]
    public void IntermediateDarWriter_writes_the_raw_version_verbatim_when_the_package_carries_one()
    {
        var package = new DamlPackage
        {
            PackageId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            Name = "daml-stdlib",
            Version = new Version(3, 3, 0),
            RawVersion = "3.3.0.20250502.13767.0",
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = [],
        };
        var model = new DarModel { MainPackage = package, Dependencies = [] };

        var proto = IntermediateDarWriter.Write(model);

        proto.Main.PackageVersion.Should().Be("3.3.0.20250502.13767.0");
    }

    [Fact]
    public void IntermediateDarWriter_modules_data_types_and_dependencies_are_emitted_in_sorted_order()
    {
        var model = new DarModel
        {
            MainPackage = Package(
            [
                Module("Zebra", dataTypes: [Record("Yak"), Record("Ant")]),
                Module("Apple"),
            ]),
            Dependencies =
            [
                Package([], packageId: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"),
                Package([], packageId: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
            ],
        };

        var proto = IntermediateDarWriter.Write(model);

        proto.Main.Modules.Select(m => string.Join(".", m.NameSegments))
            .Should().Equal("Apple", "Zebra");
        proto.Main.Modules.Single(m => m.NameSegments[0] == "Zebra")
            .DataTypes.Select(d => d.Name).Should().Equal("Ant", "Yak");
        proto.Dependencies.Select(p => p.PackageId).Should().Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff");
    }

    [Fact]
    public void IntermediateDarWriter_dar_without_dependencies_writes_no_dependency_entries()
    {
        var proto = IntermediateDarWriter.Write(SingleDataType(Record("R")));

        proto.Dependencies.Should().BeEmpty();
    }

    [Fact]
    public void IntermediateDarWriter_unmapped_data_type_definition_throws_not_supported()
    {
        var dataType = new DamlDataType
        {
            Name = "Mystery",
            Definition = new UnmappedDefinition(),
        };

        var act = () => IntermediateDarWriter.Write(SingleDataType(dataType));

        act.Should().Throw<NotSupportedException>().WithMessage("*Mystery*");
    }

    [Fact]
    public void IntermediateDarWriter_null_source_throws_argument_null()
    {
        var act = () => IntermediateDarWriter.Write(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IntermediateDarWriter_unmapped_type_node_throws_not_supported()
    {
        var model = SingleDataType(new DamlDataType
        {
            Name = "Mystery",
            Definition = new DamlRecordDefinition([new DamlFieldDefinition("position", new UnmappedTypeNode())]),
        });

        var act = () => IntermediateDarWriter.Write(model);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*UnmappedTypeNode*IntermediateDarWriter.WriteType*",
                "a DamlType subtype with no wire mapping must fail loudly through the WriteType catch-all, " +
                "naming the node and pointing at the writer as the place to add the arm alongside the proto " +
                "schema");
    }

    private sealed record UnmappedTypeNode : DamlType
    {
        public override TResult Accept<TResult>(IDamlTypeVisitor<TResult> visitor) =>
            throw new NotSupportedException("Test-only node: no visitor arm exists.");
    }

    private sealed record UnmappedDefinition : DamlDataTypeDefinition;
}
