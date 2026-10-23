// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.DigitalAsset.Daml.Lf.Archive.DamlLf;
using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using Google.Protobuf;

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.DarParser;

/// <summary>
/// Reads Daml-LF archive files (DALF format).
/// </summary>
public static class DalfReader
{
    internal const long MaxDalfBytes = 256L * 1024 * 1024;

    private const int PackageIdHexLength = 64;

    private static readonly string[] SupportedLf2Minors = ["1", "2", "3", "4", "5-rc1"];

    /// <summary>
    /// Reads a DALF file from bytes. The returned <see cref="DamlPackage.PackageId"/> is the SHA-256 of
    /// the payload bytes, never the hash the DALF declares for itself. A declared hash is held to the
    /// ascii7 lowercase hex form Daml-LF specifies and then compared exactly: every reader downstream —
    /// this parser's own dependency resolution included — matches package ids ordinally, so tolerating
    /// another form here would accept the archive and then silently fail to resolve its references.
    /// This is the only ingestion path that derives a package id from payload bytes: an intermediate DAR
    /// carries no payload, so its reader checks an id's shape and cannot verify it.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The DALF declares a hash function other than SHA256, declares a non-empty hash that is not
    /// 64 lowercase hexadecimal characters, or declares one that does not match its own payload.
    /// </exception>
    /// <exception cref="NotSupportedException">The DALF is a Daml-LF 1.x archive, or a Daml-LF 2.x archive whose minor version this reader does not support.</exception>
    /// <remarks>
    /// Daml-LF expression trees nest far deeper than protobuf's default recursion limit allows,
    /// so the limit is raised for every parse on this path. Lowering it back makes real archives
    /// fail to parse.
    /// </remarks>
    public static DamlPackage Parse(byte[] data) => ParseObservingCancellation(data, CancellationToken.None);

    internal static DamlPackage ParseObservingCancellation(byte[] data, CancellationToken cancellationToken)
    {
        const int recursionLimit = 1000;

        var archive = ParseWithRecursionLimit<Archive>(data, recursionLimit);

        if (archive.HashFunction != HashFunction.Sha256)
        {
            throw new InvalidDataException(
                $"DALF declares hash function '{archive.HashFunction}', which this reader does not implement; " +
                "only SHA256 is recognized.");
        }

        var payloadBytes = archive.Payload.ToByteArray();
        var packageId = ComputePackageId(payloadBytes);
        if (!string.IsNullOrEmpty(archive.Hash))
        {
            if (!IsPackageIdShaped(archive.Hash))
            {
                throw new InvalidDataException(
                    $"DALF declares package id '{archive.Hash}', which is not the {PackageIdHexLength} lowercase " +
                    "hexadecimal characters Daml-LF specifies for a package id. Package references are resolved by " +
                    "exact match, so a package id in any other form parses here and then silently fails to resolve.");
            }

            if (!archive.Hash.Equals(packageId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"DALF declares package id '{archive.Hash}' but its payload hashes to '{packageId}'. " +
                    "A package id is the hash of the payload bytes, so a well-formed DALF declares the hash its own bytes produce.");
            }
        }

        var payload = ParseWithRecursionLimit<ArchivePayload>(payloadBytes, recursionLimit);
        var lfVersion = $"2.{payload.Minor}";

        Package package;
        if (payload.DamlLf2.Length > 0)
        {
            RejectUnsupportedLf2Minor(payload.Minor);
            package = ParseWithRecursionLimit<Package>(payload.DamlLf2.ToByteArray(), recursionLimit);
        }
        else if (payload.DamlLf1.Length > 0)
        {
            throw new NotSupportedException("Daml-LF 1.x archives are not supported here; set <DamlCodegenMode>Dpm</DamlCodegenMode> in your project to decode them with `dpm codegen-cs`.");
        }
        else
        {
            throw new InvalidDataException("DALF ArchivePayload missing Package data");
        }

        return ConvertPackage(package, packageId, lfVersion, cancellationToken);
    }

    private static void RejectUnsupportedLf2Minor(string minor)
    {
        if (SupportedLf2Minors.Contains(minor))
        {
            return;
        }

        throw new NotSupportedException(
            $"Daml-LF 2.{minor} archives are not supported here; " +
            $"the supported minors are {string.Join(", ", SupportedLf2Minors.Select(supported => $"2.{supported}"))}; set <DamlCodegenMode>Dpm</DamlCodegenMode> in your project to decode other versions with `dpm codegen-cs`.");
    }

    private static T ParseWithRecursionLimit<T>(byte[] data, int recursionLimit) where T : IMessage<T>, new()
    {
        using var stream = new MemoryStream(data);
        var codedInput = CodedInputStream.CreateWithLimits(stream, checked((int)MaxDalfBytes), recursionLimit);
        var message = new T();
        try
        {
            message.MergeFrom(codedInput);
        }
        catch (InvalidProtocolBufferException ex)
        {
            throw new InvalidDataException($"Malformed Daml-LF {typeof(T).Name} data: {ex.Message}", ex);
        }
        return message;
    }

