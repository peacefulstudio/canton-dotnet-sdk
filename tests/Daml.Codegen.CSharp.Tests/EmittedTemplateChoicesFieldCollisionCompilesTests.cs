// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Pins that a template payload field PascalCasing to <c>Choices</c> does not collide with
/// the template's own static <c>IHasChoices&lt;TSelf&gt;.Choices</c> witness. Unlike the view
/// record's marker enrichment (see <see cref="EmittedViewFieldCollisionCompilesTests"/>), a
/// template cannot simply leave itself un-stamped — <c>IHasChoices&lt;TSelf&gt;</c> is always
/// part of a template's base list — so the witness degrades to an explicit interface
/// implementation instead, the same escape hatch the key witness already uses for a payload
/// field named <c>key</c>.
/// </summary>
public class EmittedTemplateChoicesFieldCollisionCompilesTests
{
    private const string TemplateName = "Vault";
    private const string TemplateFileName = "Vault.cs";

    private static IReadOnlyList<GeneratedFile> EmitTemplateWithChoicesField(string fieldName)
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = TemplateName,
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Grant",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = TemplateName,
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition(fieldName, new DamlPrimitiveType(DamlPrimitive.Text)),
                    ]),
                },
            ],
            Interfaces = [],
        };

        return CreateGenerator(
            new CodeGenOptions
            {
                EnableNullableReferenceTypes = true,
                UseFileScopedNamespaces = true,
            })
            .Generate(CreateTestDar(module));
    }

    private static string SourceOf(IReadOnlyList<GeneratedFile> files, string fileName) =>
        files.Single(f => f.RelativePath.EndsWith(fileName, StringComparison.Ordinal)).Content;

    private static IReadOnlyList<GeneratedFile> EmitTemplateNamedChoices()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Choices",
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Grant",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Choices",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                    ]),
                },
            ],
            Interfaces = [],
        };

        return CreateGenerator(
            new CodeGenOptions
            {
                EnableNullableReferenceTypes = true,
                UseFileScopedNamespaces = true,
            })
            .Generate(CreateTestDar(module));
    }

    [Fact]
    public void Emitted_template_named_Choices_degrades_the_witness_to_an_explicit_implementation_and_compiles()
    {
        var files = EmitTemplateNamedChoices();

        var choicesTemplate = SourceOf(files, "Choices.cs");
        choicesTemplate.Should().Contain(
            "static IReadOnlyList<IChoice> IHasChoices<Choices>.Choices { get; } = [ChoiceGrant];",
            "the template itself is named Choices, so a plain public static Choices member would be CS0542");
        choicesTemplate.Should().NotContain(
            "public static IReadOnlyList<IChoice> Choices",
            "a plain public static Choices on a record named Choices would share the enclosing type's name");

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a template literally named 'Choices' must still compile; got: {0}",
            string.Join("; ", errors.Select(d => d.ToString())));
    }

    [Fact]
    public void Emitted_template_with_a_field_named_choices_degrades_the_witness_to_an_explicit_implementation_and_compiles()
    {
        var files = EmitTemplateWithChoicesField("choices");

        var vault = SourceOf(files, TemplateFileName);
        vault.Should().Contain(
            "static IReadOnlyList<IChoice> IHasChoices<Vault>.Choices { get; } = [ChoiceGrant];",
            "the payload field already owns the instance member Choices, so the witness must degrade to an explicit interface implementation");
        vault.Should().NotContain(
            "public static IReadOnlyList<IChoice> Choices",
            "a plain public static Choices alongside the payload's instance Choices property would be CS0102");
        vault.Should().Contain("string Choices", "the payload field keeps its own PascalCased member name");

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a template with a field literally named 'choices' must still compile; got: {0}",
            string.Join("; ", errors.Select(d => d.ToString())));
    }

    [Fact]
    public void Emitted_template_with_no_colliding_field_keeps_the_plain_static_witness_and_compiles()
    {
        var files = EmitTemplateWithChoicesField("memo");

        var vault = SourceOf(files, TemplateFileName);
        vault.Should().Contain(
            "public static IReadOnlyList<IChoice> Choices { get; } = [ChoiceGrant];",
            "the baseline every degraded case above falls back from must still emit the plain static witness");

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "the baseline fixture must compile; got: {0}",
            string.Join("; ", errors.Select(d => d.ToString())));
    }

    [Fact]
    public void Emitted_template_with_a_nested_choice_arg_type_named_Choices_degrades_the_witness_to_an_explicit_implementation_and_compiles()
    {
        // A non-Unit choice named "Choices" with a same-package argument type causes
        // WriteNestedChoiceArgumentType to emit "public sealed record Choices" inside
        // the template partial — colliding with the static Choices property (CS0102).
        const string moduleName = "Test.Module";
        const string localPackageId = "test-package-id";
        var module = new DamlModule
        {
            Name = moduleName,
            Templates =
            [
                new DamlTemplate
                {
                    Name = TemplateName,
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Choices",
                            Consuming = false,
                            ArgumentType = new DamlTypeRef(localPackageId, moduleName, "Choices"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = TemplateName,
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                    ]),
                },
                new DamlDataType
                {
                    Name = "Choices",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("count", new DamlPrimitiveType(DamlPrimitive.Int64)),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var files = CreateGenerator(
            new CodeGenOptions
            {
                EnableNullableReferenceTypes = true,
                UseFileScopedNamespaces = true,
            })
            .Generate(CreateTestDar(module));

        var vault = SourceOf(files, TemplateFileName);
        vault.Should().Contain(
            "static IReadOnlyList<IChoice> IHasChoices<Vault>.Choices { get; } = [ChoiceChoices];",
            "the nested choice-arg type 'Choices' inside the partial record collides with the static witness, so it must degrade to an explicit interface implementation");
        vault.Should().NotContain(
            "public static IReadOnlyList<IChoice> Choices",
            "a plain public static Choices alongside the nested record Choices would be CS0102");

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a template with a choice named 'Choices' whose arg type emits a nested record must still compile; got: {0}",
            string.Join("; ", errors.Select(d => d.ToString())));
    }

    private static IReadOnlyList<GeneratedFile> EmitTemplateWithChoiceNamed(string choiceName)
    {
        const string moduleName = "Test.Module";
        const string localPackageId = "test-package-id";
        var module = new DamlModule
        {
            Name = moduleName,
            Templates =
            [
                new DamlTemplate
                {
                    Name = TemplateName,
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = choiceName,
                            Consuming = true,
                            ArgumentType = new DamlTypeRef(localPackageId, moduleName, choiceName),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = TemplateName,
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                    ]),
                },
                new DamlDataType
                {
                    Name = choiceName,
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("count", new DamlPrimitiveType(DamlPrimitive.Int64)),
                    ]),
                },
            ],
            Interfaces = [],
        };

        return CreateGenerator(
            new CodeGenOptions
            {
                EnableNullableReferenceTypes = true,
                UseFileScopedNamespaces = true,
            })
            .Generate(CreateTestDar(module));
    }

    [Fact]
    public void Emitted_choice_named_IChoice_root_qualifies_the_aggregate_witness_and_compiles()
    {
        var files = EmitTemplateWithChoiceNamed("IChoice");

        var vault = SourceOf(files, TemplateFileName);
        vault.Should().Contain(
            "public static IReadOnlyList<global::Daml.Runtime.Commands.IChoice> Choices { get; } = [ChoiceIChoice];",
            "the nested choice-arg record 'IChoice' shadows the runtime Daml.Runtime.Commands.IChoice, so the aggregate witness's element type must be root-qualified");

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a template with a choice named 'IChoice' whose arg type emits a nested record must still compile; got: {0}",
            string.Join("; ", errors.Select(d => d.ToString())));
    }

    [Fact]
    public void Emitted_choice_named_IReadOnlyList_root_qualifies_the_aggregate_witness_and_compiles()
    {
        var files = EmitTemplateWithChoiceNamed("IReadOnlyList");

        var vault = SourceOf(files, TemplateFileName);
        vault.Should().Contain(
            "public static global::System.Collections.Generic.IReadOnlyList<IChoice> Choices { get; } = [ChoiceIReadOnlyList];",
            "the nested choice-arg record 'IReadOnlyList' shadows the runtime System.Collections.Generic.IReadOnlyList<T>, so the aggregate witness's head type must be root-qualified");

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a template with a choice named 'IReadOnlyList' whose arg type emits a nested record must still compile; got: {0}",
            string.Join("; ", errors.Select(d => d.ToString())));
    }

    [Fact]
    public void Emitted_choice_named_IHasChoices_root_qualifies_the_explicit_witness_interface_name_and_compiles()
    {
        // A non-Unit choice named "IHasChoices" with a same-package argument type causes
        // GenerateNestedChoiceArgumentType to emit a nested record named "IHasChoices" inside the
        // template partial. When the template also has a payload field named "choices" (which takes
        // the Choices member name), the witness degrades to an explicit IHasChoices<TSelf>.Choices
        // implementation — and that interface reference must be root-qualified, because the nested
        // record "IHasChoices" otherwise shadows the generic Daml.Runtime.Contracts.IHasChoices<>
        // making the non-generic nested record incompatible with the generic constraint (CS0308).
        const string moduleName = "Test.Module";
        const string localPackageId = "test-package-id";
        var module = new DamlModule
        {
            Name = moduleName,
            Templates =
            [
                new DamlTemplate
                {
                    Name = TemplateName,
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "IHasChoices",
                            Consuming = false,
                            ArgumentType = new DamlTypeRef(localPackageId, moduleName, "IHasChoices"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = TemplateName,
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("choices", new DamlPrimitiveType(DamlPrimitive.Text)),
                    ]),
                },
                new DamlDataType
                {
                    Name = "IHasChoices",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("count", new DamlPrimitiveType(DamlPrimitive.Int64)),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var files = CreateGenerator(
            new CodeGenOptions
            {
                EnableNullableReferenceTypes = true,
                UseFileScopedNamespaces = true,
            })
            .Generate(CreateTestDar(module));

        var vault = SourceOf(files, TemplateFileName);
        vault.Should().Contain(
            "static IReadOnlyList<IChoice> global::Daml.Runtime.Contracts.IHasChoices<Vault>.Choices { get; } = [ChoiceIHasChoices];",
            "the nested choice-arg record 'IHasChoices' shadows the runtime Daml.Runtime.Contracts.IHasChoices<T>, so the explicit witness's interface name must be root-qualified");

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a template with a choice named 'IHasChoices' whose arg type emits a nested record must still compile when the witness is explicit; got: {0}",
            string.Join("; ", errors.Select(d => d.ToString())));
    }

    [Fact]
    public void Emitted_choice_named_DamlRecord_root_qualifies_to_record_and_from_record_and_compiles()
    {
        var files = EmitTemplateWithChoiceNamed("DamlRecord");

        var vault = SourceOf(files, TemplateFileName);
        vault.Should().Contain(
            "public global::Daml.Runtime.Data.DamlRecord ToRecord() => global::Daml.Runtime.Data.DamlRecord.Create(",
            "the template's own ToRecord return type and body must be root-qualified once a nested choice-arg record named 'DamlRecord' shadows the runtime type");
        vault.Should().Contain(
            "public static Vault FromRecord(global::Daml.Runtime.Data.DamlRecord record) =>",
            "the template's own FromRecord parameter type must be root-qualified for the same reason");
        vault.Should().NotContain(
            "public DamlRecord ToRecord()",
            "no bare unqualified DamlRecord return type should survive once the nested choice-arg record shadows it");

        var nestedRecord = SourceOf(files, "Vault.DamlRecord.cs");
        nestedRecord.Should().Contain(
            "public global::Daml.Runtime.Data.DamlRecord ToRecord() => global::Daml.Runtime.Data.DamlRecord.Create(",
            "the nested record's own ToRecord return type and body must be root-qualified because an unqualified 'DamlRecord' inside its own body would self-reference the nested record instead of the runtime type");
        nestedRecord.Should().Contain(
            "public static DamlRecord FromRecord(global::Daml.Runtime.Data.DamlRecord record) =>",
            "the nested record's own FromRecord parameter type must be root-qualified for the same reason, while its return type keeps referring to the enclosing DamlRecord record itself");

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a template with a choice named 'DamlRecord' whose arg type emits a nested record must still compile; got: {0}",
            string.Join("; ", errors.Select(d => d.ToString())));
    }

    [Fact]
    public void Emitted_choice_named_DamlField_root_qualifies_the_field_serialization_and_compiles()
    {
        var files = EmitTemplateWithChoiceNamed("DamlField");

        var vault = SourceOf(files, TemplateFileName);
        vault.Should().Contain(
            "global::Daml.Runtime.Data.DamlField.Create(\"owner\",",
            "the template's own ToRecord field serialization must be root-qualified once a nested choice-arg record named 'DamlField' shadows the runtime type");
        vault.Should().NotContain(
            " DamlField.Create(\"owner\",",
            "no unqualified DamlField.Create should survive in the template's own ToRecord once the nested choice-arg record shadows it");

        var nestedRecord = SourceOf(files, "Vault.DamlField.cs");
        nestedRecord.Should().Contain(
            "global::Daml.Runtime.Data.DamlField.Create(\"count\",",
            "the nested record's own ToRecord field serialization must be root-qualified because an unqualified 'DamlField' inside its own body would self-reference the nested record instead of the runtime type");

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a template with a choice named 'DamlField' whose arg type emits a nested record must still compile; got: {0}",
            string.Join("; ", errors.Select(d => d.ToString())));
    }

    [Fact]
    public void Emitted_choice_named_DamlFieldAttribute_root_qualifies_the_property_attribute_and_compiles()
    {
        const string moduleName = "Test.Module";
        const string localPackageId = "test-package-id";
        var module = new DamlModule
        {
            Name = moduleName,
            Templates =
            [
                new DamlTemplate
                {
                    Name = TemplateName,
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "DamlFieldAttribute",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef(localPackageId, moduleName, "DamlFieldAttribute"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = TemplateName,
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                    ]),
                },
                new DamlDataType
                {
                    Name = "DamlFieldAttribute",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("count", new DamlPrimitiveType(DamlPrimitive.Int64)),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var files = CreateGenerator(
            new CodeGenOptions
            {
                EnableNullableReferenceTypes = true,
                UseFileScopedNamespaces = true,
            })
            .Generate(CreateTestDar(module));

        var nestedRecord = SourceOf(files, "Vault.DamlFieldAttribute.cs");
        nestedRecord.Should().Contain(
            "[property: global::Daml.Runtime.Data.DamlFieldAttribute(\"count\")]",
            "the nested record's own property attribute must be root-qualified because an unqualified 'DamlFieldAttribute' inside its own body would self-reference the nested record instead of the runtime attribute type");

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a template with a choice named 'DamlFieldAttribute' whose arg type emits a nested record must still compile; got: {0}",
            string.Join("; ", errors.Select(d => d.ToString())));
    }

    /// <summary>
    /// Regression test: root-qualifying only the template's own
    /// <c>ToRecord</c>/<c>FromRecord</c> method signatures left every other unqualified
    /// <c>DamlRecord</c> reference inside the same partial <c>Vault</c> class — the payload
    /// field deserialization, the choice's return-type decoder, and the key decoder — still
    /// shadowed by a nested choice-argument record named <c>DamlRecord</c>.
    /// </summary>
    [Fact]
    public void Emitted_choice_named_DamlRecord_root_qualifies_every_record_typed_field_key_and_return_deserialization_and_compiles()
    {
        const string moduleName = "Test.Module";
        const string localPackageId = "test-package-id";
        var module = new DamlModule
        {
            Name = moduleName,
            Templates =
            [
                new DamlTemplate
                {
                    Name = TemplateName,
                    Key = new DamlTypeRef(localPackageId, moduleName, "Detail"),
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "DamlRecord",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef(localPackageId, moduleName, "DamlRecord"),
                            ReturnType = new DamlTypeRef(localPackageId, moduleName, "Detail"),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = TemplateName,
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("detail", new DamlTypeRef(localPackageId, moduleName, "Detail")),
                    ]),
                },
                new DamlDataType
                {
                    Name = "Detail",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("text", new DamlPrimitiveType(DamlPrimitive.Text)),
                    ]),
                },
                new DamlDataType
                {
                    Name = "DamlRecord",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("count", new DamlPrimitiveType(DamlPrimitive.Int64)),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var files = CreateGenerator(
            new CodeGenOptions
            {
                EnableNullableReferenceTypes = true,
                UseFileScopedNamespaces = true,
            })
            .Generate(CreateTestDar(module));

        var vault = SourceOf(files, TemplateFileName);
        vault.Should().Contain(
            "Detail: Detail.FromRecord(record.GetRequiredField(\"detail\").As<global::Daml.Runtime.Data.DamlRecord>())",
            "the template's own record-typed payload field deserialization must be root-qualified once the nested choice-arg record named 'DamlRecord' shadows the runtime type");
        vault.Should().Contain(
            "ResultDecoder = val => Detail.FromRecord(val.As<global::Daml.Runtime.Data.DamlRecord>()),",
            "a record-typed choice return-value decoder must be root-qualified for the same reason");
        vault.Should().Contain(
            "KeyDecoder = value => global::Test.Module.Detail.FromRecord(value.As<global::Daml.Runtime.Data.DamlRecord>()),",
            "a record-typed template key decoder must be root-qualified for the same reason");
        vault.Should().NotContain(
            ".As<DamlRecord>()",
            "no unqualified DamlRecord cast should survive anywhere in the template body once the nested choice-arg record shadows it");

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a template with a choice named 'DamlRecord', a record-typed payload field, a record-typed key, and a record-typed choice return must still compile; got: {0}",
            string.Join("; ", errors.Select(d => d.ToString())));
    }

    [Fact]
    public void Emitted_choice_named_DamlLfJsonDecoders_compiles_because_runtime_references_are_always_root_qualified()
    {
        var files = EmitTemplateWithChoiceNamed("DamlLfJsonDecoders");

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a template with a choice named 'DamlLfJsonDecoders' whose arg type emits a nested record must still compile; got: {0}",
            string.Join("; ", errors.Select(d => d.ToString())));
    }

    [Fact]
    public void Emitted_choice_named_DamlLfJsonDecodeContext_compiles_because_runtime_references_are_always_root_qualified()
    {
        var files = EmitTemplateWithChoiceNamed("DamlLfJsonDecodeContext");

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a template with a choice named 'DamlLfJsonDecodeContext' whose arg type emits a nested record must still compile; got: {0}",
            string.Join("; ", errors.Select(d => d.ToString())));
    }
}
