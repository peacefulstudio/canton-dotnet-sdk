// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using Microsoft.Extensions.Logging;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// The template a choice-argument record is emitted nested inside, identified by the
/// module that declares the template and the template's Daml name. The record's own
/// declaring module may differ, so a reference to the record must be qualified with the
/// template's namespace, not the record's.
/// </summary>
/// <param name="Module">The Daml module declaring the template.</param>
/// <param name="Name">The template's Daml name, before sanitisation.</param>
/// <param name="NestedClassName">The sanitised C# class name the emitter assigns to the nested type (derived from the choice name, not the argument type name).</param>
internal sealed record NestingTemplate(string Module, string Name, string NestedClassName);

/// <summary>
/// Immutable value the C# emitter threads through its emit methods. Built once per package
/// by <see cref="ForPackage"/>, which hands back one context per module: the package-wide
/// scan — the data-type lookup, the reserved and marker names, the choice-argument homes —
/// is shared by all of them, while <see cref="Module"/>, <see cref="Namespace"/> and
/// <see cref="Qualifier"/> belong to the module being emitted. Read-only during emission.
/// </summary>
internal sealed partial class PackageEmitContext
{
    /// <summary>The Daml package this context was built for.</summary>
    public DamlPackage Package { get; }

    /// <summary>The module of <see cref="Package"/> whose types this context emits.</summary>
    public DamlModule Module { get; }

    /// <summary>
    /// The C# namespace <see cref="Module"/>'s types are emitted into — the module name
    /// itself, prefixed by <see cref="CodeGenOptions.NamespacePrefix"/> on the main package
    /// (see <see cref="Identifiers.ModuleNamespace"/>).
    /// </summary>
    public string Namespace { get; }

    /// <summary>
    /// The C# namespace of every module in <see cref="Package"/>, keyed by module name.
    /// Identical across the package's module contexts; consulted when a reference from one
    /// module names a type declared in another, which must then be qualified with the
    /// declaring module's namespace.
    /// </summary>
    public IReadOnlyDictionary<string, string> ModuleNamespaces { get; }

    /// <summary>Qualifier scoped to <see cref="Namespace"/>.</summary>
    public TypeReferenceQualifier Qualifier { get; }

    /// <summary>
    /// Lookup of every data type across all modules, keyed by module-qualified
    /// (<c>Module:Name</c>) name. Module-qualified because Daml allows the same simple
    /// name in multiple modules — keying on the simple name alone would let one module's
    /// data type silently shadow another's and emit the wrong field list.
    /// </summary>
    public IReadOnlyDictionary<string, DamlDataType> DataTypes { get; }

    /// <summary>
    /// Sanitised C# names of every top-level type declared anywhere in the package —
    /// every template plus every record/enum/variant, excluding the records LF declares
    /// alongside a same-named interface (they are replaced by the marker itself, so counting
    /// them would falsely self-disambiguate) and choice-argument records (they are emitted nested inside
    /// their parent template, not at the top level). This is the reserved-name input
    /// <see cref="Identifiers.InterfaceMarkerName"/> disambiguates interface marker names
    /// against: reserving across the whole package rather than per namespace keeps the marker
    /// assignment independent of how modules map to namespaces, so every reference to an
    /// interface — local or foreign — derives the same marker. The shadow set handed to
    /// <see cref="Qualifier"/> is narrower: only the types declared in <see cref="Namespace"/>
    /// or one of its ancestor namespaces can shadow an imported name there.
    /// </summary>
    public IReadOnlySet<string> LocalReservedTypeNames { get; }

    /// <summary>
    /// Sanitised C# names of the top-level types emitted for <see cref="Module"/>: its
    /// templates, its records/enums/variants other than interface placeholders and
    /// choice-argument records, and its interface markers. The input the namespace guards
    /// compare against every emitted namespace.
    /// </summary>
    public IReadOnlySet<string> TopLevelTypeNames { get; }

    /// <summary>
    /// Every interface declared in the package, keyed by its module-qualified
    /// (<c>Module:Name</c>) name, mapped to its final disambiguated C# marker name. See
    /// <see cref="InterfaceMarkerNames"/> for how the assignment is made deterministic.
    /// Callers that need an interface's marker name — the interface emitter, the
    /// generated file-path builder, and the cross-package resolver's local-ref path —
    /// must look it up here rather than recomputing it ad hoc, so every reference to a
    /// given interface agrees on the same marker.
    /// </summary>
    public IReadOnlyDictionary<string, string> LocalInterfaceMarkerNames { get; }

    /// <summary>
    /// Module-qualified (<c>Module:Name</c>) names of enums declared in the package.
    /// Required because Daml allows the same simple name in multiple modules.
    /// </summary>
    public IReadOnlySet<string> LocalEnumQualifiedNames { get; }

    /// <summary>Module-qualified names of variants declared in the package.</summary>
    public IReadOnlySet<string> LocalVariantQualifiedNames { get; }

