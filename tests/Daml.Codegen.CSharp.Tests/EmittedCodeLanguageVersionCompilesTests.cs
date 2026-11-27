// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Pins that emitted sources compile as C# 14 (the .NET 10 SDK default) and as C# 15
/// (the .NET 11 SDK default), because consumers of the generator build with either SDK.
/// </summary>
public sealed class EmittedCodeLanguageVersionCompilesTests
{
    [Fact]
    public void EmittedCodeLanguageVersions_cover_csharp_14_and_csharp_15()
    {
        EmittedCodeLanguageVersions.Should().Equal(LanguageVersion.CSharp14, LanguageVersion.CSharp15);
    }

    [Theory]
    [InlineData(LanguageVersion.CSharp14)]
    [InlineData(LanguageVersion.CSharp15)]
    public void EmittedKeyBearingTemplate_compiles_without_errors_or_warnings_under_language_version(
        LanguageVersion languageVersion)
    {
        var files = GenerateKeyBearingTemplate();

        var diagnostics = CompileEmittedFilesToCompilation(files, DocumentationMode.Parse, languageVersion)
            .GetDiagnostics(TestContext.Current.CancellationToken);

        diagnostics
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Select(d => d.ToString())
            .Should()
            .BeEmpty();
    }
}
