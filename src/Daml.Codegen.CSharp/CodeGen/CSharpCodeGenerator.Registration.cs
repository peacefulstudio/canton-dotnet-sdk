// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Serialization;
using RuntimeNamespaces = Daml.Runtime.RuntimeNamespaces;

namespace Daml.Codegen.CSharp.CodeGen;

public sealed partial class CSharpCodeGenerator
{
    private const string ModuleInitializerAttribute = "global::System.Runtime.CompilerServices.ModuleInitializer";

    private const string RegistryType = $"global::{RuntimeNamespaces.Serialization}.{nameof(GeneratedTypeReaders)}";

    private const string RegisterMethodName = "Register";

    private IEnumerable<GeneratedFile> GenerateRegistration(
        DarCrossPackageResolver resolver,
        IReadOnlyList<PackageEmitContext> moduleContexts)
    {
        if (moduleContexts.Count == 0 || moduleContexts[0].NameTable.Registration is not { } registration)
        {
            yield break;
        }

        var nameTable = moduleContexts[0].NameTable;
        var calls = new List<string>();
        foreach (var template in registration.Templates)
        {
            var context = moduleContexts.Single(candidate => candidate.Module.Name == template.Module.Name);
            calls.AddRange(RegistrationCallsOf(template, context, new DamlTypeMapper(context, resolver)));
        }
        foreach (var iface in registration.Interfaces)
        {
            var marker = QualifiedName(nameTable, iface.Module.Name, nameTable.InterfaceMarkerName(iface.Module.Name, iface.Interface.Name));
            calls.Add(RegistrationCall("ForChoices", marker));
        }

        var code = EmitFile(registration.Namespace, indent =>
        {
            indent.AppendLine($"internal static class {registration.ClassName}");
            indent.AppendLine("{");
            indent.Indent();
            indent.AppendLine($"[{ModuleInitializerAttribute}]");
            indent.AppendLine($"internal static void {RegisterMethodName}()");
            indent.AppendLine("{");
            indent.Indent();
            foreach (var call in calls)
            {
                indent.AppendLine(call);
            }
            indent.Dedent();
            indent.AppendLine("}");
            indent.Dedent();
            indent.AppendLine("}");
        });

        yield return GeneratedFile.Text(RelativeFilePath(registration.Namespace, $"{registration.ClassName}.cs"), code);
    }

    private static IEnumerable<string> RegistrationCallsOf(
        RegisteredTemplate template,
        PackageEmitContext context,
        DamlTypeMapper mapper)
    {
        var templateType = QualifiedName(
            context.NameTable,
            template.Module.Name,
            context.EmittedTypeName(template.Module.Name, template.Template.Name));

        yield return RegistrationCall("ForRecord", templateType);
        yield return RegistrationCall("ForChoices", templateType);
        if (template.Template.Key is { } keyType)
        {
            yield return RegistrationCall("ForKey", templateType, mapper.MapType(keyType));
        }
    }

    private static string RegistrationCall(string method, params string[] typeArguments) =>
        $"{RegistryType}.{method}<{string.Join(", ", typeArguments)}>();";

    private static string QualifiedName(PackageNameTable nameTable, string moduleName, string typeName) =>
        Identifiers.GlobalQualified(nameTable.ModuleNamespaces[moduleName], typeName);
}
