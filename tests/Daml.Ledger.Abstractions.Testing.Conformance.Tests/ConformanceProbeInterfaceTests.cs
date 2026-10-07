// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

public sealed class ConformanceProbeInterfaceTests
{
    [Fact]
    public void InterfaceId_names_the_kit_interface()
    {
        var interfaceId = IConformanceProbe.InterfaceId;

        interfaceId.PackageId.Should().Be("ledger-conformance-kit-pkg");
        interfaceId.ModuleName.Should().Be("Conformance.Kit");
        interfaceId.EntityName.Should().Be("ConformanceProbe");
    }

    [Fact]
    public void Interface_package_metadata_names_the_kit_package()
    {
        InterfacePackageId<IConformanceProbe>().Should().Be("ledger-conformance-kit-pkg");
        InterfacePackageName<IConformanceProbe>().Should().Be("ledger-conformance-kit");
        InterfacePackageVersion<IConformanceProbe>().Should().Be(new Version(0, 1, 0));
    }

    [Fact]
    public void Interface_DamlTypeId_is_an_interface_descriptor_over_the_kit_identity()
    {
        var descriptor = DamlTypeIdOf<IConformanceProbe>();

        descriptor.Kind.Should().Be(DamlTypeKind.Interface);
        descriptor.Identifier.Should().Be(IConformanceProbe.InterfaceId);
        descriptor.PackageName.Should().Be("ledger-conformance-kit");
    }

    [Fact]
    public void View_is_one_shared_descriptor_for_the_kit_interface()
    {
        IConformanceProbe.View.Should().BeSameAs(IConformanceProbe.View);
    }

    [Fact]
    public void View_ToRecord_carries_the_amount_as_a_Numeric_field()
    {
        var record = new ConformanceProbeView(42.5m).ToRecord();

        record.GetRequiredField("amount").As<DamlNumeric>().Value.Should().Be(42.5m);
    }

    [Fact]
    public void View_FromRecord_reads_the_amount_field()
    {
        var record = DamlRecord.Create(DamlField.Create("amount", new DamlNumeric(42.5m)));

        ConformanceProbeView.FromRecord(record).Should().Be(new ConformanceProbeView(42.5m));
    }

    [Fact]
    public void View_reads_the_amount_from_Daml_LF_JSON_as_a_Numeric_string()
    {
        var record = DamlLfJsonReader.ReadRecord<ConformanceProbeView>("""{"amount": "42.5"}""");

        ConformanceProbeView.FromRecord(record).Amount.Should().Be(42.5m);
    }

    private static string InterfacePackageId<T>() where T : IDamlInterface => T.PackageId;

    private static string InterfacePackageName<T>() where T : IDamlInterface => T.PackageName;

    private static Version InterfacePackageVersion<T>() where T : IDamlInterface => T.PackageVersion;

    private static DamlTypeDescriptor DamlTypeIdOf<T>() where T : IDamlType => T.DamlTypeId;
}
