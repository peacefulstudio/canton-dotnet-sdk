// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.Tooling;

/// <summary>
/// Produces the <c>Daml.Runtime.Stdlib</c> bindings for the <see cref="StdlibRuntimeInventoryAccess.Generated"/>
/// stdlib types by running the ordinary emitters over a synthetic package that holds exactly those
/// types in a single module, so the result carries the same members as any generated type.
/// </summary>
public static class StdlibRuntimeBindings
{
    private const string StdlibNamespace = "Daml.Runtime.Stdlib";

    private static readonly string SyntheticPackageId = new('0', 64);

    /// <summary>
    /// Emits one file per generated type, named <c>&lt;Type&gt;.cs</c> and declared in
    /// <c>Daml.Runtime.Stdlib</c>, in ordinal file-name order.
    /// </summary>
    /// <param name="stablePackages">
    /// The stdlib packages declaring the types, which also resolve the cross-package references
    /// those types make.
    /// </param>
    public static IReadOnlyList<GeneratedFile> Generate(IReadOnlyList<DamlPackage> stablePackages)
    {
        ArgumentNullException.ThrowIfNull(stablePackages);

        var dataTypes = SelectGeneratedDataTypes(stablePackages);
        var package = new DamlPackage
        {
            PackageId = SyntheticPackageId,
            Name = "daml-runtime-stdlib",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = StdlibNamespace,
                    Templates = [],
                    DataTypes = dataTypes,
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };
        var dar = new DarModel { MainPackage = package, Dependencies = stablePackages };
        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            IncludeDependencies = false,
        };

        var namespaceDirectory = StdlibNamespace.Replace('.', '/') + "/";
        return new CSharpCodeGenerator(options).Generate(dar)
            .Select(file => GeneratedFile.Text(file.RelativePath[namespaceDirectory.Length..], file.Content))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToList();
    }

    private static List<DamlDataType> SelectGeneratedDataTypes(IReadOnlyList<DamlPackage> stablePackages)
    {
        var declared = stablePackages
            .SelectMany(package => package.Modules.SelectMany(module => module.DataTypes.Select(dataType => (module.Name, dataType))))
            .ToLookup(entry => (entry.Name, entry.dataType.Name), entry => entry.dataType);

        var missing = StdlibRuntimeInventoryAccess.Generated.Where(key => !declared.Contains(key)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "The stdlib packages do not declare these inventory types: "
                + string.Join(", ", missing.OrderBy(key => key).Select(key => $"{key.Module}:{key.Name}")));
        }

        return StdlibRuntimeInventoryAccess.Generated
            .OrderBy(key => key, Comparer<(string Module, string Name)>.Default)
            .Select(key => WithLocalReferencesInSyntheticModule(declared[key].Single()))
            .ToList();
    }

    private static DamlDataType WithLocalReferencesInSyntheticModule(DamlDataType dataType) =>
        new()
        {
            Name = dataType.Name,
            TypeParams = dataType.TypeParams,
            Serializable = dataType.Serializable,
            Definition = dataType.Definition switch
            {
                DamlRecordDefinition record => new DamlRecordDefinition(
                    record.Fields.Select(field => field with { Type = Relocate(field.Type) }).ToList()),
                DamlVariantDefinition variant => new DamlVariantDefinition(
                    variant.Constructors.Select(constructor => constructor with
                    {
                        ArgumentType = constructor.ArgumentType is null ? null : Relocate(constructor.ArgumentType),
                    }).ToList()),
                var other => other,
            },
        };

    private static DamlType Relocate(DamlType type) => type.Accept(LocalReferenceRelocator.Instance);

    private sealed class LocalReferenceRelocator : IDamlTypeVisitor<DamlType>
    {
        public static LocalReferenceRelocator Instance { get; } = new();

        public DamlType VisitPrimitive(DamlPrimitiveType type) => type;

        public DamlType VisitTypeVar(DamlTypeVar type) => type;

        public DamlType VisitTypeRef(DamlTypeRef type) =>
            type.PackageId.Length == 0 ? type with { Module = StdlibNamespace } : type;

        public DamlType VisitTypeApp(DamlTypeApp type) =>
            new DamlTypeApp(Relocate(type.Base), type.Arguments.Select(Relocate).ToList());

        public DamlType VisitList(DamlListType type) => new DamlListType(Relocate(type.Element));

        public DamlType VisitOptional(DamlOptionalType type) => new DamlOptionalType(Relocate(type.Value));

        public DamlType VisitTextMap(DamlTextMapType type) => new DamlTextMapType(Relocate(type.Value));

        public DamlType VisitGenMap(DamlGenMapType type) => new DamlGenMapType(Relocate(type.Key), Relocate(type.Value));

        public DamlType VisitContractId(DamlContractIdType type) => new DamlContractIdType(Relocate(type.Payload));
    }
}
