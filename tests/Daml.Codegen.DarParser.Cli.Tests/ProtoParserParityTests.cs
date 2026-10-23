// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.CSharp.Tests;
using Daml.Codegen.DarParser;
using Daml.Codegen.Intermediate;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.DarParser.Cli.Tests;

/// <summary>
/// Snapshot-equivalence test for the emitter input contract.
/// The proto-direct path (IntermediateDarReader against a JVM-helper
/// produced .binpb) must produce codegen output identical to the
/// parser-direct path (DarArchive against the same .dar) for every
/// snapshot fixture that carries both.
///
/// The per-file comparison is exact string equality and must stay that
/// way. Comparing the two outputs as sets — or multisets — of lines would
/// mask a dropped duplicate line: emitting one of two identical lines is a
/// real regression that a line-set diff reports as parity. Exact equality
/// gives that sensitivity structurally, with no helper that can quietly
/// regress into set-based diffing, so the comparison must not be
/// "optimised" into one.
/// </summary>
public class ProtoParserParityTests
{
    private const int LeastSnapshotsWorthComparing = 12;

    public static TheoryData<string> SnapshotNames() => ParitySnapshots.Names();

    [Fact]
    public void ProtoParserParity_corpus_covers_every_snapshot_fixture()
    {
        ParitySnapshots.Discovered().Should().HaveCountGreaterThanOrEqualTo(LeastSnapshotsWorthComparing,
            "the csproj must copy every Snapshots/<name> .dar and intermediate.binpb pair, " +
            "otherwise parity quietly narrows back to a couple of fixtures");
    }

    [Fact]
    public async Task ProtoParserParity_corpus_declares_at_least_one_numeric_field()
    {
        var numericFieldCount = 0;
        foreach (var snapshotName in ParitySnapshots.Discovered())
        {
            var parserDirect = await DarArchive.ReadAsync(
                ParitySnapshots.DarPath(snapshotName), TestContext.Current.CancellationToken);
            numericFieldCount += NumericArgumentsByFieldPath(parserDirect.MainPackage).Count;
        }

        numericFieldCount.Should().BeGreaterThan(0,
            "the numeric-scale comparison proves nothing unless some fixture DAR declares a Numeric field");
    }

