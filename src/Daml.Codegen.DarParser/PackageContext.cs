// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using Type = Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type;

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.DarParser;

/// <summary>
/// Resolves a Daml-LF package's interned values and owns the parser's single
/// bounded conversion from a Daml-LF type expression to its model.
/// </summary>
internal sealed class PackageContext
{
    internal const int MaxTypeDepth = 256;

    private readonly Package _package;
    private readonly string _selfPackageId;
    private readonly List<DamlType?> _internedTypes = [];
    private readonly List<int> _internedTypeDepths = [];
    private readonly List<HashSet<string>> _internedTypeDependencies = [];

    public PackageContext(Package package, string selfPackageId = "")
    {
        _package = package;
        _selfPackageId = selfPackageId;

        foreach (var type in package.InternedTypes)
        {
            try
            {
                var converted = ConvertTypeInternal(type, depth: 0);
                _internedTypes.Add(AppliedTypeFolding.FoldSubexpression(converted.Type));
                _internedTypeDepths.Add(converted.Depth);
            }
            catch (StructTypeSignal)
            {
                _internedTypes.Add(null);
                _internedTypeDepths.Add(0);
            }

            var dependencies = new HashSet<string>(StringComparer.Ordinal);
            CollectTypeDependenciesInternal(type, dependencies, depth: 0);
            _internedTypeDependencies.Add(dependencies);
        }
    }

    /// <summary>
    /// Resolves an interned-string index. An index outside the interned-string table
    /// names no string at all and fails with <see cref="InvalidDataException"/>
    /// rather than resolving to a placeholder that reads downstream as a real name.
    /// </summary>
    public string GetString(int index)
    {
        if (TryGetString(index, out var value))
            return value;

        throw new InvalidDataException(
            $"Daml-LF interned-string index {index} is out of range of the interned-string table of size {_package.InternedStrings.Count}. The archive is malformed.");
    }

