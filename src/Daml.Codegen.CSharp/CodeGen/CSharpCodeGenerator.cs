// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RuntimeNamespaces = Daml.Runtime.RuntimeNamespaces;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Generates C# code from Daml packages.
/// </summary>
/// <param name="options">Emission options.</param>
/// <param name="logger">
/// Where progress and warnings go. Omit it — or pass <c>null</c> — and the generator stays silent.
/// </param>
public sealed partial class CSharpCodeGenerator(CodeGenOptions options, ILogger<CSharpCodeGenerator>? logger = null)
{
    private readonly ILogger _log = logger ?? NullLogger<CSharpCodeGenerator>.Instance;

    private readonly TypeRootFilter _rootFilter = new(options.RootFilter);

    private readonly PartyAnalysis _party = new();

    /// <summary>
    /// Generates C# code for all types in the DAR. Every module of every emitted package is
    /// mapped to its namespace first and the map is checked as a whole — two modules sharing
    /// a namespace, or a namespace spelled like an emitted type — before any file is produced.
    /// The readers deliver applied builtins as typed nodes, and the emitter consumes them
    /// natively through <see cref="IDamlTypeVisitor{TResult}"/> arms; a hand-built legacy
    /// application of one of the five folding builtins folds into its typed node at the
    /// visitor, so both representations map identically.
    /// </summary>
    public IReadOnlyList<GeneratedFile> Generate(IDarSource dar)
    {
        ArgumentNullException.ThrowIfNull(dar);

        var files = new List<GeneratedFile>();

        var nameTables = new PackageNameTableCache(options, dar.MainPackage.PackageId, _log);
        var resolver = new DarCrossPackageResolver(dar, nameTables, _log);

        var mainModules = PackageEmitContext.ForPackage(dar.MainPackage, nameTables);
        var emittedDependencies = options.IncludeDependencies
            ? dar.Dependencies.Where(dep => !IsProvidedByRuntime(dep)).ToList()
            : [];
        var dependencyModules = emittedDependencies
            .Select(dep => PackageEmitContext.ForPackage(dep, nameTables))
            .ToList();

        ModuleNamespaceGuards.Check(
            mainModules.Concat(dependencyModules.SelectMany(modules => modules)).Select(EmittedModuleOf).ToList());

        var emittedModules = new List<EmittedModule>();
        files.AddRange(GeneratePackage(resolver, mainModules, emittedModules));

        foreach (var (dep, modules) in emittedDependencies.Zip(dependencyModules))
        {
            LogGeneratingDependency(_log, dep.Name);
            files.AddRange(GeneratePackage(resolver, modules, emittedModules));
        }

        ModuleNamespaceGuards.Check(emittedModules);

        if (options.GenerateProjectFile)
        {
            var externalRefs = new List<DamlPackage>();
            foreach (var id in resolver.DiscoveredExternalPackageIds)
            {
                var pkg = dar.GetPackageById(id);
                if (pkg is null)
                {
                    LogExternalPackageMissing(_log, id[..Math.Min(16, id.Length)]);
                    continue;
                }
                if (IsProvidedByRuntime(pkg))
                {
                    continue;
                }
                externalRefs.Add(pkg);
            }
            var projectGenerator = new ProjectFileGenerator(options);
            files.Add(projectGenerator.GenerateProjectFile(dar.MainPackage, externalRefs));
            files.Add(projectGenerator.GenerateReadme(dar.MainPackage));
            files.Add(projectGenerator.GenerateIcon());
        }

        return files;
    }

    private static bool IsProvidedByRuntime(DamlPackage package) =>
        IsStdlibPackage(package.Name) || IsPlaceholderPackageName(package.Name);

    private static bool IsStdlibPackage(string packageName) => StdlibPackages.IsStdlibPackage(packageName);

    private static bool IsPlaceholderPackageName(string packageName) => StdlibPackages.IsPlaceholderPackageName(packageName);

    /// <summary>
    /// Whether <paramref name="typeName"/> in <paramref name="moduleName"/> passes
    /// <see cref="CodeGenOptions.RootFilter"/> — the single place that answers this question, so
    /// every file kind (template, its nested choice-argument types, interface) is filtered by the
    /// same rule instead of by copies that could drift apart. An absent filter admits everything.
    /// </summary>
    private bool IsIncludedByRootFilter(string moduleName, string typeName) =>
        _rootFilter.Includes(moduleName, typeName);

    private static EmittedModule EmittedModuleOf(PackageEmitContext context) =>
        EmittedModuleOf(context, context.NameTable.TopLevelTypeNames(context.Module.Name));

