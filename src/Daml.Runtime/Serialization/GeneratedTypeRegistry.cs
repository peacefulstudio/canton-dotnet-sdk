// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Serialization;

internal sealed class GeneratedTypeRegistry
{
    private readonly RegistryTable<NoDiscriminator, DamlLfElementReader> _records = new();
    private readonly RegistryTable<NoDiscriminator, IKeyDescriptor> _keys = new();
    private readonly RegistryTable<ChoiceName, IChoice> _choices = new();
    private readonly Action _loadBindings;
    private readonly object _loadBindingsLock = new();
    private volatile bool _bindingsLoaded;

    public GeneratedTypeRegistry()
        : this(() => { })
    {
    }

    public GeneratedTypeRegistry(Action loadBindings)
    {
        _loadBindings = loadBindings;
    }

    public void ForRecord<T>() where T : IDamlType, IDamlRecord<T>
    {
        var values = new Dictionary<NoDiscriminator, DamlLfElementReader> { [default] = T.__ReadDamlLfJson };
        _records.Register(T.DamlTypeId.Identifier, typeof(T), values);
    }

    public void ForKey<TTemplate, TKey>() where TTemplate : ITemplate, IHasKey<TTemplate, TKey>
    {
        var values = new Dictionary<NoDiscriminator, IKeyDescriptor> { [default] = TTemplate.Key };
        _keys.Register(TTemplate.TemplateId, typeof(TTemplate), values);
    }

    public void ForChoices<T>() where T : IDamlType, IHasChoices<T>
    {
        var values = new Dictionary<ChoiceName, IChoice>();
        foreach (var choice in T.Choices)
        {
            values.TryAdd(choice.Name, choice);
        }

        _choices.Register(T.DamlTypeId.Identifier, typeof(T), values);
    }

    public RegistryLookup<DamlLfElementReader> FindRecordReader(Identifier identifier) =>
        FindOrLoadBindings(_records, identifier, default);

    public RegistryLookup<IKeyDescriptor> FindKeyDescriptor(Identifier identifier) =>
        FindOrLoadBindings(_keys, identifier, default);

    public RegistryLookup<IChoice> FindChoice(Identifier identifier, ChoiceName choice) =>
        FindOrLoadBindings(_choices, identifier, choice);

    private RegistryLookup<TValue> FindOrLoadBindings<TDiscriminator, TValue>(
        RegistryTable<TDiscriminator, TValue> table, Identifier identifier, TDiscriminator discriminator)
        where TDiscriminator : notnull
        where TValue : notnull
    {
        if (table.HasExact(identifier))
        {
            return table.Find(identifier, discriminator);
        }

        LoadBindingsOnce();
        return table.Find(identifier, discriminator);
    }

    private void LoadBindingsOnce()
    {
        if (_bindingsLoaded || Monitor.IsEntered(_loadBindingsLock))
        {
            return;
        }

        lock (_loadBindingsLock)
        {
            if (_bindingsLoaded)
            {
                return;
            }

            try
            {
                _loadBindings();
            }
            finally
            {
                _bindingsLoaded = true;
            }
        }
    }

    private readonly record struct NoDiscriminator;

    private sealed class RegistryTable<TDiscriminator, TValue>
        where TDiscriminator : notnull
        where TValue : notnull
    {
        private sealed record Owner(Type DeclaringType, IReadOnlyDictionary<TDiscriminator, TValue> Values);

        private sealed record Snapshot(
            ImmutableDictionary<Identifier, ImmutableList<Owner>> ByIdentifier,
            ImmutableDictionary<(string ModuleName, string EntityName), ImmutableList<Owner>> ByModuleEntity)
        {
            public static Snapshot Empty { get; } = new(
                ImmutableDictionary<Identifier, ImmutableList<Owner>>.Empty,
                ImmutableDictionary<(string ModuleName, string EntityName), ImmutableList<Owner>>.Empty);
        }

        private readonly object _writeLock = new();
        private Snapshot _snapshot = Snapshot.Empty;

        public void Register(Identifier identifier, Type declaringType, IReadOnlyDictionary<TDiscriminator, TValue> values)
        {
            lock (_writeLock)
            {
                var current = _snapshot;
                var owners = current.ByIdentifier.GetValueOrDefault(identifier, []);
                if (owners.Exists(existing => existing.DeclaringType == declaringType))
                {
                    return;
                }

                var owner = new Owner(declaringType, values);
                var moduleEntityKey = (identifier.ModuleName, identifier.EntityName);
                var moduleEntityOwners = current.ByModuleEntity.GetValueOrDefault(moduleEntityKey, []);
                var next = new Snapshot(
                    current.ByIdentifier.SetItem(identifier, owners.Add(owner)),
                    current.ByModuleEntity.SetItem(moduleEntityKey, moduleEntityOwners.Add(owner)));
                Volatile.Write(ref _snapshot, next);
            }
        }

        public bool HasExact(Identifier identifier) =>
            Volatile.Read(ref _snapshot).ByIdentifier.ContainsKey(identifier);

        public RegistryLookup<TValue> Find(Identifier identifier, TDiscriminator discriminator)
        {
            var snapshot = Volatile.Read(ref _snapshot);
            if (snapshot.ByIdentifier.TryGetValue(identifier, out var exactOwners))
            {
                return Resolve(exactOwners, discriminator);
            }

            var moduleEntityKey = (identifier.ModuleName, identifier.EntityName);
            return snapshot.ByModuleEntity.TryGetValue(moduleEntityKey, out var moduleEntityOwners)
                ? Resolve(moduleEntityOwners, discriminator)
                : new RegistryLookup<TValue>.Missing();
        }

        private static RegistryLookup<TValue> Resolve(ImmutableList<Owner> owners, TDiscriminator discriminator)
        {
            if (owners.Count > 1)
            {
                var candidates = owners
                    .Select(owner => owner.DeclaringType.FullName ?? owner.DeclaringType.Name)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                return new RegistryLookup<TValue>.Ambiguous(candidates);
            }

            var only = owners[0];
            return only.Values.TryGetValue(discriminator, out var value)
                ? new RegistryLookup<TValue>.Resolved(value, only.DeclaringType)
                : new RegistryLookup<TValue>.Missing();
        }
    }
}