    /// <summary>
    /// Module-qualified (<c>Module:Name</c>) names of the records LF declares alongside an
    /// interface of the same name in the same module. They are not emitted: the marker
    /// carries the interface's identity, and <c>ContractId&lt;IMarker&gt;</c> serves the
    /// contract-id fields and choice extensions that would otherwise need a record. The set
    /// is therefore the emitter's "this local name is an interface, not a record" oracle —
    /// read by the record emitter to skip the declaration, by the cross-package resolver and
    /// the choice-created-slot walker to resolve a local ref to its marker, and by
    /// <see cref="LocalViewRecord(DamlTypeRef)"/> to reject a view naming an interface.
    /// </summary>
    public IReadOnlySet<string> LocalInterfaceQualifiedNames { get; }

    /// <summary>
    /// Maps a choice-argument type's module-qualified (<c>Module:Name</c>) name to the
    /// template it is emitted nested inside, for qualifying nested choice-argument types
    /// declared in this package. Module-qualified because Daml allows the same simple name in
    /// multiple modules — keying on the simple name alone would let one module's choice-arg
    /// type silently shadow another's and mis-resolve cross-references. The value carries the
    /// template's module because the argument record may be declared in a different module
    /// than the template that nests it.
    /// </summary>
    public IReadOnlyDictionary<string, NestingTemplate> LocalChoiceArgToTemplate { get; }

    /// <summary>
    /// Maps a record's module-qualified (<c>Module:Name</c>) name to the C# marker name
    /// of the single local interface declaring that record as its view type. Only
    /// package-local, non-generic record views with exactly one viewing interface and no
    /// field that mirrors onto the marker under a different name or over a member the
    /// marker already declares are mapped: a dependency package is emitted without
    /// knowledge of its dependents, so a foreign view record cannot be stamped with this
    /// package's markers; a record stamped with two markers would inherit two explicit
    /// implementations of the same identity statics — no most specific implementation, a
    /// compile error; and a field whose two mirror names disagree would leave the marker
    /// declaring a member the record never implements. The record emitter stamps the
    /// marker into the view record's base list, and the interface emitter mirrors the
    /// view's fields onto the marker for the same set, so both degrade together.
    /// </summary>
    public IReadOnlyDictionary<string, string> LocalViewRecordMarkerNames { get; }

    /// <summary>
    /// Returns true when <paramref name="typeRef"/> points at a type declared in this
    /// package — either an empty package id (self-reference) or a matching package id.
    /// </summary>
    public bool IsLocalRef(DamlTypeRef typeRef) =>
        string.IsNullOrEmpty(typeRef.PackageId)
        || typeRef.PackageId == Package.PackageId;

    /// <summary>
    /// Returns the C# namespace of <paramref name="moduleName"/>, which must be a module of
    /// <see cref="Package"/>; a local reference into a module the package does not declare
    /// is a malformed model and fails the emit rather than being spelled as a name nothing
    /// declares.
    /// </summary>
    public string NamespaceOf(string moduleName) =>
        ModuleNamespaces.TryGetValue(moduleName, out var moduleNamespace)
            ? moduleNamespace
            : throw new CodegenException(
                $"Module '{moduleName}' is referenced as local to package '{Package.Name}', which declares no such module. " +
                "The Daml model is malformed: a same-package reference must name a module of that package.");

    /// <summary>
    /// Spells a type name so it binds to the type even where a nearer member or nested type
    /// of the same spelling would otherwise capture it: a bare name — a type declared in
    /// <see cref="Module"/>'s own namespace — comes back <c>global::</c>-rooted under
    /// <see cref="Namespace"/>; a name that already carries a dot was spelled with its own
    /// qualifier by the resolver, another module's or another package's namespace, and is
    /// returned unchanged.
    /// </summary>
    public string QualifyInModule(string typeName) =>
        typeName.Contains('.', StringComparison.Ordinal)
            ? typeName
            : Identifiers.GlobalQualified(Namespace, typeName);

    /// <summary>
    /// Returns true when <paramref name="iface"/>'s view type can stand as the
    /// <c>TView</c> of a <see cref="Daml.Runtime.Contracts.ViewDescriptor{TInterface, TView}"/>,
    /// whose <c>TView : IDamlRecord&lt;TView&gt;</c> constraint admits only a non-generic
    /// record. A local view reference must resolve to one here; a foreign reference is
    /// taken as such, since a dependency package emits its own non-generic records with
    /// that facet. A view naming a variant, an enum, a generic record, an interface, or a
    /// type this package does not declare therefore degrades to the
    /// bare <see cref="Daml.Runtime.Contracts.IHasView{TView}"/> facet rather than an
    /// uncompilable witness.
    /// </summary>
    public bool HasWitnessableViewRecord(DamlInterface iface) =>
        iface.ViewType is DamlTypeRef viewRef
        && (!IsLocalRef(viewRef) || LocalViewRecord(viewRef) is not null);

