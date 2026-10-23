// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Globalization;
using Daml.Codegen.Intermediate.Model;
using PbBuiltin = Daml.Codegen.Intermediate.BuiltinType;
using PbChoice = Daml.Codegen.Intermediate.Choice;
using PbDar = Daml.Codegen.Intermediate.IntermediateDar;
using PbDataType = Daml.Codegen.Intermediate.DataType;
using PbDynamicParties = Daml.Codegen.Intermediate.DynamicParties;
using PbEnum = Daml.Codegen.Intermediate.Enum;
using PbField = Daml.Codegen.Intermediate.Field;
using PbInterface = Daml.Codegen.Intermediate.Interface;
using PbInterfaceMethod = Daml.Codegen.Intermediate.InterfaceMethod;
using PbModule = Daml.Codegen.Intermediate.IntermediateModule;
using PbPackage = Daml.Codegen.Intermediate.IntermediatePackage;
using PbPartyAnalysis = Daml.Codegen.Intermediate.PartyAnalysis;
using PbRecord = Daml.Codegen.Intermediate.Record;
using PbStaticParties = Daml.Codegen.Intermediate.StaticParties;
using PbTemplate = Daml.Codegen.Intermediate.Template;
using PbType = Daml.Codegen.Intermediate.Type;
using PbTypeApp = Daml.Codegen.Intermediate.TypeApp;
using PbTypeConName = Daml.Codegen.Intermediate.TypeConName;
using PbVariant = Daml.Codegen.Intermediate.Variant;

namespace Daml.Codegen.DarParser;

/// <summary>
/// Pure-.NET producer of the <c>IntermediateDar</c> protobuf
/// (<c>proto/intermediate_dar.proto</c>) from a parsed
/// <see cref="IDarSource"/>. This is the inverse of
/// <c>Daml.Codegen.CSharp.IntermediateDarReader.Read</c>: emitting a
/// <see cref="PbDar"/> with this writer and reading it back yields a
/// structurally identical model.
/// <para>
/// It is the pure-.NET <c>dar → intermediate.binpb</c> path — a JDK-free
/// alternative to the JVM helper. The deterministic
/// ordering invariants documented on the proto schema (dependencies by
/// package id; modules, data types, templates, interfaces and choices by
/// name) are reproduced here so the output is byte-stable across runs.
/// </para>
/// </summary>
public static class IntermediateDarWriter
{
    /// <summary>
    /// Converts a parsed DAR into the <c>IntermediateDar</c> proto graph.
    /// </summary>
    public static PbDar Write(IDarSource dar)
    {
        ArgumentNullException.ThrowIfNull(dar);

        var proto = new PbDar { Main = WritePackage(dar.MainPackage) };
        proto.Dependencies.AddRange(dar.Dependencies
            .Select(WritePackage)
            .OrderBy(p => p.PackageId, StringComparer.Ordinal));
        return proto;
    }

    private static PbPackage WritePackage(DamlPackage package)
    {
        var proto = new PbPackage
        {
            PackageId = package.PackageId,
            PackageName = package.Name,
            PackageVersion = package.RawVersion ?? FormatVersion(package.Version),
            LanguageVersion = package.LfVersion,
            UpgradedPackageId = package.UpgradedPackageId ?? string.Empty,
        };
        proto.Modules.AddRange(package.Modules
            .Select(WriteModule)
            .OrderBy(m => string.Join(".", m.NameSegments), StringComparer.Ordinal));
        return proto;
    }

    private static PbModule WriteModule(DamlModule module)
    {
        var proto = new PbModule();
        proto.NameSegments.AddRange(module.Name.Split('.'));
        proto.DataTypes.AddRange(module.DataTypes
            .Select(WriteDataType)
            .OrderBy(dt => dt.Name, StringComparer.Ordinal));
        proto.Templates.AddRange(module.Templates
            .Select(WriteTemplate)
            .OrderBy(t => t.Name, StringComparer.Ordinal));
        proto.Interfaces.AddRange(module.Interfaces
            .Select(WriteInterface)
            .OrderBy(i => i.Name, StringComparer.Ordinal));
        return proto;
    }

    private static PbDataType WriteDataType(DamlDataType dataType)
    {
        var proto = new PbDataType
        {
            Name = dataType.Name,
            IsSerializable = dataType.Serializable,
        };
        proto.TypeParameters.AddRange(dataType.TypeParams);

        switch (dataType.Definition)
        {
            case DamlRecordDefinition record:
                proto.Record = WriteRecord(record);
                break;
            case DamlVariantDefinition variant:
                proto.Variant = WriteVariant(variant);
                break;
            case DamlEnumDefinition @enum:
                proto.EnumType = WriteEnum(@enum);
                break;
            default:
                throw new NotSupportedException(
                    $"DataType '{dataType.Name}' has an unrecognised definition shape " +
                    $"'{dataType.Definition.GetType().Name}'. Add the mapping in " +
                    "IntermediateDarWriter.WriteDataType alongside the proto schema.");
        }

        return proto;
    }

