// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.DigitalAsset.Daml.Lf.Archive.DamlLf;
using Google.Protobuf;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.DarParser.Tests;

public sealed class DalfReaderLfVersionTests
{
    [Fact]
    public void Parse_throws_when_payload_is_lf1()
    {
        var act = () => DalfReader.Parse(DalfTestFixtures.Lf1Dalf());

        act.Should().Throw<NotSupportedException>()
            .WithMessage("Daml-LF 1.x archives are not supported here; set <DamlCodegenMode>Dpm</DamlCodegenMode> in your project to decode them with `dpm codegen-cs`.");
    }

    [Theory]
    [InlineData("1", "2.1")]
    [InlineData("2", "2.2")]
    [InlineData("3", "2.3")]
    [InlineData("4", "2.4")]
    [InlineData("5-rc1", "2.5-rc1")]
    public void Parse_reads_lf2_payload_and_reports_minor_in_lf_version(string minor, string expectedLfVersion)
    {
        var result = DalfReader.Parse(Lf2DalfWithMinor(minor));

        result.Name.Should().Be("test-pkg");
        result.LfVersion.Should().Be(expectedLfVersion);
    }

    [Theory]
    [InlineData("99")]
    [InlineData("dev")]
    [InlineData("5")]
    [InlineData("5-rc2")]
    [InlineData("")]
    public void Parse_throws_when_lf2_minor_is_not_supported(string minor)
    {
        var act = () => DalfReader.Parse(Lf2DalfWithMinor(minor));

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"Daml-LF 2.{minor} archives are not supported here; " +
                "the supported minors are 2.1, 2.2, 2.3, 2.4, 2.5-rc1; set <DamlCodegenMode>Dpm</DamlCodegenMode> in your project to decode other versions with `dpm codegen-cs`.");
    }

    private static byte[] Lf2DalfWithMinor(string minor)
    {
        var builder = new DalfPackageBuilder();
        var payload = new ArchivePayload
        {
            Minor = minor,
            DamlLf2 = ByteString.CopyFrom(builder.Package.ToByteArray())
        };
        return DalfTestFixtures.ArchiveWithVerifiedHash(payload.ToByteArray()).ToByteArray();
    }
}