    private static DamlPackage ConvertPackage(
        Package package, string packageId, string lfVersion, CancellationToken cancellationToken)
    {
        var metadata = package.Metadata ?? throw new InvalidDataException(
            "Daml-LF package carries no package metadata, so it names neither a package name nor a version. The archive is malformed.");
        var ctx = new PackageContext(package, packageId);

        var modules = package.Modules.Select(m =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ConvertModule(m, ctx);
        }).ToList();

        var packageName = ctx.GetString(metadata.NameInternedStr);

        var versionStr = ctx.GetString(metadata.VersionInternedStr);

        var version = PackageVersionParser.Parse(versionStr);

        var dependencyRefs = ExtractDependencyReferences(package, ctx);

        string? upgradedPackageId = null;
        if (metadata.UpgradedPackageId != null)
        {
            upgradedPackageId = ctx.GetString(metadata.UpgradedPackageId.UpgradedPackageIdInternedStr);
        }

        return new DamlPackage
        {
            PackageId = packageId,
            Name = packageName,
            Version = version,
            RawVersion = versionStr,
            LfVersion = lfVersion,
            Modules = modules,
            DependencyReferences = dependencyRefs,
            UpgradedPackageId = upgradedPackageId
        };
    }

    /// <remarks>
    /// Two sources are unioned. <c>PackageImports</c> only exists from Daml-LF 2.2 on, so the
    /// interned strings referenced by type definitions are scanned as well — that second sweep is
    /// the only source of dependencies for the LF versions that predate the import table.
    /// </remarks>
    private static List<DamlPackageReference> ExtractDependencyReferences(Package package, PackageContext ctx)
    {
        var dependencies = new HashSet<string>();

        if (package.ImportsSumCase == Package.ImportsSumOneofCase.PackageImports)
        {
            foreach (var importedPackageId in package.PackageImports.ImportedPackages)
            {
                if (!string.IsNullOrEmpty(importedPackageId))
                {
                    dependencies.Add(importedPackageId);
                }
            }
        }

        foreach (var module in package.Modules)
        {
            CollectDependenciesFromModule(module, ctx, dependencies);
        }

        return dependencies
            .Select(pkgId => new DamlPackageReference { PackageId = pkgId })
            .ToList();
    }

    private static void CollectDependenciesFromModule(Module module, PackageContext ctx, HashSet<string> dependencies)
    {
        foreach (var dataType in module.DataTypes)
        {
            CollectDependenciesFromDataType(dataType, ctx, dependencies);
        }

        foreach (var template in module.Templates)
        {
            CollectDependenciesFromTemplate(template, ctx, dependencies);
        }

        foreach (var iface in module.Interfaces)
        {
            CollectDependenciesFromInterface(iface, ctx, dependencies);
        }
    }

    private static void CollectDependenciesFromDataType(DefDataType dataType, PackageContext ctx, HashSet<string> dependencies)
    {
        switch (dataType.DataConsCase)
        {
            case DefDataType.DataConsOneofCase.Record:
                foreach (var field in dataType.Record.Fields_)
                {
                    ctx.CollectTypeDependencies(field.Type, dependencies);
                }
                break;
            case DefDataType.DataConsOneofCase.Variant:
                foreach (var field in dataType.Variant.Fields_)
                {
                    ctx.CollectTypeDependencies(field.Type, dependencies);
                }
                break;
        }
    }

    private static void CollectDependenciesFromTemplate(DefTemplate template, PackageContext ctx, HashSet<string> dependencies)
    {
        if (template.Key != null)
        {
            ctx.CollectTypeDependencies(template.Key.Type, dependencies);
        }

        foreach (var choice in template.Choices)
        {
            if (choice.ArgBinder != null)
            {
                ctx.CollectTypeDependencies(choice.ArgBinder.Type, dependencies);
            }
            if (choice.RetType != null)
            {
                ctx.CollectTypeDependencies(choice.RetType, dependencies);
            }
        }

        foreach (var impl in template.Implements)
        {
            var pkgId = ctx.GetImportedPackageId(impl.Interface);
            if (!string.IsNullOrEmpty(pkgId))
            {
                dependencies.Add(pkgId);
            }
        }
    }

    private static void CollectDependenciesFromInterface(DefInterface iface, PackageContext ctx, HashSet<string> dependencies)
    {
        if (iface.View != null)
        {
            ctx.CollectTypeDependencies(iface.View, dependencies);
        }

        foreach (var method in iface.Methods)
        {
            ctx.CollectTypeDependencies(method.Type, dependencies);
        }

        foreach (var choice in iface.Choices)
        {
            if (choice.ArgBinder != null)
            {
                ctx.CollectTypeDependencies(choice.ArgBinder.Type, dependencies);
            }
            if (choice.RetType != null)
            {
                ctx.CollectTypeDependencies(choice.RetType, dependencies);
            }
        }
    }

    private static DamlModule ConvertModule(Module module, PackageContext ctx)
    {
        var moduleName = ctx.GetDottedName(module.NameInternedDname);

        var dataTypes = module.DataTypes
            .Where(dt => !IsStrippedAsNonSerializable(dt))
            .Select(dt => ConvertDataType(dt, moduleName, ctx))
            .ToList();
        var templates = module.Templates.Select(t => ConvertTemplate(t, ctx)).ToList();
        var interfaces = module.Interfaces.Select(i => ConvertInterface(i, ctx)).ToList();

        return new DamlModule
        {
            Name = moduleName,
            DataTypes = dataTypes,
            Templates = templates,
            Interfaces = interfaces
        };
    }

    private static bool IsStrippedAsNonSerializable(DefDataType defDataType) =>
        !defDataType.Serializable
        && defDataType.DataConsCase is DefDataType.DataConsOneofCase.Record
            or DefDataType.DataConsOneofCase.Variant
            or DefDataType.DataConsOneofCase.Enum;

    private static DamlDataType ConvertDataType(DefDataType defDataType, string moduleName, PackageContext ctx)
    {
        var name = ctx.GetDottedName(defDataType.NameInternedDname);
        var typeParams = defDataType.Params.Select(p => ctx.GetString(p.VarInternedStr)).ToList();

        DamlDataTypeDefinition definition = defDataType.DataConsCase switch
        {
            DefDataType.DataConsOneofCase.Record => new DamlRecordDefinition(
                defDataType.Record.Fields_.Select(f => ConvertField(f, ctx)).ToList()),
            DefDataType.DataConsOneofCase.Variant => new DamlVariantDefinition(
                defDataType.Variant.Fields_.Select(f =>
                    new DamlVariantConstructor(ctx.GetString(f.FieldInternedStr), ctx.ConvertType(f.Type))).ToList()),
            DefDataType.DataConsOneofCase.Enum => new DamlEnumDefinition(
                defDataType.Enum.ConstructorsInternedStr.Select(ctx.GetString).ToList()),
            DefDataType.DataConsOneofCase.Interface => new DamlRecordDefinition([]),
            _ => throw new InvalidDataException(
                $"Daml-LF data type '{moduleName}.{name}' declares data_cons case '{defDataType.DataConsCase}', " +
                "which names no record, variant, enum or interface shape. Reading it as a record with no fields " +
                "would drop every field the type declares from the generated bindings and from JSON serialization.")
        };

        return new DamlDataType
        {
            Name = name,
            TypeParams = typeParams,
            Serializable = defDataType.Serializable,
            Definition = definition
        };
    }

    private static DamlFieldDefinition ConvertField(FieldWithType field, PackageContext ctx)
    {
        return new DamlFieldDefinition(
            ctx.GetString(field.FieldInternedStr),
            ctx.ConvertType(field.Type));
    }

    /// <remarks>
    /// The emitted template carries no fields: they come from the module's matching data-type
    /// definition, which is converted separately.
    /// <para>
    /// The template's own parameter binder is captured up front and threaded into every choice:
    /// the party analyzer needs it to tell payload-derived parties from argument-derived ones.
    /// Signatories and observers carry the same <c>List Party</c> shape and go through the same
    /// analyzer; a statically resolvable observer list feeds both the documentation helper on
    /// <c>SubmissionExtensions</c> and the <c>readAs</c> contribution on emitted async wrappers.
    /// </para>
    /// </remarks>
    private static DamlTemplate ConvertTemplate(DefTemplate defTemplate, PackageContext ctx)
    {
        var name = ctx.GetDottedName(defTemplate.TyconInternedDname);

        var templateParamStr = defTemplate.ParamInternedStr;

        var choices = defTemplate.Choices
            .Select(c => ConvertChoice(c, templateParamStr, ctx))
            .OrderBy(choice => choice.Name, StringComparer.Ordinal)
            .ToList();

        DamlType? keyType = null;
        if (defTemplate.Key != null)
        {
            keyType = ctx.ConvertType(defTemplate.Key.Type);
        }

        var implements = defTemplate.Implements
            .Select(i => ConvertTypeConIdToRef(i.Interface, ctx))
            .OrderBy(reference => reference.PackageId, StringComparer.Ordinal)
            .ThenBy(reference => reference.Module, StringComparer.Ordinal)
            .ThenBy(reference => reference.Name, StringComparer.Ordinal)
            .ToList();

        var signatories = PartyExpressionAnalyzer.Analyze(
            RequireClause(defTemplate.Signatories, "signatory"), templateParamStr, ctx);

        var observers = PartyExpressionAnalyzer.Analyze(
            RequireClause(defTemplate.Observers, "observer"), templateParamStr, ctx);

        return new DamlTemplate
        {
            Name = name,
            Choices = choices,
            Key = keyType,
            Implements = implements,
            Signatories = signatories,
            Observers = observers,
        };
    }

    /// <remarks>
    /// A choice body has two binders in scope: the template parameter, so that
    /// <c>controller someTemplateField</c> projects through the payload, and the choice
    /// argument's own binder, so that <c>controller arg.requester</c> projects through the
    /// argument record. Only the payload-derived case resolves here; everything else falls
    /// through to Dynamic and codegen emits an explicit submitter parameter. A choice's optional
    /// <c>observer</c> clause carries the same <c>List Party</c> shape, so the analyzer applies
    /// unchanged.
    /// </remarks>
    private static DamlChoice ConvertChoice(TemplateChoice choice, int templateParamStr, PackageContext ctx)
    {
        var name = ctx.GetString(choice.NameInternedStr);

        var controllers = PartyExpressionAnalyzer.Analyze(
            RequireClause(choice.Controllers, "controller"), templateParamStr, ctx);

        var observers = PartyExpressionAnalyzer.Analyze(
            RequireClause(choice.Observers, "observer"), templateParamStr, ctx);

        return BuildChoice(choice, name, ctx, controllers, observers);
    }

    private static DamlChoice ConvertInterfaceChoice(TemplateChoice choice, PackageContext ctx)
    {
        RequireClause(choice.Controllers, "controller");
        RequireClause(choice.Observers, "observer");

        return BuildChoice(
            choice,
            ctx.GetString(choice.NameInternedStr),
            ctx,
            DamlPartyAnalysis.Dynamic,
            DamlPartyAnalysis.Dynamic);
    }

    private static Expr RequireClause(Expr? clause, string clauseName)
    {
        if (clause is null || clause.SumCase == Expr.SumOneofCase.None)
        {
            throw new InvalidDataException(
                $"Daml-LF {clauseName} clause declares no expression. Every template and choice declares one, and reading none as an unresolvable party list would hide the malformed archive. The archive is malformed.");
        }

        return clause;
    }

    private static DamlChoice BuildChoice(
        TemplateChoice choice,
        string name,
        PackageContext ctx,
        DamlPartyAnalysis controllers,
        DamlPartyAnalysis observers)
    {
        return new DamlChoice
        {
            Name = name,
            Consuming = choice.Consuming,
            ArgumentType = choice.ArgBinder != null
                ? ctx.ConvertType(choice.ArgBinder.Type)
                : new DamlPrimitiveType(DamlPrimitive.Unit),
            ReturnType = choice.RetType != null
                ? ctx.ConvertType(choice.RetType)
                : new DamlPrimitiveType(DamlPrimitive.Unit),
            Controllers = controllers,
            Observers = observers,
        };
    }

    /// <remarks>
    /// Interface choices carry Dynamic controllers and observers, as the JVM helper writes them:
    /// a choice reached through an interface has no template payload to resolve a party against.
    /// </remarks>
    private static DamlInterface ConvertInterface(DefInterface defInterface, PackageContext ctx)
    {
        var name = ctx.GetDottedName(defInterface.TyconInternedDname);

        var choices = defInterface.Choices
            .Select(c => ConvertInterfaceChoice(c, ctx))
            .OrderBy(choice => choice.Name, StringComparer.Ordinal)
            .ToList();

        var methods = defInterface.Methods
            .Select(m => new DamlInterfaceMethod(ctx.GetString(m.MethodInternedName), ctx.ConvertType(m.Type)))
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ToList();

        DamlType? viewType = null;
        if (defInterface.View != null)
        {
            viewType = ctx.ConvertType(defInterface.View);
        }

        return new DamlInterface
        {
            Name = name,
            Methods = methods,
            Choices = choices,
            ViewType = viewType
        };
    }

    private static DamlTypeRef ConvertTypeConIdToRef(TypeConId typeConId, PackageContext ctx)
    {
        var packageId = ctx.GetReferencedPackageId(typeConId);
        var moduleName = ctx.GetDottedName(typeConId.Module.ModuleNameInternedDname);
        var typeName = ctx.GetDottedName(typeConId.NameInternedDname);

        return new DamlTypeRef(packageId, moduleName, typeName);
    }

    private static bool IsPackageIdShaped(string declaredHash) =>
        declaredHash.Length == PackageIdHexLength
        && declaredHash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string ComputePackageId(byte[] data)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();
    }
}