    private static PbRecord WriteRecord(DamlRecordDefinition record)
    {
        var proto = new PbRecord();
        proto.Fields.AddRange(record.Fields.Select(WriteField));
        return proto;
    }

    private static PbVariant WriteVariant(DamlVariantDefinition variant)
    {
        var proto = new PbVariant();
        proto.Constructors.AddRange(variant.Constructors.Select(WriteVariantConstructor));
        return proto;
    }

    private static PbEnum WriteEnum(DamlEnumDefinition @enum)
    {
        var proto = new PbEnum();
        proto.Constructors.AddRange(@enum.Constructors);
        return proto;
    }

    private static PbField WriteField(DamlFieldDefinition field) =>
        new() { Name = field.Name, Type = WriteType(field.Type) };

    private static PbField WriteVariantConstructor(DamlVariantConstructor constructor) =>
        new()
        {
            Name = constructor.Name,
            Type = WriteType(constructor.ArgumentType ?? new DamlPrimitiveType(DamlPrimitive.Unit)),
        };

    private static PbTemplate WriteTemplate(DamlTemplate template)
    {
        var proto = new PbTemplate
        {
            Name = template.Name,
            Signatories = WritePartyAnalysis(template.Signatories),
            Observers = WritePartyAnalysis(template.Observers),
        };
        if (template.Key is not null)
        {
            proto.KeyType = WriteType(template.Key);
        }
        proto.Choices.AddRange(template.Choices
            .Select(WriteChoice)
            .OrderBy(c => c.Name, StringComparer.Ordinal));
        proto.Implements.AddRange(template.Implements
            .Select(WriteTypeConRef)
            .OrderBy(tcn => tcn.PackageId, StringComparer.Ordinal)
            .ThenBy(tcn => string.Join(".", tcn.ModuleNameSegments), StringComparer.Ordinal)
            .ThenBy(tcn => string.Join(".", tcn.NameSegments), StringComparer.Ordinal));
        return proto;
    }

    private static PbInterface WriteInterface(DamlInterface @interface)
    {
        var proto = new PbInterface { Name = @interface.Name };
        if (@interface.ViewType is not null)
        {
            proto.ViewType = WriteType(@interface.ViewType);
        }
        proto.Methods.AddRange(@interface.Methods
            .Select(WriteInterfaceMethod)
            .OrderBy(m => m.Name, StringComparer.Ordinal));
        proto.Choices.AddRange(@interface.Choices
            .Select(WriteChoice)
            .OrderBy(c => c.Name, StringComparer.Ordinal));
        return proto;
    }

    private static PbInterfaceMethod WriteInterfaceMethod(DamlInterfaceMethod method) =>
        new() { Name = method.Name, ReturnType = WriteType(method.ReturnType) };

    private static PbChoice WriteChoice(DamlChoice choice) =>
        new()
        {
            Name = choice.Name,
            Consuming = choice.Consuming,
            ArgumentType = WriteType(choice.ArgumentType),
            ReturnType = WriteType(choice.ReturnType),
            Controllers = WritePartyAnalysis(choice.Controllers),
            Observers = WritePartyAnalysis(choice.Observers),
        };

    private static PbPartyAnalysis WritePartyAnalysis(DamlPartyAnalysis analysis)
    {
        if (analysis.Source == DamlPartySource.Static)
        {
            var @static = new PbStaticParties();
            @static.PayloadFields.AddRange(analysis.Parties.Select(party => party switch
            {
                DamlPartyPayloadField field => field.FieldName,
                _ => throw new NotSupportedException(
                    $"Static party reference '{party.GetType().Name}' has no IntermediateDar mapping. " +
                    "Add it in IntermediateDarWriter.WritePartyAnalysis alongside the proto schema."),
            }));
            return new PbPartyAnalysis { Static = @static };
        }

        return new PbPartyAnalysis { Dynamic = new PbDynamicParties() };
    }

