// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace Daml.Runtime.Stdlib;

/// <summary>
/// Stubs for generic type-parameter cases that the C# codegen cannot fully resolve.
/// Daml supports parametric polymorphism on records (e.g. a record with a Daml
/// type variable as a field); the C# codegen has no way to call <c>.ToRecord()</c>
/// on a bare type parameter without a static-abstract dispatch path. These stubs
/// let the generated types compile so the rest of the package surface area is
/// usable, while loudly failing if anyone actually tries to serialize a generic
/// instance.
/// <para>
/// Call sites: the <c>DamlTypeVar</c> fallback arms of <c>DamlTypeMapper.ToValue</c>,
/// <c>DamlTypeMapper.FromValue</c>, and <c>DamlTypeMapper.FromJson</c>, reached only for a
/// type variable outside any generic record or variant body whose converter delegates
/// would otherwise resolve it — for example an interface choice's open-typed return
/// before it is instantiated with concrete arguments.
/// </para>
/// </summary>
public static class GenericStub
{
    /// <summary>
    /// Throws <see cref="NotImplementedException"/>. Marked <see cref="DoesNotReturnAttribute"/>
    /// so flow analysis treats the call site as a dead end — important because the
    /// signature returns <typeparamref name="T"/> for use in expression position
    /// (record initializers, ternaries) where C# otherwise insists on a value.
    /// </summary>
    /// <param name="context">
    /// Identifier shown in the exception message. Codegen call sites pass either the
    /// field name (in <c>DamlTypeMapper.ToValue</c>) or the Daml type-var name (in
    /// <c>DamlTypeMapper.FromValue</c>), whichever is most useful at that site.
    /// </param>
    [DoesNotReturn]
    public static T NotImplemented<T>(string context) =>
        throw new NotImplementedException(
            $"Generic type-parameter serialization for '{context}' is not yet supported by canton-dotnet-sdk. "
            + "Workaround: instantiate the type with concrete arguments and write the conversion by hand.");
}
