// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

internal sealed partial class PackageNameTable
{
    private void RequireNestedTypesClearOfTheirEnclosers(TypeRootFilter rootFilter)
    {
        foreach (var module in _package.Modules)
        {
            RequireVariantConstructorsClearOfTheirVariant(module);
            RequireChoiceArgumentsClearOfTheirTemplate(module, rootFilter);
        }
    }

    private void RequireVariantConstructorsClearOfTheirVariant(DamlModule module)
    {
        foreach (var dataType in module.DataTypes)
        {
            if (dataType.Definition is not DamlVariantDefinition variant)
            {
                continue;
            }

            var variantTypeName = EmittedName(module.Name, dataType.Name);
            var someConstructorCarriesPayload = variant.Constructors.Any(VariantEmitter.HasVariantPayload);
            var typeParameterNames = dataType.TypeParams.Select(EmitterHelpers.TypeParameterName).ToHashSet(StringComparer.Ordinal);
            var constructorByNestedTypeName = new Dictionary<string, DamlVariantConstructor>(StringComparer.Ordinal);
            foreach (var constructor in variant.Constructors)
            {
                var nestedTypeName = VariantEmitter.VariantConstructorName(constructor.Name, variantTypeName);
                if (!constructorByNestedTypeName.TryAdd(nestedTypeName, constructor))
                {
                    throw new CodegenException(
                        $"Constructors '{constructorByNestedTypeName[nestedTypeName].Name}' and '{constructor.Name}' of Daml variant '{module.Name}:{dataType.Name}' in package '{_package.Name}' " +
                        $"would both be emitted as the nested C# type '{nestedTypeName}'. Rename one of them in Daml.");
                }
                if (typeParameterNames.Contains(nestedTypeName))
                {
                    throw new CodegenException(
                        $"Constructor '{constructor.Name}' of Daml variant '{module.Name}:{dataType.Name}' in package '{_package.Name}' would be emitted as a nested C# type named '{nestedTypeName}', " +
                        "which the generated variant type already declares as a type parameter. " +
                        $"Rename the constructor '{constructor.Name}' or the type variable in Daml.");
                }
                var reserved = ReservedMemberNames.OfVariantConstructorType(VariantEmitter.HasVariantPayload(constructor), someConstructorCarriesPayload);
                if (reserved.Contains(nestedTypeName))
                {
                    throw new CodegenException(
                        $"Constructor '{constructor.Name}' of Daml variant '{module.Name}:{dataType.Name}' in package '{_package.Name}' would be emitted as a nested C# type named '{nestedTypeName}', " +
                        "which the generated variant type already declares as a member. " +
                        $"Rename the constructor '{constructor.Name}' in Daml.");
                }
            }
        }
    }

    private void RequireChoiceArgumentsClearOfTheirTemplate(DamlModule module, TypeRootFilter rootFilter)
    {
        foreach (var template in module.Templates.Where(template => rootFilter.Includes(module.Name, template.Name)))
        {
            var templateTypeName = EmittedName(module.Name, template.Name);
            var templateFieldCount = (module.DataTypes.FirstOrDefault(dataType => dataType.Name == template.Name)?.Definition as DamlRecordDefinition)?.Fields.Count ?? 0;
            foreach (var choice in template.Choices)
            {
                if (NestedChoiceArgumentRecord(choice) is not { } argumentRecord)
                {
                    continue;
                }

                var nestedTypeName = Identifiers.Sanitize(choice.Name);
                var reserved = ReservedMemberNames.OfNestedChoiceArgumentType(template, templateFieldCount, _package.UpgradedPackageId is not null, argumentRecord.Fields.Count);
                if (reserved.Contains(nestedTypeName) || nestedTypeName == templateTypeName)
                {
                    throw new CodegenException(
                        $"Choice '{choice.Name}' of Daml template '{module.Name}:{template.Name}' in package '{_package.Name}' takes a record argument that would be emitted as a nested C# type named '{nestedTypeName}', " +
                        "which the generated template type already declares as a member. " +
                        $"Rename the choice '{choice.Name}' in Daml.");
                }
            }
        }
    }
}
