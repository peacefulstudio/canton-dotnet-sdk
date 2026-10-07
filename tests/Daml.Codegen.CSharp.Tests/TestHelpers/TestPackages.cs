// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.Tests.TestHelpers;

/// <summary>Packages built in memory for the dependencies an emitter test resolves type refs against.</summary>
public static class TestPackages
{
    /// <summary>The package id <see cref="DamlPrim"/> carries.</summary>
    public const string DamlPrimPackageId = "daml-prim";

    /// <summary>A package of the given id and name declaring <paramref name="modules"/>.</summary>
    public static DamlPackage Named(string packageId, string name, params DamlModule[] modules) =>
        new()
        {
            PackageId = packageId,
            Name = name,
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = modules,
            DependencyReferences = [],
        };

    /// <summary>The <c>daml-prim</c> stdlib package, so a ref into it resolves to the mapped runtime type.</summary>
    public static DamlPackage DamlPrim() => Named(DamlPrimPackageId, "daml-prim");

    /// <summary>A module declaring the given data types and nothing else.</summary>
    public static DamlModule ModuleOf(string name, params DamlDataType[] dataTypes) =>
        new() { Name = name, DataTypes = dataTypes, Templates = [], Interfaces = [] };

    /// <summary>A record data type with the given fields.</summary>
    public static DamlDataType Record(string name, params DamlFieldDefinition[] fields) =>
        new() { Name = name, Definition = new DamlRecordDefinition(fields) };
}