    /// <summary>
    /// Resolves an interned-string index, reporting whether it was in range.
    /// A caller that mints the resolved name into a result — the payload field
    /// a party analysis records, say — needs an out-of-range index signalled:
    /// <see cref="GetString"/>'s placeholder reads downstream as a real name,
    /// and no name is the honest answer.
    /// </summary>
    public bool TryGetString(int index, out string value)
    {
        if (index >= 0 && index < _package.InternedStrings.Count)
        {
            value = _package.InternedStrings[index];
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>
    /// Resolves an interned dotted-name index to its dot-joined segments. An index
    /// outside the interned dotted-name table, or a segment outside the interned-string
    /// table, fails with <see cref="InvalidDataException"/> like <see cref="GetString"/>.
    /// </summary>
    public string GetDottedName(int index)
    {
        if (index < 0 || index >= _package.InternedDottedNames.Count)
        {
            throw new InvalidDataException(
                $"Daml-LF interned dotted-name index {index} is out of range of the interned dotted-name table of size {_package.InternedDottedNames.Count}. The archive is malformed.");
        }

        var dottedName = _package.InternedDottedNames[index];
        return string.Join(".", dottedName.SegmentsInternedStr.Select(GetString));
    }

    /// <summary>
    /// Converts a Daml-LF type expression into its <see cref="DamlType"/> model.
    /// This is the parser's only <c>Type</c> to <see cref="DamlType"/> conversion:
    /// a DAR is untrusted input, so the traversal is bounded and a type nested
    /// past <c>MaxTypeDepth</c> fails with <see cref="InvalidDataException"/>
    /// rather than being converted unchecked. Depth composed across interned type
    /// references is charged to the referencing conversion, so the bound holds
    /// over the produced model and not merely over one traversal. An index that names
    /// a package — an interned-type index, a package-import index, or the interned
    /// string an archive predating the import table uses instead — has no valid
    /// reading past the end of its table, and fails with
    /// <see cref="InvalidDataException"/> too rather than being read as a plausible
    /// value. A reference that names no package at all — an absent module or
    /// package-id field, or a package-id field with no case set — fails the same
    /// way, rather than escaping as a <see cref="NullReferenceException"/> or being
    /// read as this package. A type position that declares no type expression at
    /// all — an absent <c>Type</c> field, or one populating none of its sum cases —
    /// fails the same way too: <c>Unit</c> is a real Daml type, so reading nothing
    /// as <c>Unit</c> mints a shape the archive never declared. Builtin types keep
    /// their real identity: the conversion resolves each Daml-LF builtin through
    /// <see cref="DamlLfBuiltinBridge"/> and the catalog it fronts, so the structural
    /// type-formers map to their own <c>DamlPrimitive</c> members instead of being
    /// erased to <c>Unit</c>, and a builtin whose catalog disposition is Unsupported
    /// throws <see cref="NotSupportedException"/>. Applications of the five folding
    /// builtins fold into their typed nodes by <see cref="AppliedTypeFolding.FoldComplete"/>
    /// over the finished conversion, with the catalog arity as the oracle — the raw
    /// conversion keeps every application generic and flattens curried <c>TApp</c> chains,
    /// so the fold runs once per complete type, composing across interned references whose
    /// pool entries were folded eagerly at construction. A struct type fails
    /// with <see cref="InvalidDataException"/> too: no serializable position can name one, so reading
    /// it as <c>Unit</c> mints a binding the archive never declared. The interned-type pool is the
    /// one place a struct is legal, so a pool entry holding one is accepted until a converted type
    /// position references it. The dependency walk stays lenient: a data type the conversion never
    /// reaches, a non-serializable one say, may still reference a pooled struct, and its imports count.
    /// Module and type names resolve through <see cref="GetString"/> and
    /// <see cref="GetDottedName"/>, which fail on an out-of-range index the same way.
    /// </summary>
    public DamlType ConvertType(Type? type) =>
        FailingOnStruct(() => AppliedTypeFolding.FoldComplete(ConvertTypeInternal(type, depth: 0).Type));

    /// <summary>
    /// Resolves an interned-type index to its converted model. An index pointing
    /// past the end of the interned-type pool has no valid reading at all and
    /// fails with <see cref="InvalidDataException"/> rather than resolving to a
    /// plausible <c>Unit</c>.
    /// </summary>
    public DamlType GetType(int index)
    {
        if (index < 0 || index >= _internedTypes.Count)
            throw InternedTypeIndexOutOfRange(index, _internedTypes.Count);

        return _internedTypes[index] ?? throw StructTypeNotSupported();
    }

    /// <summary>
    /// Collects into <paramref name="dependencies"/> the ids of the packages a
    /// Daml-LF type expression imports. Shares <see cref="ConvertType"/>'s depth
    /// bound: the walk is over the same untrusted tree, so a type nested past
    /// <c>MaxTypeDepth</c> fails with <see cref="InvalidDataException"/> here too,
    /// whatever order a caller runs its walks in. An interned type reference
    /// contributes the imports of the pool entry it names: damlc emits type
    /// positions as bare interned references, so a walk that stopped at the
    /// reference would collect nothing at all. Shares the same fail-loud posture
    /// too: an interned-type or package-import index out of range of its table, and
    /// a type reference naming no package at all, fail with
    /// <see cref="InvalidDataException"/> rather than silently contributing no
    /// dependency.
    /// </summary>
    public void CollectTypeDependencies(Type? type, ISet<string> dependencies) =>
        CollectTypeDependenciesInternal(type, dependencies, depth: 0);

    /// <summary>
    /// Returns the package id a type constructor reference imports, or null when it
    /// names this package. A reference that resolves to no package id at all fails
    /// with <see cref="InvalidDataException"/> rather than being read as a value:
    /// an import-table index that is negative or past the end of the table, an
    /// import-table index in a package carrying no such table, and an out-of-range
    /// interned-string index in the pre-import-table form all throw. So do the two
    /// shapes that carry no package reference to resolve — an absent module or
    /// package-id field, which in proto3 deserializes to null, and a package-id
    /// field with no case set, which names neither this package nor an import.
    /// Only <c>SelfPackageId</c> reads as null.
    /// </summary>
    public string? GetImportedPackageId(TypeConId typeConId) =>
        GetImportedPackageId(typeConId.Module);

    /// <summary>
    /// Returns the package id a type constructor reference names: the imported package's
    /// id, or this package's own id when it names this package. Fails on the same
    /// malformed references as <see cref="GetImportedPackageId(TypeConId)"/>.
    /// </summary>
    public string GetReferencedPackageId(TypeConId typeConId) =>
        GetImportedPackageId(typeConId) ?? _selfPackageId;

    /// <summary>
    /// Resolves an interned-expression index. Interned expressions appear in
    /// LF 2.2+ and let the proto deduplicate repeated subtrees; analyzers
    /// must dereference them before recursing. Returns null when the index is
    /// out of range — the caller should treat that as an unresolvable shape.
    /// </summary>
    public Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Expr? GetInternedExpr(int index)
    {
        if (index >= 0 && index < _package.InternedExprs.Count)
            return _package.InternedExprs[index];
        return null;
    }

    /// <summary>
    /// Resolves a top-level value reference (<c>App(Val(...), ...)</c>'s
    /// callee) to its defining expression, scoped to the current package.
    /// Returns null when the reference targets an imported package (this
    /// context only sees its own package's modules and cannot dereference
    /// cross-package value ids) or names a module/value this package does
    /// not define — callers must treat both as "not resolvable". A package-id
    /// field with no case set reads as null on the same terms: it names neither
    /// this package nor an import, so it resolves to nothing, which is what this
    /// method's null already means. An absent module or package-id field is a
    /// different matter — proto3 deserializes an unset message field to null, and
    /// a required field that is not there describes no reference at all — so those
    /// two fail with <see cref="InvalidDataException"/> rather than being read as
    /// unresolvable, matching every other read in this class.
    /// </summary>
    public Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Expr? GetValueExpr(ValueId valueId)
    {
        var referencedModule = valueId.Module ?? throw ModuleReferenceAbsent("value");
        var packageId = referencedModule.PackageId ?? throw PackageIdFieldAbsent();

        if (packageId.SumCase != SelfOrImportedPackageId.SumOneofCase.SelfPackageId)
            return null;

        foreach (var module in _package.Modules)
        {
            if (module.NameInternedDname != referencedModule.ModuleNameInternedDname)
                continue;

            foreach (var value in module.Values)
            {
                if (value.NameWithType.NameInternedDname == valueId.NameInternedDname)
                    return value.Expr;
            }
            return null;
        }
        return null;
    }

    private void CollectTypeDependenciesInternal(Type? type, ISet<string> dependencies, int depth)
    {
        ThrowIfTypeTooDeep(depth);

        if (type == null) return;

        switch (type.SumCase)
        {
            case Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.SumOneofCase.Con:
                AddImportedPackageId(type.Con.Tycon.Module, dependencies);
                foreach (var arg in type.Con.Args)
                {
                    CollectTypeDependenciesInternal(arg, dependencies, depth + 1);
                }
                break;

            case Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.SumOneofCase.Syn:
                AddImportedPackageId(type.Syn.Tysyn.Module, dependencies);
                foreach (var arg in type.Syn.Args)
                {
                    CollectTypeDependenciesInternal(arg, dependencies, depth + 1);
                }
                break;

            case Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.SumOneofCase.Builtin:
                foreach (var arg in type.Builtin.Args)
                {
                    CollectTypeDependenciesInternal(arg, dependencies, depth + 1);
                }
                break;

            case Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.SumOneofCase.Var:
                foreach (var arg in type.Var.Args)
                {
                    CollectTypeDependenciesInternal(arg, dependencies, depth + 1);
                }
                break;

            case Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.SumOneofCase.Forall:
                CollectTypeDependenciesInternal(type.Forall.Body, dependencies, depth + 1);
                break;

            case Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.SumOneofCase.Tapp:
                CollectTypeDependenciesInternal(type.Tapp.Lhs, dependencies, depth + 1);
                CollectTypeDependenciesInternal(type.Tapp.Rhs, dependencies, depth + 1);
                break;

            case Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.SumOneofCase.Struct:
                foreach (var field in type.Struct.Fields)
                {
                    CollectTypeDependenciesInternal(field.Type, dependencies, depth + 1);
                }
                break;

            case Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.SumOneofCase.InternedType:
                CollectInternedTypeDependencies(type.InternedType, dependencies, depth);
                break;

            case Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.SumOneofCase.Nat:
            case Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.SumOneofCase.None:
                break;

            default:
                throw new NotSupportedException(
                    $"Daml-LF Type sum case '{type.SumCase}' is unknown to the dependency walk. " +
                    "Update PackageContext.CollectTypeDependenciesInternal to walk it, or to state that it carries none.");
        }
    }

    private void CollectInternedTypeDependencies(int index, ISet<string> dependencies, int depth)
    {
        if (index < 0 || index >= _internedTypeDependencies.Count)
            throw InternedTypeIndexOutOfRange(index, _internedTypeDependencies.Count);

        ThrowIfTypeTooDeep(depth + _internedTypeDepths[index]);
        dependencies.UnionWith(_internedTypeDependencies[index]);
    }

    private void AddImportedPackageId(ModuleId? module, ISet<string> dependencies)
    {
        var importedPackageId = GetImportedPackageId(module);
        if (!string.IsNullOrEmpty(importedPackageId))
        {
            dependencies.Add(importedPackageId);
        }
    }

    private string? GetImportedPackageId(ModuleId? module)
    {
        if (module is null)
            throw ModuleReferenceAbsent("type");

        return GetImportedPackageId(module.PackageId);
    }

    private string? GetImportedPackageId(SelfOrImportedPackageId? packageId)
    {
        if (packageId is null)
            throw PackageIdFieldAbsent();

        return packageId.SumCase switch
        {
            SelfOrImportedPackageId.SumOneofCase.ImportedPackageIdInternedStr =>
                GetImportedPackageIdString(packageId.ImportedPackageIdInternedStr),
            SelfOrImportedPackageId.SumOneofCase.PackageImportId =>
                GetImportedPackage(packageId.PackageImportId),
            SelfOrImportedPackageId.SumOneofCase.SelfPackageId => null,
            SelfOrImportedPackageId.SumOneofCase.None => throw PackageIdCaseUnset(),
            _ => throw new NotSupportedException(
                $"Daml-LF SelfOrImportedPackageId sum case '{packageId.SumCase}' is unknown to the package-id read. " +
                "Update PackageContext.GetImportedPackageId to resolve it.")
        };
    }

    private string GetImportedPackageIdString(int index)
    {
        if (!TryGetString(index, out var packageId))
        {
            throw new InvalidDataException(
                $"Daml-LF package-id string index {index} is out of range of the interned-string table of size {_package.InternedStrings.Count}. The archive is malformed.");
        }

        return packageId;
    }

    private string GetImportedPackage(int index)
    {
        if (_package.ImportsSumCase != Package.ImportsSumOneofCase.PackageImports)
        {
            throw new InvalidDataException(
                $"Daml-LF package-import index {index} cannot be resolved: the package carries no package-import table. The archive is malformed.");
        }

        var importedPackages = _package.PackageImports.ImportedPackages;
        if (index < 0 || index >= importedPackages.Count)
        {
            throw new InvalidDataException(
                $"Daml-LF package-import index {index} is out of range of the package-import table of size {importedPackages.Count}. The archive is malformed.");
        }

        return importedPackages[index];
    }

    private ConvertedType ConvertTypeInternal(Type? type, int depth)
    {
        ThrowIfTypeTooDeep(depth);

        if (type == null)
            throw TypeReferenceAbsent();

        if (type.SumCase == Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.SumOneofCase.InternedType)
            return ResolveInternedType(type.InternedType, depth);

        return type.SumCase switch
        {
            Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.SumOneofCase.Var => ConvertTypeVarInternal(type.Var, depth),
            Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.SumOneofCase.Con => ConvertTypeConInternal(type.Con, depth),
            Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.SumOneofCase.Builtin => ConvertTypeBuiltinInternal(type.Builtin, depth),
            Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.SumOneofCase.Forall => ConvertTypeInternal(type.Forall.Body, depth + 1),
            Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.SumOneofCase.Nat => Leaf(new DamlTypeVar(type.Nat.ToString(CultureInfo.InvariantCulture))),
            Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.SumOneofCase.Syn => ConvertTypeSynInternal(type.Syn, depth),
            Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.SumOneofCase.Tapp => ConvertTypeTAppInternal(type.Tapp, depth),
            Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.SumOneofCase.Struct => throw new StructTypeSignal(),
            Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.SumOneofCase.None => throw TypeSumCaseUnset(),
            _ => throw new NotSupportedException(
                $"Daml-LF Type sum case '{type.SumCase}' is unknown and has no C# mapping. " +
                "Update PackageContext.ConvertTypeInternal to convert it.")
        };
    }

    private ConvertedType ResolveInternedType(int index, int depth)
    {
        if (index < 0 || index >= _internedTypes.Count)
            throw InternedTypeIndexOutOfRange(index, _internedTypes.Count);

        var internedType = _internedTypes[index] ?? throw new StructTypeSignal();
        var internedDepth = _internedTypeDepths[index];
        ThrowIfTypeTooDeep(depth + internedDepth);
        return new ConvertedType(internedType, internedDepth);
    }

    private ConvertedType ConvertTypeVarInternal(Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.Types.Var typeVar, int depth)
    {
        var varName = GetString(typeVar.VarInternedStr);
        var baseType = new DamlTypeVar(varName);

        if (typeVar.Args.Count <= 0) return Leaf(baseType);
        var args = typeVar.Args.Select(arg => ConvertTypeInternal(arg, depth + 1)).ToList();
        return Applied(baseType, args);
    }

    private ConvertedType ConvertTypeConInternal(Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.Types.Con typeCon, int depth)
    {
        var packageId = GetImportedPackageId(typeCon.Tycon.Module) ?? _selfPackageId;
        var moduleName = GetDottedName(typeCon.Tycon.Module.ModuleNameInternedDname);
        var typeName = GetDottedName(typeCon.Tycon.NameInternedDname);

        var typeRef = new DamlTypeRef(packageId, moduleName, typeName);

        if (typeCon.Args.Count <= 0) return Leaf(typeRef);
        var args = typeCon.Args.Select(arg => ConvertTypeInternal(arg, depth + 1)).ToList();
        return Applied(typeRef, args);
    }

    private ConvertedType ConvertTypeSynInternal(Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.Types.Syn typeSyn, int depth)
    {
        var packageId = GetImportedPackageId(typeSyn.Tysyn.Module) ?? _selfPackageId;
        var moduleName = GetDottedName(typeSyn.Tysyn.Module.ModuleNameInternedDname);
        var typeName = GetDottedName(typeSyn.Tysyn.NameInternedDname);

        var typeRef = new DamlTypeRef(packageId, moduleName, typeName);

        if (typeSyn.Args.Count <= 0) return Leaf(typeRef);
        var args = typeSyn.Args.Select(arg => ConvertTypeInternal(arg, depth + 1)).ToList();
        return Applied(typeRef, args);
    }

    private ConvertedType ConvertTypeTAppInternal(Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.Types.TApp tApp, int depth)
    {
        var lhs = ConvertTypeInternal(tApp.Lhs, depth + 1);
        var rhs = ConvertTypeInternal(tApp.Rhs, depth + 1);
        var appliedDepth = Math.Max(lhs.Depth, rhs.Depth) + 1;

        if (lhs.Type is not DamlTypeApp app) return new ConvertedType(new DamlTypeApp(lhs.Type, [rhs.Type]), appliedDepth);
        var args = app.Arguments.ToList();
        args.Add(rhs.Type);
        var lhsDepthExcludingItsApplicationNode = lhs.Depth - 1;
        var flattenedDepth = Math.Max(lhsDepthExcludingItsApplicationNode, rhs.Depth) + 1;
        return new ConvertedType(new DamlTypeApp(app.Base, args), flattenedDepth);
    }

    private ConvertedType ConvertTypeBuiltinInternal(Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.Types.Builtin typeBuiltin, int depth)
    {
        var row = DamlPrimitiveCatalog.Get(DamlLfBuiltinBridge.ToProtoBuiltin(typeBuiltin.Builtin_));
        if (row.Disposition == DamlPrimitiveDisposition.Unsupported)
        {
            throw new NotSupportedException(
                $"Daml-LF BuiltinType '{typeBuiltin.Builtin_}' is cataloged as Unsupported — {row.Rationale}");
        }

        var primType = new DamlPrimitiveType(row.Primitive!.Value);

        if (typeBuiltin.Args.Count <= 0) return Leaf(primType);

        var args = typeBuiltin.Args.Select(arg => ConvertTypeInternal(arg, depth + 1)).ToList();
        return Applied(primType, args);
    }

    private static ConvertedType Leaf(DamlType type) => new(type, 0);

    private static ConvertedType Applied(DamlType baseType, List<ConvertedType> args) =>
        new(new DamlTypeApp(baseType, args.Select(arg => arg.Type).ToList()), args.Max(arg => arg.Depth) + 1);

    private static InvalidDataException InternedTypeIndexOutOfRange(int index, int available) =>
        new($"Daml-LF interned-type index {index} is out of range of the interned-type pool of size {available}. The archive is malformed.");

    private static InvalidDataException ModuleReferenceAbsent(string referenceKind) =>
        new($"Daml-LF {referenceKind} reference carries no module reference. The archive is malformed.");

    private static InvalidDataException PackageIdFieldAbsent() =>
        new("Daml-LF module reference carries no package-id field. The archive is malformed.");

    private static InvalidDataException TypeReferenceAbsent() =>
        new("Daml-LF type position carries no type: in proto3 an unset message field deserializes to null, so no type expression was declared at all. The archive is malformed.");

    private static InvalidDataException TypeSumCaseUnset() =>
        new("Daml-LF type has no sum case set: it names no type expression at all. The archive is malformed.");

    private static InvalidDataException PackageIdCaseUnset() =>
        new("Daml-LF module reference has no package-id case set: it names neither this package nor an import. The archive is malformed.");

    private static void ThrowIfTypeTooDeep(int depth)
    {
        if (depth > MaxTypeDepth)
        {
            throw new InvalidDataException(
                $"Daml-LF type conversion exceeded the maximum type depth of {MaxTypeDepth}. The type is too deeply nested to convert safely.");
        }
    }

    private readonly record struct ConvertedType(DamlType Type, int Depth);

    private sealed class StructTypeSignal : Exception;

    private static T FailingOnStruct<T>(Func<T> conversion)
    {
        try
        {
            return conversion();
        }
        catch (StructTypeSignal)
        {
            throw StructTypeNotSupported();
        }
    }

    private static InvalidDataException StructTypeNotSupported() =>
        new("Daml-LF struct type is not supported in a field, parameter or argument position: only the interned-type pool may carry one, and nothing may reference it. The archive is malformed.");
}