    private static EmittedModule EmittedModuleOf(PackageEmitContext context, IReadOnlySet<string> emittedTypeNames) =>
        new(context.Package.Name, context.Module.Name, context.Namespace, context.NameTable.TopLevelTypeNames(context.Module.Name).Union(emittedTypeNames).ToHashSet(StringComparer.Ordinal));

    /// <summary>
    /// Generates C# code for a single package, one module at a time: every file of a module
    /// is written into that module's namespace and directory.
    /// </summary>
    private IEnumerable<GeneratedFile> GeneratePackage(DarCrossPackageResolver resolver, IReadOnlyList<PackageEmitContext> moduleContexts, List<EmittedModule> emittedModules)
    {
        foreach (var context in moduleContexts)
        {
            var moduleFiles = GenerateModule(resolver, context).ToList();
            EmittedTypeNameClashes.Require(
                context.Package.Name,
                context.Module.Name,
                context.Namespace,
                moduleFiles);
            emittedModules.Add(EmittedModuleOf(context, EmittedTypeNameClashes.TopLevelTypeNamesOf(moduleFiles)));
            foreach (var emitted in moduleFiles)
            {
                yield return emitted.File;
            }
        }

        foreach (var registrationFile in GenerateRegistration(resolver, moduleContexts))
        {
            yield return registrationFile;
        }
    }

    private IEnumerable<EmittedDeclarationFile> GenerateModule(DarCrossPackageResolver resolver, PackageEmitContext context)
    {
        var package = context.Package;
        var module = context.Module;
        var moduleNamespace = context.Namespace;
        var mapper = new DamlTypeMapper(context, resolver);
        var choiceEmitter = new ChoiceEmitter(context, resolver, options, mapper, _party);
        var enumEmitter = new EnumEmitter(options);
        var variantEmitter = new VariantEmitter(context, resolver, options, mapper);
        var recordSerialization = new RecordSerializationEmitter(context, resolver, options, mapper);
        var recordEmitter = new RecordEmitter(context, options, recordSerialization);
        var interfaceEmitter = new InterfaceEmitter(context, mapper, resolver, choiceEmitter, options);
        var submissionExtensions = new SubmissionExtensionsEmitter(options, _party);
        var templateEmitter = new TemplateEmitter(context, resolver, recordSerialization, choiceEmitter, submissionExtensions, options, logger);

        var templateNames = module.Templates.Select(t => t.Name).ToHashSet();
        var dataTypesByName = module.DataTypes
            .Where(dt => dt.Definition is DamlRecordDefinition)
            .ToDictionary(dt => dt.Name, dt => (DamlRecordDefinition)dt.Definition!);

        foreach (var template in module.Templates)
        {
            if (!IsIncludedByRootFilter(module.Name, template.Name))
            {
                LogSkippingTemplate(_log, module.Name, template.Name);
                continue;
            }

            if (!dataTypesByName.TryGetValue(template.Name, out var recordDef))
            {
                var sameNamed = module.DataTypes.FirstOrDefault(dt => dt.Name == template.Name);
                var cause = sameNamed is null
                    ? "no data type of that name exists in the module"
                    : $"the same-named data type is a {sameNamed.Definition.GetType().Name}";

                throw new CodegenException(
                    $"Template '{module.Name}:{template.Name}' has no same-named record definition in its module: {cause}. " +
                    "An LF template payload is always a same-named record carrying the template's fields, so " +
                    "this means the model is malformed and no payload type can be emitted.");
            }

            var code = GenerateTemplate(context, templateEmitter, package, module, template, recordDef.Fields);
            var path = RelativeFilePath(moduleNamespace, $"{context.EmittedTypeName(module.Name, template.Name)}.cs");

            yield return new EmittedDeclarationFile("template", template.Name, GeneratedFile.Text(path, code));
        }

        foreach (var dataType in module.DataTypes)
        {
            if (templateNames.Contains(dataType.Name))
            {
                continue;
            }

            if (context.NameTable.IsInterface(module.Name, dataType.Name))
            {
                continue;
            }

            if (context.NameTable.NestedChoiceArgumentHome(module.Name, dataType.Name) is not null)
            {
                continue;
            }

            var code = GenerateDataType(context, recordEmitter, enumEmitter, variantEmitter, module, dataType);
            var path = RelativeFilePath(moduleNamespace, $"{context.EmittedTypeName(module.Name, dataType.Name)}.cs");

            yield return new EmittedDeclarationFile(DamlKindOf(dataType), dataType.Name, GeneratedFile.Text(path, code));
        }

        foreach (var template in module.Templates)
        {
            if (!IsIncludedByRootFilter(module.Name, template.Name))
            {
                continue;
            }

            foreach (var choice in template.Choices)
            {
                if (context.NameTable.NestedChoiceArgumentRecord(choice) is { } argumentRecord)
                {
                    var code = GenerateNestedChoiceArgumentType(context, templateEmitter,
                        template, choice, argumentRecord);
                    var path = RelativeFilePath(
                        moduleNamespace,
                        $"{context.EmittedTypeName(module.Name, template.Name)}.{EmitterHelpers.SanitizeIdentifier(choice.Name)}.cs");

                    yield return new EmittedDeclarationFile("template", template.Name, GeneratedFile.Text(path, code));
                }
            }
        }

        foreach (var iface in module.Interfaces)
        {
            if (!IsIncludedByRootFilter(module.Name, iface.Name))
            {
                LogSkippingInterface(_log, module.Name, iface.Name);
                continue;
            }

            var code = GenerateInterface(context, interfaceEmitter, package, module, iface);
            var path = RelativeFilePath(moduleNamespace, $"{context.NameTable.InterfaceMarkerName(module.Name, iface.Name)}.cs");

            yield return new EmittedDeclarationFile("interface", iface.Name, GeneratedFile.Text(path, code));
        }
    }

