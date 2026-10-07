// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// DAR-scoped resolution of a <see cref="DamlTypeRef"/> to a C# name against an
/// <see cref="IDarSource"/>. Owns the archive lookup, the foreign data-type index and the set of
/// external package ids it has discovered while resolving — read after emission to emit a
/// <c>&lt;PackageReference&gt;</c> per id. Lives for one
/// <see cref="CSharpCodeGenerator.Generate"/> call. Every namespace it spells comes from
/// <see cref="Identifiers.ModuleNamespace"/> — the same function the emitter names its
/// files and namespaces with — so a reference always lands on a namespace that is emitted.
/// A foreign package is named through the <see cref="PackageNameTable"/> the shared
/// <see cref="PackageNameTableCache"/> holds for it, so the resolver and the emit context never
/// disagree about a name. The foreign data-type index and the discovered external-package-id set
/// are DAR-scoped — they live for the resolver's lifetime, not per package.
/// </summary>
internal sealed partial class DarCrossPackageResolver
{
    private readonly IDarSource _dar;
    private readonly PackageNameTableCache _nameTables;
    private readonly ILogger _logger;
    private readonly HashSet<string> _discoveredExternalPackageIds = [];
    private readonly Dictionary<string, ILookup<(string Module, string Name), DamlDataTypeDefinition>> _foreignDataTypeCache = [];

