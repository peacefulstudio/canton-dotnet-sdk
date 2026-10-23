// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using Com.DigitalAsset.Daml.Lf.Archive.DamlLf;
using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using Google.Protobuf;
using Type = Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type;

namespace Daml.Codegen.DarParser.Tests;

internal static class DalfTestFixtures
{
    private const int PkgNameIdx = 0;
    private const int PkgVersionIdx = 1;
    private const int ModuleNameSegmentIdx = 2;
    private const int TypeNameSegmentIdx = 3;
    private const int ModuleDottedNameIdx = 0;
    private const int TypeDottedNameIdx = 1;

    internal static Type DeeplyNestedListType(int depth)
    {
        var current = new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Text } };
        for (var level = 0; level < depth; level++)
        {
            var list = new Type.Types.Builtin { Builtin_ = BuiltinType.List };
            list.Args.Add(current);
            current = new Type { Builtin = list };
        }

        return current;
    }

    internal static Expr DeclaredNoParties() =>
        new() { Nil = new Expr.Types.Nil { Type = new Type { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Party } } } };

    internal static byte[] SerializeArchive(Package package) =>
        ArchiveWithVerifiedHash(PayloadBytes(package)).ToByteArray();

    internal static Archive ArchiveWithVerifiedHash(byte[] payloadBytes) => new()
    {
        HashFunction = HashFunction.Sha256,
        Hash = HashOf(payloadBytes),
        Payload = ByteString.CopyFrom(payloadBytes)
    };

    internal static string PackageIdOf(byte[] dalf) => HashOf(Archive.Parser.ParseFrom(dalf).Payload.ToByteArray());

    internal static byte[] Lf1Dalf()
    {
        var payload = new ArchivePayload
        {
            Minor = "1",
            DamlLf1 = ByteString.CopyFrom([0xDE, 0xAD, 0xBE, 0xEF])
        };
        return ArchiveWithVerifiedHash(payload.ToByteArray()).ToByteArray();
    }

    internal static byte[] DalfDeclaringAMismatchedHash(byte[] dalf)
    {
        var archive = Archive.Parser.ParseFrom(dalf);
        var declared = archive.Hash;
        archive.Hash = declared[..^1] + (declared[^1] == '0' ? '1' : '0');
        return archive.ToByteArray();
    }

    internal static byte[] DalfDeclaringItsHashInUppercase(byte[] dalf)
    {
        var archive = Archive.Parser.ParseFrom(dalf);
        archive.Hash = archive.Hash.ToUpperInvariant();
        return archive.ToByteArray();
    }

    internal static byte[] MinimalDalf(string name = "test-pkg", string version = "1.0.0")
    {
        var pkg = new Package();
        pkg.InternedStrings.Add(name);
        pkg.InternedStrings.Add(version);
        pkg.Metadata = new PackageMetadata { NameInternedStr = PkgNameIdx, VersionInternedStr = PkgVersionIdx };
        return SerializeArchive(pkg);
    }

    internal static byte[] MinimalDalfImporting(string name, string version, params string[] importedPackageIds)
    {
        var pkg = new Package();
        pkg.InternedStrings.Add(name);
        pkg.InternedStrings.Add(version);
        pkg.Metadata = new PackageMetadata { NameInternedStr = PkgNameIdx, VersionInternedStr = PkgVersionIdx };
        pkg.PackageImports = new PackageImports();
        pkg.PackageImports.ImportedPackages.AddRange(importedPackageIds);
        return SerializeArchive(pkg);
    }

    internal static Package PackageWithDataType(DefDataType dataType, params string[] extraStrings)
    {
        var pkg = new Package();
        pkg.InternedStrings.Add("test-pkg");
        pkg.InternedStrings.Add("1.0.0");
        pkg.InternedStrings.Add("TestModule");
        pkg.InternedStrings.Add("TestType");
        foreach (var s in extraStrings)
            pkg.InternedStrings.Add(s);

        pkg.Metadata = new PackageMetadata { NameInternedStr = PkgNameIdx, VersionInternedStr = PkgVersionIdx };

        var moduleDn = new InternedDottedName();
        moduleDn.SegmentsInternedStr.Add(ModuleNameSegmentIdx);
        pkg.InternedDottedNames.Add(moduleDn);

        var typeDn = new InternedDottedName();
        typeDn.SegmentsInternedStr.Add(TypeNameSegmentIdx);
        pkg.InternedDottedNames.Add(typeDn);

        dataType.NameInternedDname = TypeDottedNameIdx;

        var module = new Module { NameInternedDname = ModuleDottedNameIdx };
        module.DataTypes.Add(dataType);
        pkg.Modules.Add(module);

        return pkg;
    }

    private static byte[] PayloadBytes(Package package)
    {
        var payload = new ArchivePayload
        {
            Minor = "1",
            DamlLf2 = ByteString.CopyFrom(package.ToByteArray())
        };
        return payload.ToByteArray();
    }

    private static string HashOf(byte[] payloadBytes) =>
        Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLowerInvariant();
}
