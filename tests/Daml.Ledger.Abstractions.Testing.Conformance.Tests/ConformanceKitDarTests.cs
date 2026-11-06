// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Daml.Runtime.Contracts;
using Xunit;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

public sealed partial class ConformanceKitDarTests
{
    private const string KitPackageId = "40acda968038dce8336b3272860ef8687a1e925349b5d89a662ceac0a77e72d4";

    private const string KitPackageName = "conformancekit-hddd04e2d0e6f";

    [GeneratedRegex(@"^(?<name>.+)-(?<version>\d+\.\d+\.\d+)-(?<id>[0-9a-f]{64})/\k<name>-\k<version>-\k<id>\.dalf$")]
    private static partial Regex MainDalfEntry();

    [Fact]
    public void Open_returns_an_independent_readable_stream_on_each_call()
    {
        using var first = ConformanceKitDar.Open();
        using var second = ConformanceKitDar.Open();

        first.Should().NotBeSameAs(second);
        first.ReadByte().Should().Be('P');
        second.Position.Should().Be(0);
    }

    [Fact]
    public void Embedded_dar_main_package_has_the_pinned_package_id()
    {
        MainDalf().Groups["id"].Value.Should().Be(KitPackageId);
    }

    [Fact]
    public void Embedded_dar_main_package_has_the_pinned_package_name_and_version()
    {
        var main = MainDalf();

        main.Groups["name"].Value.Should().Be(KitPackageName);
        main.Groups["version"].Value.Should().Be("0.1.0");
    }

    [Fact]
    public void Embedded_dar_declares_the_probe_interface_module()
    {
        using var zip = new ZipArchive(ConformanceKitDar.Open());

        zip.Entries.Should().Contain(entry => entry.FullName.EndsWith("/Conformance/Kit.daml", StringComparison.Ordinal));
    }

    [Fact]
    public void IConformanceProbe_carries_the_package_id_of_the_embedded_dar()
    {
        IConformanceProbe.InterfaceId.PackageId.Should().Be(KitPackageId);
        IConformanceProbe.InterfaceId.PackageId.Should().Be(MainDalf().Groups["id"].Value);
    }

    [Fact]
    public void IConformanceProbe_carries_the_package_name_of_the_embedded_dar()
    {
        PackageNameOf<IConformanceProbe>().Should().Be(KitPackageName);
        PackageNameOf<IConformanceProbe>().Should().Be(MainDalf().Groups["name"].Value);
    }

    private static string PackageNameOf<T>() where T : IDamlInterface => T.PackageName;

    private static Match MainDalf()
    {
        using var zip = new ZipArchive(ConformanceKitDar.Open());
        var matches = zip.Entries
            .Select(entry => MainDalfEntry().Match(entry.FullName))
            .Where(match => match.Success)
            .ToList();

        matches.Should().ContainSingle();
        return matches[0];
    }
}
