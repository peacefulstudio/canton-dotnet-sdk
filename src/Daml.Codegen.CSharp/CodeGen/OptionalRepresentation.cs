// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Decides, once and before translation, which of a type tree's Optionals C# nullable
/// syntax can carry and which need the runtime wrapper. The rule needs the path from the
/// root — is this Optional inside another Optional, is it a type argument to a generic,
/// is it a GenMap key — which a root-down walk has natively and the bottom-up
/// recursive translator never does. Rewriting up front keeps the translator stateless.
/// A rewriter over the typed nodes: each of the five has an arm carrying its children's
/// path state, including the GenMap-key rule that forces the flat wrapper, and the
/// legacy application spellings route to the same decisions so a hand-built model that
/// never passed a reader is rewritten exactly like its normalized equivalent.
/// </summary>
internal static class OptionalRepresentation
{
    private const int MaxTypeDepth = 256;

    /// <summary>
    /// Returns <paramref name="type"/> with every Optional in a wrapper position replaced by
    /// a <see cref="DamlWrappedOptional"/>. Pure, and idempotent on its own output.
    /// </summary>
    /// <param name="type">The type to rewrite.</param>
    /// <param name="localPackage">
    /// The package <paramref name="type"/> was read from. A type ref declared in the same
    /// package carries no package id, so this is what such a ref resolves against.
    /// </param>
    /// <param name="resolver">Resolves type refs that name another package.</param>
    /// <exception cref="CodegenException">
    /// An Optional is passed as a type argument to a generic whose declaration wraps that
    /// same type parameter in an Optional.
    /// </exception>
    public static DamlType Rewrite(DamlType type, DamlPackage localPackage, ICrossPackageResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(localPackage);
        ArgumentNullException.ThrowIfNull(resolver);
        return Rewrite(type, localPackage, resolver, required: null, parentIsOptional: false, depth: 0);
    }

    private static DamlType Rewrite(
        DamlType type,
        DamlPackage localPackage,
        ICrossPackageResolver resolver,
        OptionalEncoding? required,
        bool parentIsOptional,
        int depth)
    {
        if (depth > MaxTypeDepth)
        {
            throw new InvalidDataException(
                $"Deciding Optional representation exceeded the maximum Daml type depth of {MaxTypeDepth}. "
                + "The type is too deeply nested to emit safely.");
        }

        return type is DamlWrappedOptional wrapped
            ? wrapped with
            {
                Argument = Rewrite(
                    wrapped.Argument,
                    localPackage,
                    resolver,
                    required: null,
                    parentIsOptional: IsChainLevel(wrapped.Encoding),
                    depth + 1),
            }
            : type.Accept(new RewriteVisitor(localPackage, resolver, required, parentIsOptional, depth));
    }

