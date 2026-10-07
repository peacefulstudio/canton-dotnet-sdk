// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// A Daml type emitted under a C# name other than its sanitised Daml name because that name is a
/// member the generated type carries.
/// </summary>
/// <param name="Module">The declaring Daml module.</param>
/// <param name="DamlName">The Daml type name, which stays the wire name.</param>
/// <param name="Kind">The declaration kind: <c>record</c>, <c>variant</c> or <c>template</c>.</param>
/// <param name="ClashingMember">The generated member the sanitised Daml name equals.</param>
/// <param name="EmittedName">The C# name actually emitted: the sanitised name plus one trailing <c>_</c>.</param>
internal sealed record TypeRename(string Module, string DamlName, string Kind, string ClashingMember, string EmittedName);

internal sealed partial class PackageNameTable
{
    private const string RenameSuffix = "_";

    private readonly IReadOnlyDictionary<string, TypeRename> _renamesByQualifiedName;
    private readonly IReadOnlyList<FieldRename> _fieldRenames;
    private readonly IReadOnlyDictionary<string, string> _interfaceMarkersByQualifiedName;
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _topLevelTypeNamesByModule;

    private sealed record FieldRename(string Kind, string Module, string DamlName, string FieldName, string ClashingMember, string EmittedName);

    private sealed record TopLevelDeclaration(string DamlName, string Kind, IReadOnlySet<string> ReservedMembers);

    /// <summary>Every type emitted under a suffixed name, in module declaration order.</summary>
    internal IReadOnlyList<TypeRename> Renames { get; }

    /// <summary>
    /// The sanitised C# names of every top-level type declared anywhere in the package — every
    /// template plus every record, enum and variant, excluding the records LF declares alongside a
    /// same-named interface (the marker replaces them, so counting them would falsely
    /// self-disambiguate) and the choice-argument records (emitted nested inside their template).
    /// Reserving across the whole package rather than per namespace keeps the interface marker
    /// assignment independent of how modules map to namespaces, so every reference to an
    /// interface derives the same marker.
    /// </summary>
    internal IReadOnlySet<string> ReservedTopLevelTypeNames { get; }

    /// <summary>
    /// The C# name the record, variant, template or enum <paramref name="damlName"/> of
    /// <paramref name="moduleName"/> is emitted under: its sanitised Daml name, unless a record,
    /// variant or template carries a member of that very name (CS0542), in which case it gains
    /// exactly one trailing <c>_</c>. The Daml name is the wire name and is never touched; enums,
    /// choices and interface markers are not renamed here.
    /// </summary>
    internal string EmittedName(string moduleName, string damlName) =>
        _renamesByQualifiedName.TryGetValue(QualifiedName(moduleName, damlName), out var rename)
            ? rename.EmittedName
            : Identifiers.Sanitize(damlName);

    /// <summary>
    /// The C# marker name of the interface <paramref name="interfaceName"/> declared in
    /// <paramref name="moduleName"/>: <c>I</c> plus its sanitised name, suffixed with <c>_</c> until
    /// it clashes with no top-level type of the package and no other interface's marker.
    /// Interfaces are assigned in a stable ordinal order over their module-qualified name — not
    /// module declaration order — so when two interfaces of different modules sanitise to the same
    /// marker, the same one wins the unsuffixed name on every run.
    /// </summary>
    internal string InterfaceMarkerName(string moduleName, string interfaceName) =>
        _interfaceMarkersByQualifiedName[QualifiedName(moduleName, interfaceName)];

    /// <summary>Whether <paramref name="name"/> of <paramref name="moduleName"/> is an interface, which is emitted as a marker rather than under its own name.</summary>
    internal bool IsInterface(string moduleName, string name) =>
        _interfaceMarkersByQualifiedName.ContainsKey(QualifiedName(moduleName, name));

    /// <summary>
    /// The sanitised C# names of the top-level types emitted for <paramref name="moduleName"/>: its
    /// templates, its records, enums and variants other than interface placeholders and
    /// choice-argument records, and its interface markers.
    /// </summary>
    internal IReadOnlySet<string> TopLevelTypeNames(string moduleName) =>
        _topLevelTypeNamesByModule[moduleName];

    private IReadOnlyList<TypeRename> TypeRenames(TypeRootFilter rootFilter)
    {
        var renames = new List<TypeRename>();
        foreach (var module in _package.Modules)
        {
            var declarations = TopLevelDeclarations(module, rootFilter);
            var declaredNames = declarations.ToLookup(declaration => Identifiers.Sanitize(declaration.DamlName));
            foreach (var declaration in declarations)
            {
                var sanitised = Identifiers.Sanitize(declaration.DamlName);
                if (!declaration.ReservedMembers.Contains(sanitised))
                {
                    continue;
                }

                var emitted = sanitised + RenameSuffix;
                if (declaredNames[emitted].FirstOrDefault() is { } occupant)
                {
                    throw new CodegenException(
                        $"Daml {declaration.Kind} '{module.Name}:{declaration.DamlName}' in package '{_package.Name}' is named like the member '{sanitised}' " +
                        $"the generated {declaration.Kind} type carries, so it would be emitted as '{emitted}' - but Daml type '{module.Name}:{occupant.DamlName}' " +
                        $"already emits that C# name. Rename one of '{declaration.DamlName}' and '{occupant.DamlName}' in Daml.");
                }
                renames.Add(new TypeRename(module.Name, declaration.DamlName, declaration.Kind, sanitised, emitted));
            }
        }
        return renames;
    }

