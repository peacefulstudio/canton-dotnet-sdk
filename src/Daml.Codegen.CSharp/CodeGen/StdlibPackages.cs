// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using RuntimeNamespaces = Daml.Runtime.RuntimeNamespaces;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Classifies Daml packages that are baked into <c>Daml.Runtime</c> (daml-prim,
/// daml-stdlib) or carry no usable metadata, maps known stdlib type names to
/// their <c>Daml.Runtime.Stdlib.*</c> equivalents, and emits the namespace
/// <c>Require</c>s needed by emitted field types. Shared by the emitter and the
/// cross-package resolver so both agree on what is stdlib.
/// </summary>
internal static class StdlibPackages
{
    /// <summary>
    /// Returns true for packages whose types are baked into <c>Daml.Runtime</c>
    /// (daml-prim, daml-stdlib) and therefore must not be emitted as a
    /// PackageReference. Cross-package refs into these packages are mapped to
    /// <c>Daml.Runtime.Stdlib.*</c> types.
    /// </summary>
    public static bool IsStdlibPackage(string packageName) =>
        packageName.StartsWith("daml-prim", StringComparison.Ordinal)
        || packageName.StartsWith("daml-stdlib", StringComparison.Ordinal)
        || packageName.StartsWith("ghc-stdlib", StringComparison.Ordinal);

    /// <summary>Returns true for packages carrying no usable name metadata.</summary>
    public static bool IsPlaceholderPackageName(string packageName) =>
        string.IsNullOrEmpty(packageName)
        || packageName == "-no-package-metadata"
        || packageName == "UnknownPackage";

    /// <summary>
    /// Maps a stdlib type reference to its simple Daml.Runtime.Stdlib type name (no
    /// namespace), or null if there is no known mapping.
    /// </summary>
    public static string? MapStdlibType(string module, string typeName) => (module, typeName) switch
    {
        ("DA.Date.Types", "DayOfWeek") => "DayOfWeek",
        ("DA.Time.Types", "RelTime") => "RelTime",
        ("DA.Types", "Tuple2") => "Tuple2",
        ("DA.Types", "Tuple3") => "Tuple3",
        ("DA.Types", "Either") => "Either",
        ("DA.Set.Types", "Set") => "Set",
        ("DA.NonEmpty.Types", "NonEmpty") => "NonEmpty",
        ("DA.Map.Types", "Map") => "Map",
        ("DA.Internal.Map", "Map") => "Map",
        _ => null,
    };

    /// <summary>
    /// The parameterised stdlib types whose <c>ToRecord</c>/<c>FromRecord</c> need
    /// caller-supplied converters per type argument (delegate-based round-trip). Single
    /// source of truth: <see cref="IsParametricStdlibType"/> and the emitter's
    /// conversion table both derive from this set.
    /// </summary>
    public static IReadOnlySet<(string Module, string Name)> ParametricStdlibTypes { get; } = new HashSet<(string, string)>
    {
        ("DA.Types", "Tuple2"),
        ("DA.Types", "Tuple3"),
        ("DA.Types", "Either"),
        ("DA.Set.Types", "Set"),
        ("DA.NonEmpty.Types", "NonEmpty"),
        ("DA.Map.Types", "Map"),
        ("DA.Internal.Map", "Map"),
    };

    /// <summary>
    /// Returns true if a stdlib reference points at one of the
    /// <see cref="ParametricStdlibTypes"/>.
    /// </summary>
    public static bool IsParametricStdlibType(string module, string typeName) =>
        ParametricStdlibTypes.Contains((module, typeName));

    /// <summary>
    /// Returns true if <paramref name="typeRef"/> resolves to a known stdlib type
    /// in a known stdlib package. Gating on package id matters because user
    /// packages can legally define a module named e.g. <c>DA.Types</c> with
    /// a type <c>Tuple2</c>; without the package check the codegen would
    /// route those through <c>Daml.Runtime.Stdlib.*</c> and emit broken code.
    /// </summary>
    internal static bool IsStdlibTypeRef(ICrossPackageResolver resolver, DamlTypeRef typeRef, bool parametric) =>
        IsInStdlibPackage(resolver, typeRef)
        && (parametric
            ? IsParametricStdlibType(typeRef.Module, typeRef.Name)
            : MapStdlibType(typeRef.Module, typeRef.Name) is not null);

    /// <summary>
    /// Returns true if <paramref name="typeRef"/>'s package resolves to a stdlib
    /// package or a metadata-less placeholder package.
    /// </summary>
    internal static bool IsInStdlibPackage(ICrossPackageResolver resolver, DamlTypeRef typeRef)
    {
        if (string.IsNullOrEmpty(typeRef.PackageId))
        {
            return false;
        }
        var foreignPkg = resolver.LookupPackage(typeRef.PackageId);
        return foreignPkg is not null
            && (IsStdlibPackage(foreignPkg.Name) || IsPlaceholderPackageName(foreignPkg.Name));
    }

