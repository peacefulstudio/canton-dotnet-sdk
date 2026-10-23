// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using Com.DigitalAsset.Daml.Lf.Archive.DamlLf;
using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using Google.Protobuf;
using AwesomeAssertions;
using Xunit;
using PbInt = Daml.Codegen.Intermediate;
using DalfType = Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type;

namespace Daml.Codegen.DarParser.Tests;

/// <summary>
/// Characterization of <c>BUILTIN_TYPE_FAILURE_CATEGORY</c> in the checked-in splice-api-token-holding-v1
/// fixture, recorded ahead of the builtin-catalog milestone. The facts pinned here drive the catalog's
/// FAILURE_CATEGORY disposition: the SignatureOnly row. The pool indices are measured against the
/// fixture's stdlib 3.3.0.20250502.13767.0 (Daml-LF 2.1) by a schema-driven typed-protobuf descent.
/// </summary>
public sealed class FailureCategoryCharacterizationTests
{
    private const string StdlibPackageName = "daml-stdlib";
    private const string FailTypesModule = "DA.Internal.Fail.Types";
    private static readonly string FailTypesPackageName = "daml-stdlib-" + FailTypesModule.Replace('.', '-');

    private static readonly int[] StdlibFailureCategoryPoolIndices =
        [2185, 2186, 2187, 2188, 2189, 2190, 2191, 2210, 2213, 2220, 2221, 2223, 2224, 2225, 2230, 2231, 2232, 2233, 2234];
    private static readonly int[] FailTypesFailureCategoryPoolIndices = [1, 6, 7, 9];

    [Fact]
    public void FailureCategoryCharacterization_splice_fixture_pool_carries_failure_category_only_in_stdlib_packages()
    {
        var hits = ReadFixturePackages()
            .Select(p => (p.Name, Indices: PoolIndicesCarryingFailureCategory(p.Package).ToList()))
            .ToList();

        hits.Where(h => h.Name != StdlibPackageName && h.Name != FailTypesPackageName)
            .Should().NotContain(
                h => h.Indices.Count > 0,
                "the main splice package and every non-stdlib dependency carry no FAILURE_CATEGORY anywhere in their interned type pools");

        var stdlib = hits.Should().ContainSingle(h => h.Name == StdlibPackageName).Subject;
        stdlib.Indices.Should().BeEquivalentTo(StdlibFailureCategoryPoolIndices);

        var failTypes = hits.Should().ContainSingle(h => h.Name == FailTypesPackageName).Subject;
        failTypes.Indices.Should().BeEquivalentTo(FailTypesFailureCategoryPoolIndices);
    }

