// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Pins that two Daml type names which damlc mangling keeps distinct — a name
/// carrying a symbol (<c>Foo'</c> → LF <c>Foo$u0027</c>) and the literal spelling of
/// that escape (<c>Foo_u0027</c>) — emit two distinct C# types. A lossy
/// <c>$</c>-to-<c>_</c> sanitiser collapses both onto <c>Foo_u0027</c>, so the
/// emitted sources declare the same record twice and fail to compile (CS0101). That
/// is a hard semantic error, not a doc diagnostic, so a Roslyn compile catches it
/// where a text-compare drift test cannot.
/// </summary>
public class EmittedMangledTypeNamesCompileTests
{
    private static DarModel TwoRecordDar(string firstName, string secondName)
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = firstName,
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = secondName,
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
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
    public void Emitted_types_from_a_mangled_symbol_and_its_literal_escape_do_not_collide()
    {
        var files = CreateGenerator().Generate(TwoRecordDar("Foo$u0027", "Foo_u0027"));

        var errors = CompileEmittedFiles(files)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        errors.Should().BeEmpty(
            "Daml Foo' mangles to Foo$u0027 and must emit a C# type distinct from the "
            + "literal type Foo_u0027, not collapse onto it (CS0101); got: "
            + $"{string.Join("; ", errors.Select(d => d.ToString()))}");
    }

    [Fact]
    public void Emitted_types_from_an_astral_code_point_and_its_escaped_dollar_spelling_do_not_collide()
    {
        var files = CreateGenerator().Generate(TwoRecordDar("Smile$U0001f642", "Smile$$U0001f642"));

        var errors = CompileEmittedFiles(files)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        errors.Should().BeEmpty(
            "damlc mangles a Smile followed by U+1F642 to Smile$U0001f642 and a literal Smile$U0001f642 to "
            + "Smile$$U0001f642, which must stay two distinct C# types; got: "
            + $"{string.Join("; ", errors.Select(d => d.ToString()))}");
    }
}
