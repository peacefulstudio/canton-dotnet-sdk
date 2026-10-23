// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;

namespace Daml.Codegen.DarParser.Tests;

internal sealed class DalfPackageBuilder
{
    public Package Package { get; } = new();

    public DalfPackageBuilder()
    {
        Package.Metadata = new PackageMetadata
        {
            NameInternedStr = Intern("test-pkg"),
            VersionInternedStr = Intern("1.0.0"),
        };
    }

    public int Intern(string value)
    {
        var index = Package.InternedStrings.Count;
        Package.InternedStrings.Add(value);
        return index;
    }

    public int DottedName(params string[] segments)
    {
        var dn = new InternedDottedName();
        foreach (var segment in segments)
            dn.SegmentsInternedStr.Add(Intern(segment));
        var index = Package.InternedDottedNames.Count;
        Package.InternedDottedNames.Add(dn);
        return index;
    }

    public TypeConId LocalTypeConId(int nameDname, int moduleDname) =>
        new()
        {
            Module = new ModuleId
            {
                PackageId = new SelfOrImportedPackageId { SelfPackageId = new Unit() },
                ModuleNameInternedDname = moduleDname,
            },
            NameInternedDname = nameDname,
        };

    public TypeConId ImportedTypeConId(int nameDname, int moduleDname, int importedPackageId) =>
        new()
        {
            Module = new ModuleId
            {
                PackageId = new SelfOrImportedPackageId { ImportedPackageIdInternedStr = importedPackageId },
                ModuleNameInternedDname = moduleDname,
            },
            NameInternedDname = nameDname,
        };
}
