// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Xunit;
using static Daml.Codegen.CSharp.Tests.ReservedTypeNameFixtures;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedFilePathCaseClashTests
{
    [Fact]
    public void Two_records_whose_names_differ_only_by_case_are_a_codegen_error_naming_both()
    {
        var dar = DarOf([], Record("Foo", Field("x", IntType)), Record("FOO", Field("x", IntType)));

        var failure = () => CreateGenerator().Generate(dar);

        failure.Should().Throw<CodegenException>().Which.Message.Should().Be(
            "The generated files for Daml record 'test-package:Test.Module:Foo' and Daml record 'test-package:Test.Module:FOO' " +
            "would collide on a case-insensitive file system: 'Test/Module/Foo.cs' and 'Test/Module/FOO.cs' are one file on macOS and Windows. " +
            "Rename one of them in Daml.");
    }

    [Fact]
    public void Two_modules_whose_names_differ_only_by_case_are_a_codegen_error_when_they_emit_the_same_file_name()
    {
        var dar = DarOfModules(
            ModuleOf("A.Foo", [], Record("T", Field("x", IntType))),
            ModuleOf("A.FOO", [], Record("T", Field("x", IntType))));

        var failure = () => CreateGenerator().Generate(dar);

        failure.Should().Throw<CodegenException>().Which.Message.Should()
            .Contain("Daml record 'test-package:A.Foo:T'").And.Contain("Daml record 'test-package:A.FOO:T'")
            .And.Contain("'A/Foo/T.cs' and 'A/FOO/T.cs'");
    }

    [Fact]
    public void Two_choice_argument_files_whose_choice_names_differ_only_by_case_are_a_codegen_error_naming_both()
    {
        var dar = DarOf(
            [Template("Asset", null, Choice("Go", IntType, Ref("GoArg")), Choice("GO", IntType, Ref("GOArg")))],
            Record("Asset", Field("owner", PartyType)),
            Record("GoArg", Field("x", IntType)),
            Record("GOArg", Field("x", IntType)));

        var failure = () => CreateGenerator().Generate(dar);

        failure.Should().Throw<CodegenException>().Which.Message.Should()
            .Contain("'Test/Module/Asset.Go.cs' and 'Test/Module/Asset.GO.cs'").And.Contain("Daml template 'test-package:Test.Module:Asset'");
    }

    [Fact]
    public void Records_whose_names_differ_by_more_than_case_generate()
    {
        var dar = DarOf([], Record("Foo", Field("x", IntType)), Record("Bar", Field("x", IntType)));

        var generating = () => CreateGenerator().Generate(dar);

        generating.Should().NotThrow();
    }
}
