// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Closes a <see cref="CodeGenOptions.RootFilter"/> selection so the emitted files compile on their
/// own. Records, variants and enums are emitted whatever the filter says, and templates and
/// interfaces only when it matches them; this walks every emitted type and admits each template or
/// interface the emitted code names: an interface an emitted template implements, the interface an
/// emitted record is the view of, and any template or interface a field, key, choice argument or
/// choice result refers to. Admitting one walks it in turn, until nothing new is reached.
/// </summary>
internal static class RootFilterClosure
{
    /// <summary>A template or interface the filter did not match but the emitted code needs.</summary>
    internal sealed record AdmittedRoot(string Module, string Name, string Reason)
    {
        internal string QualifiedName => $"{Module}:{Name}";
    }

    /// <summary>
    /// The templates and interfaces of <paramref name="emittedPackages"/> that
    /// <paramref name="filter"/> rejects but the rest of the emitted code refers to, in the order
    /// they are reached. Empty when the filter admits everything.
    /// </summary>
    internal static IReadOnlyList<AdmittedRoot> Close(TypeRootFilter filter, IReadOnlyList<DamlPackage> emittedPackages)
    {
        if (filter.Pattern is null)
        {
            return [];
        }

        var walk = new Walk(filter, emittedPackages);
        walk.Run();
        return walk.Admitted;
    }

    private sealed class Walk(TypeRootFilter filter, IReadOnlyList<DamlPackage> emittedPackages)
    {
        private readonly Dictionary<(string PackageId, string Module, string Name), Entity> _entities = Index(emittedPackages);
        private readonly HashSet<(string PackageId, string Module, string Name)> _included = [];
        private readonly Queue<Entity> _pending = new();

        internal List<AdmittedRoot> Admitted { get; } = [];

        internal void Run()
        {
            foreach (var entity in _entities.Values)
            {
                if (entity.IsAlwaysEmitted || filter.Includes(entity.Module.Name, entity.Name))
                {
                    Include(entity, reason: null);
                }
            }

            while (_pending.TryDequeue(out var entity))
            {
                foreach (var (reference, reason) in entity.References())
                {
                    var key = (reference.PackageId.Length == 0 ? entity.Package.PackageId : reference.PackageId, reference.Module, reference.Name);
                    if (_entities.TryGetValue(key, out var target) && !target.IsAlwaysEmitted)
                    {
                        Include(target, $"{reason} {entity.Module.Name}:{entity.Name}");
                    }
                }
            }
        }

        private void Include(Entity entity, string? reason)
        {
            if (!_included.Add(entity.Key))
            {
                return;
            }
            if (reason is not null)
            {
                Admitted.Add(new AdmittedRoot(entity.Module.Name, entity.Name, reason));
            }
            _pending.Enqueue(entity);
        }

        private static Dictionary<(string, string, string), Entity> Index(IReadOnlyList<DamlPackage> packages)
        {
            var entities = new Dictionary<(string, string, string), Entity>();
            foreach (var package in packages)
            {
                var packageInterfaces = package.Modules
                    .SelectMany(module => module.Interfaces.Select(iface => (module.Name, iface)))
                    .ToList();
                foreach (var module in package.Modules)
                {
                    var templates = module.Templates.ToDictionary(template => template.Name, StringComparer.Ordinal);
                    var interfaces = module.Interfaces.ToDictionary(iface => iface.Name, StringComparer.Ordinal);
                    var records = module.DataTypes
                        .Where(dataType => dataType.Definition is DamlRecordDefinition)
                        .ToDictionary(dataType => dataType.Name, dataType => (DamlRecordDefinition)dataType.Definition, StringComparer.Ordinal);
                    var choiceArguments = module.Templates
                        .SelectMany(template => template.Choices)
                        .Select(choice => choice.ArgumentType)
                        .OfType<DamlTypeRef>()
                        .Where(reference => (reference.PackageId.Length == 0 || reference.PackageId == package.PackageId) && reference.Module == module.Name)
                        .Select(reference => reference.Name)
                        .ToHashSet(StringComparer.Ordinal);

                    foreach (var template in module.Templates)
                    {
                        var payload = records.GetValueOrDefault(template.Name);
                        entities.TryAdd((package.PackageId, module.Name, template.Name), new TemplateEntity(package, module, template, payload, records));
                    }
                    foreach (var iface in module.Interfaces)
                    {
                        entities.TryAdd((package.PackageId, module.Name, iface.Name), new InterfaceEntity(package, module, iface));
                    }
                    foreach (var dataType in module.DataTypes.Where(dataType =>
                        !templates.ContainsKey(dataType.Name) && !interfaces.ContainsKey(dataType.Name) && !choiceArguments.Contains(dataType.Name)))
                    {
                        entities.TryAdd((package.PackageId, module.Name, dataType.Name), new DataTypeEntity(package, module, dataType, packageInterfaces));
                    }
                }
            }
            return entities;
        }
    }

    private abstract class Entity(DamlPackage package, DamlModule module, string name)
    {
        internal DamlPackage Package { get; } = package;

