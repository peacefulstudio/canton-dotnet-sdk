// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedJvmStdlibBindingsCompileTests
{
    private static DamlType Prim(DamlPrimitive primitive) => new DamlPrimitiveType(primitive);

    private static DarModel QuickstartDar()
    {
        var protoPath = Path.Combine(AppContext.BaseDirectory, "QuickstartSample", "intermediate.binpb");
        using var stream = File.OpenRead(protoPath);
        return IntermediateDarReader.Read(IntermediateDar.Parser.ParseFrom(stream));
    }

    private static DamlTypeRef Stdlib(DarModel dar, string module, string name) =>
        new(dar.Dependencies.Single(package => package.Modules.Any(m =>
            m.Name == module && m.DataTypes.Any(d => d.Name == name))).PackageId, module, name);

    private static DarModel UserDarOver(DarModel jvmDar)
    {
        var fields = new List<DamlFieldDefinition>
        {
            new("tuple5", new DamlTypeApp(
                Stdlib(jvmDar, "DA.Types", "Tuple5"),
                [Prim(DamlPrimitive.Int64), Prim(DamlPrimitive.Text), Prim(DamlPrimitive.Bool), Prim(DamlPrimitive.Party), Prim(DamlPrimitive.Date)])),
            new("validation", new DamlTypeApp(
                Stdlib(jvmDar, "DA.Validation.Types", "Validation"),
                [Prim(DamlPrimitive.Text), Prim(DamlPrimitive.Int64)])),
            new("wrapped", new DamlTypeApp(Stdlib(jvmDar, "GHC.Tuple", "Unit"), [Prim(DamlPrimitive.Party)])),
            new("sum", new DamlTypeApp(Stdlib(jvmDar, "DA.Monoid.Types", "Sum"), [Prim(DamlPrimitive.Int64)])),
            new("location", Stdlib(jvmDar, "DA.Stack.Types", "SrcLoc")),
        };
        var module = new DamlModule
        {
            Name = "User.Jvm",
            Templates = [],
            DataTypes = [new DamlDataType { Name = "Holder", Definition = new DamlRecordDefinition(fields) }],
            Interfaces = [],
        };
        return new DarModel
        {
            MainPackage = new DamlPackage
            {
                PackageId = "user-package-id",
                Name = "user-package",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules = [module],
                DependencyReferences = [],
            },
            Dependencies = jvmDar.Dependencies,
        };
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Generate_over_jvm_produced_dependencies_binds_stdlib_fields_to_runtime_types(bool includeDependencies)
    {
        var options = new CodeGenOptions { IncludeDependencies = includeDependencies };

        var files = CreateGenerator(options).Generate(UserDarOver(QuickstartDar()));

        var holder = files.Single(file => file.RelativePath == "User/Jvm/Holder.cs").Content;
        holder.Should()
            .Contain("global::Daml.Runtime.Stdlib.Tuple5<long, string, bool, global::Daml.Runtime.Data.Party, global::System.DateOnly> Tuple5")
            .And.Contain("global::Daml.Runtime.Stdlib.Validation<string, long> Validation")
            .And.Contain("global::Daml.Runtime.Stdlib.Unit<global::Daml.Runtime.Data.Party> Wrapped")
            .And.Contain("global::Daml.Runtime.Stdlib.Sum<long> Sum")
            .And.Contain("global::Daml.Runtime.Stdlib.SrcLoc Location");
        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Theory]
    [InlineData("DA.Validation.Types", "Validation", 2)]
    [InlineData("DA.Types", "Tuple5", 5)]
    [InlineData("DA.Monoid.Types", "Sum", 1)]
    [InlineData("DA.Semigroup.Types", "Min", 1)]
    [InlineData("DA.Internal.Down", "Down", 1)]
    [InlineData("GHC.Tuple", "Unit", 1)]
    public void Generate_imports_the_runtime_stdlib_namespace_for_a_record_whose_only_stdlib_field_is_a_generated_parametric_type(
        string module,
        string name,
        int arity)
    {
        var jvmDar = QuickstartDar();
        var arguments = new DamlType[] { Prim(DamlPrimitive.Int64), Prim(DamlPrimitive.Text), Prim(DamlPrimitive.Bool), Prim(DamlPrimitive.Party), Prim(DamlPrimitive.Date) }
            .Take(arity)
            .ToList();
        var holder = new DamlModule
        {
            Name = "User.Only",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holder",
                    Definition = new DamlRecordDefinition([new("value", new DamlTypeApp(Stdlib(jvmDar, module, name), arguments))]),
                },
            ],
            Interfaces = [],
        };
        var dar = new DarModel
        {
            MainPackage = new DamlPackage
            {
                PackageId = "user-package-id",
                Name = "user-package",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules = [holder],
                DependencyReferences = [],
            },
            Dependencies = jvmDar.Dependencies,
        };

        var files = CreateGenerator(new CodeGenOptions { IncludeDependencies = false }).Generate(dar);

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }
}
