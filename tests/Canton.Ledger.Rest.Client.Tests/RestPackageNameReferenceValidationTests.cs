// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestPackageNameReferenceValidationTests
{
    private static readonly SubmitterInfo Submitter = (SubmitterInfo)new Party("party::alice");

    [Fact]
    public void FilterIdentifier_of_a_marker_with_an_empty_package_name_is_rejected_instead_of_going_on_the_wire_as_a_bare_hash()
    {
        var reading = () => RestMarkerMatcher<EmptyPackageNameMarker>.FilterIdentifier;

        reading.Should().Throw<ArgumentException>()
            .WithMessage("*Token.Api:IHolding*iface-pkg*")
            .And.ParamName.Should().Be("packageName");
    }

    [Fact]
    public void FilterIdentifier_of_a_marker_whose_package_name_already_carries_the_prefix_is_rejected()
    {
        var reading = () => RestMarkerMatcher<PrefixedPackageNameMarker>.FilterIdentifier;

        reading.Should().Throw<ArgumentException>().WithMessage("*'#token-api' already carries the '#'*");
    }

    [Fact]
    public void FilterIdentifier_of_a_marker_with_a_package_name_is_hash_prefixed()
    {
        var filter = RestMarkerMatcher<NamedPackageMarker>.FilterIdentifier;

        filter.PackageId.Should().Be("#token-api");
        filter.ModuleName.Should().Be("Token.Api");
        filter.EntityName.Should().Be("IHolding");
    }

    [Fact]
    public void A_marker_that_is_neither_a_template_nor_an_interface_is_rejected_when_the_matcher_is_first_used()
    {
        var reading = () => RestMarkerMatcher<NeitherMarker>.IsInterface;

        reading.Should().Throw<TypeInitializationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*is neither IDamlInterface nor ITemplate*");
    }

    [Fact]
    public void BuildTransactionFormat_rejects_an_interface_view_with_an_empty_package_name()
    {
        var building = () => RestSubscribeRequestBuilder.BuildTransactionFormat(
            Submitter, [EmptyPackageNameMarker.DamlTypeId]);

        building.Should().Throw<ArgumentException>()
            .WithMessage("*Token.Api:IHolding*iface-pkg*")
            .And.ParamName.Should().Be("packageName");
    }

    [Fact]
    public void BuildTransactionFormat_hash_prefixes_the_package_name_of_an_interface_view()
    {
        var format = RestSubscribeRequestBuilder.BuildTransactionFormat(
            Submitter, [NamedPackageMarker.DamlTypeId]);

        var interfaceId = format.EventFormat.FiltersByParty["party::alice"]
            .Cumulative.ElementAt(1).IdentifierFilter!.InterfaceFilter!.InterfaceId;
        interfaceId.PackageId.Should().Be("#token-api");
        interfaceId.ModuleName.Should().Be("Token.Api");
        interfaceId.EntityName.Should().Be("IHolding");
    }

    private sealed record NeitherMarker : IDamlType
    {
        public static DamlTypeDescriptor DamlTypeId =>
            throw new NotSupportedException("a degenerate marker is never asked for its descriptor");
    }

    private sealed record EmptyPackageNameMarker : IDamlInterface
    {
        public static RuntimeIdentifier InterfaceId { get; } = new("iface-pkg", "Token.Api", "IHolding");
        public static string PackageId => "iface-pkg";
        public static string PackageName => string.Empty;
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);
        public DamlRecord ToRecord() => new(InterfaceId, []);
    }

    private sealed record PrefixedPackageNameMarker : IDamlInterface
    {
        public static RuntimeIdentifier InterfaceId { get; } = new("iface-pkg", "Token.Api", "IHolding");
        public static string PackageId => "iface-pkg";
        public static string PackageName => "#token-api";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);
        public DamlRecord ToRecord() => new(InterfaceId, []);
    }

    private sealed record NamedPackageMarker : IDamlInterface
    {
        public static RuntimeIdentifier InterfaceId { get; } = new("iface-pkg", "Token.Api", "IHolding");
        public static string PackageId => "iface-pkg";
        public static string PackageName => "token-api";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);
        public DamlRecord ToRecord() => new(InterfaceId, []);
    }
}
