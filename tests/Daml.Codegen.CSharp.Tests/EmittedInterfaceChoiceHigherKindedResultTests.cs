// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedInterfaceChoiceHigherKindedResultTests
{
    [Fact]
    public void Generate_should_throw_for_an_interface_choice_returning_a_type_variable_application()
    {
        var iface = new DamlInterface
        {
            Name = "Holding",
            Choices =
            [
                new DamlChoice
                {
                    Name = "Split",
                    Consuming = true,
                    ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                    ReturnType = new DamlTypeApp(new DamlTypeVar("f"), [new DamlTypeVar("a")]),
                },
            ],
        };

        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule { Name = "Test.Module", Templates = [], DataTypes = [], Interfaces = [iface] },
            ],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };

        CreateGenerator().Invoking(g => g.Generate(dar))
            .Should().Throw<CodegenException>()
            .WithMessage("*type variable applied to 1*'f'*");
    }
}
