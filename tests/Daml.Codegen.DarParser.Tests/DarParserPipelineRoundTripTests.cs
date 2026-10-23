// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using Daml.Codegen.CSharp;
using Daml.Codegen.Intermediate;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.DarParser;
using AwesomeAssertions;
using Xunit;
using Type = Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type;
using PbBuiltin = Daml.Codegen.Intermediate.BuiltinType;
using LfBuiltinType = Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.BuiltinType;

namespace Daml.Codegen.DarParser.Tests;

/// <summary>
/// The full .NET producer pipeline (<see cref="DalfReader"/> parse, <see cref="IntermediateDarWriter"/>
/// write, <see cref="IntermediateDarReader"/> read) on a synthetic package whose record fields carry
/// structural builtins. The synthetic route through <see cref="DalfPackageBuilder"/> is the only
/// evidence for parser-reachable positions: the splice fixture carries structural builtins only in its
/// interned pools, which <c>PackageContextSplicePoolResolutionTests</c> pins.
/// </summary>
public sealed class DarParserPipelineRoundTripTests
{
    [Fact]
    public void DarParserPipeline_synthetic_structural_builtin_fields_round_trip_with_identity_intact()
    {
        var package = BuilderOfRecordWithAllSixStructuralTypeFormers().Package;

        var parsed = DalfReader.Parse(DalfTestFixtures.SerializeArchive(package));
        var parsedRecord = (DamlRecordDefinition)parsed.Modules.Single().DataTypes.Single().Definition;
        AssertParserSurfacesRealIdentitiesNotUnitErasures(parsedRecord);

        var model = new DarModel { MainPackage = parsed, Dependencies = [] };
        var proto = IntermediateDarWriter.Write(model);
        AssertWriterEmitsIdenticalProtoBuiltinsThroughCatalogReverseLookup(proto);

        var reread = IntermediateDarReader.Read(proto);
        var rereadRecord = (DamlRecordDefinition)reread.MainPackage.Modules.Single().DataTypes.Single().Definition;
        rereadRecord.Fields.Should().HaveSameCount(parsedRecord.Fields)
            .And.OnlyContain(f => parsedRecord.Fields.Any(p =>
                p.Name == f.Name && p.Type.Equals(f.Type)),
                "every field must round-trip parse → write → read with its structural builtin identity intact");
        rereadRecord.Fields.SelectMany(f => PrimitiveLeaves(f.Type)).Should().NotContain(DamlPrimitive.Unit);
    }

    private static DalfPackageBuilder BuilderOfRecordWithAllSixStructuralTypeFormers()
    {
        var builder = new DalfPackageBuilder();
        var moduleDn = builder.DottedName("M");
        var typeDn = builder.DottedName("R");
        var record = new DefDataType.Types.Fields();
        record.Fields_.Add(Field(builder, "fAny", LfBuiltin(LfBuiltinType.Any)));
        record.Fields_.Add(Field(builder, "fTypeRep", LfBuiltin(LfBuiltinType.TypeRep)));
        record.Fields_.Add(Field(builder, "fAnyException", LfBuiltin(LfBuiltinType.AnyException)));
        record.Fields_.Add(Field(builder, "fFailureCategory", LfBuiltin(LfBuiltinType.FailureCategory)));
        record.Fields_.Add(Field(builder, "fArrow",
            LfBuiltin(LfBuiltinType.Arrow, LfBuiltin(LfBuiltinType.Text), LfBuiltin(LfBuiltinType.Int64))));
        record.Fields_.Add(Field(builder, "fUpdate", LfBuiltin(LfBuiltinType.Update, LfBuiltin(LfBuiltinType.Any))));
        var dataType = new DefDataType { NameInternedDname = typeDn, Record = record, Serializable = true };
        var module = new Module { NameInternedDname = moduleDn };
        module.DataTypes.Add(dataType);
        builder.Package.Modules.Add(module);
        return builder;
    }

    private static void AssertParserSurfacesRealIdentitiesNotUnitErasures(DamlRecordDefinition parsedRecord)
    {
        ParsedType(parsedRecord, "fAny").Should().Be(new DamlPrimitiveType(DamlPrimitive.Any));
        ParsedType(parsedRecord, "fTypeRep").Should().Be(new DamlPrimitiveType(DamlPrimitive.TypeRep));
        ParsedType(parsedRecord, "fAnyException").Should().Be(new DamlPrimitiveType(DamlPrimitive.AnyException));
        ParsedType(parsedRecord, "fFailureCategory").Should().Be(new DamlPrimitiveType(DamlPrimitive.FailureCategory));
        var parsedArrow = ParsedType(parsedRecord, "fArrow").Should().BeOfType<DamlTypeApp>().Subject;
        parsedArrow.Base.Should().Be(new DamlPrimitiveType(DamlPrimitive.Arrow));
        parsedArrow.Arguments.Should().HaveCount(2);
        parsedArrow.Arguments[0].Should().Be(new DamlPrimitiveType(DamlPrimitive.Text));
        parsedArrow.Arguments[1].Should().Be(new DamlPrimitiveType(DamlPrimitive.Int64));
        var parsedUpdate = ParsedType(parsedRecord, "fUpdate").Should().BeOfType<DamlTypeApp>().Subject;
        parsedUpdate.Base.Should().Be(new DamlPrimitiveType(DamlPrimitive.Update));
        parsedUpdate.Arguments.Should().ContainSingle()
            .Which.Should().Be(new DamlPrimitiveType(DamlPrimitive.Any));
    }

    private static void AssertWriterEmitsIdenticalProtoBuiltinsThroughCatalogReverseLookup(IntermediateDar proto)
    {
        var protoFields = proto.Main.Modules.Single().DataTypes.Single().Record.Fields
            .ToDictionary(f => f.Name, f => f.Type);
        protoFields["fAny"].Builtin.Should().Be(PbBuiltin.Any);
        protoFields["fTypeRep"].Builtin.Should().Be(PbBuiltin.TypeRep);
        protoFields["fAnyException"].Builtin.Should().Be(PbBuiltin.AnyException);
        protoFields["fFailureCategory"].Builtin.Should().Be(PbBuiltin.FailureCategory);
        protoFields["fArrow"].TypeApp.Function.Builtin.Should().Be(PbBuiltin.Arrow);
        protoFields["fUpdate"].TypeApp.Function.Builtin.Should().Be(PbBuiltin.Update);
    }

    private static DamlType ParsedType(DamlRecordDefinition record, string fieldName) =>
        record.Fields.Single(f => f.Name == fieldName).Type;

    private static FieldWithType Field(DalfPackageBuilder builder, string name, Type type) => new()
    {
        FieldInternedStr = builder.Intern(name),
        Type = type,
    };

    private static Type LfBuiltin(LfBuiltinType builtin, params Type[] args)
    {
        var type = new Type.Types.Builtin { Builtin_ = builtin };
        type.Args.AddRange(args);
        return new Type { Builtin = type };
    }

    private static IEnumerable<DamlPrimitive> PrimitiveLeaves(DamlType type)
    {
        switch (type)
        {
            case DamlPrimitiveType primitive:
                yield return primitive.Primitive;
                break;
            case DamlTypeApp app:
                foreach (var leaf in PrimitiveLeaves(app.Base))
                {
                    yield return leaf;
                }
                foreach (var argument in app.Arguments)
                {
                    foreach (var leaf in PrimitiveLeaves(argument))
                    {
                        yield return leaf;
                    }
                }
                break;
        }
    }
}