    private static string DamlKindOf(DamlDataType dataType) => dataType.Definition switch
    {
        DamlRecordDefinition => "record",
        DamlVariantDefinition => "variant",
        _ => "enum",
    };

    /// <summary>
    /// Generates C# code for a template.
    /// </summary>
    private string GenerateTemplate(
        PackageEmitContext context,
        TemplateEmitter templateEmitter,
        DamlPackage package,
        DamlModule module,
        DamlTemplate template,
        IReadOnlyList<DamlFieldDefinition> fields) =>
        EmitFile(context.Namespace, indent =>
        {
            RequireCommonNamespaces(indent);
            templateEmitter.WriteTemplateType(indent, package, module, template, fields);
        });

    /// <summary>
    /// Generates C# code for a data type.
    /// </summary>
    private string GenerateDataType(
        PackageEmitContext context,
        RecordEmitter recordEmitter,
        EnumEmitter enumEmitter,
        VariantEmitter variantEmitter,
        DamlModule module,
        DamlDataType dataType) =>
        EmitFile(context.Namespace, indent =>
        {
            RequireCommonNamespaces(indent);

            switch (dataType.Definition)
            {
                case DamlRecordDefinition record:
                    recordEmitter.WriteRecordType(indent, module, dataType, record);
                    break;
                case DamlVariantDefinition variant:
                    variantEmitter.WriteVariantType(indent, dataType, variant);
                    break;
                case DamlEnumDefinition enumDef:
                    enumEmitter.WriteEnumType(indent, context, dataType, enumDef);
                    break;
            }
        });

    /// <summary>
    /// Generates C# code for a Daml interface.
    /// </summary>
    private string GenerateInterface(
        PackageEmitContext context,
        InterfaceEmitter interfaceEmitter,
        DamlPackage package,
        DamlModule module,
        DamlInterface iface) =>
        EmitFile(context.Namespace, indent =>
        {
            RequireCommonNamespaces(indent);
            interfaceEmitter.WriteInterfaceType(indent, package, module, iface);
        });

    private static string RelativeFilePath(string dottedNamespace, string fileName) =>
        $"{dottedNamespace.Replace('.', '/')}/{fileName}";

    /// <summary>
    /// Generates a partial file with the choice argument type nested inside the template.
    /// </summary>
    private string GenerateNestedChoiceArgumentType(
        PackageEmitContext context,
        TemplateEmitter templateEmitter,
        DamlTemplate template,
        DamlChoice choice,
        DamlRecordDefinition argumentRecord) =>
        EmitFile(context.Namespace, indent =>
        {
            RequireCommonNamespaces(indent);
            templateEmitter.WriteNestedChoiceArgumentType(indent, template, choice, argumentRecord);
        });

    [LoggerMessage(EventId = 1000, Level = LogLevel.Debug, Message = "Generating code for dependency: {PackageName}")]
    private static partial void LogGeneratingDependency(ILogger logger, string packageName);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "External package id {PackageIdPrefix}\u2026 is not present in the DAR \u2014 no <PackageReference> will be emitted for it. Generated code that references it will fail to compile.")]
    private static partial void LogExternalPackageMissing(ILogger logger, string packageIdPrefix);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Debug, Message = "Skipping template {ModuleName}:{TemplateName} (filtered)")]
    private static partial void LogSkippingTemplate(ILogger logger, string moduleName, string templateName);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Debug, Message = "Skipping interface {ModuleName}:{InterfaceName} (filtered)")]
    private static partial void LogSkippingInterface(ILogger logger, string moduleName, string interfaceName);
}
