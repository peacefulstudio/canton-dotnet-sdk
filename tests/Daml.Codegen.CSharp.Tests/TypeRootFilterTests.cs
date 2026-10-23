// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class TypeRootFilterTests
{
    [Fact]
    public void Includes_should_match_the_pattern_against_module_colon_type()
    {
        var filter = new TypeRootFilter(@"^Main:Asset$");

        filter.Includes("Main", "Asset").Should().BeTrue();
        filter.Includes("Main", "Other").Should().BeFalse();
    }

    [Fact]
    public void Includes_should_admit_everything_without_a_pattern()
    {
        TypeRootFilter.IncludeAll.Includes("Any.Module", "Anything").Should().BeTrue();
    }

    [Fact]
    public void Constructor_should_throw_codegen_exception_naming_the_option_for_an_invalid_pattern()
    {
        var act = () => new TypeRootFilter("(unclosed");

        act.Should().Throw<CodegenException>()
            .WithMessage("*RootFilter*(unclosed*");
    }

    [Fact]
    public void Includes_should_throw_codegen_exception_naming_the_option_when_matching_exceeds_the_timeout()
    {
        var filter = new TypeRootFilter(@"^Main:(a|aa)+$");
        var catastrophicInput = new string('a', 64) + "!";

        var act = () => filter.Includes("Main", catastrophicInput);

        act.Should().Throw<CodegenException>()
            .WithMessage("*RootFilter*^Main:(a|aa)+$*timed out*");
    }
}
