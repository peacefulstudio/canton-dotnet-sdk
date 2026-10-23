// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Runtime.Tests;

public sealed class PackageNameReferenceTests
{
    private static readonly Identifier Template = new("9b63deadbeefhash", "RichTypes", "RichRecord");

    [Fact]
    public void WithPackageNameReference_puts_the_hash_prefixed_package_name_in_the_package_id_slot()
    {
        var referenced = Template.WithPackageNameReference("richtypes");

        referenced.Should().Be(new Identifier("#richtypes", "RichTypes", "RichRecord"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithPackageNameReference_rejects_a_blank_package_name_naming_the_template_and_its_package_id(
        string packageName)
    {
        var referencing = () => Template.WithPackageNameReference(packageName);

        referencing.Should().Throw<ArgumentException>()
            .WithMessage("*RichTypes:RichRecord*9b63deadbeefhash*")
            .And.ParamName.Should().Be("packageName");
    }

    [Fact]
    public void WithPackageNameReference_rejects_a_null_package_name_with_the_same_message()
    {
        var referencing = () => Template.WithPackageNameReference(null!);

        referencing.Should().Throw<ArgumentNullException>()
            .WithMessage("*RichTypes:RichRecord*9b63deadbeefhash*")
            .And.ParamName.Should().Be("packageName");
    }

    [Fact]
    public void WithPackageNameReference_rejects_a_package_name_that_already_carries_the_prefix()
    {
        var referencing = () => Template.WithPackageNameReference("#richtypes");

        referencing.Should().Throw<ArgumentException>()
            .WithMessage("*'#richtypes' already carries the '#'*")
            .And.ParamName.Should().Be("packageName");
    }

    [Theory]
    [InlineData("-no-package-metadata")]
    [InlineData("UnknownPackage")]
    [InlineData("")]
    public void AddressedByPackageName_leaves_the_identifier_alone_for_a_placeholder_package_name(
        string placeholder)
    {
        Template.AddressedByPackageName(placeholder).Should().BeSameAs(Template);
    }

    [Fact]
    public void AddressedByPackageName_references_a_real_package_name()
    {
        Template.AddressedByPackageName("richtypes").Should().Be(new Identifier("#richtypes", "RichTypes", "RichRecord"));
    }

    [Fact]
    public void AddressedByPackageName_rejects_a_package_name_that_already_carries_the_prefix()
    {
        var addressing = () => Template.AddressedByPackageName("#richtypes");

        addressing.Should().Throw<ArgumentException>().WithMessage("*already carries the '#'*");
    }

    [Fact]
    public void MarkerDescriptors_rejects_a_marker_that_is_neither_a_template_nor_an_interface()
    {
        var resolving = MarkerDescriptors.Of<NeitherMarker>;

        resolving.Should().Throw<InvalidOperationException>()
            .WithMessage("*NeitherMarker*is neither IDamlInterface nor ITemplate*");
    }

    [Fact]
    public void MarkerDescriptors_returns_the_descriptor_of_an_interface_marker()
    {
        var descriptor = MarkerDescriptors.Of<InterfaceMarker>();

        descriptor.Should().Be(new DamlTypeDescriptor(
            new Identifier("iface-pkg", "Token.Api", "IHolding"), DamlTypeKind.Interface, "token-api"));
    }

    private sealed record NeitherMarker : IDamlType
    {
        public static DamlTypeDescriptor DamlTypeId =>
            throw new NotSupportedException("a degenerate marker is never asked for its descriptor");
    }

    private sealed record InterfaceMarker : IDamlInterface
    {
        public static Identifier InterfaceId { get; } = new("iface-pkg", "Token.Api", "IHolding");
        public static string PackageId => "iface-pkg";
        public static string PackageName => "token-api";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);
        public DamlRecord ToRecord() => new(InterfaceId, []);
    }
}
