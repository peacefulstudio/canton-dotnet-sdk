// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using Com.DigitalAsset.Daml.Lf.Archive.DamlLf;
using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.DarParser;
using Google.Protobuf;
using AwesomeAssertions;
using Xunit;
using Type = Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type;

namespace Daml.Codegen.DarParser.Tests;

/// <summary>
/// Characterization of the structural builtins in the checked-in splice-api-token-holding-v1 fixture's
/// interned type pools, proving the parser's pool resolution preserves real identity: the six
/// structural type-formers are catalog SignatureOnly rows with real <see cref="DamlPrimitive"/> members,
/// and the splice pool carries them. The parser never surfaces these
/// into <see cref="DarModel"/> (interface methods are dropped and serializable positions cannot
/// reference them), so pool resolution through <see cref="PackageContext"/> is the only observable
/// surface. The pinned indices are measured against the fixture (bundled stdlib
/// 3.3.0.20250502.13767.0, Daml-LF 2.1), so a fixture refresh that moves them fails loudly instead of
/// silently passing stale pins. The main package's interned type pool has 385 entries: ANY and TYPE_REP
/// appear bare, ARROW and UPDATE appear applied (2 and 1 arguments), and ANY_EXCEPTION and
/// FAILURE_CATEGORY live only in the bundled daml-stdlib pool. The stdlib FAILURE_CATEGORY pool pin at [2185] extends
/// <c>FailureCategoryCharacterizationTests</c>, which pins the Fail-Types pool and the containing-entry
/// index lists.
/// </summary>
public sealed class PackageContextSplicePoolResolutionTests
{
    private const string MainPackageName = "splice-api-token-holding-v1";
    private const string StdlibPackageName = "daml-stdlib";

    private static readonly int[] MainAnyIndices = [63];
    private static readonly int[] MainTypeRepIndices = [69];
    private static readonly int[] MainUpdateIndices = [211];
    private static readonly int[] MainArrowIndices =
    [
        15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 64, 66, 70, 78, 79, 84, 85, 102, 103,
        104, 105, 106, 107, 109, 110, 111, 112, 113, 114, 115, 117, 118, 120, 121, 122, 123,
        124, 126, 127, 129, 130, 131, 132, 133, 135, 136, 138, 139, 146, 147, 150, 151,
        152, 153, 154, 157, 159, 161, 164, 165, 167, 168, 169, 171, 172, 173, 174, 175,
        176, 177, 179, 180, 181, 183, 184, 185, 186, 187, 188, 189, 191, 192, 193, 195,
        196, 197, 198, 199, 200, 201, 203, 204, 210, 212, 213, 215, 217, 220, 221, 222,
        223, 230, 231, 233, 234, 240, 241, 242, 243, 245, 246, 247, 248, 249, 250, 251,
        252, 253, 254, 255,
    ];

    private static readonly int[] StdlibAnyExceptionIndices = [3882];
    private static readonly int[] StdlibFailureCategoryIndices = [2185];

    [Fact]
    public void PackageContext_splice_main_pool_structural_builtin_indices_match_the_pinned_measurement()
    {
        var package = ReadFixturePackage(MainPackageName);

        IndicesOf(package, BuiltinType.Any).Should().Equal(MainAnyIndices);
        IndicesOf(package, BuiltinType.TypeRep).Should().Equal(MainTypeRepIndices);
        IndicesOf(package, BuiltinType.Update).Should().Equal(MainUpdateIndices);
        IndicesOf(package, BuiltinType.Arrow).Should().Equal(MainArrowIndices);
        IndicesOf(package, BuiltinType.AnyException).Should().BeEmpty(
            "the main package's pool carries no ANY_EXCEPTION — it lives in the bundled stdlib pool");
        IndicesOf(package, BuiltinType.FailureCategory).Should().BeEmpty(
            "the main package's pool carries no FAILURE_CATEGORY — see FailureCategoryCharacterizationTests");
    }

    [Fact]
    public void PackageContext_splice_main_pool_resolves_structural_builtins_to_real_identity()
    {
        var context = new PackageContext(ReadFixturePackage(MainPackageName));

        context.GetType(MainAnyIndices.Single())
            .Should().Be(new DamlPrimitiveType(DamlPrimitive.Any),
                "the documented ANY pool index must resolve to Any's real identity, " +
                "never an erasure to Unit");
        context.GetType(MainTypeRepIndices.Single())
            .Should().Be(new DamlPrimitiveType(DamlPrimitive.TypeRep),
                "the documented TYPE_REP pool index must resolve to TypeRep's real identity");

        var update = context.GetType(MainUpdateIndices.Single())
            .Should().BeOfType<DamlTypeApp>().Subject;
        update.Base.Should().Be(new DamlPrimitiveType(DamlPrimitive.Update),
            "the applied UPDATE pool entry keeps Update's real identity as the application's base");
        update.Arguments.Should().HaveCount(1);

        var arrow = context.GetType(MainArrowIndices.First())
            .Should().BeOfType<DamlTypeApp>().Subject;
        arrow.Base.Should().Be(new DamlPrimitiveType(DamlPrimitive.Arrow),
            "the applied ARROW pool entries keep Arrow's real identity as the application's base");
        arrow.Arguments.Should().HaveCount(2);
    }

    [Fact]
    public void PackageContext_splice_stdlib_pool_resolves_any_exception_and_failure_category_to_real_identity()
    {
        var context = new PackageContext(ReadFixturePackage(StdlibPackageName));

        context.GetType(StdlibAnyExceptionIndices.Single())
            .Should().Be(new DamlPrimitiveType(DamlPrimitive.AnyException),
                "the bundled stdlib's ANY_EXCEPTION pool index must resolve to AnyException's real " +
                "identity — the fixture's only ANY_EXCEPTION pool occurrence");
        context.GetType(StdlibFailureCategoryIndices.Single())
            .Should().Be(new DamlPrimitiveType(DamlPrimitive.FailureCategory),
                "the bundled stdlib's bare FAILURE_CATEGORY pool index must resolve to " +
                "FailureCategory's real identity per its SignatureOnly characterization");
    }

    [Fact]
    public async Task PackageContext_splice_fixture_parses_end_to_end_with_the_new_pool_identities()
    {
        var darPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "splice-api-token-holding-v1.dar");
        var archive = await DarArchive.ReadAsync(darPath, TestContext.Current.CancellationToken);

        archive.MainPackage.Name.Should().Be(MainPackageName,
            "parsing the fixture eagerly converts every package's whole interned type pool — " +
            "structural builtins included — so a successful parse proves the real-identity mapping " +
            "holds over the real corpus, not just synthetic inputs");
    }

    private static IEnumerable<int> IndicesOf(Package package, BuiltinType builtin)
    {
        for (var i = 0; i < package.InternedTypes.Count; i++)
        {
            var type = package.InternedTypes[i];
            if (type.SumCase == Type.SumOneofCase.Builtin && type.Builtin.Builtin_ == builtin)
            {
                yield return i;
            }
        }
    }

    private static Package ReadFixturePackage(string packageName)
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
            if (PackageNameOf(package) == packageName)
            {
                return package;
            }
        }

        throw new InvalidOperationException(
            $"the fixture carries no package named '{packageName}'");
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
}