    /// <summary>
    /// Returns the non-generic record definition <paramref name="viewRef"/> names in this
    /// package, or <c>null</c> when it names an interface, a generic record, a non-record
    /// definition, or a type this package does not declare. Callers that also
    /// care about locality must test <see cref="IsLocalRef"/> first — the lookup key
    /// carries no package id, so a foreign reference can otherwise collide with a
    /// same-named local declaration.
    /// </summary>
    public DamlRecordDefinition? LocalViewRecord(DamlTypeRef viewRef) =>
        LocalViewRecord(viewRef, DataTypes, LocalInterfaceQualifiedNames);

    private static DamlRecordDefinition? LocalViewRecord(
        DamlTypeRef viewRef,
        IReadOnlyDictionary<string, DamlDataType> dataTypes,
        IReadOnlySet<string> localInterfaceQualifiedNames)
    {
        ArgumentNullException.ThrowIfNull(viewRef);
        var viewKey = $"{viewRef.Module}:{viewRef.Name}";
        return !localInterfaceQualifiedNames.Contains(viewKey)
            && dataTypes.TryGetValue(viewKey, out var viewDataType)
            && viewDataType.TypeParams.Count == 0
            && viewDataType.Definition is DamlRecordDefinition viewRecord
                ? viewRecord
                : null;
    }

    private PackageEmitContext(
        PackageScan scan,
        DamlModule module,
        string moduleNamespace,
        TypeReferenceQualifier qualifier,
        IReadOnlySet<string> topLevelTypeNames)
    {
        Package = scan.Package;
        Module = module;
        Namespace = moduleNamespace;
        ModuleNamespaces = scan.ModuleNamespaces;
        Qualifier = qualifier;
        TopLevelTypeNames = topLevelTypeNames;
        DataTypes = scan.DataTypes;
        LocalReservedTypeNames = scan.ReservedTypeNames;
        LocalInterfaceMarkerNames = scan.InterfaceMarkerNames;
        LocalEnumQualifiedNames = scan.EnumQualifiedNames;
        LocalVariantQualifiedNames = scan.VariantQualifiedNames;
        LocalInterfaceQualifiedNames = scan.InterfaceQualifiedNames;
        LocalChoiceArgToTemplate = scan.ChoiceArgToTemplate;
        LocalViewRecordMarkerNames = scan.ViewRecordMarkerNames;
    }

    private sealed record PackageScan(
        DamlPackage Package,
        IReadOnlyDictionary<string, string> ModuleNamespaces,
        IReadOnlyDictionary<string, DamlDataType> DataTypes,
        IReadOnlyDictionary<string, IReadOnlySet<string>> ReservedTypeNamesByModule,
        IReadOnlySet<string> ReservedTypeNames,
        IReadOnlyDictionary<string, string> InterfaceMarkerNames,
        IReadOnlySet<string> EnumQualifiedNames,
        IReadOnlySet<string> VariantQualifiedNames,
        IReadOnlySet<string> InterfaceQualifiedNames,
        IReadOnlyDictionary<string, NestingTemplate> ChoiceArgToTemplate,
        IReadOnlyDictionary<string, string> ViewRecordMarkerNames);

    /// <summary>
    /// Scans <paramref name="package"/> once and returns one fully-populated immutable
    /// context per module, in declaration order: maps every module to its namespace
    /// (honouring <see cref="CodeGenOptions.NamespacePrefix"/> when
    /// <paramref name="isMainPackage"/>), builds the package-wide data-type lookup and the
    /// local enum / variant / interface / choice-argument name sets, and scopes each
    /// context's <see cref="Qualifier"/> to its module's namespace. When two templates in
    /// the package map the same module-qualified choice-argument type,
    /// <paramref name="logger"/> (when supplied) receives a warning and the first-seen mapping
    /// is kept.
    /// </summary>
    public static IReadOnlyList<PackageEmitContext> ForPackage(
        DamlPackage package,
        CodeGenOptions options,
        bool isMainPackage,
        ILogger? logger = null,
        DamlPackage? mainPackageSibling = null,
        IReadOnlyList<DamlPackage>? depSiblings = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(options);

        var scan = Scan(package, NamespacesByModule(package, options, isMainPackage), logger);
        var crossPackageShadowTypes = (mainPackageSibling is not null || depSiblings is { Count: > 0 })
            ? CrossPackageShadowTypes(mainPackageSibling, depSiblings, options)
            : [];

        return package.Modules
            .Select(module => new PackageEmitContext(
                scan,
                module,
                scan.ModuleNamespaces[module.Name],
                new TypeReferenceQualifier(scan.ModuleNamespaces[module.Name], ShadowingTypeNames(scan, module, crossPackageShadowTypes)),
                TopLevelTypeNamesOf(scan, module)))
            .ToList();
    }

