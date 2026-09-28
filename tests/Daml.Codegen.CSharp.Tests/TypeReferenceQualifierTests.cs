// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class TypeReferenceQualifierTests
{
    [Fact]
    public void TypeReferenceQualifier_all_namespaces_expands_a_leaf_into_every_ancestor_prefix()
    {
        var qualifier = new TypeReferenceQualifier("Canton.Party.Replication");

        qualifier.AllNamespaces.Should().BeEquivalentTo(
            "Canton", "Canton.Party", "Canton.Party.Replication");
    }

    [Fact]
    public void Qualify_returns_the_bare_name_when_no_namespace_collides()
    {
        var qualifier = new TypeReferenceQualifier("Splice.Api.Token.Holding.V1");

        qualifier.Qualify("ContractId")
            .Should().Be("ContractId");
    }

    [Fact]
    public void Qualify_global_qualifies_party_when_a_namespace_segment_shadows_it()
    {
        var qualifier = new TypeReferenceQualifier("Canton.Party.Replication");

        qualifier.Qualify("Party")
            .Should().Be("global::Daml.Runtime.Data.Party");
    }

    [Fact]
    public void Qualify_leaves_party_bare_when_no_namespace_segment_shadows_it()
    {
        var qualifier = new TypeReferenceQualifier("Splice.Api.Token.Holding.V1");

        qualifier.Qualify("Party")
            .Should().Be("Party");
    }

    [Fact]
    public void Qualify_does_not_double_prefix_an_already_global_qualified_name()
    {
        var qualifier = new TypeReferenceQualifier("Canton.Party.Replication");

        qualifier.Qualify("global::Daml.Runtime.Data.Party")
            .Should().Be("global::Daml.Runtime.Data.Party");
    }

    [Fact]
    public void Qualify_leaves_unregistered_local_names_unchanged()
    {
        var qualifier = new TypeReferenceQualifier("Canton.Party.Replication");

        qualifier.Qualify("Replication")
            .Should().Be("Replication");
    }

    [Theory]
    [InlineData("ExerciseCommand", "Daml.Runtime.Commands")]
    [InlineData("CommandsSubmission", "Daml.Runtime.Commands")]
    [InlineData("IDamlInterface", "Daml.Runtime.Contracts")]
    [InlineData("IHasView", "Daml.Runtime.Contracts")]
    [InlineData("ViewDescriptor", "Daml.Runtime.Contracts")]
    [InlineData("CreatedEvent", "Daml.Runtime.Contracts")]
    [InlineData("EquatableArray", "Daml.Runtime.Contracts")]
    public void Qualify_global_qualifies_command_and_contract_names_when_a_namespace_segment_shadows_it(
        string simpleName, string owningNamespace)
    {
        var qualifier = new TypeReferenceQualifier($"Acme.{simpleName}.V1");

        qualifier.Qualify(simpleName)
            .Should().Be($"global::{owningNamespace}.{simpleName}");
    }

    [Theory]
    [InlineData("ExerciseCommand")]
    [InlineData("CommandsSubmission")]
    [InlineData("IDamlInterface")]
    [InlineData("IHasView")]
    [InlineData("ViewDescriptor")]
    [InlineData("CreatedEvent")]
    [InlineData("EquatableArray")]
    public void Qualify_leaves_command_and_contract_names_bare_when_no_namespace_segment_shadows_it(
        string simpleName)
    {
        var qualifier = new TypeReferenceQualifier("Splice.Api.Token.Holding.V1");

        qualifier.Qualify(simpleName)
            .Should().Be(simpleName);
    }

    [Theory]
    [InlineData("RelTime")]
    [InlineData("Tuple2")]
    [InlineData("Tuple3")]
    [InlineData("Either")]
    [InlineData("Set")]
    [InlineData("NonEmpty")]
    [InlineData("Map")]
    [InlineData("Optional")]
    [InlineData("GenericStub")]
    public void Qualify_global_qualifies_stdlib_names_when_a_namespace_segment_shadows_it(
        string simpleName)
    {
        var qualifier = new TypeReferenceQualifier($"Acme.{simpleName}.V1");

        qualifier.Qualify(simpleName)
            .Should().Be($"global::Daml.Runtime.Stdlib.{simpleName}");
    }

    [Theory]
    [InlineData("RelTime")]
    [InlineData("Tuple2")]
    [InlineData("Tuple3")]
    [InlineData("Either")]
    [InlineData("Set")]
    [InlineData("NonEmpty")]
    [InlineData("Map")]
    [InlineData("Optional")]
    [InlineData("GenericStub")]
    public void Qualify_leaves_stdlib_names_bare_when_no_namespace_segment_shadows_it(
        string simpleName)
    {
        var qualifier = new TypeReferenceQualifier("My.Package.Module");

        qualifier.Qualify(simpleName)
            .Should().Be(simpleName);
    }

    [Fact]
    public void Qualify_leaves_a_package_declared_unit_type_bare_because_the_runtime_has_no_stdlib_unit()
    {
        var qualifier = new TypeReferenceQualifier("Splice.Wallet.Payments", declaredTypeNames: ["Unit"]);

        qualifier.Qualify("Unit")
            .Should().Be("Unit");
    }

    [Fact]
    public void Qualify_leaves_unit_bare_when_no_declared_type_name_matches_it()
    {
        var qualifier = new TypeReferenceQualifier("Splice.Wallet.Payments", declaredTypeNames: ["Sink"]);

        qualifier.Qualify("Unit")
            .Should().Be("Unit");
    }

    [Fact]
    public void Qualify_global_qualifies_optional_when_the_package_declares_its_own_optional_type()
    {
        var qualifier = new TypeReferenceQualifier("Splice.Wallet.Payments", declaredTypeNames: ["Optional"]);

        qualifier.Qualify("Optional")
            .Should().Be("global::Daml.Runtime.Stdlib.Optional");
    }
}
