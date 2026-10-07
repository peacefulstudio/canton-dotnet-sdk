// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class TypeReferenceQualifierTests
{
    [Theory]
    [InlineData("ContractId", "Daml.Runtime.Contracts")]
    [InlineData("ExerciseCommand", "Daml.Runtime.Commands")]
    [InlineData("IHasView", "Daml.Runtime.Contracts")]
    [InlineData("DamlRecord", "Daml.Runtime.Data")]
    [InlineData("DamlUnit", "Daml.Runtime.Data")]
    [InlineData("Party", "Daml.Runtime.Data")]
    [InlineData("Optional", "Daml.Runtime.Stdlib")]
    [InlineData("Either", "Daml.Runtime.Stdlib")]
    [InlineData("DayOfWeek", "Daml.Runtime.Stdlib")]
    [InlineData("IReadOnlyList", "System.Collections.Generic")]
    [InlineData("HashCode", "System")]
    public void Qualify_roots_every_imported_name_at_global(string simpleName, string owningNamespace)
    {
        TypeReferenceQualifier.Qualify(simpleName)
            .Should().Be($"global::{owningNamespace}.{simpleName}");
    }

    [Theory]
    [InlineData("Widget")]
    [InlineData("Gadget")]
    public void Qualify_throws_for_a_name_the_runtime_does_not_import(string simpleName)
    {
        var qualify = () => TypeReferenceQualifier.Qualify(simpleName);

        qualify.Should().Throw<CodegenException>()
            .WithMessage($"Runtime type '{simpleName}' has no owning namespace. Register it in TypeReferenceQualifier before emitting a reference to it.");
    }

    [Theory]
    [InlineData("global::Daml.Runtime.Data.DamlRecord")]
    [InlineData("Acme.Widget")]
    public void Qualify_leaves_an_already_qualified_name_unchanged(string qualifiedName)
    {
        TypeReferenceQualifier.Qualify(qualifiedName).Should().Be(qualifiedName);
    }
}
