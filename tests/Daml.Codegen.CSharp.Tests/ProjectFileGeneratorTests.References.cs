// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using System.Reflection;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public partial class ProjectFileGeneratorTests
{
    [Fact]
    public void GenerateProjectFile_floats_dependency_generation_and_prerelease_when_no_release_coordinates_are_supplied()
    {
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };
        var externalReferences = new List<DamlPackage>
        {
            new()
            {
                PackageId = "dep-id-1",
                Name = "my-dependency",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules = [],
                DependencyReferences = []
            }
        };

        var file = generator.GenerateProjectFile(package, externalReferences);

        file.Content.Should().NotContain(
            "<PackageReference Include=\"My.Dependency\" Version=\"1.0.0.0\" />",
            "the 4th segment is a codegen-generation ordinal and the prerelease tag is a release-time decision, both facts about how the dependency was published and neither recoverable from the DAR, so pinning a dependency to generation 0 with no suffix names a version nobody published and fails restore with NU1103");
        file.Content.Should().Contain(
            "<PackageReference Include=\"My.Dependency\" Version=\"1.0.0.*-*\" />",
            "the DAR supplies only the intrinsic Major.Minor.Patch, so a run that is not itself producing the dependency must float over every generation and prerelease tag published for that intrinsic version");
    }

    [Fact]
    public void GenerateProjectFile_still_floats_dependencies_when_a_version_suffix_stamps_this_package_only()
    {
        var options = CreateOptions(versionSuffix: "preview.1");
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };
        var externalReferences = new List<DamlPackage>
        {
            new()
            {
                PackageId = "dep-id-1",
                Name = "my-dependency",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules = [],
                DependencyReferences = []
            }
        };

        var file = generator.GenerateProjectFile(package, externalReferences);

        file.Content.Should().Contain(
            "<PackageReference Include=\"My.Dependency\" Version=\"1.0.0.*-*\" />",
            "a prerelease suffix stamps the version this run produces for its own package and says nothing about how the dependency was published, so it must not be projected onto the dependency reference");
        file.Content.Should().Contain(
            "<Version>1.0.0.0-preview.1</Version>",
            "the suffix still applies to the package this run does produce");
    }

    [Fact]
    public void GenerateProjectFile_pins_sibling_references_at_generation_zero_when_the_run_publishes_them()
    {
        var options = new CodeGenOptions
        {
            TargetFramework = "net10.0",
            GenerateProjectFile = true,
            PublishesReferencedPackages = true,
        };
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };
        var externalReferences = new List<DamlPackage>
        {
            new()
            {
                PackageId = "dep-id-1",
                Name = "my-dependency",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                DependencyReferences = [],
                Modules = []
            }
        };

        var file = generator.GenerateProjectFile(package, externalReferences);

        file.Content.Should().Contain(
            "<PackageReference Include=\"My.Dependency\" Version=\"1.0.0.0\" />",
            "an empty release-counter store mints generation ordinal 0, so a genuine first release of a family co-produces siblings at .0 and must pin them there — the ordinal's value cannot stand in for whether the run publishes its dependencies");
    }

    [Fact]
    public void GenerateProjectFile_applies_emitter_counter_and_suffix_to_sibling_references()
    {
        var options = new CodeGenOptions
        {
            TargetFramework = "net10.0",
            GenerateProjectFile = true,
            RuntimePackageVersion = "1.2.3",
            EmitterCounter = 5,
            VersionSuffix = "preview.2",
            PublishesReferencedPackages = true,
        };
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(0, 1, 6),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };
        var externalReferences = new List<DamlPackage>
        {
            new()
            {
                PackageId = "dep-id-1",
                Name = "my-dependency",
                Version = new Version(3, 0, 0),
                LfVersion = "2.1",
                Modules = [],
                DependencyReferences = []
            }
        };

        var file = generator.GenerateProjectFile(package, externalReferences);

        file.Content.Should().Contain(
            "<PackageReference Include=\"My.Dependency\" Version=\"3.0.0.5-preview.2\" />",
            "a co-produced sibling carries the same emitter counter and prerelease suffix as the main package, so a stable-floor reference would exclude the actually-produced prerelease (NU1102)");
        file.Content.Should().Contain(
            "<PackageReference Include=\"Daml.Runtime\" Version=\"1.2.3\" />",
            "the runtime reference is not a co-produced sibling and keeps its supplied version unchanged");
    }

    [Fact]
    public void GenerateProjectFile_should_include_runtime_package_reference()
    {
        var options = CreateOptions(runtimeVersion: "1.2.3");
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().Contain("<PackageReference Include=\"Daml.Runtime\" Version=\"1.2.3\" />");
        file.Content.Should().NotContain("Daml.Codegen.CSharp.Runtime");
    }

    [Fact]
    public void GenerateProjectFile_should_default_runtime_reference_to_the_emitter_lockstep_version()
    {
        var options = CreateOptions(runtimeVersion: null);
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateProjectFile(package);

        var lockstepVersion = typeof(ProjectFileGenerator).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];
        file.Content.Should().Contain(
            $"<PackageReference Include=\"Daml.Runtime\" Version=\"{lockstepVersion}\" />",
            "with --runtime-version omitted, the generated project must pin the runtime that is lockstep-versioned with this emitter");
        lockstepVersion.Should().NotContain("+",
            "NuGet version ranges reject SemVer build metadata, so the source-revision suffix must be stripped");
        lockstepVersion.Should().MatchRegex(
            @"^\d+\.\d+\.\d+(\.\d+)?(-[0-9A-Za-z][0-9A-Za-z.-]*)?$",
            "the emitted reference must parse as a NuGet package version");
        file.Content.Should().NotContain("Version=\"*\"",
            "a floating * matches stable versions only and fails restore when the runtime ships as a prerelease");
    }

    [Fact]
    public void GenerateProjectFile_should_include_dependency_references()
    {
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };
        var externalReferences = new List<DamlPackage>
        {
            new()
            {
                PackageId = "dep-id-1",
                Name = "my-dependency",
                Version = new Version(2, 0, 0),
                LfVersion = "2.1",
                Modules = [],
                DependencyReferences = []
            }
        };

        var file = generator.GenerateProjectFile(package, externalReferences);

        file.Content.Should().Contain("<PackageReference Include=\"My.Dependency\" Version=\"2.0.0.*-*\" />");
    }

    [Fact]
    public void GenerateProjectFile_should_not_emit_leading_dot_package_id_for_leading_hyphen_dependency_name()
    {
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };
        var externalReferences = new List<DamlPackage>
        {
            new()
            {
                PackageId = "lf1x-prim-id",
                Name = "-no-package-metadata",
                Version = new Version(0, 0, 0),
                LfVersion = "1.6",
                Modules = [],
                DependencyReferences = []
            }
        };

        var file = generator.GenerateProjectFile(package, externalReferences);

        file.Content.Should().Contain("<PackageReference Include=\"No.Package.Metadata\" Version=\"0.0.0.*-*\" />");
        file.Content.Should().NotContain("Include=\".No.Package.Metadata");
    }

    [Fact]
    public void GenerateProjectFile_should_include_ledger_abstractions_package_reference()
    {
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().Contain("<PackageReference Include=\"Daml.Ledger.Abstractions\"");
    }

    [Fact]
    public void GenerateProjectFile_should_emit_one_PackageReference_per_external_dependency()
    {
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };
        var externalReferences = new List<DamlPackage>
        {
            new()
            {
                PackageId = "dep1-id",
                Name = "known-dep",
                Version = new Version(2, 0, 0),
                LfVersion = "2.1",
                Modules = [],
                DependencyReferences = []
            },
            new()
            {
                PackageId = "dep3-id",
                Name = "another-known-dep",
                Version = new Version(3, 0, 0),
                LfVersion = "2.1",
                Modules = [],
                DependencyReferences = []
            }
        };

        var file = generator.GenerateProjectFile(package, externalReferences);

        file.Content.Should().Contain("<PackageReference Include=\"Another.Known.Dep\" Version=\"3.0.0.*-*\" />");
        file.Content.Should().Contain("<PackageReference Include=\"Known.Dep\" Version=\"2.0.0.*-*\" />");
    }

    [Fact]
    public void GenerateProjectFile_should_xml_escape_runtime_version_in_attribute_value()
    {
        var options = new CodeGenOptions
        {
            TargetFramework = "net10.0",
            RuntimePackageVersion = "1.0.0\"injected",
            GenerateProjectFile = true,
        };
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().Contain(
            "Version=\"1.0.0&quot;injected\"",
            "user-supplied runtime version flows into a csproj attribute and embedded quotes must be escaped");
    }

    [Fact]
    public void GenerateReadme_should_show_the_runtime_row_with_the_lockstep_fallback_when_runtime_version_is_null()
    {
        var options = CreateOptions(runtimeVersion: null);
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "splice-amulet",
            Version = new Version(1, 2, 3),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateReadme(package);

        var lockstepVersion = typeof(ProjectFileGenerator).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];
        file.Content.Should().Contain(
            $"| Runtime dependency  | `Daml.Runtime` `{lockstepVersion}` |",
            "with --runtime-version omitted the README must document the same lockstep runtime the csproj pins");
    }
}