    [Theory]
    [MemberData(nameof(SnapshotNames))]
    public async Task ProtoParserParity_proto_and_parser_paths_emit_equivalent_csharp_for_fixture_dar(string snapshotName)
    {
        var darPath = ParitySnapshots.DarPath(snapshotName);
        var protoPath = ParitySnapshots.ProtoPath(snapshotName);

        File.Exists(darPath).Should().BeTrue($"fixture DAR must ship at {darPath}");
        File.Exists(protoPath).Should().BeTrue(
            $"the JVM-helper-produced IntermediateDar snapshot must ship at {protoPath}; " +
            "refresh with `java -jar jvm-helper/target/scala-2.13/daml-dar-to-proto.jar " +
            $"--dar tests/Daml.Codegen.CSharp.Tests/Snapshots/{snapshotName}/{snapshotName}.dar " +
            $"--out tests/Daml.Codegen.CSharp.Tests/Snapshots/{snapshotName}/intermediate.binpb`");

        var generator = new CSharpCodeGenerator(new CodeGenOptions());

        var parserDirect = await DarArchive.ReadAsync(darPath, TestContext.Current.CancellationToken);
        var parserDirectFiles = generator.Generate(parserDirect);

        IntermediateDar proto;
        await using (var stream = File.OpenRead(protoPath))
        {
            proto = IntermediateDar.Parser.ParseFrom(stream);
        }
        var protoDirect = IntermediateDarReader.Read(proto);
        var protoDirectFiles = new CSharpCodeGenerator(new CodeGenOptions()).Generate(protoDirect);

        var parserDirectPaths = parserDirectFiles
            .Select(f => f.RelativePath.Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        var protoDirectPaths = protoDirectFiles
            .Select(f => f.RelativePath.Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        protoDirectPaths.Should().Equal(parserDirectPaths,
            "the proto-direct and parser-direct paths must produce the same set of generated files for the same DAR");

        var parserByPath = parserDirectFiles.ToDictionary(f => f.RelativePath.Replace('\\', '/'), f => f.Content);
        var protoByPath = protoDirectFiles.ToDictionary(f => f.RelativePath.Replace('\\', '/'), f => f.Content);

        foreach (var relPath in parserDirectPaths)
        {
            var parserContent = parserByPath[relPath];
            var protoContent = protoByPath[relPath];

            if (parserContent == protoContent) continue;

            var diff = UnifiedDiff.Render(parserContent, protoContent)
                ?? "(contents differ but render identically)";
            throw new Xunit.Sdk.XunitException(
                $"`{relPath}` is not identical between the parser-direct and proto-direct paths.\n\n{diff}");
        }
    }

    [Theory]
    [MemberData(nameof(SnapshotNames))]
    public async Task ProtoParserParity_proto_and_parser_paths_agree_on_numeric_scale_for_fixture_dar(string snapshotName)
    {
        var darPath = ParitySnapshots.DarPath(snapshotName);
        var protoPath = ParitySnapshots.ProtoPath(snapshotName);

        File.Exists(darPath).Should().BeTrue($"fixture DAR must ship at {darPath}");
        File.Exists(protoPath).Should().BeTrue(
            $"the JVM-helper-produced IntermediateDar snapshot must ship at {protoPath}; " +
            "refresh with `java -jar jvm-helper/target/scala-2.13/daml-dar-to-proto.jar " +
            $"--dar tests/Daml.Codegen.CSharp.Tests/Snapshots/{snapshotName}/{snapshotName}.dar " +
            $"--out tests/Daml.Codegen.CSharp.Tests/Snapshots/{snapshotName}/intermediate.binpb`");

        var parserDirect = await DarArchive.ReadAsync(darPath, TestContext.Current.CancellationToken);

        IntermediateDar proto;
        await using (var stream = File.OpenRead(protoPath))
        {
            proto = IntermediateDar.Parser.ParseFrom(stream);
        }
        var protoDirect = IntermediateDarReader.Read(proto);

        var parserScales = NumericArgumentsByFieldPath(parserDirect.MainPackage);
        var protoScales = NumericArgumentsByFieldPath(protoDirect.MainPackage);

        parserScales.Should().Equal(protoScales,
            "the parser-direct and proto-direct paths must resolve every Numeric to the same scale");
        parserScales.Values.Should().AllSatisfy(argument => argument.Should().StartWith("var:"),
            "a Numeric's scale must survive parsing as its own type argument, not collapse to a primitive");
    }

    [Theory]
    [MemberData(nameof(SnapshotNames))]
    public async Task ProtoParserParity_proto_and_parser_paths_declare_the_same_data_types_in_every_package(string snapshotName)
    {
        var parserDirect = await DarArchive.ReadAsync(
            ParitySnapshots.DarPath(snapshotName), TestContext.Current.CancellationToken);

        IntermediateDar proto;
        await using (var stream = File.OpenRead(ParitySnapshots.ProtoPath(snapshotName)))
        {
            proto = IntermediateDar.Parser.ParseFrom(stream);
        }
        var protoDirect = IntermediateDarReader.Read(proto);

        var parserNames = DataTypeNamesByPackage(parserDirect);
        var protoNames = DataTypeNamesByPackage(protoDirect);

        protoNames.Values.SelectMany(names => names).Should().NotContain(
            ["DA.Internal.Fail.Types:FailureStatus", "DA.Numeric:RoundingMode"],
            "the JVM helper strips these non-serializable stdlib types, which is what makes this comparison bite");
        parserNames.Should().BeEquivalentTo(protoNames,
            "the .NET DarParser must strip exactly the non-serializable data types the JVM helper's SignatureErasure strips");
    }

    private static SortedDictionary<string, List<string>> DataTypeNamesByPackage(IDarSource dar)
    {
        var byPackage = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var package in dar.AllPackages)
        {
            byPackage[package.PackageId] = package.Modules
                .SelectMany(module => module.DataTypes.Select(dataType => $"{module.Name}:{dataType.Name}"))
                .Order(StringComparer.Ordinal)
                .ToList();
        }
        return byPackage;
    }

    private static SortedDictionary<string, string> NumericArgumentsByFieldPath(DamlPackage package)
    {
        var byPath = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var module in package.Modules)
        {
            foreach (var dataType in module.DataTypes)
            {
                if (dataType.Definition is not DamlRecordDefinition record) continue;
                foreach (var field in record.Fields)
                {
                    CollectNumericArguments($"{module.Name}.{dataType.Name}.{field.Name}", field.Type, byPath);
                }
            }
        }
        return byPath;
    }

    private static void CollectNumericArguments(
        string path, DamlType type, SortedDictionary<string, string> byPath)
    {
        switch (type)
        {
            case DamlTypeApp app:
                if (app.Base is DamlPrimitiveType { Primitive: DamlPrimitive.Numeric })
                {
                    byPath[path] = string.Join(", ", app.Arguments.Select(DescribeArgument));
                    return;
                }

                for (var index = 0; index < app.Arguments.Count; index++)
                {
                    CollectNumericArguments($"{path}[{index}]", app.Arguments[index], byPath);
                }
                break;
            case DamlListType list:
                CollectNumericArguments($"{path}[list]", list.Element, byPath);
                break;
            case DamlOptionalType optional:
                CollectNumericArguments($"{path}[optional]", optional.Value, byPath);
                break;
            case DamlTextMapType textMap:
                CollectNumericArguments($"{path}[textmap]", textMap.Value, byPath);
                break;
            case DamlGenMapType genMap:
                CollectNumericArguments($"{path}[genmap-key]", genMap.Key, byPath);
                CollectNumericArguments($"{path}[genmap-value]", genMap.Value, byPath);
                break;
            case DamlContractIdType contractId:
                CollectNumericArguments($"{path}[contractid]", contractId.Payload, byPath);
                break;
        }
    }

    private static string DescribeArgument(DamlType type) => type switch
    {
        DamlTypeVar typeVar => $"var:{typeVar.Name}",
        DamlPrimitiveType primitive => $"primitive:{primitive.Primitive}",
        _ => $"other:{type}",
    };
}
