// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using AwesomeAssertions;
using Daml.Codegen.Intermediate.Model;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Pins that a record nested in its template as a choice argument reserves only the member names
/// a record field may not take, not the template-only ones: a field <c>templateId</c> keeps the
/// member name <c>TemplateId</c> on the nested record, where it redeclares nothing.
/// </summary>
public class EmittedChoiceArgumentFieldCompilesTests
{
    [Fact]
    public void Nested_choice_argument_field_named_like_a_template_only_member_keeps_its_name_and_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Vault",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Revise",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "Revise"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                        },
                    ],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Vault",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "Revise",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("templateId", new DamlPrimitiveType(DamlPrimitive.Text))]),
                },
            ],
            Interfaces = [],
        };

        var dar = new DarModel
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

        var files = CreateGenerator().Generate(dar).ToList();

        var emitted = string.Join("\n", files.Select(f => f.Content));
        emitted.Should().Contain("DamlFieldAttribute(\"templateId\")] string TemplateId\n",
            "a nested choice-argument record reserves record-field names only, so templateId stays TemplateId");
        emitted.Should().NotContain("TemplateId_",
            "no member of the nested record collides, so none is disambiguated");

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a choice-argument record field named like a template-only member must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }
}