    /// <summary>Creates a resolver scoped to a single <see cref="IDarSource"/>.</summary>
    /// <param name="dar">The archive type refs are resolved against.</param>
    /// <param name="nameTables">
    /// The name tables of the archive, shared with the emit contexts: a reference into the main
    /// package is spelled under the same namespace prefix the main package is emitted with.
    /// </param>
    /// <param name="logger">Where unmapped-stdlib warnings go; omit it and the resolver stays silent.</param>
    public DarCrossPackageResolver(IDarSource dar, PackageNameTableCache nameTables, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(dar);
        ArgumentNullException.ThrowIfNull(nameTables);
        _dar = dar;
        _nameTables = nameTables;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>The external package ids encountered during resolution so far.</summary>
    public IReadOnlySet<string> DiscoveredExternalPackageIds => _discoveredExternalPackageIds;

    /// <summary>
    /// Returns the package with the given id from the DAR, or <c>null</c> if absent.
    /// Lets the emitter classify a type ref (local / stdlib / cross-package) without
    /// holding the archive itself.
    /// </summary>
    public DamlPackage? LookupPackage(string packageId) => _dar.GetPackageById(packageId);

    /// <summary>
    /// The data-type definitions the package with the given id declares, indexed by the
    /// declaring module's name and the type's own name, so classifying a cross-package ref
    /// costs a lookup instead of a walk of every module. Empty when the package is absent
    /// from the DAR; a name declared under the same key more than once keeps every
    /// definition, so asking whether any of them is a record, a variant or an enum answers
    /// what a walk would have answered.
    /// </summary>
    public ILookup<(string Module, string Name), DamlDataTypeDefinition> DataTypeDefinitions(string packageId)
    {
        if (!_foreignDataTypeCache.TryGetValue(packageId, out var definitions))
        {
            definitions = IndexDataTypeDefinitions(LookupPackage(packageId));
            _foreignDataTypeCache[packageId] = definitions;
        }
        return definitions;
    }

    /// <summary>
    /// Resolves <paramref name="typeRef"/> to a C# identifier or fully qualified name.
    /// Every ref to a type declared in the DAR, other than an unmapped stdlib type,
    /// returns a <c>global::</c>-rooted name. A local ref returns the emitted name under the
    /// namespace of the module that homes the type — the emitting module or another module of
    /// the same package — and a nested choice-argument type is qualified with its parent
    /// template name under the template's module namespace. A cross-package ref returns the
    /// same shape under the referent module's namespace and records the package id so a
    /// <c>&lt;PackageReference&gt;</c> can be emitted for it. A stdlib ref returns the mapped
    /// runtime type, rooted by <see cref="TypeReferenceQualifier.Qualify"/>, or, when the
    /// stdlib type has no mapping, its bare sanitized name with warning 1100 logged.
    /// </summary>
    public string Resolve(DamlTypeRef typeRef, PackageEmitContext context)
    {
        ArgumentNullException.ThrowIfNull(typeRef);
        ArgumentNullException.ThrowIfNull(context);

        if (context.IsLocalRef(typeRef))
        {
            return ResolveLocal(typeRef, context);
        }

        var foreignPkg = _dar.GetPackageById(typeRef.PackageId);
        if (foreignPkg is null)
        {
            throw new InvalidOperationException(
                $"Cross-package type ref {typeRef.Module}:{typeRef.Name} points at package {typeRef.PackageId[..Math.Min(16, typeRef.PackageId.Length)]}… which is not present in the DAR. Rebuild the DAR with the missing package included, or pass a multi-DAR input that resolves it.");
        }

        if (StdlibPackages.IsStdlibPackage(foreignPkg.Name) || StdlibPackages.IsPlaceholderPackageName(foreignPkg.Name))
        {
            var mapped = StdlibPackages.MapStdlibType(typeRef.Module, typeRef.Name);
            if (mapped is not null)
            {
                return TypeReferenceQualifier.Qualify(mapped);
            }
            LogUnmappedStdlibType(_logger, foreignPkg.Name, typeRef.Module, typeRef.Name);
            return Identifiers.Sanitize(typeRef.Name);
        }

        _discoveredExternalPackageIds.Add(typeRef.PackageId);
        return ResolveForeign(typeRef, ForeignNameTable(foreignPkg));
    }

    private static ILookup<(string Module, string Name), DamlDataTypeDefinition> IndexDataTypeDefinitions(DamlPackage? package) =>
        (package?.Modules ?? [])
            .SelectMany(module => module.DataTypes.Select(dataType =>
                (Key: (Module: module.Name, Name: dataType.Name), dataType.Definition)))
            .ToLookup(entry => entry.Key, entry => entry.Definition);

    private static string ResolveForeign(DamlTypeRef typeRef, PackageNameTable nameTable)
    {
        if (nameTable.IsInterface(typeRef.Module, typeRef.Name))
        {
            return Identifiers.GlobalQualified(nameTable.NamespaceOf(typeRef.Module), nameTable.InterfaceMarkerName(typeRef.Module, typeRef.Name));
        }
        if (nameTable.NestedChoiceArgumentHome(typeRef.Module, typeRef.Name) is { } nestingTemplate)
        {
            return $"{Identifiers.GlobalPrefix}{nameTable.NamespaceOf(nestingTemplate.Module)}.{nameTable.EmittedName(nestingTemplate.Module, nestingTemplate.Name)}.{nestingTemplate.NestedClassName}";
        }
        return Identifiers.GlobalQualified(nameTable.NamespaceOf(typeRef.Module), nameTable.EmittedName(typeRef.Module, typeRef.Name));
    }

    /// <summary>
    /// A same-package reference is <c>global::</c>-qualified with the referent module's
    /// namespace — a relative spelling could bind to a same-named member or nested type of
    /// the emitting scope. A choice-argument record is homed on the template that nests it, which
    /// may sit in a different module than the record's own declaration.
    /// </summary>
    private static string ResolveLocal(DamlTypeRef typeRef, PackageEmitContext context)
    {
        var (homeModule, name) =
            context.NameTable.IsInterface(typeRef.Module, typeRef.Name)
                ? (typeRef.Module, context.NameTable.InterfaceMarkerName(typeRef.Module, typeRef.Name))
                : context.NameTable.NestedChoiceArgumentHome(typeRef.Module, typeRef.Name) is { } nestingTemplate
                    ? (nestingTemplate.Module, $"{context.EmittedTypeName(nestingTemplate.Module, nestingTemplate.Name)}.{nestingTemplate.NestedClassName}")
                    : (typeRef.Module, context.EmittedTypeName(typeRef.Module, typeRef.Name));

        var homeNamespace = context.NameTable.NamespaceOf(homeModule);
        return Identifiers.GlobalQualified(homeNamespace, name);
    }

    private PackageNameTable ForeignNameTable(DamlPackage pkg)
    {
        try
        {
            return _nameTables.For(pkg);
        }
        catch (CodegenException clash)
        {
            throw new CodegenException(
                $"Dependency package '{pkg.Name}' cannot be referenced: {clash.Message} The clash is in the dependency's Daml source, so upgrade it or stop depending on it.",
                clash);
        }
    }

    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Warning,
        Message = "Unmapped stdlib type {PackageName}:{ModuleName}:{TypeName} \u2014 generated code will not compile (no stdlib mapping for this type yet)")]
    private static partial void LogUnmappedStdlibType(ILogger logger, string packageName, string moduleName, string typeName);
}