    private static PbType WriteType(DamlType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return type switch
        {
            DamlPrimitiveType primitive => new PbType { Builtin = WriteBuiltin(primitive.Primitive) },
            DamlTypeRef typeRef => new PbType { TypeCon = WriteTypeConRef(typeRef) },
            DamlTypeApp typeApp => new PbType { TypeApp = WriteTypeApp(typeApp) },
            DamlListType list => WriteAppliedBuiltin(DamlPrimitive.List, list.Element),
            DamlOptionalType optional => WriteAppliedBuiltin(DamlPrimitive.Optional, optional.Value),
            DamlTextMapType textMap => WriteAppliedBuiltin(DamlPrimitive.TextMap, textMap.Value),
            DamlGenMapType genMap => WriteAppliedBuiltin(DamlPrimitive.GenMap, genMap.Key, genMap.Value),
            DamlContractIdType contractId => WriteAppliedBuiltin(DamlPrimitive.ContractId, contractId.Payload),
            DamlTypeVar typeVar => WriteTypeVar(typeVar),
            _ => throw new NotSupportedException(
                $"Type '{type.GetType().Name}' has no IntermediateDar mapping. " +
                "Add it in IntermediateDarWriter.WriteType alongside the proto schema."),
        };
    }

    /// <summary>
    /// Writes one of the five typed nodes back to the canonical
    /// <c>TypeApp(builtin, args)</c> encoding the reader folds it in from — the node is the
    /// wire's application spelled as a first-class model node, so a node the writer
    /// produces round-trips through the reader unchanged.
    /// </summary>
    private static PbType WriteAppliedBuiltin(DamlPrimitive builtin, params DamlType[] arguments) =>
        new()
        {
            TypeApp = new PbTypeApp
            {
                Function = new PbType { Builtin = WriteBuiltin(builtin) },
                Arguments = { arguments.Select(WriteType) },
            },
        };

    private static PbType WriteTypeVar(DamlTypeVar typeVar) =>
        long.TryParse(typeVar.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var nat)
            ? new PbType { Nat = nat }
            : new PbType { TypeVar = typeVar.Name };

    private static PbTypeApp WriteTypeApp(DamlTypeApp typeApp)
    {
        var proto = new PbTypeApp { Function = WriteType(typeApp.Base) };
        proto.Arguments.AddRange(typeApp.Arguments.Select(WriteType));
        return proto;
    }

    private static PbTypeConName WriteTypeConRef(DamlTypeRef typeRef)
    {
        var proto = new PbTypeConName { PackageId = typeRef.PackageId };
        proto.ModuleNameSegments.AddRange(typeRef.Module.Split('.'));
        proto.NameSegments.AddRange(typeRef.Name.Split('.'));
        return proto;
    }

    /// <summary>
    /// The catalog's reverse lookup: every <see cref="DamlPrimitive"/> that carries a
    /// catalog row's model identity (the <see cref="DamlPrimitiveDisposition.SupportedValue"/>
    /// and <see cref="DamlPrimitiveDisposition.SignatureOnly"/> rows) mapped back to its
    /// identical proto <see cref="PbBuiltin"/> value. The row set is keyed structurally on
    /// the row's <see cref="DamlPrimitiveCatalogRow.Disposition"/> — a row's
    /// <see cref="DamlPrimitiveCatalogRow.Primitive"/> nullability is a catalog convention
    /// (pinned by the catalog shape rules), not the acceptance rule. Duplicate identities
    /// across rows would mean a broken catalog and fail this map's construction loudly.
    /// </summary>
    private static readonly FrozenDictionary<DamlPrimitive, PbBuiltin> BuiltinByPrimitive =
        DamlPrimitiveCatalog.Rows
            .Where(row => row.Disposition != DamlPrimitiveDisposition.Unsupported)
            .ToFrozenDictionary(row => row.Primitive!.Value, row => row.Builtin);

    /// <summary>
    /// Writes a <see cref="DamlPrimitive"/> back to its proto <see cref="PbBuiltin"/> value
    /// through the catalog reverse lookup — the writer has no mapping of its own, so every
    /// builtin the reader accepts writes back to the identical proto value.
    /// </summary>
    private static PbBuiltin WriteBuiltin(DamlPrimitive primitive) =>
        BuiltinByPrimitive.TryGetValue(primitive, out var builtin)
            ? builtin
            : throw new NotSupportedException(
                $"DamlPrimitive '{primitive}' has no IntermediateDar BuiltinType mapping. " +
                "Add its row in DamlPrimitiveCatalog (Model/DamlPrimitiveCatalog.cs) alongside the proto " +
                "schema and IntermediateDarReader.ConvertBuiltin.");

    private static string FormatVersion(Version version) =>
        $"{version.Major}.{version.Minor}.{(version.Build < 0 ? 0 : version.Build)}";
}