    /// <summary>
    /// Walks <paramref name="type"/> and records on <paramref name="indent"/> every
    /// namespace the emitted code for that field type needs, classifying stdlib refs
    /// via <paramref name="resolver"/>. Shared by the choice-exercise and
    /// data/template emit paths so both require an identical namespace set. The type runs
    /// through <see cref="OptionalRepresentation.Rewrite(DamlType, DamlPackage, ICrossPackageResolver)"/>
    /// first, so the walk sees the same representation the emitter will, rather than
    /// deciding a second time which Optionals need the runtime wrapper.
    /// </summary>
    internal static void RequireForFieldType(
        ICrossPackageResolver resolver, DamlPackage localPackage, IndentWriter indent, DamlType type) =>
        RequireForRewrittenType(resolver, indent, OptionalRepresentation.Rewrite(type, localPackage, resolver));

    private static void RequireForRewrittenType(ICrossPackageResolver resolver, IndentWriter indent, DamlType type)
    {
        switch (type)
        {
            case DamlListType list:
                indent.Require("System.Collections.Generic");
                indent.Require("System.Linq");
                RequireForRewrittenType(resolver, indent, list.Element);
                break;
            case DamlOptionalType optional:
                RequireForRewrittenType(resolver, indent, optional.Value);
                break;
            case DamlTextMapType textMap:
                indent.Require("System.Collections.Generic");
                indent.Require("System.Linq");
                RequireForRewrittenType(resolver, indent, textMap.Value);
                break;
            case DamlGenMapType genMap:
                indent.Require("System.Collections.Generic");
                indent.Require("System.Linq");
                RequireForRewrittenType(resolver, indent, genMap.Key);
                RequireForRewrittenType(resolver, indent, genMap.Value);
                break;
            case DamlContractIdType:
                indent.Require(RuntimeNamespaces.Contracts);
                break;
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List } } app:
                indent.Require("System.Collections.Generic");
                indent.Require("System.Linq");
                RequireForRewrittenType(resolver, indent, app.Arguments[0]);
                break;
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional } } app:
                RequireForRewrittenType(resolver, indent, app.Arguments[0]);
                break;
            case DamlWrappedOptional wrapped:
                indent.Require(RuntimeNamespaces.Stdlib);
                RequireForRewrittenType(resolver, indent, wrapped.Argument);
                break;
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap } } app:
                indent.Require("System.Collections.Generic");
                indent.Require("System.Linq");
                RequireForRewrittenType(resolver, indent, app.Arguments[0]);
                break;
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.GenMap } } app:
                indent.Require("System.Collections.Generic");
                indent.Require("System.Linq");
                RequireForRewrittenType(resolver, indent, app.Arguments[0]);
                RequireForRewrittenType(resolver, indent, app.Arguments[1]);
                break;
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId } }:
                indent.Require(RuntimeNamespaces.Contracts);
                break;
            case DamlPrimitiveType { Primitive: DamlPrimitive.Date }:
            case DamlPrimitiveType { Primitive: DamlPrimitive.Timestamp }:
                indent.Require("System");
                break;
            case DamlPrimitiveType { Primitive: DamlPrimitive.Unit }:
                indent.Require(RuntimeNamespaces.Stdlib);
                break;
            case DamlTypeApp { Base: DamlTypeRef typeRef } app
                when IsStdlibTypeRef(resolver, typeRef, parametric: true):
                indent.Require(RuntimeNamespaces.Stdlib);
                foreach (var arg in app.Arguments)
                {
                    RequireForRewrittenType(resolver, indent, arg);
                }
                break;
            case DamlTypeRef typeRef when IsStdlibTypeRef(resolver, typeRef, parametric: false):
                indent.Require(RuntimeNamespaces.Stdlib);
                break;
            case DamlTypeVar:
                indent.Require(RuntimeNamespaces.Stdlib);
                break;
            case DamlTypeApp { Base: DamlTypeVar or DamlTypeApp } app:
                indent.Require(RuntimeNamespaces.Stdlib);
                foreach (var argument in app.Arguments)
                {
                    if (argument is DamlTypeVar)
                    {
                        continue;
                    }
                    RequireForRewrittenType(resolver, indent, argument);
                }
                break;
            case DamlTypeApp app:
                foreach (var argument in app.Arguments)
                {
                    if (argument is DamlTypeVar)
                    {
                        continue;
                    }
                    RequireForRewrittenType(resolver, indent, argument);
                }
                break;
        }
    }
}
