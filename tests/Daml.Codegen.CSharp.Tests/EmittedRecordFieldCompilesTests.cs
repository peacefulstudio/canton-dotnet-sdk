// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedRecordFieldCompilesTests
{
    [Fact]
    public void Emitted_record_with_field_that_pascalcases_to_the_type_name_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Period",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("period", new DamlPrimitiveType(DamlPrimitive.Text)),
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
        var files = CreateGenerator().Generate(dar).ToList();

        var periodSource = files.Single(f => f.RelativePath.EndsWith("Period.cs", StringComparison.Ordinal)).Content;
        periodSource.Should().NotMatchRegex(
            @"\b(string|required)\s+Period\b\s*\{",
            "a property whose name equals the enclosing type Period would be CS0542; the field must be disambiguated");

        periodSource.Should().Contain("Period_",
            "the colliding member must be disambiguated to Period_");
        periodSource.Should().Contain("record.GetRequiredField(\"period\")",
            "deserialization must read the original Daml wire field name \"period\", unchanged by the C# member rename");
        periodSource.Should().Contain("Create(\"period\", ",
            "serialization must emit the original Daml wire field name \"period\", unchanged by the C# member rename");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a record whose field PascalCases to its own type name must still compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_record_with_stdlib_DayOfWeek_enum_field_compiles()
    {
        var stdlibModule = new DamlModule
        {
            Name = "DA.Date.Types",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "DayOfWeek",
                    Definition = new DamlEnumDefinition(
                        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"]),
                }
            ],
            Interfaces = [],
        };
        var stdlibPackage = new DamlPackage
        {
            PackageId = "daml-stdlib-id",
            Name = "daml-stdlib",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [stdlibModule],
            DependencyReferences = [],
        };

        var mainModule = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Schedule",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("day", new DamlTypeRef("daml-stdlib-id", "DA.Date.Types", "DayOfWeek")),
                    ]),
                },
            ],
            Interfaces = [],
        };
        var mainPackage = new DamlPackage
        {
            PackageId = "test-pkg",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [mainModule],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = mainPackage, Dependencies = [stdlibPackage] };

        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            IncludeDependencies = true,
        };
        var generator = new CSharpCodeGenerator(options);
        var files = generator.Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a record field whose type is the stdlib DayOfWeek enum must round-trip via the runtime-provided Daml.Runtime.Stdlib.DayOfWeekExtensions, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_record_with_genmap_of_list_field_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Holding",
                    Choices = [],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                    ])
                },
                new DamlDataType
                {
                    Name = "InstrumentId",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("id", new DamlPrimitiveType(DamlPrimitive.Text)),
                    ])
                },
                new DamlDataType
                {
                    Name = "BatchResult",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("senderChangeMap", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.GenMap),
                            [
                                new DamlTypeRef("", "Test.Module", "InstrumentId"),
                                new DamlTypeApp(
                                    new DamlPrimitiveType(DamlPrimitive.List),
                                    [new DamlTypeApp(
                                        new DamlPrimitiveType(DamlPrimitive.ContractId),
                                        [new DamlTypeRef("", "Test.Module", "Holding")])])
                            ])),
                    ])
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
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "GenMap-of-List FromRecord must compile without CS1503 against IReadOnlyDictionary<K,IReadOnlyList<V>>, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_record_with_nested_map_of_map_field_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "NestedMaps",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("byGenMapOfGenMap", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.GenMap),
                            [
                                new DamlPrimitiveType(DamlPrimitive.Party),
                                new DamlTypeApp(
                                    new DamlPrimitiveType(DamlPrimitive.GenMap),
                                    [new DamlPrimitiveType(DamlPrimitive.Text), new DamlPrimitiveType(DamlPrimitive.Int64)])
                            ])),
                        new DamlFieldDefinition("byTextMapOfTextMap", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.TextMap),
                            [new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.TextMap),
                                [new DamlPrimitiveType(DamlPrimitive.Int64)])])),
                        new DamlFieldDefinition("byGenMapOfTextMapOfList", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.GenMap),
                            [
                                new DamlPrimitiveType(DamlPrimitive.Party),
                                new DamlTypeApp(
                                    new DamlPrimitiveType(DamlPrimitive.TextMap),
                                    [new DamlTypeApp(
                                        new DamlPrimitiveType(DamlPrimitive.List),
                                        [new DamlPrimitiveType(DamlPrimitive.Int64)])])
                            ])),
                    ])
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
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "nested Map-of-Map FromRecord must compile without CS1503 against the declared nested IReadOnlyDictionary<K1, IReadOnlyDictionary<K2,V>>, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_record_with_either_field_compiles()
    {
        var stdlibPackage = new DamlPackage
        {
            PackageId = "daml-prim-id",
            Name = "daml-prim",
            Version = new Version(0, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = "DA.Types",
                    Templates = [],
                    DataTypes =
                    [
                        new DamlDataType
                        {
                            Name = "Either",
                            TypeParams = ["a", "b"],
                            Definition = new DamlVariantDefinition([]),
                        },
                    ],
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

        var eitherTextInt = new DamlTypeApp(
            new DamlTypeRef("daml-prim-id", "DA.Types", "Either"),
            [new DamlPrimitiveType(DamlPrimitive.Text), new DamlPrimitiveType(DamlPrimitive.Int64)]);

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Decision",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("outcome", eitherTextInt)]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-pkg",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [stdlibPackage] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a record field typed as DA.Types:Either must map to Daml.Runtime.Stdlib.Either and compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));

        var emitted = string.Join("\n", files.Select(f => f.Content));
        emitted.Should().Contain(
            ".ToValue(",
            "the emitted Decision.ToValue must wire DA.Types:Either through Either.ToValue (EmitParametricStdlibToValue)");
        emitted.Should().Contain(
            "Either<string, long>.FromValue(",
            "the emitted decoder must wire DA.Types:Either through Either.FromValue (EmitParametricStdlibFromValue)");
    }

    [Fact]
    public void Emitted_record_with_either_field_carrying_an_optional_compiles()
    {
        var stdlibPackage = new DamlPackage
        {
            PackageId = "daml-prim-id",
            Name = "daml-prim",
            Version = new Version(0, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = "DA.Types",
                    Templates = [],
                    DataTypes =
                    [
                        new DamlDataType
                        {
                            Name = "Either",
                            TypeParams = ["a", "b"],
                            Definition = new DamlVariantDefinition([]),
                        },
                    ],
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

        var eitherTextOptionalText = new DamlTypeApp(
            new DamlTypeRef("daml-prim-id", "DA.Types", "Either"),
            [
                new DamlPrimitiveType(DamlPrimitive.Text),
                new DamlTypeApp(
                    new DamlPrimitiveType(DamlPrimitive.Optional),
                    [new DamlPrimitiveType(DamlPrimitive.Text)]),
            ]);

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Decision",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("outcome", eitherTextOptionalText)]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-pkg",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [stdlibPackage] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "an Optional in a parametric stdlib generic's type-argument position takes the wrapper, so "
            + "Either's notnull type parameters are satisfied, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));

        var emitted = string.Join("\n", files.Select(f => f.Content));
        emitted.Should().Contain(
            "Either<string, Optional<string>>",
            "the Optional type argument must render as the wrapper rather than as string?");
        emitted.Should().NotContain(
            "Either<string, string?>",
            "string? cannot satisfy Either's notnull type parameter");
    }

    [Fact]
    public void Emitted_record_with_unmappable_object_field_compiles()
    {
        // Regression: ToValue's `_` arm unconditionally emitted
        // `<value>.ToRecord()` for any field the type mapper falls back to
        // `object` for. `object` has no ToRecord(), so the emitted body was CS1061.
        // The fallback-producing shape is a higher-kinded application (`f a` where
        // the base is a type var), which no other MapType arm names. (Arrow types
        // like DA.Monoid.Types.Endo's `appEndo : a -> a` no longer reach here
        // either: since the builtin catalog, both producer paths carry ARROW with
        // its real `DamlPrimitive.Arrow` identity, and DamlTypeMapper fails loudly
        // with NotSupportedException when a bare or applied signature-only builtin
        // reaches a data position — no cataloged builtin shape maps to `object`
        // anymore.) `f a` is the parser-independent reproduction of the remaining,
        // deliberate fallback: an application whose base is a type variable, which
        // no catalog row describes, still maps to `object` and emits the throwing
        // GenericStub.NotImplemented stubs pinned below.
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Endo",
                    TypeParams = ["a"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("appEndo", new DamlTypeApp(
                            new DamlTypeVar("f"),
                            [new DamlTypeVar("a")])),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-pkg",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar).ToList();

        var endoSource = files.Single(f => f.RelativePath.EndsWith("Endo.cs", StringComparison.Ordinal)).Content;
        endoSource.Should().NotContain("AppEndo.ToRecord()",
            "an object-typed (unmappable) field must not emit `.ToRecord()` — object has no such method");
        endoSource.Should().Contain("NotImplemented<DamlValue>(\"AppEndo\")",
            "an object-typed (unmappable) field must serialize via the GenericStub.NotImplemented stub");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a record with an unmappable object-typed field must compile (no .ToRecord() on object), but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }
}
