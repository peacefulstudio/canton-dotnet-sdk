// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections;
using System.Reflection;
using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Decodes crafted <c>GenMap</c> payloads through the <c>FromRecord</c> the emitter writes, compiled in
/// memory, and counts the distinct hash codes the decoded dictionary's own comparer gives its keys.
/// Every key is a multiple of <c>4294967297</c>, whose two 32-bit halves are equal, so a comparer that
/// folds a 64-bit value by exclusive-or puts all of them in one bucket.
/// </summary>
public class EmittedGenMapKeyHashCollisionTests
{
    private const string ModuleName = "Test.Keys";
    private const int KeyCount = 1_000;
    private const int MinimumDistinctHashes = 990;

    private static readonly DamlTypeRef KeyRef = new("", ModuleName, "Key");
    private static readonly DamlTypeRef BoxRef = new("", ModuleName, "Box");
    private static readonly DamlPrimitiveType Text = new(DamlPrimitive.Text);

    private static readonly Lazy<Type> Holder = new(() => CompileHolder());

    private static IEnumerable<long> KeysWithEqualLowAndHighHalves() =>
        Enumerable.Range(1, KeyCount).Select(k => k * 4_294_967_297L);

    private static Type CompileHolder()
    {
        var module = new DamlModule
        {
            Name = ModuleName,
            Templates = [],
            Interfaces = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Key",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("a", new DamlPrimitiveType(DamlPrimitive.Int64))]),
                },
                new DamlDataType
                {
                    Name = "Box",
                    TypeParams = ["t"],
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("value", new DamlTypeVar("t"))]),
                },
                new DamlDataType
                {
                    Name = "Holder",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("byInt", new DamlGenMapType(new DamlPrimitiveType(DamlPrimitive.Int64), Text)),
                        new DamlFieldDefinition("byNumeric", new DamlGenMapType(new DamlPrimitiveType(DamlPrimitive.Numeric), Text)),
                        new DamlFieldDefinition("byTime", new DamlGenMapType(new DamlPrimitiveType(DamlPrimitive.Timestamp), Text)),
                        new DamlFieldDefinition("byKey", new DamlGenMapType(KeyRef, Text)),
                        new DamlFieldDefinition(
                            "byBox",
                            new DamlGenMapType(new DamlTypeApp(BoxRef, [new DamlPrimitiveType(DamlPrimitive.Int64)]), Text)),
                    ]),
                },
            ],
        };

        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var files = new CSharpCodeGenerator(new CodeGenOptions { EnableNullableReferenceTypes = true })
            .Generate(new DarModel { MainPackage = package, Dependencies = [] });

        return EmittedCodeCompilesTestHelpers.EmitToAssembly(files).GetTypes().Single(type => type.Name == "Holder");
    }

    private static DamlGenMap MapOf(Func<long, DamlValue> key) =>
        new([.. KeysWithEqualLowAndHighHalves().Select(k => (key(k), (DamlValue)new DamlText("v")))]);

    private static DamlRecord SingleField(string name, DamlValue value) => DamlRecord.Create(DamlField.Create(name, value));

    private static object DecodeHolder() =>
        Holder.Value.GetMethod("FromRecord", BindingFlags.Public | BindingFlags.Static, [typeof(DamlRecord)])!
            .Invoke(null, [DamlRecord.Create(
                DamlField.Create("byInt", MapOf(k => new DamlInt64(k))),
                DamlField.Create("byNumeric", MapOf(k => new DamlNumeric((decimal)k))),
                DamlField.Create("byTime", MapOf(k => new DamlTimestamp(new DateTimeOffset(k * 10, TimeSpan.Zero)))),
                DamlField.Create("byKey", MapOf(k => SingleField("a", new DamlInt64(k)))),
                DamlField.Create("byBox", MapOf(k => SingleField("value", new DamlInt64(k)))))])!;

    private static int DistinctHashesUnderTheDictionarysComparer(string property)
    {
        var dictionary = Holder.Value.GetProperty(property)!.GetValue(DecodeHolder())!;
        var keyType = dictionary.GetType().GetGenericArguments()[0];
        var comparer = dictionary.GetType().GetProperty("Comparer")!.GetValue(dictionary)!;
        var hash = typeof(IEqualityComparer<>).MakeGenericType(keyType).GetMethod("GetHashCode")!;
        var keys = ((IDictionary)dictionary).Keys.Cast<object>().ToList();

        keys.Should().HaveCount(1_000);
        return keys.Select(key => (int)hash.Invoke(comparer, [key])!).Distinct().Count();
    }

    [Theory]
    [InlineData("ByInt")]
    [InlineData("ByNumeric")]
    [InlineData("ByTime")]
    [InlineData("ByKey")]
    [InlineData("ByBox")]
    public void FromRecord_spreads_genmap_keys_whose_64_bit_halves_are_equal(string property)
    {
        DistinctHashesUnderTheDictionarysComparer(property).Should().BeGreaterThanOrEqualTo(MinimumDistinctHashes);
    }
}
