// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Pins that a payload <c>Party</c> field whose PascalCased name equals its own
/// template's name is named identically everywhere it is emitted. The record
/// emitter has to rename such a property — a member may not share the name of
/// its enclosing type (CS0542) — so <c>steward</c> on template <c>Steward</c> is
/// declared as <c>Steward_</c>. Every site that reads the party back off the
/// payload has to agree, or the emitted code names a property that was never
/// declared and fails to compile with CS1061.
///
/// <para>
/// Four emission sites read a party off a payload, and each one is reached by a
/// different party shape: a single static signatory, several static signatories,
/// a static observer clause, and a static choice controller reached through the
/// fetched-contract exerciser overload. The fixture below drives all four in one
/// package, because each was written independently and only the compile gate
/// proves they still agree.
/// </para>
/// </summary>
public class EmittedPayloadPropertyCollidingWithTemplateNameCompilesTests
{
    private static DamlFieldDefinition PartyField(string name) =>
        new(name, new DamlPrimitiveType(DamlPrimitive.Party));

    private static DarModel CollidingPayloadPropertyDar()
    {
        var stewardFields = new[] { PartyField("steward"), PartyField("deputy") };
        var wardenFields = new[] { PartyField("warden"), PartyField("deputy") };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Steward",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Revise",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf("Steward"),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("steward")]),
                        },
                    ],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("steward")]),
                    Observers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("steward")]),
                },
                new DamlTemplate
                {
                    Name = "Warden",
                    Choices = [],
                    Signatories = DamlPartyAnalysis.Static(
                        [new DamlPartyPayloadField("warden"), new DamlPartyPayloadField("deputy")]),
                    Observers = DamlPartyAnalysis.Dynamic,
                },
            ],
            DataTypes =
            [
                new DamlDataType { Name = "Steward", Definition = new DamlRecordDefinition(stewardFields) },
                new DamlDataType { Name = "Warden", Definition = new DamlRecordDefinition(wardenFields) },
            ],
            Interfaces = [],
        };

        return new DarModel
        {
            MainPackage = new DamlPackage
            {
                PackageId = "test-package-id",
                Name = "test-package",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules = [module],
                DependencyReferences = [],
            },
            Dependencies = [],
        };
    }

    [Fact]
    public void Emitted_payload_property_colliding_with_its_template_name_is_declared_renamed()
    {
        var steward = CreateGenerator().Generate(CollidingPayloadPropertyDar())
            .Single(f => f.RelativePath.EndsWith("Steward.cs", StringComparison.Ordinal));

        steward.Content.Should().Contain(
            "Party Steward_",
            "the record emitter must rename a property that would otherwise share its enclosing type's name; "
            + "if this fixture stops colliding the compile gate below passes without proving anything");
    }

    [Fact]
    public void Emitted_multi_signatory_submitter_reads_every_payload_party_by_its_declared_name()
    {
        var warden = CreateGenerator().Generate(CollidingPayloadPropertyDar())
            .Single(f => f.RelativePath.EndsWith("Warden.cs", StringComparison.Ordinal));

        warden.Content.Should().Contain(
            "new global::Daml.Runtime.Commands.SubmitterInfo(new global::System.Collections.Generic.HashSet<global::Daml.Runtime.Data.Party>",
            "a template with several static signatories derives its submitter from a party set");
        warden.Content.Should().Contain(
            "payload.Warden_",
            "the signatory colliding with the template name has to be read through the renamed property "
            + "the record emitter declared");
        warden.Content.Should().Contain(
            "payload.Deputy",
            "emitting the same payload property for both signatories still compiles, so the compile gate "
            + "cannot see it; naming each signatory separately is what pins that the loop reads distinct fields");
    }

    [Fact]
    public void Emitted_code_reading_a_colliding_payload_party_off_the_payload_compiles()
    {
        var files = CreateGenerator().Generate(CollidingPayloadPropertyDar());

        var errors = CompileEmittedFiles(files)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        errors.Should().BeEmpty(
            "every site reading a party off the payload must name the property the record emitter declared, "
            + "not the undisambiguated PascalCased field name; "
            + $"got: {string.Join("; ", errors.Select(d => d.ToString()))}");
    }
}
