// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Daml.Codegen.Intermediate.Model;
using PbBuiltin = Daml.Codegen.Intermediate.BuiltinType;
using PbChoice = Daml.Codegen.Intermediate.Choice;
using PbDar = Daml.Codegen.Intermediate.IntermediateDar;
using PbDataType = Daml.Codegen.Intermediate.DataType;
using PbField = Daml.Codegen.Intermediate.Field;
using PbInterface = Daml.Codegen.Intermediate.Interface;
using PbInterfaceMethod = Daml.Codegen.Intermediate.InterfaceMethod;
using PbModule = Daml.Codegen.Intermediate.IntermediateModule;
using PbPackage = Daml.Codegen.Intermediate.IntermediatePackage;
using PbPartyAnalysis = Daml.Codegen.Intermediate.PartyAnalysis;
using PbTemplate = Daml.Codegen.Intermediate.Template;
using PbType = Daml.Codegen.Intermediate.Type;
using PbTypeConName = Daml.Codegen.Intermediate.TypeConName;

namespace Daml.Codegen.CSharp;

/// <summary>
/// Maps a <see cref="PbDar"/> protobuf message (produced by the JVM helper
/// bundled in <c>dpm codegen-cs</c>, against the schema in
/// <c>proto/intermediate_dar.proto</c>) into the emitter's in-memory
/// <see cref="DarModel"/>. Static party analysis
/// (<c>signatories</c>, <c>observers</c>, <c>controllers</c>,
/// <c>choiceObservers</c>) is preserved through the proto when the JVM
/// helper runs in its default full-decode mode. When the helper runs with
/// <c>--schema-only</c>, the proto carries <see cref="DamlPartyAnalysis.Dynamic"/>
/// on every template and choice and the typed-<c>actAs</c> codegen path
/// falls back to an explicit <c>SubmitterInfo</c> parameter.
/// </summary>
public static partial class IntermediateDarReader
{
    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_$]*\z")]
    private static partial Regex IdentifierGrammar();

    [GeneratedRegex(@"\A[A-Za-z0-9._-]+\z")]
    private static partial Regex PackageCoordinateGrammar();

    private static string RequireIdentifier(string value, string kind)
    {
        if (!IdentifierGrammar().IsMatch(value))
        {
            throw new InvalidDataException(
                $"IntermediateDar {kind} '{value}' is not a valid Daml-LF identifier " +
                "([A-Za-z_][A-Za-z0-9_$]*). Names outside the grammar are rejected because the " +
                "emitter interpolates them into generated C# source and string literals.");
        }
        return value;
    }

    private static string JoinValidatedSegments(IEnumerable<string> segments, string kind) =>
        string.Join(".", segments.Select(segment => RequireIdentifier(segment, kind)));

    private static string RequireDottedIdentifier(string value, string kind)
    {
        if (value.Length == 0 || !value.Split('.').All(segment => IdentifierGrammar().IsMatch(segment)))
        {
            throw new InvalidDataException(
                $"IntermediateDar {kind} '{value}' is not a valid dotted Daml-LF identifier " +
                "(period-separated [A-Za-z_][A-Za-z0-9_$]* segments). Names outside the grammar are rejected " +
                "because the emitter interpolates them into generated C# source and string literals.");
        }
        return value;
    }

    private static string RequirePackageCoordinate(string value, string kind)
    {
        if (!PackageCoordinateGrammar().IsMatch(value))
        {
            throw new InvalidDataException(
                $"IntermediateDar {kind} '{value}' is not a valid package coordinate " +
                "(non-empty [A-Za-z0-9._-]+). Values outside the grammar are rejected because the " +
                "emitter interpolates them into generated C# source and string literals.");
        }
        return value;
    }

    /// <summary>
    /// Converts the proto graph into a <see cref="DarModel"/>; requires
    /// <c>proto.Main</c> to be set and throws <see cref="InvalidDataException"/> otherwise.
    /// </summary>
    public static DarModel Read(PbDar proto)
    {
        ArgumentNullException.ThrowIfNull(proto);
        if (proto.Main is null)
            throw new InvalidDataException("IntermediateDar.main is required");

        var main = ConvertPackage(proto.Main);
        var deps = proto.Dependencies.Select(ConvertPackage).ToList();

        return new DarModel { MainPackage = main, Dependencies = deps };
    }

    private static DamlPackage ConvertPackage(PbPackage pkg) =>
        new()
        {
            PackageId = RequirePackageCoordinate(pkg.PackageId, "package id"),
            Name = RequirePackageCoordinate(pkg.PackageName, "package name"),
            Version = ParseVersion(pkg.PackageVersion),
            LfVersion = pkg.LanguageVersion,
            Modules = pkg.Modules.Select(ConvertModule).ToList(),
            DependencyReferences = [],
            UpgradedPackageId = string.IsNullOrEmpty(pkg.UpgradedPackageId)
                ? null
                : RequirePackageCoordinate(pkg.UpgradedPackageId, "upgraded package id"),
        };

    private static DamlModule ConvertModule(PbModule module)
    {
        var dataTypes = module.DataTypes.Select(ConvertDataType).ToList();
        var templates = module.Templates.Select(ConvertTemplate).ToList();
        var interfaces = module.Interfaces.Select(ConvertInterface).ToList();

        return new DamlModule
        {
            Name = JoinValidatedSegments(module.NameSegments, "module name segment"),
            Templates = templates,
            DataTypes = dataTypes,
            Interfaces = interfaces,
        };
    }

    private static DamlDataType ConvertDataType(PbDataType dt)
    {
        DamlDataTypeDefinition definition = dt.ShapeCase switch
        {
            PbDataType.ShapeOneofCase.Record =>
                new DamlRecordDefinition(dt.Record.Fields.Select(ConvertField).ToList()),
            PbDataType.ShapeOneofCase.Variant =>
                new DamlVariantDefinition(dt.Variant.Constructors.Select(ConvertVariantConstructor).ToList()),
            PbDataType.ShapeOneofCase.EnumType =>
                new DamlEnumDefinition(dt.EnumType.Constructors
                    .Select(ctor => RequireIdentifier(ctor, "enum constructor"))
                    .ToList()),
            PbDataType.ShapeOneofCase.None => throw new InvalidDataException(
                $"IntermediateDar DataType '{dt.Name}' has no shape set — every DataType must populate exactly one of {{record, variant, enum_type}}."),
            _ => throw new InvalidDataException(
                $"IntermediateDar DataType '{dt.Name}' has unknown shape '{dt.ShapeCase}'. " +
                "Update IntermediateDarReader.ConvertDataType to handle the new shape, alongside the proto schema and the JVM helper."),
        };

        return new DamlDataType
        {
            Name = RequireDottedIdentifier(dt.Name, "DataType name"),
            TypeParams = dt.TypeParameters.Select(tp => RequireIdentifier(tp, "type parameter")).ToList(),
            Serializable = dt.IsSerializable,
            Definition = definition,
        };
    }

    private static DamlFieldDefinition ConvertField(PbField field)
    {
        if (field.Type is null)
            throw new InvalidDataException(
                $"IntermediateDar Field '{field.Name}' is missing type — every field must declare its type.");
        return new DamlFieldDefinition(RequireIdentifier(field.Name, "field name"), ConvertType(field.Type));
    }

    private static DamlVariantConstructor ConvertVariantConstructor(PbField field)
    {
        if (field.Type is null)
            throw new InvalidDataException(
                $"IntermediateDar Variant constructor '{field.Name}' is missing type — every constructor must declare its argument type (use BuiltinType.UNIT for a no-arg constructor).");
        return new DamlVariantConstructor(RequireIdentifier(field.Name, "variant constructor"), ConvertType(field.Type));
    }

    private static DamlTemplate ConvertTemplate(PbTemplate template)
    {
        return new DamlTemplate
        {
            Name = RequireDottedIdentifier(template.Name, "template name"),
            Choices = template.Choices.Select(ConvertChoice).ToList(),
            Key = template.KeyType is not null ? ConvertType(template.KeyType) : null,
            Implements = template.Implements.Select(ConvertTypeConRef).ToList(),
            Signatories = ConvertPartyAnalysis(template.Signatories),
            Observers = ConvertPartyAnalysis(template.Observers),
        };
    }

    private static DamlChoice ConvertChoice(PbChoice choice)
    {
        if (choice.ArgumentType is null)
            throw new InvalidDataException(
                $"IntermediateDar Choice '{choice.Name}' is missing argument_type — every choice must declare its argument type (use BuiltinType.UNIT for a no-arg choice).");
        if (choice.ReturnType is null)
            throw new InvalidDataException(
                $"IntermediateDar Choice '{choice.Name}' is missing return_type — every choice must declare its return type (use BuiltinType.UNIT for a no-result choice).");

        return new DamlChoice
        {
            Name = RequireIdentifier(choice.Name, "choice name"),
            Consuming = choice.Consuming,
            ArgumentType = ConvertType(choice.ArgumentType),
            ReturnType = ConvertType(choice.ReturnType),
            Controllers = ConvertPartyAnalysis(choice.Controllers),
            Observers = ConvertPartyAnalysis(choice.Observers),
        };
    }

    /// <summary>
    /// Maps a proto <see cref="PbPartyAnalysis"/> into the model
    /// <see cref="DamlPartyAnalysis"/>. An absent or unset analysis (the
    /// proto field defaults to none on the wire when the helper ran in
    /// <c>--schema-only</c> mode) is read as <see cref="DamlPartyAnalysis.Dynamic"/>.
    /// <para>
    /// Interface-choice party analysis is currently always <c>Dynamic</c>:
    /// the JVM helper computes static party analysis for template choices
    /// only and reports every interface choice as dynamic. Adding
    /// typed-<c>actAs</c> derivation for interface choices is a separate
    /// follow-up.
    /// </para>
    /// </summary>
    private static DamlPartyAnalysis ConvertPartyAnalysis(PbPartyAnalysis? analysis)
    {
        if (analysis is null)
            return DamlPartyAnalysis.Dynamic;

        return analysis.ShapeCase switch
        {
            PbPartyAnalysis.ShapeOneofCase.Static =>
                DamlPartyAnalysis.Static(analysis.Static.PayloadFields
                    .Select(field => (DamlPartyReference)new DamlPartyPayloadField(
                        RequireIdentifier(field, "party payload field")))
                    .ToList()),
            PbPartyAnalysis.ShapeOneofCase.Dynamic => DamlPartyAnalysis.Dynamic,
            PbPartyAnalysis.ShapeOneofCase.None => DamlPartyAnalysis.Dynamic,
            _ => throw new InvalidDataException(
                $"IntermediateDar PartyAnalysis has unknown shape '{analysis.ShapeCase}'. " +
                "Update IntermediateDarReader.ConvertPartyAnalysis to handle the new shape, alongside the proto schema and the JVM helper."),
        };
    }

    private static DamlInterface ConvertInterface(PbInterface iface) =>
        new()
        {
            Name = RequireDottedIdentifier(iface.Name, "interface name"),
            Methods = iface.Methods.Select(ConvertInterfaceMethod).ToList(),
            Choices = iface.Choices.Select(ConvertChoice).ToList(),
            ViewType = iface.ViewType is not null ? ConvertType(iface.ViewType) : null,
        };

    /// <summary>
    /// Maps a proto <c>InterfaceMethod</c> entry into the model, preserving the
    /// wire order of the enclosing list (callers convert the repeated field in
    /// sequence). An interface produced without the <c>methods</c> field never
    /// reaches here — proto3 repeated-field absence reads as an empty list, which
    /// the model's default mirrors.
    /// </summary>
    private static DamlInterfaceMethod ConvertInterfaceMethod(PbInterfaceMethod method)
    {
        if (method.ReturnType is null)
            throw new InvalidDataException(
                $"IntermediateDar InterfaceMethod '{method.Name}' is missing return_type — every interface method must declare its return type.");
        return new DamlInterfaceMethod(
            RequireIdentifier(method.Name, "interface method name"),
            ConvertType(method.ReturnType));
    }

    /// <summary>
    /// Converts a complete type expression — the type of every field, variant constructor,
    /// choice argument and result, interface view, and interface method. The raw conversion
    /// keeps every application generic (flattening curried ones), then
    /// <see cref="AppliedTypeFolding.FoldComplete"/> folds the finished tree's applications
    /// of the five folding builtins into their typed nodes with the catalog arity as the
    /// oracle: folding at the complete-type boundary is what lets a curried chain accumulate
    /// its arguments before the arity is checked.
    /// </summary>
    private static DamlType ConvertType(PbType type) =>
        AppliedTypeFolding.FoldComplete(ConvertTypeUnresolved(type));

    private static DamlType ConvertTypeUnresolved(PbType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return type.SortCase switch
        {
            PbType.SortOneofCase.Builtin => new DamlPrimitiveType(ConvertBuiltin(type.Builtin)),
            PbType.SortOneofCase.TypeCon => ConvertTypeConRef(type.TypeCon),
            PbType.SortOneofCase.TypeApp => ConvertTypeApp(type),
            PbType.SortOneofCase.TypeVar => new DamlTypeVar(RequireIdentifier(type.TypeVar, "type variable")),
            PbType.SortOneofCase.Nat => new DamlTypeVar(type.Nat.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            PbType.SortOneofCase.None => throw new InvalidDataException(
                "IntermediateDar Type has no sort set — every Type must populate exactly one of {builtin, type_con, type_app, type_var, nat}."),
            _ => throw new InvalidDataException(
                $"IntermediateDar Type has unknown sort '{type.SortCase}'. " +
                "Update IntermediateDarReader.ConvertType to handle the new sort, alongside the proto schema and the JVM helper."),
        };
    }

    private static DamlTypeApp ConvertTypeApp(PbType type)
    {
        if (type.TypeApp.Function is null)
            throw new InvalidDataException(
                "IntermediateDar TypeApp is missing function — every type application must declare the head type being applied.");
        var head = ConvertTypeUnresolved(type.TypeApp.Function);
        var args = type.TypeApp.Arguments.Select(arg =>
        {
            if (arg is null)
                throw new InvalidDataException(
                    "IntermediateDar TypeApp has a null entry in arguments — every type-app argument must be a populated Type.");
            return ConvertTypeUnresolved(arg);
        }).ToList();
        if (head is DamlTypeApp curried)
        {
            return new DamlTypeApp(
                curried.Base,
                [.. curried.Arguments, .. args]);
        }
        return new DamlTypeApp(head, args);
    }

    private static string RequirePackageCoordinateOrSamePackageMarker(string packageId, string kind) =>
        packageId.Length == 0 ? packageId : RequirePackageCoordinate(packageId, kind);

    private static DamlTypeRef ConvertTypeConRef(PbTypeConName tcn) =>
        new(
            RequirePackageCoordinateOrSamePackageMarker(tcn.PackageId, "type-constructor package id"),
            JoinValidatedSegments(tcn.ModuleNameSegments, "type-constructor module segment"),
            JoinValidatedSegments(tcn.NameSegments, "type-constructor name segment"));

    /// <summary>
    /// Resolves a proto <see cref="PbBuiltin"/> through the
    /// <see cref="DamlPrimitiveCatalog"/>: <see cref="DamlPrimitiveDisposition.SupportedValue"/>
    /// and <see cref="DamlPrimitiveDisposition.SignatureOnly"/> rows resolve to their real
    /// <see cref="DamlPrimitive"/> identity, and <see cref="DamlPrimitiveDisposition.Unsupported"/>
    /// rows throw — the reader has no mapping of its own and no silent fall-through for any
    /// proto value.
    /// </summary>
    private static DamlPrimitive ConvertBuiltin(PbBuiltin builtin)
    {
        if (!System.Enum.IsDefined(builtin))
            throw new InvalidDataException(
                $"IntermediateDar BuiltinType '{builtin}' is not a defined proto BuiltinType value — " +
                "the intermediate was produced against a newer proto schema than this reader understands. " +
                "Add the value's row in DamlPrimitiveCatalog alongside the proto schema and the JVM helper.");
        if (builtin == PbBuiltin.Unspecified)
            throw new InvalidDataException(
                "IntermediateDar BuiltinType is BUILTIN_TYPE_UNSPECIFIED — every BuiltinType must be set to a defined value by the producer.");

        var row = DamlPrimitiveCatalog.Get(builtin);
        return row.Disposition switch
        {
            DamlPrimitiveDisposition.SupportedValue or DamlPrimitiveDisposition.SignatureOnly =>
                row.Primitive!.Value,
            _ => throw new NotSupportedException(
                $"IntermediateDar BuiltinType '{builtin}' is cataloged as Unsupported — {row.Rationale}"),
        };
    }

    private static Version ParseVersion(string raw) =>
        PackageVersionParser.Parse(raw);
}