    private IReadOnlyList<TopLevelDeclaration> TopLevelDeclarations(DamlModule module, TypeRootFilter rootFilter)
    {
        var hasUpgradedPackageId = _package.UpgradedPackageId is not null;
        var interfaceNames = module.Interfaces.Select(iface => iface.Name).ToHashSet(StringComparer.Ordinal);
        var templateNames = module.Templates.Select(template => template.Name).ToHashSet(StringComparer.Ordinal);
        var declarations = new List<TopLevelDeclaration>();

        foreach (var template in module.Templates.Where(template => rootFilter.Includes(module.Name, template.Name)))
        {
            var payloadFieldCount = module.DataTypes
                .Select(dataType => dataType.Name == template.Name ? dataType.Definition as DamlRecordDefinition : null)
                .FirstOrDefault(record => record is not null)?.Fields.Count ?? 0;
            declarations.Add(new TopLevelDeclaration(template.Name, "template", ReservedMemberNames.OfTemplateType(template, payloadFieldCount, hasUpgradedPackageId)));
        }

        foreach (var dataType in module.DataTypes)
        {
            if (templateNames.Contains(dataType.Name)
                || interfaceNames.Contains(dataType.Name)
                || NestedChoiceArgumentHome(module.Name, dataType.Name) is not null)
            {
                continue;
            }

            var declaration = dataType.Definition switch
            {
                DamlRecordDefinition record => new TopLevelDeclaration(dataType.Name, "record", ReservedMemberNames.OfRecordType(record.Fields.Count)),
                DamlVariantDefinition => new TopLevelDeclaration(dataType.Name, "variant", ReservedMemberNames.OfVariantType()),
                _ => new TopLevelDeclaration(dataType.Name, "enum", new HashSet<string>()),
            };
            declarations.Add(declaration);
        }
        return declarations;
    }

    private IReadOnlyList<FieldRename> FieldRenames()
    {
        var renames = new List<FieldRename>();
        var recordFieldReservedNames = ReservedMemberNames.OfRecordField();
        foreach (var module in _package.Modules)
        {
            var templateNames = module.Templates.Select(template => template.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var dataType in module.DataTypes.Where(dataType => dataType.Definition is DamlRecordDefinition))
            {
                var isTemplatePayload = templateNames.Contains(dataType.Name);
                var reserved = isTemplatePayload
                    ? ReservedMemberNames.OfTemplateField(module.Templates.First(template => template.Name == dataType.Name), _package.UpgradedPackageId is not null, this)
                    : recordFieldReservedNames;
                var entityName = EmittedName(module.Name, dataType.Name);
                foreach (var field in ((DamlRecordDefinition)dataType.Definition).Fields)
                {
                    var property = Identifiers.MemberName(field.Name, entityName);
                    if (reserved.Contains(property))
                    {
                        renames.Add(new FieldRename(isTemplatePayload ? "template" : "record", module.Name, dataType.Name, field.Name, property, property + RenameSuffix));
                    }
                }
            }
        }
        return renames;
    }

    private IReadOnlyDictionary<string, IReadOnlySet<string>> ReservedTopLevelTypeNamesByModule()
    {
        var reservedByModule = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var module in _package.Modules)
        {
            var interfaceNames = module.Interfaces.Select(iface => iface.Name).ToHashSet(StringComparer.Ordinal);
            var reserved = new HashSet<string>(StringComparer.Ordinal);
            foreach (var template in module.Templates)
            {
                reserved.Add(EmittedName(module.Name, template.Name));
            }
            foreach (var dataType in module.DataTypes)
            {
                if (interfaceNames.Contains(dataType.Name)
                    || NestedChoiceArgumentHome(module.Name, dataType.Name) is not null)
                {
                    continue;
                }
                reserved.Add(EmittedName(module.Name, dataType.Name));
            }
            reservedByModule[module.Name] = reserved;
        }
        return reservedByModule;
    }

    private IReadOnlyDictionary<string, string> InterfaceMarkers(IReadOnlySet<string> reservedTypeNames)
    {
        var reserved = new HashSet<string>(reservedTypeNames, StringComparer.Ordinal);
        var markers = new Dictionary<string, string>(StringComparer.Ordinal);

        var interfaces = _package.Modules
            .SelectMany(module => module.Interfaces.Select(iface => (Module: module.Name, Interface: iface)))
            .OrderBy(entry => QualifiedName(entry.Module, entry.Interface.Name), StringComparer.Ordinal);

        foreach (var (moduleName, iface) in interfaces)
        {
            var marker = Identifiers.InterfaceMarkerName(iface.Name, reserved);
            reserved.Add(marker);
            markers[QualifiedName(moduleName, iface.Name)] = marker;
        }

        return markers;
    }

    private IReadOnlyDictionary<string, IReadOnlySet<string>> TopLevelTypeNamesByModule(
        IReadOnlyDictionary<string, IReadOnlySet<string>> reservedByModule)
    {
        var namesByModule = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var module in _package.Modules)
        {
            var names = new HashSet<string>(reservedByModule[module.Name], StringComparer.Ordinal);
            foreach (var iface in module.Interfaces)
            {
                names.Add(InterfaceMarkerName(module.Name, iface.Name));
            }
            if (Registration is { } registration && registration.Namespace == ModuleNamespaces[module.Name])
            {
                names.Add(registration.ClassName);
            }
            namesByModule[module.Name] = names;
        }
        return namesByModule;
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
}