    /// <summary>
    /// Collects every top-level type name and interface marker emitted by the sibling packages,
    /// each paired with the C# namespace of its declaring module. The main package sibling (when
    /// present) is resolved with <c>isMainPackage: true</c> so its namespace carries
    /// <see cref="CodeGenOptions.NamespacePrefix"/>; dependency siblings use
    /// <c>isMainPackage: false</c>. Used to seed the cross-package shadow set so that any
    /// top-level type in a sibling package whose namespace is an ancestor of the emitting
    /// module's namespace can be detected as shadowing and root-qualified.
    /// </summary>
    private static IReadOnlyList<(string Namespace, string TypeName)> CrossPackageShadowTypes(
        DamlPackage? mainPackage, IReadOnlyList<DamlPackage>? depPackages, CodeGenOptions options)
    {
        var result = new List<(string, string)>();
        if (mainPackage is not null)
        {
            AppendShadowTypesForPackage(result, mainPackage, options, isMainPackage: true);
        }
        foreach (var pkg in depPackages ?? [])
        {
            AppendShadowTypesForPackage(result, pkg, options, isMainPackage: false);
        }
        return result;
    }

    private static void AppendShadowTypesForPackage(
        List<(string Namespace, string TypeName)> result,
        DamlPackage pkg,
        CodeGenOptions options,
        bool isMainPackage)
    {
        var namespaces = NamespacesByModule(pkg, options, isMainPackage);
        var reservedNamesByModule = ReservedTopLevelTypeNamesByModule(pkg);
        var reservedNames = Union(reservedNamesByModule.Values);
        var markerNames = InterfaceMarkerNames(pkg, reservedNames);

        foreach (var (moduleName, moduleReservedNames) in reservedNamesByModule)
        {
            if (namespaces.TryGetValue(moduleName, out var ns))
            {
                foreach (var typeName in moduleReservedNames)
                    result.Add((ns, typeName));
            }
        }

        foreach (var (key, markerName) in markerNames)
        {
            var colonIdx = key.IndexOf(':', StringComparison.Ordinal);
            if (colonIdx >= 0)
            {
                var moduleName = key[..colonIdx];
                if (namespaces.TryGetValue(moduleName, out var ns))
                    result.Add((ns, markerName));
            }
        }
    }

