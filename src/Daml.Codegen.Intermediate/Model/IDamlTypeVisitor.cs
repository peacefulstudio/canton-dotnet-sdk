// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.Intermediate.Model;

/// <summary>
/// A closed visitor over the public <see cref="DamlType"/> node set: exactly one method per
/// node, and no default arm anywhere. <see cref="DamlType.Accept{TResult}(IDamlTypeVisitor{TResult})"/>
/// dispatches a node to its matching arm, so dispatch never falls through to a shared default
/// at runtime, and exhaustiveness is enforced by the compiler: an implementation that misses
/// a method fails the build with CS0535 (unimplemented interface member), so adding a
/// <see cref="DamlType"/> node without extending every visitor fails at the first implementer.
/// </summary>
/// <typeparam name="TResult">The value every arm produces.</typeparam>
public interface IDamlTypeVisitor<TResult>
{
    /// <summary>Handles a <see cref="DamlPrimitiveType"/> node.</summary>
    /// <param name="type">The node being visited.</param>
    TResult VisitPrimitive(DamlPrimitiveType type);

    /// <summary>Handles a <see cref="DamlTypeRef"/> node.</summary>
    /// <param name="type">The node being visited.</param>
    TResult VisitTypeRef(DamlTypeRef type);

    /// <summary>
    /// Handles a <see cref="DamlTypeApp"/> node — a generic application the typed nodes do
    /// not own: a user-defined type constructor's application, or the <c>Numeric n</c> scale
    /// pun, whose scale rides a type variable.
    /// </summary>
    /// <param name="type">The node being visited.</param>
    TResult VisitTypeApp(DamlTypeApp type);

    /// <summary>Handles a <see cref="DamlTypeVar"/> node.</summary>
    /// <param name="type">The node being visited.</param>
    TResult VisitTypeVar(DamlTypeVar type);

    /// <summary>Handles a <see cref="DamlListType"/> node.</summary>
    /// <param name="type">The node being visited.</param>
    TResult VisitList(DamlListType type);

    /// <summary>Handles a <see cref="DamlOptionalType"/> node.</summary>
    /// <param name="type">The node being visited.</param>
    TResult VisitOptional(DamlOptionalType type);

    /// <summary>Handles a <see cref="DamlTextMapType"/> node.</summary>
    /// <param name="type">The node being visited.</param>
    TResult VisitTextMap(DamlTextMapType type);

    /// <summary>Handles a <see cref="DamlGenMapType"/> node.</summary>
    /// <param name="type">The node being visited.</param>
    TResult VisitGenMap(DamlGenMapType type);

    /// <summary>Handles a <see cref="DamlContractIdType"/> node.</summary>
    /// <param name="type">The node being visited.</param>
    TResult VisitContractId(DamlContractIdType type);
}