    /// <summary>
    /// The rewriter's per-node arms: the five typed nodes carry their children's path state
    /// (an Optional's child is parented by an Optional; a GenMap's key takes the flat
    /// wrapper; a generic's arguments take the flat wrapper), the leaf nodes pass through,
    /// and <see cref="VisitTypeApp"/> keeps the legacy application spellings alive — a
    /// complete application of <c>Optional</c> or <c>GenMap</c> routes to the same decision
    /// as its typed node, and every other application recurses into its arguments.
    /// </summary>
    private sealed class RewriteVisitor(
        DamlPackage localPackage,
        ICrossPackageResolver resolver,
        OptionalEncoding? required,
        bool parentIsOptional,
        int depth) : IDamlTypeVisitor<DamlType>
    {
        public DamlType VisitPrimitive(DamlPrimitiveType type) => type;

        public DamlType VisitTypeRef(DamlTypeRef type) => type;

        public DamlType VisitTypeVar(DamlTypeVar type) => type;

        public DamlType VisitOptional(DamlOptionalType type) =>
            RewrittenOptional(type.Value, rewritten => type with { Value = rewritten });

        public DamlType VisitList(DamlListType type) =>
            type with { Element = RewriteChild(type.Element) };

        public DamlType VisitTextMap(DamlTextMapType type) =>
            type with { Value = RewriteChild(type.Value) };

        public DamlType VisitGenMap(DamlGenMapType type) => type with
        {
            Key = Rewrite(type.Key, localPackage, resolver, OptionalEncoding.Flat, parentIsOptional: false, depth + 1),
            Value = Rewrite(type.Value, localPackage, resolver, required: null, parentIsOptional: false, depth + 1),
        };

        public DamlType VisitContractId(DamlContractIdType type) =>
            type with { Payload = RewriteChild(type.Payload) };

        public DamlType VisitTypeApp(DamlTypeApp type)
        {
            if (type.Base is DamlPrimitiveType { Primitive: DamlPrimitive.Optional }
                && type.Arguments is [var argument])
            {
                return RewrittenOptional(argument, rewritten => type with { Arguments = [rewritten] });
            }
            if (type.Base is DamlPrimitiveType { Primitive: DamlPrimitive.GenMap }
                && type.Arguments is [var key, var value])
            {
                return type with
                {
                    Arguments = RewriteGenMapArguments(key, value, localPackage, resolver, depth),
                };
            }
            if (type.Base is DamlTypeRef typeRef)
            {
                return type with
                {
                    Arguments =
                    [
                        .. type.Arguments.Select((argument, index) =>
                            RewriteGenericArgument(typeRef, argument, index, localPackage, resolver, depth)),
                    ],
                };
            }
            return type with
            {
                Arguments =
                [
                    .. type.Arguments.Select(argument =>
                        Rewrite(argument, localPackage, resolver, required: null, parentIsOptional: false, depth + 1)),
                ],
            };
        }

        private DamlType RewriteChild(DamlType child) =>
            Rewrite(child, localPackage, resolver, required: null, parentIsOptional: false, depth + 1);

        private DamlType RewrittenOptional(DamlType argument, Func<DamlType, DamlType> withoutWrapper)
        {
            var rewrittenArgument =
                Rewrite(argument, localPackage, resolver, required: null, parentIsOptional: true, depth + 1);
            var encoding = parentIsOptional || IsOptional(argument)
                ? OptionalEncoding.NestedChain
                : required ?? (argument is DamlTypeVar ? OptionalEncoding.Flat : (OptionalEncoding?)null);
            return encoding is { } chosen
                ? new DamlWrappedOptional(rewrittenArgument, chosen)
                : withoutWrapper(rewrittenArgument);
        }
    }

    /// <summary>
    /// Rewrites a GenMap's two type arguments. A GenMap is emitted as a C# dictionary, whose
    /// key type parameter is <c>notnull</c>: a <c>null</c> key is rejected at compile time and
    /// throws on insert, so C# nullable syntax cannot carry an Optional in that position and the
    /// key takes the wrapper in the flat encoding, which leaves the wire form unchanged. The
    /// value sits in an ordinary dictionary slot and stays nullable.
    /// </summary>
    private static IReadOnlyList<DamlType> RewriteGenMapArguments(
        DamlType key,
        DamlType value,
        DamlPackage localPackage,
        ICrossPackageResolver resolver,
        int depth) =>
    [
        Rewrite(key, localPackage, resolver, OptionalEncoding.Flat, parentIsOptional: false, depth + 1),
        Rewrite(value, localPackage, resolver, required: null, parentIsOptional: false, depth + 1),
    ];

    /// <summary>
    /// Rewrites one type argument of a generic — emitted or a parametric stdlib generic,
    /// which are alike in that a C# type argument cannot carry a <c>?</c> — refusing the
    /// substitution the emitter cannot encode. A generic's body is emitted once from its
    /// declaration, so an Optional the declaration wraps around a type parameter gets its
    /// chain-or-flat encoding decided there, blind to the argument each use site substitutes.
    /// Substituting an Optional adds a level adjacent to that one: the two sides then
    /// disagree, and the composed converter writes one array level short of the chain
    /// encoding — a value the participant accepts and reads as a different Optional.
    /// </summary>
    private static DamlType RewriteGenericArgument(
        DamlTypeRef typeRef,
        DamlType argument,
        int index,
        DamlPackage localPackage,
        ICrossPackageResolver resolver,
        int depth)
    {
        if (IsOptional(argument)
            && OptionalWrappedParameter(resolver, localPackage, typeRef, index, []) is { } parameter)
        {
            throw new CodegenException(
                $"Codegen does not support a Daml Optional as the '{parameter}' type argument of "
                + $"{typeRef.Module}:{typeRef.Name}. That declaration wraps '{parameter}' in an Optional, and a "
                + "generic's body is emitted once from its declaration, so the Optional levels the declaration "
                + "contributes and the ones the type argument contributes cannot agree on the nested-chain array "
                + "encoding — the value would go on the wire one array level short and be read back as a different "
                + $"Optional. Refactor the Daml signature so no Optional is passed through '{parameter}'.");
        }

        return Rewrite(argument, localPackage, resolver, OptionalEncoding.Flat, parentIsOptional: false, depth + 1);
    }