    private static IReadOnlyDictionary<string, string> NamespacesByModule(
        DamlPackage package, CodeGenOptions options, bool isMainPackage)
    {
        var namespaces = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var module in package.Modules)
        {
            if (!namespaces.TryAdd(module.Name, Identifiers.ModuleNamespace(module.Name, isMainPackage, options)))
            {
                throw new CodegenException(
                    $"Package '{package.Name}' declares module '{module.Name}' more than once. " +
                    "The Daml model is malformed: module names are unique within a package.");
            }
        }
        return namespaces;
    }

    /// <summary>
    /// The top-level type names that shadow an imported runtime/BCL name inside
    /// <paramref name="module"/>'s namespace: C# binds a simple name by walking the
    /// enclosing namespaces outward before consulting <c>using</c> directives, so a type
    /// declared in the module's own namespace or in any ancestor namespace binds first,
    /// while a type in a sibling namespace does not. Also includes view-field member names
    /// mirrored into interface markers in ancestor namespaces (which shadow types inside
    /// the interface body), and type names from <paramref name="crossPackageShadowTypes"/>
    /// (both top-level types and interface markers from sibling packages) whose namespace is
    /// an ancestor of the emitting module's namespace.
    /// </summary>
    private static IReadOnlySet<string> ShadowingTypeNames(
        PackageScan scan,
        DamlModule module,
        IReadOnlyList<(string Namespace, string TypeName)> crossPackageShadowTypes)
    {
        var emittingNamespace = scan.ModuleNamespaces[module.Name];
        var shadowing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (moduleName, moduleNamespace) in scan.ModuleNamespaces)
        {
            if (Identifiers.StartsWithSegments(emittingNamespace, moduleNamespace))
            {
                shadowing.UnionWith(scan.ReservedTypeNamesByModule[moduleName]);
                var modulePrefix = $"{moduleName}:";
                foreach (var (key, markerName) in scan.InterfaceMarkerNames)
                {
                    if (key.StartsWith(modulePrefix, StringComparison.Ordinal))
                    {
                        shadowing.Add(markerName);
                        // A stamped view mirrors its fields as properties onto the marker
                        // interface. Inside the marker body, a property named e.g. `Party`
                        // shadows the runtime Party type in expression position, so add each
                        // such member name to the shadow set.
                        foreach (var (viewKey, vMarkerName) in scan.ViewRecordMarkerNames)
                        {
                            if (vMarkerName == markerName
                                && scan.DataTypes.TryGetValue(viewKey, out var viewDataType)
                                && viewDataType.Definition is DamlRecordDefinition viewRecord)
                            {
                                foreach (var field in viewRecord.Fields)
                                    shadowing.Add(Identifiers.MemberName(field.Name, markerName));
                            }
                        }
                    }
                }
            }
        }
        foreach (var (typeNamespace, typeName) in crossPackageShadowTypes)
        {
            if (Identifiers.StartsWithSegments(emittingNamespace, typeNamespace))
                shadowing.Add(typeName);
        }
        return shadowing;
    }

    private static IReadOnlySet<string> TopLevelTypeNamesOf(PackageScan scan, DamlModule module)
    {
        var names = new HashSet<string>(scan.ReservedTypeNamesByModule[module.Name], StringComparer.Ordinal);
        foreach (var iface in module.Interfaces)
        {
            names.Add(scan.InterfaceMarkerNames[$"{module.Name}:{iface.Name}"]);
        }
        return names;
    }

    private static PackageScan Scan(
        DamlPackage package,
        IReadOnlyDictionary<string, string> moduleNamespaces,
        ILogger? logger)
    {
        var reservedTypeNamesByModule = ReservedTopLevelTypeNamesByModule(package);
        var reservedTypeNames = Union(reservedTypeNamesByModule.Values);

        var dataTypes = new Dictionary<string, DamlDataType>();
        var enumQualifiedNames = new HashSet<string>();
        var variantQualifiedNames = new HashSet<string>();
        var interfaceQualifiedNames = new HashSet<string>();
        foreach (var module in package.Modules)
        {
            var interfaceNames = module.Interfaces.Select(i => i.Name).ToHashSet();

            foreach (var dataType in module.DataTypes)
            {
                dataTypes[$"{module.Name}:{dataType.Name}"] = dataType;
                if (dataType.Definition is DamlEnumDefinition)
                {
                    enumQualifiedNames.Add($"{module.Name}:{dataType.Name}");
                }
                if (dataType.Definition is DamlVariantDefinition)
                {
                    variantQualifiedNames.Add($"{module.Name}:{dataType.Name}");
                }
                if (interfaceNames.Contains(dataType.Name))
                {
                    interfaceQualifiedNames.Add($"{module.Name}:{dataType.Name}");
                }
            }
        }

        var choiceArgToTemplate = new Dictionary<string, NestingTemplate>();
        foreach (var module in package.Modules)
        {
            foreach (var template in module.Templates)
            {
                foreach (var choice in template.Choices)
                {
                    if (choice.ArgumentType is DamlTypeRef typeRef)
                    {
                        var key = $"{typeRef.Module}:{typeRef.Name}";
                        if (dataTypes.ContainsKey(key))
                        {
                            if (choiceArgToTemplate.TryGetValue(key, out var existingTemplate)
                                && existingTemplate.Name != template.Name)
                            {
                                if (logger is not null)
                                {
                                    LogAmbiguousLocalChoiceArgument(logger, key, existingTemplate.Name, template.Name);
                                }
                                continue;
                            }
                            choiceArgToTemplate[key] = new NestingTemplate(module.Name, template.Name, EmitterHelpers.SanitizeIdentifier(choice.Name));
                        }
                    }
                }
            }
        }

        var interfaceMarkerNames = InterfaceMarkerNames(package, reservedTypeNames);
        var viewRecordMarkerNames = ViewRecordMarkerNames(
            package, dataTypes, interfaceQualifiedNames, interfaceMarkerNames, choiceArgToTemplate);

        return new PackageScan(
            package,
            moduleNamespaces,
            dataTypes,
            reservedTypeNamesByModule,
            reservedTypeNames,
            interfaceMarkerNames,
            enumQualifiedNames,
            variantQualifiedNames,
            interfaceQualifiedNames,
            choiceArgToTemplate,
            viewRecordMarkerNames);
    }

    /// <summary>
    /// Maps every package-local, non-generic view record with exactly one viewing
    /// interface and a clean field mirror to that interface's marker name — the source of
    /// <see cref="LocalViewRecordMarkerNames"/>. Foreign view references, references to
    /// types this package does not declare, generic records, non-record definitions, and
    /// the records LF declares alongside a same-named interface are all excluded (only a
    /// record this package emits itself can be stamped with a marker); a record viewed by more than one interface is
    /// excluded because the stamp would inherit two explicit implementations of the same
    /// identity statics — no most specific implementation, a compile error; and a record
    /// failing <see cref="ViewFieldsMirrorCleanly"/> is excluded because the marker would
    /// declare a member the stamped record does not implement.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ViewRecordMarkerNames(
        DamlPackage package,
        IReadOnlyDictionary<string, DamlDataType> dataTypes,
        IReadOnlySet<string> localInterfaceQualifiedNames,
        IReadOnlyDictionary<string, string> localInterfaceMarkerNames,
        IReadOnlyDictionary<string, NestingTemplate> choiceArgToTemplate)
    {
        var markersByViewRecord = new Dictionary<string, SortedSet<string>>();
        var declaredMemberNamesByMarker = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var module in package.Modules)
        {
            foreach (var iface in module.Interfaces)
            {
                if (iface.ViewType is not DamlTypeRef viewRef
                    || (!string.IsNullOrEmpty(viewRef.PackageId) && viewRef.PackageId != package.PackageId)
                    || LocalViewRecord(viewRef, dataTypes, localInterfaceQualifiedNames) is null)
                {
                    continue;
                }

                var viewKey = $"{viewRef.Module}:{viewRef.Name}";
                if (!markersByViewRecord.TryGetValue(viewKey, out var markers))
                {
                    markers = new SortedSet<string>(StringComparer.Ordinal);
                    markersByViewRecord[viewKey] = markers;
                }
                var qualifiedIfaceKey = $"{module.Name}:{iface.Name}";
                markers.Add(qualifiedIfaceKey);
                declaredMemberNamesByMarker[qualifiedIfaceKey] = DeclaredMemberNames(iface, module.Name, module.DataTypes, choiceArgToTemplate);
            }
        }

        return markersByViewRecord
            .Where(entry => entry.Value.Count == 1)
            .Select(entry => (ViewKey: entry.Key, QualifiedKey: entry.Value.Single()))
            .Select(pair => (pair.ViewKey, pair.QualifiedKey, Marker: localInterfaceMarkerNames[pair.QualifiedKey]))
            .Where(pair => ViewFieldsMirrorCleanly(dataTypes[pair.ViewKey], pair.Marker, declaredMemberNamesByMarker[pair.QualifiedKey]))
            .ToDictionary(pair => pair.ViewKey, pair => pair.Marker);
    }

    /// <summary>
    /// Members a generated interface marker declares in its own right, which a mirrored
    /// view field must not shadow: the <c>View</c> witness, the <c>InterfaceId</c> identity
    /// re-declaration (both CS0102), the reserved <c>__ReadDamlLfJson</c> decoder name every
    /// record and variant emits (see <see cref="RecordSerializationEmitter.WriteReadDamlLfJsonMethod"/>),
    /// and — when <paramref name="iface"/> has choices — the
    /// <c>Choices</c> aggregate and each per-choice <c>Choice{X}</c> descriptor property.
    /// <c>Choices</c> is emitted as an explicit <c>IHasChoices&lt;TSelf&gt;</c>
    /// implementation, so a same-named mirrored field would not itself fail to compile, but
    /// it would still trigger CS0108 (member hides an inherited interface member) — a
    /// warning a consumer building with <c>TreatWarningsAsErrors</c> would fail on — so it is
    /// reserved defensively alongside the <c>Choice{X}</c> properties, which are plain
    /// <c>public static</c> members and would collide outright (CS0102).
    ///
    /// Additionally, the sanitised C# name of every decoder receiver reachable from each
    /// choice's argument and return type is reserved. <c>DamlTypeMapper.FromValue</c> emits
    /// a bare (unqualified) type name as the static receiver for any same-module
    /// <see cref="DamlTypeRef"/> — records and variants as <c>TypeName.FromRecord/FromVariant</c>,
    /// enums as <c>TypeNameExtensions.FromDamlEnum</c> — and recurses into the type arguments of
    /// any <see cref="DamlTypeApp"/>, so the same bare receivers can appear inside generic
    /// wrappers such as <c>List&lt;Result&gt;</c>. Cross-module types use a <c>global::</c>-qualified
    /// receiver and cannot be shadowed. Inside the interface marker body, a mirrored view-field
    /// property of the same C# name would shadow the type in expression position
    /// (CS0176 / CS0120). Reserving every reachable receiver name causes
    /// <see cref="ViewFieldsMirrorCleanly"/> to decline stamping when any such collision exists.
    /// </summary>
    private static IReadOnlySet<string> DeclaredMemberNames(
        DamlInterface iface,
        string moduleName,
        IReadOnlyList<DamlDataType> moduleDataTypes,
        IReadOnlyDictionary<string, NestingTemplate> choiceArgToTemplate)
    {
        var enumNames = new HashSet<string>(
            moduleDataTypes
                .Where(dt => dt.Definition is DamlEnumDefinition)
                .Select(dt => dt.Name),
            StringComparer.Ordinal);

        var names = new HashSet<string>(StringComparer.Ordinal) { "View", "InterfaceId", "__ReadDamlLfJson" };
        if (iface.Choices.Count > 0)
        {
            names.Add("Choices");
            foreach (var choice in iface.Choices)
            {
                names.Add($"Choice{EmitterHelpers.SanitizeIdentifier(choice.Name)}");
                CollectDecoderReceiverNames(choice.ArgumentType, moduleName, enumNames, choiceArgToTemplate, names);
                CollectDecoderReceiverNames(choice.ReturnType, moduleName, enumNames, choiceArgToTemplate, names);
            }

            // Also reserve every same-module nesting-template name: when a locally-declared
            // record is nested as a choice argument inside a template, DarCrossPackageResolver
            // emits its decoder receiver as "TemplateName.RecordName.FromRecord(...)". A mirrored
            // view-field property with the same name as TemplateName shadows the bare type receiver
            // in expression position (CS0426 / CS0117). CollectDecoderReceiverNames above covers
            // these names via the type-tree traversal; this loop adds them as a safety net.
            foreach (var (_, nestingTemplate) in choiceArgToTemplate)
            {
                if (nestingTemplate.Module == moduleName)
                {
                    names.Add(Identifiers.Sanitize(nestingTemplate.Name));
                }
            }
        }

        return names;
    }

    private static void CollectDecoderReceiverNames(
        DamlType type,
        string moduleName,
        IReadOnlySet<string> moduleEnumNames,
        IReadOnlyDictionary<string, NestingTemplate> choiceArgToTemplate,
        HashSet<string> names)
    {
        switch (type)
        {
            case DamlTypeRef r when r.Module == moduleName:
                var key = $"{r.Module}:{r.Name}";
                if (choiceArgToTemplate.TryGetValue(key, out var nestingTemplate)
                    && nestingTemplate.Module == moduleName)
                {
                    names.Add(Identifiers.Sanitize(nestingTemplate.Name));
                }
                names.Add(moduleEnumNames.Contains(r.Name)
                    ? Identifiers.Sanitize(r.Name + "Extensions")
                    : Identifiers.Sanitize(r.Name));
                break;
            case DamlTypeApp { Base: var b, Arguments: var args }:
                CollectDecoderReceiverNames(b, moduleName, moduleEnumNames, choiceArgToTemplate, names);
                foreach (var arg in args)
                    CollectDecoderReceiverNames(arg, moduleName, moduleEnumNames, choiceArgToTemplate, names);
                break;
            case DamlOptionalType { Value: var optionalChild }:
                CollectDecoderReceiverNames(optionalChild, moduleName, moduleEnumNames, choiceArgToTemplate, names);
                break;
            case DamlListType { Element: var listChild }:
                CollectDecoderReceiverNames(listChild, moduleName, moduleEnumNames, choiceArgToTemplate, names);
                break;
            case DamlTextMapType { Value: var textMapChild }:
                CollectDecoderReceiverNames(textMapChild, moduleName, moduleEnumNames, choiceArgToTemplate, names);
                break;
            case DamlContractIdType { Payload: var contractIdChild }:
                CollectDecoderReceiverNames(contractIdChild, moduleName, moduleEnumNames, choiceArgToTemplate, names);
                break;
            case DamlGenMapType { Key: var genMapKey, Value: var genMapValue }:
                CollectDecoderReceiverNames(genMapKey, moduleName, moduleEnumNames, choiceArgToTemplate, names);
                CollectDecoderReceiverNames(genMapValue, moduleName, moduleEnumNames, choiceArgToTemplate, names);
                break;
            case DamlWrappedOptional { Argument: var arg }:
                CollectDecoderReceiverNames(arg, moduleName, moduleEnumNames, choiceArgToTemplate, names);
                break;
        }
    }

    /// <summary>
    /// Returns true when every field of <paramref name="viewRecord"/> mirrors onto the
    /// marker under the same C# member name the record itself emits for it, and under no
    /// name the marker already declares (<paramref name="markerDeclaredMemberNames"/>). The
    /// two sides derive their member names independently, each disambiguating only against
    /// its own enclosing type (CS0542), so a field PascalCasing to the record's name is
    /// emitted as <c>Name_</c> there and <c>Name</c> on the marker — and vice versa for a
    /// field PascalCasing to the marker's name — leaving the record short of a marker member
    /// (CS0535). A field PascalCasing to a name the marker already declares — see
    /// <see cref="DeclaredMemberNames"/> — would instead redeclare that member. Either way
    /// the pair is ineligible and the record stays un-stamped beside an un-enriched marker.
    /// </summary>
    private static bool ViewFieldsMirrorCleanly(
        DamlDataType viewRecord,
        string markerName,
        IReadOnlySet<string> markerDeclaredMemberNames)
    {
        if (viewRecord.Definition is not DamlRecordDefinition record)
        {
            return false;
        }

        var recordClassName = Identifiers.Sanitize(viewRecord.Name);
        return record.Fields.All(field =>
        {
            var markerMemberName = Identifiers.MemberName(field.Name, markerName);
            return markerMemberName == Identifiers.MemberName(field.Name, recordClassName)
                && !markerDeclaredMemberNames.Contains(markerMemberName);
        });
    }

    /// <summary>
    /// Computes the sanitised C# name of every top-level type declared anywhere in
    /// <paramref name="package"/> — every template plus every record/enum/variant,
    /// excluding the records LF declares alongside a same-named interface (replaced by the
    /// marker itself) and choice-argument records (emitted nested inside their parent
    /// template, not at the top level) — the single source of the reserved-name set
    /// <see cref="Identifiers.InterfaceMarkerName"/> disambiguates against, shared by
    /// <see cref="ForPackage"/> (for the emitting package) and the cross-package
    /// resolver (for foreign packages) so both sides derive the same marker name.
    /// </summary>
    internal static IReadOnlySet<string> ReservedTopLevelTypeNames(DamlPackage package) =>
        Union(ReservedTopLevelTypeNamesByModule(package).Values);

    /// <summary>
    /// The per-module breakdown of <see cref="ReservedTopLevelTypeNames"/>, keyed by module
    /// name: the source of each module's <see cref="TopLevelTypeNames"/> and of the shadow
    /// set its <see cref="Qualifier"/> is scoped to.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlySet<string>> ReservedTopLevelTypeNamesByModule(DamlPackage package)
    {
        var localInterfaceQualifiedNames = new HashSet<string>();
        var dataTypeNames = new HashSet<string>();
        foreach (var module in package.Modules)
        {
            var interfaceNames = module.Interfaces.Select(i => i.Name).ToHashSet();
            foreach (var dataType in module.DataTypes)
            {
                dataTypeNames.Add(dataType.Name);
                if (interfaceNames.Contains(dataType.Name))
                {
                    localInterfaceQualifiedNames.Add($"{module.Name}:{dataType.Name}");
                }
            }
        }

        var choiceArgumentQualifiedNames = new HashSet<string>();
        foreach (var module in package.Modules)
        {
            foreach (var template in module.Templates)
            {
                foreach (var choice in template.Choices)
                {
                    if (choice.ArgumentType is DamlTypeRef typeRef && dataTypeNames.Contains(typeRef.Name))
                    {
                        choiceArgumentQualifiedNames.Add($"{typeRef.Module}:{typeRef.Name}");
                    }
                }
            }
        }

        var reservedByModule = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var module in package.Modules)
        {
            var reserved = new HashSet<string>(StringComparer.Ordinal);
            foreach (var template in module.Templates)
            {
                reserved.Add(Identifiers.Sanitize(template.Name));
            }
            foreach (var dataType in module.DataTypes)
            {
                var qualifiedName = $"{module.Name}:{dataType.Name}";
                if (localInterfaceQualifiedNames.Contains(qualifiedName)
                    || choiceArgumentQualifiedNames.Contains(qualifiedName))
                {
                    continue;
                }
                reserved.Add(Identifiers.Sanitize(dataType.Name));
            }
            reservedByModule[module.Name] = reserved;
        }
        return reservedByModule;
    }

    private static IReadOnlySet<string> Union(IEnumerable<IReadOnlySet<string>> sets)
    {
        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var set in sets)
        {
            union.UnionWith(set);
        }
        return union;
    }

    /// <summary>
    /// Precomputes the final disambiguated C# marker name for every interface in
    /// <paramref name="package"/>, keyed by module-qualified (<c>Module:Name</c>) name,
    /// seeded from <paramref name="reservedTypeNames"/> (see
    /// <see cref="ReservedTopLevelTypeNames"/>). Interfaces are processed in a stable
    /// ordinal sort over their qualified name — not module declaration order — so that
    /// when two interfaces in different modules sanitise to the same marker, the same one
    /// deterministically wins the unsuffixed name on every run; each assigned marker is
    /// threaded into the reserved set before the next interface is disambiguated, so the
    /// loser picks up the trailing <c>_</c>. Shared by <see cref="ForPackage"/> (for the
    /// emitting package) and the cross-package resolver (for foreign packages) so both
    /// sides derive the same marker assignment.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> InterfaceMarkerNames(
        DamlPackage package, IReadOnlySet<string> reservedTypeNames)
    {
        var reserved = new HashSet<string>(reservedTypeNames);
        var markers = new Dictionary<string, string>();

        var interfaces = package.Modules
            .SelectMany(module => module.Interfaces.Select(iface => (Module: module.Name, Interface: iface)))
            .OrderBy(x => $"{x.Module}:{x.Interface.Name}", StringComparer.Ordinal);

        foreach (var (moduleName, iface) in interfaces)
        {
            var marker = Identifiers.InterfaceMarkerName(iface.Name, reserved);
            reserved.Add(marker);
            markers[$"{moduleName}:{iface.Name}"] = marker;
        }

        return markers;
    }

    [LoggerMessage(
        EventId = 1200,
        Level = LogLevel.Warning,
        Message = "Choice-argument type {ChoiceArgumentKey} is used by both templates {KeptTemplate} and {IgnoredTemplate} in the same package; keeping {KeptTemplate} and ignoring {IgnoredTemplate}. Rename one choice-argument type to disambiguate.")]
    private static partial void LogAmbiguousLocalChoiceArgument(ILogger logger, string choiceArgumentKey, string keptTemplate, string ignoredTemplate);
}
