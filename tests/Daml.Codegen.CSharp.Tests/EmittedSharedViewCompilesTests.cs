// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Microsoft.CodeAnalysis;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Compiles emitted code for a view record two interfaces declare as their view type.
/// Stamping such a record with both markers would make it inherit two explicit
/// implementations of the same identity statics — no most specific implementation, a
/// compile error — so the emitter must leave a shared view record un-stamped and keep
/// both markers un-enriched, while each still carries its <c>View</c> witness.
/// </summary>
public class EmittedSharedViewCompilesTests
{
    [Fact]
    public void Emitted_shared_view_record_compiles_without_a_marker_stamp()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "SharedView",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Asset",
                    Choices = [],
                    ViewType = new DamlTypeRef("", "Test.Module", "SharedView"),
                },
                new DamlInterface
                {
                    Name = "Bond",
                    Choices = [],
                    ViewType = new DamlTypeRef("", "Test.Module", "SharedView"),
                },
            ],
        };
        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
        };

        var files = CreateGenerator(options).Generate(CreateTestDar(module));

        files.Should().Contain(f => f.Content.Contains("global::Daml.Runtime.Contracts.ViewDescriptor<IAsset, global::Test.Module.SharedView> View", StringComparison.Ordinal));
        files.Should().Contain(f => f.Content.Contains("global::Daml.Runtime.Contracts.ViewDescriptor<IBond, global::Test.Module.SharedView> View", StringComparison.Ordinal));
        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a view record shared by two interfaces must stay un-stamped rather than inherit two identity implementations, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }
}
