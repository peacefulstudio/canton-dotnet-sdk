// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class DamlFieldAttributeEmitterTests
{
    [Fact]
    public void Positional_record_property_carries_daml_field_attribute()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("count", new DamlPrimitiveType(DamlPrimitive.Int64))
                    ])
                }
            ],
            Interfaces = []
        };

        var files = CreateGenerator().Generate(CreateTestDar(module));
        var code = files.First(f => f.RelativePath.EndsWith("Holding.cs", StringComparison.Ordinal)).Content;

        code.Should().Contain("[property: global::Daml.Runtime.Data.DamlFieldAttribute(\"owner\")] global::Daml.Runtime.Data.Party Owner");
        code.Should().Contain("[property: global::Daml.Runtime.Data.DamlFieldAttribute(\"count\")] long Count");
    }
}
