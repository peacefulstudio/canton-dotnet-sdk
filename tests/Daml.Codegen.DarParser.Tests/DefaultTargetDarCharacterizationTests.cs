// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.DarParser;
using AwesomeAssertions;
using Xunit;
using EmittedNote = Daml.Codegen.Testing.Conformance.DefaultTarget.Note;

namespace Daml.Codegen.DarParser.Tests;

public sealed class DefaultTargetDarCharacterizationTests
{
    private const string LfVersionDamlcEmitsWhenNoTargetIsRequested = "2.3";

    private static async Task<DarArchive> LoadAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "defaulttarget.dar");
        return await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DefaultTargetDarCharacterization_main_package_built_without_build_options_reports_the_damlc_default_lf_version()
    {
        var archive = await LoadAsync();

        archive.MainPackage.Name.Should().Be("defaulttarget-h4f032a323aad");
        archive.MainPackage.Version.Should().Be(new Version(0, 0, 1));
        archive.MainPackage.LfVersion.Should().Be(
            LfVersionDamlcEmitsWhenNoTargetIsRequested,
            "the fixture declares no target so damlc picks the version most scaffolded projects ship; "
            + "a mismatch means the default moved and the reader now needs to cover a new version, "
            + "not that the fixture needs a target pin");
    }

    [Fact]
    public async Task DefaultTargetDarCharacterization_template_in_the_default_target_dar_is_parsed_with_its_scalar_fields()
    {
        var archive = await LoadAsync();

        var module = archive.MainPackage.Modules.Should().ContainSingle().Which;
        module.Name.Should().Be("DefaultTarget");

        module.Templates.Should().ContainSingle().Which.Name.Should().Be("Note");

        var fields = module.DataTypes.Single(d => d.Name == "Note")
            .Definition.Should().BeOfType<DamlRecordDefinition>().Subject.Fields;

        fields.Single(f => f.Name == "owner").Type
            .Should().Be(new DamlPrimitiveType(DamlPrimitive.Party));
        fields.Single(f => f.Name == "body").Type
            .Should().Be(new DamlPrimitiveType(DamlPrimitive.Text));
        fields.Single(f => f.Name == "revision").Type
            .Should().Be(new DamlPrimitiveType(DamlPrimitive.Int64));
    }

    [Fact]
    public async Task DefaultTargetDarCharacterization_shipped_conformance_types_are_emitted_from_this_dar()
    {
        var archive = await LoadAsync();

        archive.MainPackage.LfVersion.Should().Be(
            LfVersionDamlcEmitsWhenNoTargetIsRequested,
            "this fact is the emission evidence for that LF version, and it proves nothing about it once the "
            + "fixture is rebuilt against a different target");

        EmittedNote.PackageName.Should().Be(archive.MainPackage.Name);
        EmittedNote.PackageVersion.Should().Be(archive.MainPackage.Version);
        EmittedNote.PackageId.Should().Be(
            archive.MainPackage.PackageId,
            "the conformance package compiles and ships C# the emitter produced from this archive, so a "
            + "package-id mismatch means the shipped tree came from somewhere else and nothing emits from "
            + "the version this fixture carries");

        var module = archive.MainPackage.Modules.Should().ContainSingle().Which;
        var template = module.Templates.Should().ContainSingle().Which;

        EmittedNote.TemplateId.ModuleName.Should().Be(module.Name);
        EmittedNote.TemplateId.EntityName.Should().Be(
            template.Name,
            "a hard-coded name would keep agreeing with a stale generated tree after the fixture is "
            + "renamed, so the expected value is read from the archive the shipped tree claims to "
            + "have come from");
    }

    [Fact]
    public void DefaultTargetDarCharacterization_fixture_project_requests_no_lf_target()
    {
        var projectFile = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "defaulttarget.daml.yaml"));

        projectFile.Should().NotContain(
            "--target",
            "this fixture exists to track the version damlc picks unprompted, so pinning any target — "
            + "including the version it happens to emit today — turns it into a restatement of another "
            + "fixture's pin while every other assertion in this class stays green");
        projectFile.Should().NotContain(
            "build-options",
            "a build-options block is where a target pin would arrive, so the fixture declares none");
    }
}
