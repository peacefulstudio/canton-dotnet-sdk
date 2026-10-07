// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Daml.Codegen.Intermediate.Model;
using Microsoft.CodeAnalysis;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

internal static class ReservedTypeNameFixtures
{
    internal const string Module = "Test.Module";

    internal static DamlType IntType => new DamlPrimitiveType(DamlPrimitive.Int64);

    internal static DamlType PartyType => new DamlPrimitiveType(DamlPrimitive.Party);

    internal static DamlTypeRef Ref(string name) => new("", Module, name);

    internal static DamlTypeRef RefIn(string module, string name, string packageId = "") => new(packageId, module, name);

    internal static DamlFieldDefinition Field(string name, DamlType type) => new(name, type);

    internal static DamlDataType Record(string name, params DamlFieldDefinition[] fields) =>
        new() { Name = name, Definition = new DamlRecordDefinition(fields) };

    internal static DamlDataType Variant(string name, params DamlVariantConstructor[] constructors) =>
        new() { Name = name, Definition = new DamlVariantDefinition(constructors) };

    internal static DamlDataType Enum(string name, params string[] constructors) =>
        new() { Name = name, Definition = new DamlEnumDefinition(constructors) };

    internal static DamlChoice Choice(string name, DamlType returnType, DamlType? argumentType = null) => new()
    {
        Name = name,
        Consuming = false,
        ArgumentType = argumentType ?? new DamlPrimitiveType(DamlPrimitive.Unit),
        ReturnType = returnType,
    };

    internal static DamlTemplate Template(string name, DamlType? key, params DamlChoice[] choices) =>
        new() { Name = name, Key = key, Choices = choices };

    internal static DamlModule ModuleOf(string name, IReadOnlyList<DamlTemplate> templates, params DamlDataType[] dataTypes) =>
        new() { Name = name, Templates = templates, DataTypes = dataTypes, Interfaces = [] };

    internal static DamlPackage PackageOf(string packageId, string name, params DamlModule[] modules) =>
        new()
        {
            PackageId = packageId,
            Name = name,
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = modules,
            DependencyReferences = [],
        };

    internal static DarModel DarOf(IReadOnlyList<DamlTemplate> templates, params DamlDataType[] dataTypes) =>
        DarOfModules(ModuleOf(Module, templates, dataTypes));

    internal static DarModel DarOfModules(params DamlModule[] modules) =>
        new() { MainPackage = PackageOf("test-package-id", "test-package", modules), Dependencies = [] };

    internal static List<string> CompileErrors(DarModel dar) =>
        CompileEmittedFiles(CreateGenerator().Generate(dar))
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.GetMessage(CultureInfo.InvariantCulture) + " @ " + d.Location)
            .ToList();

    internal static bool DeclaresType(DarModel dar, string typeName, string module = Module) =>
        CompileEmittedFilesToCompilation(CreateGenerator().Generate(dar), DocumentationMode.Parse)
            .GetTypeByMetadataName($"{module}.{typeName}") is not null;
}
