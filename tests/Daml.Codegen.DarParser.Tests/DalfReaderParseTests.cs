// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.DigitalAsset.Daml.Lf.Archive.DamlLf;
using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using Google.Protobuf;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.DarParser;
using AwesomeAssertions;
using Xunit;
using Type = Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type;

namespace Daml.Codegen.DarParser.Tests;

public sealed class DalfReaderParseTests
{
    [Fact]
    public void Parse_reports_the_payload_hash_as_the_package_id_for_a_well_formed_dalf()
    {
        var dalf = DalfTestFixtures.MinimalDalf();

        var result = DalfReader.Parse(dalf);

        result.PackageId.Should().Be(DalfTestFixtures.PackageIdOf(dalf));
    }

    [Fact]
    public void ParseObservingCancellation_with_a_cancelled_token_throws_OperationCanceledException_before_converting_a_module()
    {
        var dataType = new DefDataType { Serializable = true, Record = new DefDataType.Types.Fields() };
        var dalf = DalfTestFixtures.SerializeArchive(DalfTestFixtures.PackageWithDataType(dataType));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => DalfReader.ParseObservingCancellation(dalf, cancellation.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void Parse_rejects_a_declared_hash_that_does_not_match_the_payload()
    {
        var dalf = DalfTestFixtures.MinimalDalf();
        var tampered = DalfTestFixtures.DalfDeclaringAMismatchedHash(dalf);
        var declaredHash = Archive.Parser.ParseFrom(tampered).Hash;
        var computedHash = DalfTestFixtures.PackageIdOf(dalf);

        var act = () => DalfReader.Parse(tampered);

        act.Should().Throw<InvalidDataException>()
            .WithMessage($"*{declaredHash}*{computedHash}*");
    }

    [Fact]
    public void Parse_rejects_a_declared_hash_that_differs_from_the_computed_one_only_in_letter_case()
    {
        var uppercased = DalfTestFixtures.DalfDeclaringItsHashInUppercase(DalfTestFixtures.MinimalDalf());
        var declaredHash = Archive.Parser.ParseFrom(uppercased).Hash;

        var act = () => DalfReader.Parse(uppercased);

        act.Should().Throw<InvalidDataException>()
            .WithMessage(
                $"*{declaredHash}*lowercase*",
                "a producer emitting uppercase hex emits it in PackageImports too, and dependency "
                + "references resolve by exact match — so accepting the DALF only defers the failure "
                + "to a silently unresolved reference");
    }

    [Fact]
    public void Parse_rejects_a_declared_hash_that_is_not_sixty_four_hexadecimal_characters()
    {
        var archive = Archive.Parser.ParseFrom(DalfTestFixtures.MinimalDalf());
        archive.Hash = archive.Hash[..63];

        var act = () => DalfReader.Parse(archive.ToByteArray());

        act.Should().Throw<InvalidDataException>()
            .WithMessage(
                "*lowercase*",
                "the length is part of the package-id form, so a truncated hash is malformed at the "
                + "file rather than merely mismatched against the payload");
    }

    [Fact]
    public void Parse_rejects_an_archive_declaring_a_hash_function_the_reader_does_not_implement()
    {
        const HashFunction unimplemented = (HashFunction)7;
        var archive = Archive.Parser.ParseFrom(DalfTestFixtures.MinimalDalf());
        archive.HashFunction = unimplemented;

        var act = () => DalfReader.Parse(archive.ToByteArray());

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*hash function*");
    }

    [Fact]
    public void Parse_throws_when_archive_payload_has_no_package_data()
    {
        var emptyPayload = new ArchivePayload { Minor = "1" };
        var archive = DalfTestFixtures.ArchiveWithVerifiedHash(emptyPayload.ToByteArray());

        var act = () => DalfReader.Parse(archive.ToByteArray());

        act.Should().Throw<InvalidDataException>()
            .WithMessage("DALF ArchivePayload missing Package data");
    }

    [Fact]
    public void Parse_wraps_a_truncated_top_level_archive_as_invalid_data_exception()
    {
        var builder = new DalfPackageBuilder();
        var bytes = DalfTestFixtures.SerializeArchive(builder.Package);
        var truncated = bytes[..^5];

        var act = () => DalfReader.Parse(truncated);

        act.Should().Throw<InvalidDataException>()
            .Which.InnerException.Should().BeOfType<InvalidProtocolBufferException>();
    }

    [Fact]
    public void Parse_wraps_a_truncated_nested_package_message_as_invalid_data_exception()
    {
        var builder = new DalfPackageBuilder();
        var packageBytes = builder.Package.ToByteArray();
        var truncatedPackageBytes = packageBytes[..^5];

        var payload = new ArchivePayload
        {
            Minor = "1",
            DamlLf2 = ByteString.CopyFrom(truncatedPackageBytes)
        };
        var archive = DalfTestFixtures.ArchiveWithVerifiedHash(payload.ToByteArray());

        var act = () => DalfReader.Parse(archive.ToByteArray());

        act.Should().Throw<InvalidDataException>()
            .Which.InnerException.Should().BeOfType<InvalidProtocolBufferException>();
    }

    [Fact]
    public void Parse_throws_invalid_data_exception_for_a_single_byte_input()
    {
        var act = () => DalfReader.Parse([0xFF]);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Parse_throws_when_the_package_has_no_metadata()
    {
        var pkg = new Package();
        pkg.InternedStrings.Add("placeholder");

        var act = () => DalfReader.Parse(DalfTestFixtures.SerializeArchive(pkg));

        act.Should().Throw<InvalidDataException>().WithMessage("*carries no package metadata*");
    }

    [Fact]
    public void Parse_keeps_the_full_snapshot_version_string_beside_the_three_part_version()
    {
        var pkg = new Package();
        pkg.InternedStrings.Add("daml-stdlib");
        pkg.InternedStrings.Add("3.3.0.20250502.13767.0");
        pkg.Metadata = new PackageMetadata { NameInternedStr = 0, VersionInternedStr = 1 };

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(pkg));

        result.RawVersion.Should().Be("3.3.0.20250502.13767.0");
        result.Version.Should().Be(new Version(3, 3, 0));
    }

    [Fact]
    public void Parse_extracts_upgraded_package_id_when_present_in_metadata()
    {
        const int pkgNameIdx = 0;
        const int pkgVersionIdx = 1;
        const int upgradedPkgIdIdx = 2;

        var pkg = new Package();
        pkg.InternedStrings.Add("my-pkg");
        pkg.InternedStrings.Add("2.0.0");
        pkg.InternedStrings.Add("abc123oldpkgid");

        pkg.Metadata = new PackageMetadata
        {
            NameInternedStr = pkgNameIdx,
            VersionInternedStr = pkgVersionIdx,
            UpgradedPackageId = new UpgradedPackageId { UpgradedPackageIdInternedStr = upgradedPkgIdIdx }
        };

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(pkg));

        result.UpgradedPackageId.Should().Be("abc123oldpkgid");
    }

    [Fact]
    public void Parse_rejects_a_curried_tapp_chain_that_over_applies_a_text_map()
    {
        var textMapType = new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Textmap } };
        var textType = new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Text } };
        var int64Type = new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Int64 } };
        var textMapAppliedToText = new Type { Tapp = new Type.Types.TApp { Lhs = textMapType, Rhs = textType } };
        var textMapAppliedToTextAndInt64 = new Type { Tapp = new Type.Types.TApp { Lhs = textMapAppliedToText, Rhs = int64Type } };

        const int fieldNameStrIdx = 4;
        var field = new FieldWithType { FieldInternedStr = fieldNameStrIdx, Type = textMapAppliedToTextAndInt64 };
        var record = new DefDataType.Types.Fields();
        record.Fields_.Add(field);
        var dataType = new DefDataType { Record = record, Serializable = true };
        var pkg = DalfTestFixtures.PackageWithDataType(dataType, "theField");

        var act = () => DalfReader.Parse(DalfTestFixtures.SerializeArchive(pkg));

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*'TextMap'*catalog arity is 1*",
                "the nested TApp chain spells one two-argument TextMap application — flatten composes the " +
                "arity the fold checks, so currying cannot smuggle an over-applied builtin through");
    }

    [Fact]
    public void Parse_builds_enum_definition_with_constructor_names_from_interned_strings()
    {
        const int redIdx = 4;
        const int greenIdx = 5;
        const int blueIdx = 6;
        var enumCtors = new DefDataType.Types.EnumConstructors();
        enumCtors.ConstructorsInternedStr.Add(redIdx);
        enumCtors.ConstructorsInternedStr.Add(greenIdx);
        enumCtors.ConstructorsInternedStr.Add(blueIdx);
        var dataType = new DefDataType { Enum = enumCtors, Serializable = true };
        var pkg = DalfTestFixtures.PackageWithDataType(dataType, "Red", "Green", "Blue");

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(pkg));

        var definition = result.Modules[0].DataTypes[0].Definition
            .Should().BeOfType<DamlEnumDefinition>().Subject;
        definition.Constructors.Should().Equal("Red", "Green", "Blue");
    }

    [Fact]
    public void Parse_reads_an_interface_data_type_as_a_record_with_no_fields()
    {
        var pkg = DalfTestFixtures.PackageWithDataType(new DefDataType { Interface = new Unit() });

        var result = DalfReader.Parse(DalfTestFixtures.SerializeArchive(pkg));

        result.Modules[0].DataTypes[0].Definition
            .Should().BeOfType<DamlRecordDefinition>()
            .Which.Fields.Should().BeEmpty(
                "an interface's data_cons is the Unit marker, so it genuinely carries no fields — "
                + "the whole Splice corpus reaches this arm and must not be read as a malformed shape");
    }

    [Fact]
    public void Parse_rejects_a_data_type_that_sets_no_data_cons_shape()
    {
        var pkg = DalfTestFixtures.PackageWithDataType(new DefDataType());

        var act = () => DalfReader.Parse(DalfTestFixtures.SerializeArchive(pkg));

        act.Should().Throw<InvalidDataException>()
            .WithMessage(
                "*TestModule.TestType*None*",
                "a shapeless data type read as an empty record drops every field the Daml type "
                + "declares, so the diagnostic has to name the type that lost them");
    }

    [Fact]
    public void Parse_rejects_a_record_field_that_carries_no_type()
    {
        const int fieldNameStrIdx = 4;
        var record = new DefDataType.Types.Fields();
        record.Fields_.Add(new FieldWithType { FieldInternedStr = fieldNameStrIdx });
        var pkg = DalfTestFixtures.PackageWithDataType(new DefDataType { Record = record, Serializable = true }, "theField");

        var act = () => DalfReader.Parse(DalfTestFixtures.SerializeArchive(pkg));

        act.Should().Throw<InvalidDataException>()
            .WithMessage(
                "*carries no type*",
                "a field whose type is absent read as Unit round-trips as the wrong shape "
                + "rather than failing at the archive that is malformed");
    }
}
