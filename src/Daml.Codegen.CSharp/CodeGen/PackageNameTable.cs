// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

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
/// The package name table: the one place that decides what a Daml package's entities are called
/// in C# and where they live — each top-level type's emitted name after the <c>_</c> suffix that resolves a reserved-name clash,
/// the interface marker names, the reserved top-level type names, the module namespaces, and which
/// data types are emitted nested inside a template as a choice's argument record. Built once per
/// package by <see cref="For"/> and immutable afterwards; building it runs every clash check, so a
/// package that cannot be emitted never yields a table. A nested choice argument is a record, and
/// only a record: a choice whose argument is a local enum or variant is a malformed model and
/// fails the build of the table, rather than letting the type vanish from the emitted bindings.
/// </summary>
internal sealed partial class PackageNameTable
{
    private readonly DamlPackage _package;
    private readonly IReadOnlyDictionary<string, NestingTemplate> _homesByQualifiedName;
    private readonly IReadOnlyDictionary<string, DamlRecordDefinition> _recordsByQualifiedName;
    private readonly IReadOnlyList<AmbiguousChoiceArgument> _ambiguities;

    private PackageNameTable(DamlPackage package, CodeGenOptions options, bool isMainPackage, TypeRootFilter rootFilter)
    {
        _package = package;
        ModuleNamespaces = NamespacesByModule(package, options, isMainPackage);
        (_homesByQualifiedName, _recordsByQualifiedName, _ambiguities) = ChoiceArgumentHomes(package);
        Renames = TypeRenames(rootFilter);
        _renamesByQualifiedName = Renames.ToDictionary(rename => QualifiedName(rename.Module, rename.DamlName), StringComparer.Ordinal);
        RequireNestedTypesClearOfTheirEnclosers(rootFilter);
        _fieldRenames = FieldRenames();
        var reservedByModule = ReservedTopLevelTypeNamesByModule();
        ReservedTopLevelTypeNames = Union(reservedByModule.Values);
        _interfaceMarkersByQualifiedName = InterfaceMarkers(ReservedTopLevelTypeNames);
        Registration = RegistrationOf(rootFilter);
        _topLevelTypeNamesByModule = TopLevelTypeNamesByModule(reservedByModule);
    }

    private sealed record AmbiguousChoiceArgument(string QualifiedName, string KeptTemplate, string IgnoredTemplate);

    /// <summary>
    /// The C# namespace of every module of the package, keyed by module name, each from
    /// <see cref="Identifiers.ModuleNamespace"/>: the module name itself, prefixed by
    /// <see cref="CodeGenOptions.NamespacePrefix"/> on the main package only.
    /// </summary>
    internal IReadOnlyDictionary<string, string> ModuleNamespaces { get; }

    /// <summary>
    /// Builds the table of <paramref name="package"/>. Templates the
    /// <paramref name="rootFilter"/> excludes take no part in the rename and nested-type clash
    /// decisions. When two templates take the same module-qualified choice-argument type, the
    /// first template in declaration order keeps it and the clash is reported by
    /// <see cref="LogWarnings"/>.
    /// </summary>
    /// <param name="package">The Daml package to name.</param>
    /// <param name="options">Read for <see cref="CodeGenOptions.NamespacePrefix"/>.</param>
    /// <param name="isMainPackage">Whether <paramref name="package"/> is the main package, the only one the namespace prefix applies to.</param>
    /// <param name="rootFilter">Which templates take part in naming decisions.</param>
    /// <exception cref="CodegenException">
    /// The package declares a module twice; a choice takes a local enum or variant as its argument;
    /// a renamed type's suffixed name is already taken; or a nested type would redeclare a member
    /// of its enclosing type.
    /// </exception>
    internal static PackageNameTable For(DamlPackage package, CodeGenOptions options, bool isMainPackage, TypeRootFilter rootFilter)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(rootFilter);