    /// <summary>
    /// The name of the type parameter at <paramref name="index"/> of the type
    /// <paramref name="typeRef"/> names when that declaration wraps the parameter in an
    /// Optional, or <c>null</c> when it does not — including when the declaration cannot be
    /// resolved, which leaves emission to the arms that already report an unresolvable ref.
    /// <paramref name="visited"/> is the set of declaration slots already on the walk, so a
    /// recursive data type terminates.
    /// </summary>
    private static string? OptionalWrappedParameter(
        ICrossPackageResolver resolver,
        DamlPackage referringPackage,
        DamlTypeRef typeRef,
        int index,
        HashSet<string> visited)
    {
        var declaringPackage = DeclaringPackage(resolver, referringPackage, typeRef);
        var declaration = declaringPackage?.Modules
            .Where(module => module.Name == typeRef.Module)
            .SelectMany(module => module.DataTypes)
            .FirstOrDefault(dataType => dataType.Name == typeRef.Name);
        if (declaringPackage is null || declaration is null || index >= declaration.TypeParams.Count)
        {
            return null;
        }
        if (!visited.Add($"{declaringPackage.PackageId}:{typeRef.Module}:{typeRef.Name}:{index}"))
        {
            return null;
        }

        var parameter = declaration.TypeParams[index];
        return DeclaredTypes(declaration)
            .Any(declared => WrapsParameterInOptional(resolver, declaringPackage, declared, parameter, visited))
            ? parameter
            : null;
    }

    private static DamlPackage? DeclaringPackage(
        ICrossPackageResolver resolver, DamlPackage referringPackage, DamlTypeRef typeRef) =>
        string.IsNullOrEmpty(typeRef.PackageId) || typeRef.PackageId == referringPackage.PackageId
            ? referringPackage
            : resolver.LookupPackage(typeRef.PackageId);

    private static IEnumerable<DamlType> DeclaredTypes(DamlDataType declaration) => declaration.Definition switch
    {
        DamlRecordDefinition record => record.Fields.Select(field => field.Type),
        DamlVariantDefinition variant => variant.Constructors
            .Select(constructor => constructor.ArgumentType)
            .OfType<DamlType>(),
        _ => [],
    };

    /// <summary>
    /// Whether <paramref name="declaredType"/> puts <paramref name="parameter"/> directly
    /// beneath an Optional. Only an adjacent Optional matters: any other constructor between
    /// the two keeps the declaration's decision and the use site's decision independent, and
    /// both are then locally right. Walks every node kind — the five typed nodes included,
    /// since reader-fed declarations carry them — through
    /// <see cref="ParameterWrapVisitor"/>.
    /// </summary>
    private static bool WrapsParameterInOptional(
        ICrossPackageResolver resolver,
        DamlPackage declaringPackage,
        DamlType declaredType,
        string parameter,
        HashSet<string> visited) =>
        declaredType.Accept(new ParameterWrapVisitor(resolver, declaringPackage, parameter, visited));

