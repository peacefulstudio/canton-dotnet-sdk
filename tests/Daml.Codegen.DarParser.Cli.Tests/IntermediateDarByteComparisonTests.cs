// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.Intermediate;
using Google.Protobuf;
using Xunit;

namespace Daml.Codegen.DarParser.Cli.Tests;

public class IntermediateDarByteComparisonTests
{
    private static IntermediateDar Dar(string packageId = "aa", string moduleName = "Main", string firstFieldName = "owner")
    {
        var record = new DataType { Name = "Asset", Record = new Daml.Codegen.Intermediate.Record() };
        record.Record.Fields.Add(new Field { Name = firstFieldName });
        var module = new IntermediateModule();
        module.NameSegments.Add(moduleName);
        module.DataTypes.Add(record);
        var main = new IntermediatePackage { PackageId = packageId, PackageName = "pkg", PackageVersion = "1.2.3" };
        main.Modules.Add(module);
        return new IntermediateDar { Main = main };
    }

    [Fact]
    public void DescribeFirstDifference_returns_null_for_identical_bytes()
    {
        var bytes = Dar().ToByteArray();

        IntermediateDarByteComparison.DescribeFirstDifference(bytes, bytes.ToArray()).Should().BeNull();
    }

    [Fact]
    public void DescribeFirstDifference_names_a_differing_scalar_field_path()
    {
        var difference = IntermediateDarByteComparison.DescribeFirstDifference(
            Dar(packageId: "aa").ToByteArray(), Dar(packageId: "bb").ToByteArray());

        difference.Should().Be("main.packageId: oracle \"aa\", actual \"bb\"");
    }

    [Fact]
    public void DescribeFirstDifference_names_a_differing_field_nested_in_repeated_messages()
    {
        var difference = IntermediateDarByteComparison.DescribeFirstDifference(
            Dar(firstFieldName: "owner").ToByteArray(), Dar(firstFieldName: "issuer").ToByteArray());

        difference.Should().Be("main.modules[0].dataTypes[0:Asset].record.fields[0:owner].name: oracle \"owner\", actual \"issuer\"");
    }

    [Fact]
    public void DescribeFirstDifference_reports_a_differing_repeated_scalar_element()
    {
        var difference = IntermediateDarByteComparison.DescribeFirstDifference(
            Dar(moduleName: "Main").ToByteArray(), Dar(moduleName: "Other").ToByteArray());

        difference.Should().Be("main.modules[0].nameSegments[0]: oracle \"Main\", actual \"Other\"");
    }

    [Fact]
    public void DescribeFirstDifference_reports_a_missing_repeated_element()
    {
        var withoutModule = Dar();
        withoutModule.Main.Modules.Clear();

        var difference = IntermediateDarByteComparison.DescribeFirstDifference(
            Dar().ToByteArray(), withoutModule.ToByteArray());

        difference.Should().Be("main.modules: oracle has 1 elements, actual has 0");
    }

    [Fact]
    public void DescribeFirstDifference_reports_the_byte_offset_when_only_the_encoding_differs()
    {
        var canonical = Dar().ToByteArray();
        var withUnknownTrailingField = canonical.Concat(new byte[] { 0x78, 0x01 }).ToArray();

        var difference = IntermediateDarByteComparison.DescribeFirstDifference(canonical, withUnknownTrailingField);

        difference.Should().Be(
            $"no differing field; the encodings first differ at byte offset {canonical.Length} " +
            $"(oracle {canonical.Length} bytes, actual {canonical.Length + 2} bytes)");
    }
}