        return new PackageNameTable(package, options, isMainPackage, rootFilter);
    }

    /// <summary>
    /// The C# namespace of <paramref name="moduleName"/>, which must be a module of the package; a
    /// reference into a module the package does not declare is a malformed model and fails here
    /// rather than being spelled as a namespace nothing declares.
    /// </summary>
    internal string NamespaceOf(string moduleName) =>
        ModuleNamespaces.TryGetValue(moduleName, out var moduleNamespace)
            ? moduleNamespace
            : throw new CodegenException(
                $"Module '{moduleName}' is not declared by package '{_package.Name}'. " +
                "The Daml model is malformed: a reference must name a module of the package it points into.");

    /// <summary>
    /// The template that nests the choice-argument record <paramref name="typeName"/> declared in
    /// <paramref name="moduleName"/>, or <c>null</c> when no choice of the package takes that type
    /// as its argument.
    /// </summary>
    internal NestingTemplate? NestedChoiceArgumentHome(string moduleName, string typeName) =>
        _homesByQualifiedName.GetValueOrDefault(QualifiedName(moduleName, typeName));

    /// <summary>
    /// The record <paramref name="choice"/> takes as its argument, which is emitted nested inside
    /// the choice's template, or <c>null</c> when the argument is not a data type of the package.
    /// </summary>
    internal DamlRecordDefinition? NestedChoiceArgumentRecord(DamlChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);

        return choice.ArgumentType is DamlTypeRef typeRef
            ? _recordsByQualifiedName.GetValueOrDefault(QualifiedName(typeRef.Module, typeRef.Name))
            : null;
    }

    private static string QualifiedName(string moduleName, string typeName) => $"{moduleName}:{typeName}";

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

    private static (
        IReadOnlyDictionary<string, NestingTemplate> Homes,
        IReadOnlyDictionary<string, DamlRecordDefinition> Records,
        IReadOnlyList<AmbiguousChoiceArgument> Ambiguities) ChoiceArgumentHomes(DamlPackage package)
    {
        var dataTypes = package.Modules
            .SelectMany(module => module.DataTypes.Select(dataType => (Key: QualifiedName(module.Name, dataType.Name), DataType: dataType)))
            .ToDictionary(entry => entry.Key, entry => entry.DataType, StringComparer.Ordinal);

        var homes = new Dictionary<string, NestingTemplate>(StringComparer.Ordinal);
        var records = new Dictionary<string, DamlRecordDefinition>(StringComparer.Ordinal);
        var ambiguities = new List<AmbiguousChoiceArgument>();
        foreach (var module in package.Modules)
        {
            foreach (var template in module.Templates)
            {
                foreach (var choice in template.Choices)
                {
                    if (choice.ArgumentType is not DamlTypeRef typeRef)
                    {
                        continue;
                    }

                    var key = QualifiedName(typeRef.Module, typeRef.Name);
                    if (!dataTypes.TryGetValue(key, out var dataType))
                    {
                        continue;
                    }

                    if (dataType.Definition is not DamlRecordDefinition record)
                    {
                        throw NotARecord(package, module, template, choice, key, dataType);
                    }

                    if (homes.TryGetValue(key, out var existing) && existing.Name != template.Name)
                    {
                        ambiguities.Add(new AmbiguousChoiceArgument(key, existing.Name, template.Name));
                        continue;
                    }

                    records[key] = record;
                    homes[key] = new NestingTemplate(module.Name, template.Name, EmitterHelpers.SanitizeIdentifier(choice.Name));
                }
            }
        }

        return (homes, records, ambiguities);
    }

    private static CodegenException NotARecord(
        DamlPackage package,
        DamlModule module,
        DamlTemplate template,
        DamlChoice choice,
        string argumentQualifiedName,
        DamlDataType argument)
    {
        var kind = argument.Definition is DamlEnumDefinition ? "enum" : "variant";
        return new CodegenException(
            $"Choice '{choice.Name}' of Daml template '{module.Name}:{template.Name}' in package '{package.Name}' takes the {kind} '{argumentQualifiedName}' as its argument, " +
            "but a choice argument must be a record. damlc always synthesises a record for a choice, so the Daml model is malformed.");
    }
}
