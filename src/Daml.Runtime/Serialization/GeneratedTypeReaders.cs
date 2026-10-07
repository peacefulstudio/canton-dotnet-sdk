// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Serialization;

/// <summary>
/// Process-wide registry mapping a Daml type's wire identifier to the emitted Daml-LF JSON reader,
/// key descriptor and choice descriptors of the generated type that declares it. Registration is
/// generic so the compiler binds each type's emitted members; nothing here reflects over a CLR
/// type's shape. Generated code registers its own types; the SDK's clients look them up.
/// </summary>
/// <remarks>
/// Every registration is stored under its full identifier (package id, module and entity), never a
/// <see cref="Type"/>. A lookup tries the full identifier first. Two distinct declaring types
/// registered for the same full identifier make that identifier ambiguous, and registering the same
/// declaring type again changes nothing. Only if the full identifier is unknown does a lookup fall
/// back to the <c>(ModuleName, EntityName)</c> pair, and that fallback resolves only when exactly one
/// registered declaring type carries the pair. Keying on module and entity alone would collide
/// whenever two versions of one package are in play, the normal state of a Splice deployment
/// mid-upgrade. A choice name absent on the declaring type a lookup resolved is a miss, not a
/// reason to try another type. A module initializer runs on first access to its module, not when its
/// assembly loads, so the first lookup that finds no entry for its exact identifier loads every library the host's deps.json lists
/// that depends on <c>Daml.Runtime</c>, runs each one's module initializer, and looks again. That runs at
/// most once per process; a host with no deps.json runs a generated assembly's module constructor itself.
/// </remarks>
/// <threadsafety>
/// All members are safe to call from any thread at any time. Registration may run lazily, from a
/// module initializer on whichever thread first touches its module, while lookups are in flight. A
/// lookup sees each registration call wholly or not at all: every choice of a
/// <see cref="ForChoices{T}"/> call becomes visible together. A lookup counts every registration
/// that completed before it started when deciding whether a pair is ambiguous, so once every
/// registration has returned no lookup resolves a pair that is ambiguous. While one thread runs the
/// one-time library load, any other lookup that finds nothing waits for it and then looks again.
/// </threadsafety>
public static class GeneratedTypeReaders
{
    internal static readonly GeneratedTypeRegistry Shared = new(BindingAssemblyLoader.LoadHostBindings);

    /// <summary>Registers the emitted record reader for <typeparamref name="T"/>.</summary>
    public static void ForRecord<T>() where T : IDamlType, IDamlRecord<T> =>
        Shared.ForRecord<T>();

    /// <summary>Registers the emitted key reader of <typeparamref name="TTemplate"/>.</summary>
    public static void ForKey<TTemplate, TKey>()
        where TTemplate : ITemplate, IHasKey<TTemplate, TKey> =>
        Shared.ForKey<TTemplate, TKey>();

    /// <summary>Registers every choice descriptor of <typeparamref name="T"/>.</summary>
    public static void ForChoices<T>() where T : IDamlType, IHasChoices<T> =>
        Shared.ForChoices<T>();

    internal static RegistryLookup<DamlLfElementReader> FindRecordReader(Identifier identifier) =>
        Shared.FindRecordReader(identifier);

    internal static RegistryLookup<IKeyDescriptor> FindKeyDescriptor(Identifier identifier) =>
        Shared.FindKeyDescriptor(identifier);

    internal static RegistryLookup<IChoice> FindChoice(Identifier identifier, ChoiceName choice) =>
        Shared.FindChoice(identifier, choice);
}
