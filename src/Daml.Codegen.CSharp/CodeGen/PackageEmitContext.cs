// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using Microsoft.Extensions.Logging;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Immutable value the C# emitter threads through its emit methods. Built once per package
/// by <see cref="ForPackage(DamlPackage, PackageNameTableCache)"/>, which hands back one context per module: the package-wide
/// scan — the data-type lookup, the reserved and marker names, the choice-argument homes —
/// is shared by all of them, while <see cref="Module"/> and <see cref="Namespace"/>
/// belong to the module being emitted. Read-only during emission.
/// </summary>
internal sealed class PackageEmitContext
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
    /// Lookup of every data type across all modules, keyed by module-qualified
    /// (<c>Module:Name</c>) name. Module-qualified because Daml allows the same simple
    /// name in multiple modules — keying on the simple name alone would let one module's
    /// data type silently shadow another's and emit the wrong field list.
    /// </summary>
    public IReadOnlyDictionary<string, DamlDataType> DataTypes { get; }

    /// <summary>
    /// The package name table, which decides every emitted name and home of the package: each
    /// top-level type's C# name — a declaration, a reference and a file path all spell it from
    /// here, so a type renamed because it is named like a generated member is renamed everywhere
    /// at once — the interface marker names, the module namespaces, and which data types are
    /// emitted nested inside a template as a choice's argument record and inside which template.
    /// Emitters ask it every naming question; the context keeps no name set of its own.
    /// </summary>
    public PackageNameTable NameTable { get; }

    /// <summary>
    /// Maps a record's module-qualified (<c>Module:Name</c>) name to the <c>global::</c>-qualified C# marker name
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
    /// The C# name the record, variant, template or enum <paramref name="damlName"/> of this
    /// package's <paramref name="moduleName"/> is emitted under.
    /// </summary>
    public string EmittedTypeName(string moduleName, string damlName) =>
        NameTable.EmittedName(moduleName, damlName);

    /// <summary>The property names a payload field of <paramref name="template"/> may not take.</summary>
    internal IReadOnlySet<string> TemplateFieldReservedNames(DamlTemplate template) =>
        ReservedMemberNames.OfTemplateField(template, Package.UpgradedPackageId is not null, NameTable);

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
        LocalViewRecord(viewRef, DataTypes, NameTable);

    private static DamlRecordDefinition? LocalViewRecord(
        DamlTypeRef viewRef,
        IReadOnlyDictionary<string, DamlDataType> dataTypes,
        PackageNameTable nameTable)
    {
        ArgumentNullException.ThrowIfNull(viewRef);
        var viewKey = $"{viewRef.Module}:{viewRef.Name}";
        return !nameTable.IsInterface(viewRef.Module, viewRef.Name)
            && dataTypes.TryGetValue(viewKey, out var viewDataType)
            && viewDataType.TypeParams.Count == 0
            && viewDataType.Definition is DamlRecordDefinition viewRecord
                ? viewRecord
                : null;
    }

    private PackageEmitContext(
        PackageScan scan,
        DamlModule module)
    {
        Package = scan.Package;
        Module = module;
        NameTable = scan.NameTable;
        Namespace = NameTable.NamespaceOf(module.Name);
        DataTypes = scan.DataTypes;
        LocalViewRecordMarkerNames = scan.ViewRecordMarkerNames;
    }

    private sealed record PackageScan(
        DamlPackage Package,
        IReadOnlyDictionary<string, DamlDataType> DataTypes,
        PackageNameTable NameTable,
        IReadOnlyDictionary<string, string> ViewRecordMarkerNames);

    /// <summary>
    /// Scans <paramref name="package"/> once and returns one fully-populated immutable
    /// context per module, in declaration order: takes the package name table (the module
    /// namespaces, the emitted names and the choice-argument homes) from
    /// <paramref name="nameTables"/> — which builds it once and logs its warnings, so a
    /// resolver sharing the cache reads the very table the emitter does — and collects the
    /// package-wide data-type lookup and the view-record marker names.
    /// </summary>
    public static IReadOnlyList<PackageEmitContext> ForPackage(DamlPackage package, PackageNameTableCache nameTables)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(nameTables);

        var scan = Scan(package, nameTables.For(package));

        return package.Modules
            .Select(module => new PackageEmitContext(scan, module))
            .ToList();
    }

    /// <summary>
    /// Scans <paramref name="package"/> with a name table cache of its own: the module
    /// namespaces honour <see cref="CodeGenOptions.NamespacePrefix"/> when
    /// <paramref name="isMainPackage"/>, and <paramref name="logger"/> (when supplied) receives
    /// the table's warnings. See <see cref="ForPackage(DamlPackage, PackageNameTableCache)"/>.
    /// </summary>
    public static IReadOnlyList<PackageEmitContext> ForPackage(
        DamlPackage package,
        CodeGenOptions options,
        bool isMainPackage,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(options);

        return ForPackage(package, new PackageNameTableCache(options, isMainPackage ? package.PackageId : null, logger));
    }

    private static PackageScan Scan(DamlPackage package, PackageNameTable nameTable)
    {
        var dataTypes = package.Modules
            .SelectMany(module => module.DataTypes.Select(dataType => (Key: $"{module.Name}:{dataType.Name}", DataType: dataType)))
            .ToDictionary(entry => entry.Key, entry => entry.DataType);

        return new PackageScan(package, dataTypes, nameTable, ViewRecordMarkerNames(package, dataTypes, nameTable));
    }

    /// <summary>
    /// Maps every package-local, non-generic view record with exactly one viewing
    /// interface and a clean field mirror to that interface's <c>global::</c>-qualified marker name — the source of
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
        PackageNameTable nameTable)
    {
        var markersByViewRecord = new Dictionary<string, SortedSet<string>>();
        var declaredMemberNamesByMarker = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        var markerNamesByQualifiedInterface = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var module in package.Modules)
        {
            foreach (var iface in module.Interfaces)
            {
                if (iface.ViewType is not DamlTypeRef viewRef
                    || (!string.IsNullOrEmpty(viewRef.PackageId) && viewRef.PackageId != package.PackageId)
                    || LocalViewRecord(viewRef, dataTypes, nameTable) is null)
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
                declaredMemberNamesByMarker[qualifiedIfaceKey] = DeclaredMemberNames(iface);
                markerNamesByQualifiedInterface[qualifiedIfaceKey] = nameTable.InterfaceMarkerName(module.Name, iface.Name);
            }
        }

        return markersByViewRecord
            .Where(entry => entry.Value.Count == 1)
            .Select(entry => (ViewKey: entry.Key, QualifiedKey: entry.Value.Single()))
            .Select(pair => (pair.ViewKey, pair.QualifiedKey, Marker: markerNamesByQualifiedInterface[pair.QualifiedKey]))
            .Where(pair => ViewFieldsMirrorCleanly(dataTypes[pair.ViewKey], nameTable.EmittedName(pair.ViewKey.Split(':')[0], dataTypes[pair.ViewKey].Name), pair.Marker, declaredMemberNamesByMarker[pair.QualifiedKey]))
            .ToDictionary(
                pair => pair.ViewKey,
                pair => Identifiers.GlobalQualified(nameTable.NamespaceOf(pair.QualifiedKey.Split(':')[0]), pair.Marker));
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
    /// </summary>
    private static IReadOnlySet<string> DeclaredMemberNames(DamlInterface iface)
    {
        var names = new HashSet<string>(StringComparer.Ordinal) { "View", "InterfaceId", "__ReadDamlLfJson" };
        if (iface.Choices.Count > 0)
        {
            names.Add("Choices");
            foreach (var choice in iface.Choices)
            {
                names.Add($"Choice{EmitterHelpers.SanitizeIdentifier(choice.Name)}");
            }
        }

        return names;
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
        string recordClassName,
        string markerName,
        IReadOnlySet<string> markerDeclaredMemberNames)
    {
        if (viewRecord.Definition is not DamlRecordDefinition record)
        {
            return false;
        }

        return record.Fields.All(field =>
        {
            var markerMemberName = Identifiers.MemberName(field.Name, markerName);
            return markerMemberName == Identifiers.MemberName(field.Name, recordClassName, ReservedMemberNames.OfRecordField())
                && !markerDeclaredMemberNames.Contains(markerMemberName);
        });
    }
}