        internal DamlModule Module { get; } = module;

        internal string Name { get; } = name;

        internal (string, string, string) Key => (Package.PackageId, Module.Name, Name);

        internal abstract bool IsAlwaysEmitted { get; }

        internal abstract IEnumerable<(DamlTypeRef Reference, string Reason)> References();

        protected static IEnumerable<(DamlTypeRef, string)> Referenced(DamlType? type, string reason) =>
            type is null ? [] : TypeReferences.Of(type).Select(reference => (reference, reason));

        protected static IEnumerable<(DamlTypeRef, string)> ReferencedByChoices(IEnumerable<DamlChoice> choices) =>
            choices.SelectMany(choice =>
                Referenced(choice.ArgumentType, "it is a choice argument type of")
                    .Concat(Referenced(choice.ReturnType, "it is a choice result type of")));
    }

    private sealed class TemplateEntity(
        DamlPackage package,
        DamlModule module,
        DamlTemplate template,
        DamlRecordDefinition? payload,
        IReadOnlyDictionary<string, DamlRecordDefinition> moduleRecords) : Entity(package, module, template.Name)
    {
        internal override bool IsAlwaysEmitted => false;

        internal override IEnumerable<(DamlTypeRef, string)> References() =>
            template.Implements.Select(iface => (iface, "it is implemented by template"))
                .Concat((payload?.Fields ?? []).SelectMany(field => Referenced(field.Type, "it is a field type of template")))
                .Concat(Referenced(template.Key, "it is the key type of template"))
                .Concat(ReferencedByChoices(template.Choices))
                .Concat(template.Choices
                    .Select(choice => choice.ArgumentType)
                    .OfType<DamlTypeRef>()
                    .Where(reference => reference.Module == Module.Name && (reference.PackageId.Length == 0 || reference.PackageId == Package.PackageId))
                    .Select(reference => moduleRecords.GetValueOrDefault(reference.Name))
                    .OfType<DamlRecordDefinition>()
                    .SelectMany(record => record.Fields)
                    .SelectMany(field => Referenced(field.Type, "it is a choice argument field type of template")));
    }

    private sealed class InterfaceEntity(DamlPackage package, DamlModule module, DamlInterface iface) : Entity(package, module, iface.Name)
    {
        internal override bool IsAlwaysEmitted => false;

        internal override IEnumerable<(DamlTypeRef, string)> References() =>
            Referenced(iface.ViewType, "it is the view type of interface")
                .Concat(ReferencedByChoices(iface.Choices));
    }

    private sealed class DataTypeEntity(DamlPackage package, DamlModule module, DamlDataType dataType, IReadOnlyList<(string Module, DamlInterface Interface)> packageInterfaces)
        : Entity(package, module, dataType.Name)
    {
        internal override bool IsAlwaysEmitted => true;

        internal override IEnumerable<(DamlTypeRef, string)> References() =>
            ViewedInterfaces().Concat(dataType.Definition switch
            {
                DamlRecordDefinition record => record.Fields.SelectMany(field => Referenced(field.Type, "it is a field type of")),
                DamlVariantDefinition variant => variant.Constructors.SelectMany(constructor => Referenced(constructor.ArgumentType, "it is a constructor argument type of")),
                _ => [],
            });

        private IEnumerable<(DamlTypeRef, string)> ViewedInterfaces() =>
            packageInterfaces
                .Where(declared => declared.Interface.ViewType is DamlTypeRef view
                    && view.Name == Name
                    && view.Module == Module.Name
                    && (view.PackageId.Length == 0 || view.PackageId == Package.PackageId))
                .Select(declared => (new DamlTypeRef(Package.PackageId, declared.Module, declared.Interface.Name), "its view type is the record"));
    }

    private sealed class TypeReferences : IDamlTypeVisitor<IEnumerable<DamlTypeRef>>
    {
        private static readonly TypeReferences Instance = new();

        internal static IEnumerable<DamlTypeRef> Of(DamlType type) => type.Accept(Instance);

        public IEnumerable<DamlTypeRef> VisitPrimitive(DamlPrimitiveType type) => [];

        public IEnumerable<DamlTypeRef> VisitTypeRef(DamlTypeRef type) => [type];

        public IEnumerable<DamlTypeRef> VisitTypeApp(DamlTypeApp type) => Of(type.Base).Concat(type.Arguments.SelectMany(Of));

        public IEnumerable<DamlTypeRef> VisitTypeVar(DamlTypeVar type) => [];

        public IEnumerable<DamlTypeRef> VisitList(DamlListType type) => Of(type.Element);

        public IEnumerable<DamlTypeRef> VisitOptional(DamlOptionalType type) => Of(type.Value);

        public IEnumerable<DamlTypeRef> VisitTextMap(DamlTextMapType type) => Of(type.Value);

        public IEnumerable<DamlTypeRef> VisitGenMap(DamlGenMapType type) => Of(type.Key).Concat(Of(type.Value));

        public IEnumerable<DamlTypeRef> VisitContractId(DamlContractIdType type) => Of(type.Payload);
    }
}