    [Fact]
    public void FailureCategoryCharacterization_splice_fixture_serializable_data_field_positions_never_carry_failure_category()
    {
        var occurrences = ReadFixturePackages()
            .SelectMany(p => DefinitionPositionsCarryingFailureCategory(p.Package))
            .ToList();

        occurrences.Should().NotContain(
            o => o.SerializablePosition,
            "no serializable data type, template, interface, or choice position carries FAILURE_CATEGORY anywhere in the fixture");

        var fieldOccurrences = occurrences.Where(o => o.Kind == "data-field").ToList();
        fieldOccurrences.Should().ContainSingle().Which
            .Should().Be(
                new FailureCategoryOccurrence(
                    FailTypesPackageName, FailTypesModule, "data-field",
                    "FailureStatus.category", SerializablePosition: false),
                "the single data-field occurrence is the non-serializable DA.Internal.Fail.Types:FailureStatus record's category field");

        occurrences.Should().HaveCount(20);
        occurrences.Where(o => o.Kind == "value-type-signature").Should().HaveCount(
            19,
            "every other occurrence is a value-definition type signature, a position the parser never surfaces " +
            "because DalfReader converts data types, templates, and interfaces only, never DefValue types");
        occurrences.Should().OnlyContain(o =>
            o.PackageName == StdlibPackageName || o.PackageName == FailTypesPackageName);

        var emitterSuppressedStdlibSnapshotTree = Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Snapshots", "splice-api-token-holding-v1", "expected");
        Directory.EnumerateFiles(emitterSuppressedStdlibSnapshotTree, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f).Contains("FailureStatus", StringComparison.Ordinal))
            .Should().BeEmpty("the stdlib packages carrying every observed occurrence are suppressed by the emitter, so no occurrence reaches a data-position type mapping");
        Directory.EnumerateFiles(emitterSuppressedStdlibSnapshotTree, "*", SearchOption.AllDirectories)
            .Should().NotContain(f => File.ReadAllText(f).Contains("FailureStatus", StringComparison.Ordinal));
    }

    [Fact]
    public void FailureCategoryCharacterization_splice_fixture_jvm_produced_intermediate_carries_no_failure_category()
    {
        var binpbPath = Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Snapshots", "splice-api-token-holding-v1", "intermediate.binpb");
        var proto = PbInt.IntermediateDar.Parser.ParseFrom(File.ReadAllBytes(binpbPath));

        var hits = new List<string>();
        foreach (var package in new[] { proto.Main }.Concat(proto.Dependencies))
        {
            foreach (var module in package.Modules)
            {
                var moduleName = string.Join(".", module.NameSegments);
                foreach (var dataType in module.DataTypes)
                {
                    if (dataType.Record != null)
                        foreach (var field in dataType.Record.Fields)
                            ScanIntermediateType(field.Type, $"{moduleName}:{dataType.Name}.{field.Name}", hits);
                    if (dataType.Variant != null)
                        foreach (var ctor in dataType.Variant.Constructors)
                            ScanIntermediateType(ctor.Type, $"{moduleName}:{dataType.Name}.{ctor.Name}", hits);
                }
                foreach (var template in module.Templates)
                {
                    ScanIntermediateType(template.KeyType, $"{moduleName}:{template.Name}:key", hits);
                    foreach (var choice in template.Choices)
                    {
                        ScanIntermediateType(choice.ArgumentType, $"{moduleName}:{template.Name}:{choice.Name}:arg", hits);
                        ScanIntermediateType(choice.ReturnType, $"{moduleName}:{template.Name}:{choice.Name}:ret", hits);
                    }
                }
                foreach (var iface in module.Interfaces)
                {
                    ScanIntermediateType(iface.ViewType, $"{moduleName}:{iface.Name}:view", hits);
                    foreach (var method in iface.Methods)
                        ScanIntermediateType(method.ReturnType, $"{moduleName}:{iface.Name}:method:{method.Name}", hits);
                    foreach (var choice in iface.Choices)
                    {
                        ScanIntermediateType(choice.ArgumentType, $"{moduleName}:{iface.Name}:{choice.Name}:arg", hits);
                        ScanIntermediateType(choice.ReturnType, $"{moduleName}:{iface.Name}:{choice.Name}:ret", hits);
                    }
                }
            }
        }

        hits.Should().BeEmpty(
            "the JVM producer's SignatureErasure drops the only data type that references FailureCategory " +
            "(non-serializable FailureStatus), so the public pipeline's only input shape carries none at all");
    }

    private sealed record FailureCategoryOccurrence(
        string PackageName, string ModuleName, string Kind, string Position, bool SerializablePosition);

    private static IEnumerable<(string Name, Package Package)> ReadFixturePackages()
    {
        var darPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "splice-api-token-holding-v1.dar");
        using var zip = ZipFile.OpenRead(darPath);
        foreach (var entry in zip.Entries.Where(e =>
                     e.Name.EndsWith(".dalf", StringComparison.OrdinalIgnoreCase)))
        {
            using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);

            var archive = ParseWithRecursionLimit<Archive>(buffer.ToArray());
            var payload = ParseWithRecursionLimit<ArchivePayload>(archive.Payload.ToByteArray());
            var package = ParseWithRecursionLimit<Package>(payload.DamlLf2.ToByteArray());
            yield return (PackageNameOf(package), package);
        }
    }

    private static T ParseWithRecursionLimit<T>(byte[] data) where T : IMessage<T>, new()
    {
        const int recursionLimitAboveProtobufDefault = 1000;
        using var stream = new MemoryStream(data);
        var codedInput = CodedInputStream.CreateWithLimits(stream, data.Length, recursionLimitAboveProtobufDefault);
        var message = new T();
        message.MergeFrom(codedInput);
        return message;
    }

    private static string PackageNameOf(Package package)
    {
        var metadata = package.Metadata;
        if (metadata is null) return "<no-metadata>";
        return metadata.NameInternedStr >= 0 && metadata.NameInternedStr < package.InternedStrings.Count
            ? package.InternedStrings[metadata.NameInternedStr]
            : "<unknown-name>";
    }

    private static string ModuleNameOf(Package package, int internedDname) =>
        internedDname >= 0 && internedDname < package.InternedDottedNames.Count
            ? string.Join(".", package.InternedDottedNames[internedDname].SegmentsInternedStr
                .Select(i => i >= 0 && i < package.InternedStrings.Count ? package.InternedStrings[i] : $"?{i}"))
            : $"?{internedDname}";

    private static string StringOf(Package package, int internedStr) =>
        internedStr >= 0 && internedStr < package.InternedStrings.Count
            ? package.InternedStrings[internedStr]
            : $"?{internedStr}";

    private static IEnumerable<int> PoolIndicesCarryingFailureCategory(Package package)
    {
        for (var i = 0; i < package.InternedTypes.Count; i++)
        {
            if (ContainsFailureCategory(package.InternedTypes[i], package, [], depth: 0))
                yield return i;
        }
    }

    private static IEnumerable<FailureCategoryOccurrence> DefinitionPositionsCarryingFailureCategory(Package package)
    {
        foreach (var module in package.Modules)
        {
            var moduleName = ModuleNameOf(package, module.NameInternedDname);

            foreach (var synonym in module.Synonyms)
                if (ContainsFailureCategory(synonym.Type, package, [], depth: 0))
                    yield return new FailureCategoryOccurrence(
                        PackageNameOf(package), moduleName, "synonym", ModuleNameOf(package, synonym.NameInternedDname), false);

            foreach (var dataType in module.DataTypes)
            {
                var typeName = ModuleNameOf(package, dataType.NameInternedDname);
                if (dataType.Record != null)
                    foreach (var field in dataType.Record.Fields_)
                        if (ContainsFailureCategory(field.Type, package, [], depth: 0))
                            yield return new FailureCategoryOccurrence(
                                PackageNameOf(package), moduleName, "data-field",
                                $"{typeName}.{StringOf(package, field.FieldInternedStr)}", dataType.Serializable);
                if (dataType.Variant != null)
                    foreach (var constructor in dataType.Variant.Fields_)
                        if (ContainsFailureCategory(constructor.Type, package, [], depth: 0))
                            yield return new FailureCategoryOccurrence(
                                PackageNameOf(package), moduleName, "data-field",
                                $"{typeName}.{StringOf(package, constructor.FieldInternedStr)}", dataType.Serializable);
            }

            foreach (var value in module.Values)
                if (value.NameWithType != null && ContainsFailureCategory(value.NameWithType.Type, package, [], depth: 0))
                    yield return new FailureCategoryOccurrence(
                        PackageNameOf(package), moduleName, "value-type-signature",
                        ModuleNameOf(package, value.NameWithType.NameInternedDname), false);

            foreach (var template in module.Templates)
            {
                var templateName = ModuleNameOf(package, template.TyconInternedDname);
                if (template.Key != null && ContainsFailureCategory(template.Key.Type, package, [], depth: 0))
                    yield return new FailureCategoryOccurrence(
                        PackageNameOf(package), moduleName, "template", $"{templateName}:key", true);
                foreach (var choice in template.Choices)
                {
                    if (ContainsFailureCategory(choice.ArgBinder?.Type, package, [], depth: 0))
                        yield return new FailureCategoryOccurrence(
                            PackageNameOf(package), moduleName, "template",
                            $"{templateName}:{StringOf(package, choice.NameInternedStr)}:arg", true);
                    if (ContainsFailureCategory(choice.RetType, package, [], depth: 0))
                        yield return new FailureCategoryOccurrence(
                            PackageNameOf(package), moduleName, "template",
                            $"{templateName}:{StringOf(package, choice.NameInternedStr)}:ret", true);
                }
            }

            foreach (var iface in module.Interfaces)
            {
                var interfaceName = ModuleNameOf(package, iface.TyconInternedDname);
                foreach (var method in iface.Methods)
                    if (ContainsFailureCategory(method.Type, package, [], depth: 0))
                        yield return new FailureCategoryOccurrence(
                            PackageNameOf(package), moduleName, "interface",
                            $"{interfaceName}:method:{StringOf(package, method.MethodInternedName)}", false);
                foreach (var choice in iface.Choices)
                {
                    if (ContainsFailureCategory(choice.ArgBinder?.Type, package, [], depth: 0))
                        yield return new FailureCategoryOccurrence(
                            PackageNameOf(package), moduleName, "interface",
                            $"{interfaceName}:{StringOf(package, choice.NameInternedStr)}:arg", true);
                    if (ContainsFailureCategory(choice.RetType, package, [], depth: 0))
                        yield return new FailureCategoryOccurrence(
                            PackageNameOf(package), moduleName, "interface",
                            $"{interfaceName}:{StringOf(package, choice.NameInternedStr)}:ret", true);
                }
                if (ContainsFailureCategory(iface.View, package, [], depth: 0))
                    yield return new FailureCategoryOccurrence(
                        PackageNameOf(package), moduleName, "interface", $"{interfaceName}:view", true);
            }
        }
    }

    private static bool ContainsFailureCategory(DalfType? type, Package package, HashSet<int> seenPoolEntries, int depth)
    {
        const int maxDepth = 512;
        if (type is null || depth > maxDepth) return false;

        switch (type.SumCase)
        {
            case DalfType.SumOneofCase.Builtin:
                if (type.Builtin.Builtin_ == BuiltinType.FailureCategory) return true;
                return type.Builtin.Args.Any(arg => ContainsFailureCategory(arg, package, seenPoolEntries, depth + 1));
            case DalfType.SumOneofCase.Con:
                return type.Con.Args.Any(arg => ContainsFailureCategory(arg, package, seenPoolEntries, depth + 1));
            case DalfType.SumOneofCase.Syn:
                return type.Syn.Args.Any(arg => ContainsFailureCategory(arg, package, seenPoolEntries, depth + 1));
            case DalfType.SumOneofCase.Var:
                return type.Var.Args.Any(arg => ContainsFailureCategory(arg, package, seenPoolEntries, depth + 1));
            case DalfType.SumOneofCase.Forall:
                return ContainsFailureCategory(type.Forall.Body, package, seenPoolEntries, depth + 1);
            case DalfType.SumOneofCase.Tapp:
                return ContainsFailureCategory(type.Tapp.Lhs, package, seenPoolEntries, depth + 1)
                    || ContainsFailureCategory(type.Tapp.Rhs, package, seenPoolEntries, depth + 1);
            case DalfType.SumOneofCase.Struct:
                return type.Struct.Fields.Any(f => ContainsFailureCategory(f.Type, package, seenPoolEntries, depth + 1));
            case DalfType.SumOneofCase.InternedType:
                var poolIndex = type.InternedType;
                var isFirstVisitOfExistingPoolEntry = poolIndex >= 0
                    && poolIndex < package.InternedTypes.Count
                    && seenPoolEntries.Add(poolIndex);
                return isFirstVisitOfExistingPoolEntry
                    && ContainsFailureCategory(package.InternedTypes[poolIndex], package, seenPoolEntries, depth + 1);
            default:
                return false;
        }
    }

    private static void ScanIntermediateType(PbInt.Type? type, string path, List<string> hits)
    {
        if (type is null) return;
        if (type.SortCase == PbInt.Type.SortOneofCase.Builtin && type.Builtin == PbInt.BuiltinType.FailureCategory)
            hits.Add($"{path}:builtin:FAILURE_CATEGORY");
        if (type.SortCase == PbInt.Type.SortOneofCase.TypeApp)
        {
            ScanIntermediateType(type.TypeApp.Function, path, hits);
            foreach (var argument in type.TypeApp.Arguments)
                ScanIntermediateType(argument, path, hits);
        }
    }
}
