// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedRecordReadDamlLfJsonMemberShapeTests
{
    private static readonly Assembly Emitted = EmitToAssembly(Generate());

    private static IReadOnlyList<GeneratedFile> Generate()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Widget",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("name", new DamlPrimitiveType(DamlPrimitive.Text))]),
                },
                new DamlDataType
                {
                    Name = "Pair",
                    TypeParams = ["a", "b"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("first", new DamlTypeVar("a")),
                        new DamlFieldDefinition("second", new DamlTypeVar("b")),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var options = new CodeGenOptions { EnableNullableReferenceTypes = true, UseFileScopedNamespaces = true };
        return CreateGenerator(options).Generate(dar);
    }

    private static Type EmittedType(string name) => Emitted.GetTypes().Single(t => t.Name == name);

    [Fact]
    public void NonGeneric_ReadDamlLfJson_is_public_static_returns_DamlRecord_and_takes_json_then_context()
    {
        var method = EmittedType("Widget").GetMethod("__ReadDamlLfJson", BindingFlags.Public | BindingFlags.Static);

        method.Should().NotBeNull();
        method!.IsPublic.Should().BeTrue();
        method.IsStatic.Should().BeTrue();
        method.ReturnType.Should().Be<DamlRecord>();

        var parameters = method.GetParameters();
        parameters.Should().HaveCount(2);
        parameters[0].ParameterType.Should().Be<JsonElement>();
        parameters[1].ParameterType.Should().Be<DamlLfJsonDecodeContext>();
    }

    [Fact]
    public void NonGeneric_ReadDamlLfJson_carries_EditorBrowsableNever()
    {
        var method = EmittedType("Widget").GetMethod("__ReadDamlLfJson", BindingFlags.Public | BindingFlags.Static)!;

        var attribute = method.GetCustomAttribute<EditorBrowsableAttribute>();

        attribute.Should().NotBeNull();
        attribute!.State.Should().Be(EditorBrowsableState.Never);
    }

    [Fact]
    public void Generic_ReadDamlLfJson_takes_one_reader_parameter_per_type_parameter_named_by_declaration_order()
    {
        var method = EmittedType("Pair`2").GetMethod("__ReadDamlLfJson", BindingFlags.Public | BindingFlags.Static)!;

        var parameters = method.GetParameters();
        parameters.Should().HaveCount(6);
        parameters[0].ParameterType.Should().Be<JsonElement>();
        parameters[1].ParameterType.Should().Be<DamlLfJsonDecodeContext>();
        parameters[2].Name.Should().Be("readTA");
        parameters[3].Name.Should().Be("absentTA");
        parameters[3].ParameterType.Should().Be<DamlValue>();
        parameters[4].Name.Should().Be("readTB");
        parameters[5].Name.Should().Be("absentTB");
        parameters[5].ParameterType.Should().Be<DamlValue>();
    }
}