    /// <summary>
    /// The declaration-walk arms of <see cref="WrapsParameterInOptional"/>: an Optional
    /// answers for the parameter it may carry directly, a generic application answers
    /// through its slots, and the remaining constructors answer through their children.
    /// </summary>
    private sealed class ParameterWrapVisitor(
        ICrossPackageResolver resolver,
        DamlPackage declaringPackage,
        string parameter,
        HashSet<string> visited) : IDamlTypeVisitor<bool>
    {
        public bool VisitPrimitive(DamlPrimitiveType type) => false;

        public bool VisitTypeRef(DamlTypeRef type) => false;

        public bool VisitTypeVar(DamlTypeVar type) => false;

        public bool VisitOptional(DamlOptionalType type) =>
            IsParameterBeneathOptionals(type.Value, parameter)
            || WrapsParameterInOptional(resolver, declaringPackage, type.Value, parameter, visited);

        public bool VisitList(DamlListType type) =>
            WrapsParameterInOptional(resolver, declaringPackage, type.Element, parameter, visited);

        public bool VisitTextMap(DamlTextMapType type) =>
            WrapsParameterInOptional(resolver, declaringPackage, type.Value, parameter, visited);

        public bool VisitGenMap(DamlGenMapType type) =>
            WrapsParameterInOptional(resolver, declaringPackage, type.Key, parameter, visited)
            || WrapsParameterInOptional(resolver, declaringPackage, type.Value, parameter, visited);

        public bool VisitContractId(DamlContractIdType type) =>
            WrapsParameterInOptional(resolver, declaringPackage, type.Payload, parameter, visited);

        public bool VisitTypeApp(DamlTypeApp type)
        {
            if (type.Base is DamlPrimitiveType { Primitive: DamlPrimitive.Optional }
                && type.Arguments is [var argument])
            {
                return IsParameterBeneathOptionals(argument, parameter)
                    || WrapsParameterInOptional(resolver, declaringPackage, argument, parameter, visited);
            }
            if (type.Base is DamlTypeRef nested)
            {
                return type.Arguments
                    .Select((argument, index) => (Argument: argument, Index: index))
                    .Any(slot =>
                        WrapsParameterInOptional(resolver, declaringPackage, slot.Argument, parameter, visited)
                        || (IsParameterBeneathOptionals(slot.Argument, parameter)
                            && OptionalWrappedParameter(resolver, declaringPackage, nested, slot.Index, visited) is not null));
            }
            return type.Arguments.Any(argument =>
                WrapsParameterInOptional(resolver, declaringPackage, argument, parameter, visited));
        }
    }

    /// <summary>
    /// Whether <paramref name="declaredType"/> is the type parameter <paramref name="parameter"/>
    /// itself, or sits beneath Optional-only nesting — any other constructor between the
    /// Optional and the parameter disqualifies, and so does every node kind that is not an
    /// Optional.
    /// </summary>
    private static bool IsParameterBeneathOptionals(DamlType declaredType, string parameter) =>
        declaredType.Accept(new ParameterBeneathOptionalsVisitor(parameter));

    private sealed class ParameterBeneathOptionalsVisitor(string parameter) : IDamlTypeVisitor<bool>
    {
        public bool VisitTypeVar(DamlTypeVar type) => type.Name == parameter;

        public bool VisitOptional(DamlOptionalType type) => type.Value.Accept(this);

        public bool VisitTypeApp(DamlTypeApp type) =>
            type.Base is DamlPrimitiveType { Primitive: DamlPrimitive.Optional }
            && type.Arguments is [var argument]
                ? argument.Accept(this)
                : false;

        public bool VisitPrimitive(DamlPrimitiveType type) => false;

        public bool VisitTypeRef(DamlTypeRef type) => false;

        public bool VisitList(DamlListType type) => false;

        public bool VisitTextMap(DamlTextMapType type) => false;

        public bool VisitGenMap(DamlGenMapType type) => false;

        public bool VisitContractId(DamlContractIdType type) => false;
    }

    /// <remarks>
    /// A switch, not an equality test, so a newly added <see cref="OptionalEncoding"/> is a
    /// compile error here rather than silently costing its argument a chain level. Only CS8524 —
    /// the out-of-range cast — is suppressed.
    /// </remarks>
#pragma warning disable CS8524
    private static bool IsChainLevel(OptionalEncoding encoding) => encoding switch
    {
        OptionalEncoding.Flat => false,
        OptionalEncoding.NestedChain => true,
    };
#pragma warning restore CS8524

    private static bool IsOptional(DamlType type) =>
        type is DamlWrappedOptional
            or DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional } }
            or DamlOptionalType;
}
