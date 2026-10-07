// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class EnumEmitterTests
{
    private const string LocalPackageId = "pkg-id";

    private static DamlPackage Package(params DamlDataType[] dataTypes) =>
        new()
        {
            PackageId = LocalPackageId,
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = "Main",
                    Templates = [],
                    DataTypes = dataTypes,
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

    private static CodeGenOptions Options(bool generateXmlDocs) =>
        new() { NamespacePrefix = "Test.Package", GenerateXmlDocs = generateXmlDocs };

    private static string EmitEnum(string name, string[] constructors, bool generateXmlDocs = true)
    {
        var enumDef = new DamlEnumDefinition(constructors);
        var dataType = new DamlDataType { Name = name, Definition = enumDef };
        var options = Options(generateXmlDocs);
        var context = PackageEmitContext.ForPackage(Package(dataType), options, isMainPackage: true).Single();
        var emitter = new EnumEmitter(options);
        var sb = new StringBuilder();
        emitter.WriteEnumType(new IndentWriter(sb), context, dataType, enumDef);
        return sb.ToString();
    }

    [Fact]
    public void EnumEmitter_emits_the_enum_declaration_with_every_constructor()
    {
        var output = EmitEnum("Status", ["Pending", "Active", "Completed", "Cancelled"]);

        output.Should().Contain("public enum Status");
        output.Should().Contain("Pending,");
        output.Should().Contain("Active,");
        output.Should().Contain("Completed,");
        output.Should().Contain("Cancelled,");
    }

    [Fact]
    public void EnumEmitter_names_extension_methods_after_their_DamlEnum_type()
    {
        var output = EmitEnum("Status", ["Pending", "Active"]);

        output.Should().Contain("public static global::Daml.Runtime.Data.DamlEnum ToDamlEnum(this global::Test.Package.Main.Status value)");
        output.Should().Contain("public static global::Test.Package.Main.Status FromDamlEnum(global::Daml.Runtime.Data.DamlEnum value)");
        output.Should().NotContain("ToRecord");
        output.Should().NotContain("FromRecord");
    }

    [Fact]
    public void EnumEmitter_round_trips_each_constructor_through_the_DamlEnum_serializers()
    {
        var output = EmitEnum("Status", ["Pending", "Active"]);

        output.Should().Contain("global::Test.Package.Main.Status.Pending => global::Daml.Runtime.Data.DamlEnum.Create(\"Pending\"),");
        output.Should().Contain("global::Test.Package.Main.Status.Active => global::Daml.Runtime.Data.DamlEnum.Create(\"Active\"),");
        output.Should().Contain("\"Pending\" => global::Test.Package.Main.Status.Pending,");
        output.Should().Contain("\"Active\" => global::Test.Package.Main.Status.Active,");
    }

    [Fact]
    public void EnumEmitter_emits_xml_docs_when_enabled()
    {
        var output = EmitEnum("Color", ["Red", "Green", "Blue"], generateXmlDocs: true);

        output.Should().Contain("/// <summary>");
        output.Should().Contain("/// Generated from Daml enum Color");
        output.Should().Contain("/// </summary>");
        output.Should().Contain("/// Extension methods for Color serialization.");
    }

    [Fact]
    public void EnumEmitter_emits_xml_docs_for_every_enum_constructor_when_enabled()
    {
        var output = EmitEnum("Color", ["Red", "Green", "Blue"], generateXmlDocs: true);

        output.Should().Contain("/// <summary>Red enum constructor.</summary>");
        output.Should().Contain("/// <summary>Green enum constructor.</summary>");
        output.Should().Contain("/// <summary>Blue enum constructor.</summary>");
    }

    [Fact]
    public void EnumEmitter_omits_the_type_xml_docs_when_disabled()
    {
        var output = EmitEnum("Color", ["Red", "Green", "Blue"], generateXmlDocs: false);

        output.Should().NotContain("/// Generated from Daml enum Color");
        output.Should().NotContain("/// Extension methods for Color serialization.");
        output.Should().NotContain("Converts to a DamlEnum value");
        output.Should().NotContain("Creates an instance from a DamlEnum value");
        output.Should().NotContain("enum constructor.</summary>");
        output.Should().Contain("public enum Color");
    }
}
