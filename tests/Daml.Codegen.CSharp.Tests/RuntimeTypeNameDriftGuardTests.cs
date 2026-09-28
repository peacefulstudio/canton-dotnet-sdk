// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Daml.Codegen.CSharp.CodeGen;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Drift guard for the runtime type-name string literals that cannot be
/// <c>nameof</c>-backed. <see cref="RuntimeTypeNames.Contract"/>,
/// <see cref="RuntimeTypeNames.IContract"/>, <see cref="RuntimeTypeNames.Choice"/>
/// and <see cref="RuntimeTypeNames.IImplements"/>
/// are blocked by CS8920 (they name generic types constrained by <c>ITemplate</c>'s
/// or <c>IDamlInterface</c>'s static-abstract members, which makes the concrete type
/// unusable as a <c>nameof</c> type argument from the codegen project).
/// <see cref="StdlibPackages.MapStdlibType"/>'s return values are
/// keyed on Daml source names rather than the runtime type. Both are read directly
/// from production code and checked against the real public type names reflected
/// out of <c>Daml.Runtime</c>, so a rename there fails this test instead of
/// silently breaking generated code.
/// </summary>
public class RuntimeTypeNameDriftGuardTests
{
    private static readonly IReadOnlySet<string> RuntimeTypeSimpleNames =
        typeof(Daml.Runtime.Contracts.ITemplate).Assembly
            .GetExportedTypes()
            .Select(t => t.Name.Split('`')[0])
            .ToHashSet(StringComparer.Ordinal);

    private static string RuntimeTypeNamesConstant(string fieldName) =>
        (string)typeof(RuntimeTypeNames)
            .GetField(fieldName, BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

    [Theory]
    [InlineData(nameof(RuntimeTypeNames.Contract))]
    [InlineData(nameof(RuntimeTypeNames.IContract))]
    [InlineData(nameof(RuntimeTypeNames.Choice))]
    [InlineData(nameof(RuntimeTypeNames.IImplements))]
    public void RuntimeTypeNameDriftGuard_the_cs8920_blocked_template_generic_literals_name_a_real_runtime_type(string constantFieldName)
    {
        var literal = RuntimeTypeNamesConstant(constantFieldName);

        RuntimeTypeSimpleNames.Should().Contain(literal,
            $"RuntimeTypeNames.{constantFieldName} must keep naming a real public type in Daml.Runtime");
    }

    [Theory]
    [InlineData("DA.Date.Types", "DayOfWeek")]
    [InlineData("DA.Time.Types", "RelTime")]
    [InlineData("DA.Types", "Tuple2")]
    [InlineData("DA.Types", "Tuple3")]
    [InlineData("DA.Types", "Either")]
    [InlineData("DA.Set.Types", "Set")]
    [InlineData("DA.NonEmpty.Types", "NonEmpty")]
    [InlineData("DA.Map.Types", "Map")]
    [InlineData("DA.Internal.Map", "Map")]
    public void RuntimeTypeNameDriftGuard_every_mapped_stdlib_type_names_a_real_runtime_type(string module, string damlTypeName)
    {
        var mapped = StdlibPackages.MapStdlibType(module, damlTypeName);

        mapped.Should().NotBeNull();
        RuntimeTypeSimpleNames.Should().Contain(mapped!,
            $"StdlibPackages.MapStdlibType(\"{module}\", \"{damlTypeName}\") must keep naming a real public type in Daml.Runtime");
    }

    [Fact]
    public void RuntimeTypeNameDriftGuard_a_type_name_absent_from_daml_runtime_would_fail_the_drift_guard()
    {
        RuntimeTypeSimpleNames.Should().NotContain("ThisTypeDoesNotExistInDamlRuntime");
    }
}
