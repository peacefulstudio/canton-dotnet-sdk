// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>A template the package registers with the runtime's generated type registry.</summary>
/// <param name="Module">The Daml module declaring the template.</param>
/// <param name="Template">The template.</param>
internal sealed record RegisteredTemplate(DamlModule Module, DamlTemplate Template);

/// <summary>An interface with choices the package registers with the runtime's generated type registry.</summary>
/// <param name="Module">The Daml module declaring the interface.</param>
/// <param name="Interface">The interface.</param>
internal sealed record RegisteredInterface(DamlModule Module, DamlInterface Interface);

/// <summary>
/// The registration class a package emits: the class that registers the package's templates and
/// interfaces with the runtime's generated type registry from a module initializer.
/// </summary>
/// <param name="Namespace">The C# namespace the class is emitted into: that of the first module, by ordinal name, that declares a registered type.</param>
/// <param name="ClassName">The class name, unique per package id and clear of every top-level type of the package.</param>
/// <param name="Templates">The registered templates, ordered by module and then template name, both ordinal.</param>
/// <param name="Interfaces">The registered interfaces, ordered by module and then interface name, both ordinal.</param>
internal sealed record PackageRegistration(
    string Namespace,
    string ClassName,
    IReadOnlyList<RegisteredTemplate> Templates,
    IReadOnlyList<RegisteredInterface> Interfaces);

internal sealed partial class PackageNameTable
{
    private const string RegistrationClassNamePrefix = "PackageRegistration_";

    /// <summary>
    /// The registration class of the package, or <c>null</c> when it declares nothing to register:
    /// every template the root filter admits and every admitted interface that has choices, because
    /// an interface marker only carries a choice table when it has a choice. Its name is a top-level
    /// emitted name, so it is spelled from the package id — injectively, so two package ids, two
    /// versions of one package included, never share it — and gains trailing <c>_</c>s until it
    /// clashes with no top-level type or interface marker of the package.
    /// </summary>
    internal PackageRegistration? Registration { get; }

    private PackageRegistration? RegistrationOf(TypeRootFilter rootFilter)
    {
        var templates = _package.Modules
            .OrderBy(module => module.Name, StringComparer.Ordinal)
            .SelectMany(module => module.Templates
                .Where(template => rootFilter.Includes(module.Name, template.Name))
                .OrderBy(template => template.Name, StringComparer.Ordinal)
                .Select(template => new RegisteredTemplate(module, template)))
            .ToList();
        var interfaces = _package.Modules
            .OrderBy(module => module.Name, StringComparer.Ordinal)
            .SelectMany(module => module.Interfaces
                .Where(iface => iface.Choices.Count > 0 && rootFilter.Includes(module.Name, iface.Name))
                .OrderBy(iface => iface.Name, StringComparer.Ordinal)
                .Select(iface => new RegisteredInterface(module, iface)))
            .ToList();

        var homeModule = templates.Select(template => template.Module)
            .Concat(interfaces.Select(iface => iface.Module))
            .MinBy(module => module.Name, StringComparer.Ordinal);
        if (homeModule is null)
        {
            return null;
        }

        var reserved = new HashSet<string>(ReservedTopLevelTypeNames, StringComparer.Ordinal);
        reserved.UnionWith(_interfaceMarkersByQualifiedName.Values);
        var className = Identifiers.SanitizeBare(RegistrationClassNamePrefix + _package.PackageId);
        while (reserved.Contains(className))
        {
            className += "_";
        }

        return new PackageRegistration(ModuleNamespaces[homeModule.Name], className, templates, interfaces);
    }
}
