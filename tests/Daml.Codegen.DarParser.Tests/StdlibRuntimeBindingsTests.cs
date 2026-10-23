// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Com.DigitalAsset.Daml.Lf.Archive.DamlLf;
using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Tooling;
using Daml.Codegen.Intermediate.Model;
using Daml.Testing.Roslyn;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Daml.Codegen.DarParser.Tests;

public sealed class StdlibRuntimeBindingsTests
{
    private static readonly string StablePackagesDirectory =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "stable-packages");

    private static readonly Lazy<IReadOnlyList<DamlPackage>> StablePackages = new(() =>
        Directory.EnumerateFiles(StablePackagesDirectory, "*.dalf", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => DalfReader.Parse(File.ReadAllBytes(path)))
            .ToList());

    private static readonly DamlPrimitive[] ArgumentPool =
    [
        DamlPrimitive.Int64, DamlPrimitive.Text, DamlPrimitive.Bool, DamlPrimitive.Party, DamlPrimitive.Date,
    ];

    private static IEnumerable<(string Module, string Name)> SerializableTypes() =>
        StablePackages.Value.SelectMany(package => package.Modules
            .SelectMany(module => module.DataTypes.Select(dataType => (module.Name, dataType.Name))));

    private static DamlTypeRef ReferenceTo((string Module, string Name) key) =>
        new(
            StablePackages.Value.Single(package => package.Modules.Any(module =>
                module.Name == key.Module && module.DataTypes.Any(dataType => dataType.Name == key.Name))).PackageId,
            key.Module,
            key.Name);

    private static DamlDataType Declaration((string Module, string Name) key) =>
        StablePackages.Value.SelectMany(package => package.Modules)
            .Single(module => module.Name == key.Module && module.DataTypes.Any(dataType => dataType.Name == key.Name))
            .DataTypes.Single(dataType => dataType.Name == key.Name);

    private static DamlType Applied((string Module, string Name) key)
    {
        var parameters = Declaration(key).TypeParams;
        var reference = ReferenceTo(key);
        return parameters.Count == 0
            ? reference
            : new DamlTypeApp(
                reference,
                parameters.Select((_, index) => (DamlType)new DamlPrimitiveType(ArgumentPool[index % ArgumentPool.Length])).ToList());
    }

    private static DarModel UserDarHoldingEveryGeneratedType()
    {
        var fields = StdlibRuntimeInventoryAccess.Generated
            .OrderBy(key => key, Comparer<(string Module, string Name)>.Default)
            .Select(key => new DamlFieldDefinition($"field{key.Name}", Applied(key)))
            .ToList();

        var module = new DamlModule
        {
            Name = "User.Holder",
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
            Dependencies = StablePackages.Value,
        };
    }

    private static List<Diagnostic> CompileErrors(IEnumerable<GeneratedFile> files) =>
        CSharpCompilation.Create(
                "stdlib-holder",
                files.Select(file => CSharpSyntaxTree.ParseText(file.Content, path: file.RelativePath)),
                ConsumerReferenceSet.Assemblies,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToList();

    private static string Describe(IEnumerable<Diagnostic> diagnostics) =>
        string.Join("\n", diagnostics.Select(d => d.GetMessage(CultureInfo.InvariantCulture) + " @ " + d.Location));

    [Fact]
    public void Inventory_covers_every_serializable_type_the_stable_packages_declare()
    {
        var accountedFor = StdlibRuntimeInventoryAccess.HandMapped
            .Concat(StdlibRuntimeInventoryAccess.Generated)
            .Concat(StdlibRuntimeInventoryAccess.Excluded.Keys)
            .ToHashSet();

        var unaudited = SerializableTypes().Where(key => !accountedFor.Contains(key)).ToList();

        unaudited.Should().BeEmpty(
            "every serializable stdlib type must be hand-mapped, generated, or excluded with a reason");
    }

    [Fact]
    public void Inventory_generated_types_are_all_declared_by_the_stable_packages()
    {
        var declared = SerializableTypes().ToHashSet();

        StdlibRuntimeInventoryAccess.Generated.Where(key => !declared.Contains(key)).Should().BeEmpty();
    }

    [Fact]
    public void Inventory_pins_the_generated_type_count_at_thirty_five()
    {
        StdlibRuntimeInventoryAccess.Generated.Should().HaveCount(35);
    }

    [Fact]
    public void Inventory_hand_mapped_and_generated_types_do_not_overlap()
    {
        StdlibRuntimeInventoryAccess.HandMapped.Intersect(StdlibRuntimeInventoryAccess.Generated).Should().BeEmpty();
    }

    [Fact]
    public void Generate_writes_one_file_per_generated_type_in_the_stdlib_namespace()
    {
        var files = StdlibRuntimeBindings.Generate(StablePackages.Value);

        files.Should().HaveCount(35);
        files.Select(file => file.RelativePath).Should().Contain(["Ordering.cs", "Unit.cs", "Minstd.cs", "SrcLoc.cs", "Tuple20.cs", "Archive.cs"]);
        files.Should().OnlyContain(file => file.Content.Contains("namespace Daml.Runtime.Stdlib;", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_is_deterministic_across_runs()
    {
        var first = StdlibRuntimeBindings.Generate(StablePackages.Value);
        var second = StdlibRuntimeBindings.Generate(StablePackages.Value);

        first.Select(file => (file.RelativePath, file.Content))
            .Should().Equal(second.Select(file => (file.RelativePath, file.Content)));
    }

    [Fact]
    public void Generate_unit_wrapper_is_a_one_element_record_distinct_from_daml_unit()
    {
        var unit = Declaration(("GHC.Tuple", "Unit"));

        unit.TypeParams.Should().Equal("a");
        unit.Definition.Should().BeOfType<DamlRecordDefinition>().Which.Fields.Should().ContainSingle()
            .Which.Name.Should().Be("_1");
    }

    [Fact]
    public void Generate_minstd_is_a_single_constructor_variant_over_int64()
    {
        var minstd = Declaration(("DA.Random.Types", "Minstd"));

        var constructor = minstd.Definition.Should().BeOfType<DamlVariantDefinition>().Which.Constructors.Should().ContainSingle().Subject;
        constructor.Name.Should().Be("Minstd");
        constructor.ArgumentType.Should().Be(new DamlPrimitiveType(DamlPrimitive.Int64));
    }

    [Fact]
    public void Generate_srcloc_carries_seven_fields()
    {
        Declaration(("DA.Stack.Types", "SrcLoc")).Definition
            .Should().BeOfType<DamlRecordDefinition>().Which.Fields.Select(field => field.Name)
            .Should().Equal(
                "srcLocPackage", "srcLocModule", "srcLocFile", "srcLocStartLine", "srcLocStartCol", "srcLocEndLine", "srcLocEndCol");
    }

    [Fact]
    public void Failure_status_is_non_serializable_in_the_raw_archive_so_no_binding_exists_for_it()
    {
        var path = Path.Combine(StablePackagesDirectory, "lf-v2", "daml-stdlib", "DA.Internal.Fail.Types".Replace('.', '-') + ".dalf");
        var archive = Archive.Parser.ParseFrom(File.ReadAllBytes(path));
        var payload = ArchivePayload.Parser.ParseFrom(archive.Payload);
        var package = Package.Parser.ParseFrom(payload.DamlLf2);

        var declared = package.Modules.SelectMany(module => module.DataTypes).ToList();

        declared.Should().ContainSingle().Which.Serializable.Should().BeFalse();
        SerializableTypes().Should().NotContain(key => key.Module == "DA.Internal.Fail.Types");
    }

    [Fact]
    public void Generate_checked_in_bindings_match_a_fresh_run()
    {
        var checkedIn = Path.Combine(RepositoryRoot(), "src", "Daml.Runtime", "Stdlib", "Generated");

        var fresh = StdlibRuntimeBindings.Generate(StablePackages.Value);

        Directory.EnumerateFiles(checkedIn, "*.cs").Select(Path.GetFileName).Order(StringComparer.Ordinal)
            .Should().Equal(fresh.Select(file => file.RelativePath));
        foreach (var file in fresh)
        {
            File.ReadAllText(Path.Combine(checkedIn, file.RelativePath)).Should().Be(file.Content, file.RelativePath);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Emitted_user_record_holding_every_generated_type_compiles(bool includeDependencies)
    {
        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            IncludeDependencies = includeDependencies,
        };

        var files = new CSharpCodeGenerator(options).Generate(UserDarHoldingEveryGeneratedType());

        var errors = CompileErrors(files);
        errors.Should().BeEmpty("the generated stdlib bindings ship in Daml.Runtime, so user code must bind to them, got: {0}", Describe(errors));
    }

    [Fact]
    public void Emitted_user_record_references_generated_types_without_emitting_them()
    {
        var files = new CSharpCodeGenerator(new CodeGenOptions { IncludeDependencies = true })
            .Generate(UserDarHoldingEveryGeneratedType());

        files.Select(file => file.RelativePath).Should().Equal("User/Holder/Holder.cs");
        files.Single().Content.Should()
            .Contain("global::Daml.Runtime.Stdlib.Tuple5<long, string, bool, global::Daml.Runtime.Data.Party, global::System.DateOnly> FieldTuple5")
            .And.Contain("global::Daml.Runtime.Stdlib.Validation<long, string> FieldValidation")
            .And.Contain("global::Daml.Runtime.Stdlib.Unit<long> FieldUnit");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !directory.EnumerateFiles("Daml.Codegen.CSharp.slnx").Any())
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found above the test output");
    }
}
