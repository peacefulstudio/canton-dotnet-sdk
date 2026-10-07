// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Reflection;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Testing.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Smoke test that pipes the codegen-emitted source through Roslyn against the
/// real <c>Daml.Runtime</c> + <c>Daml.Ledger.Abstractions</c> assemblies. Pins
/// "the emitted shape compiles" against quiet drift: string-shape tests in
/// <see cref="ContractIdChoiceProjectorTests"/> and <see cref="ChoiceAsyncExerciserTests"/>
/// can pass while the surrounding template body introduces Roslyn errors — this
/// test fails on any such error-severity diagnostic. Warnings are not asserted
/// against; consumer projects choose their own warning policy.
/// </summary>
internal static class EmittedCodeCompilesTestHelpers
{
    internal static DamlType ContractIdOf(string templateName) => ContractIdOf("Test.Module", templateName);

    internal static DamlType ContractIdOf(string moduleName, string templateName) =>
        new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.ContractId),
            [new DamlTypeRef("", moduleName, templateName)]);

    internal static DamlType TupleType(params DamlType[] componentTypes) =>
        new DamlTypeApp(
            new DamlTypeRef("daml-prim", "DA.Types", $"Tuple{componentTypes.Length}"),
            componentTypes);

    internal static DamlType OptionalOf(DamlType inner) =>
        new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.Optional), [inner]);

    internal static IReadOnlyList<GeneratedFile> GenerateKeyBearingTemplate()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "AssetWithKey",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Reissue",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf("AssetWithKey"),
                        },
                        new DamlChoice
                        {
                            Name = "Describe",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Text),
                        },
                    ],
                    Key = new DamlPrimitiveType(DamlPrimitive.Text),
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "AssetWithKey",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
        };
        return CreateGenerator(options).Generate(dar);
    }

    /// <summary>
    /// Compiles the emitted sources and loads them as an in-memory assembly, for tests
    /// that must call a generated member rather than inspect its text — reflection over
    /// a real invocation is the only way to observe whether an emitted method throws at
    /// the call site or on the task it returns.
    /// </summary>
    internal static Assembly EmitToAssembly(IReadOnlyList<GeneratedFile> files)
    {
        var compilation = CompileEmittedFilesToCompilation(files, DocumentationMode.Parse);

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        if (!emit.Success)
        {
            throw new InvalidOperationException(
                "The emitted sources must compile before they can be invoked: "
                + string.Join(
                    "\n",
                    emit.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.GetMessage(CultureInfo.InvariantCulture) + " @ " + d.Location)));
        }

        return Assembly.Load(stream.ToArray());
    }

    internal static IReadOnlyList<Diagnostic> CompileEmittedFilesWithDocDiagnostics(IReadOnlyList<GeneratedFile> files) =>
        CompileEmittedFiles(files, DocumentationMode.Diagnose);

    internal static IReadOnlyList<Diagnostic> CompileEmittedFiles(IReadOnlyList<GeneratedFile> files) =>
        CompileEmittedFiles(files, DocumentationMode.Parse);

    internal static IReadOnlyList<Diagnostic> CompileEmittedFiles(
        IReadOnlyList<GeneratedFile> files,
        DocumentationMode documentationMode) =>
        CompileEmittedFilesToCompilation(files, documentationMode).GetDiagnostics();

    /// <summary>
    /// The compilation behind <see cref="CompileEmittedFiles(IReadOnlyList{GeneratedFile})"/>,
    /// for tests that need the semantic model rather than the diagnostics — asking Roslyn
    /// which member a call site bound to, rather than inferring it from the
    /// overload-resolution rules.
    /// </summary>
    internal static CSharpCompilation CompileEmittedFilesToCompilation(
        IReadOnlyList<GeneratedFile> files,
        DocumentationMode documentationMode)
    {
        var parseOptions = new CSharpParseOptions(documentationMode: documentationMode);
        var trees = files
            .Where(f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal))
            .Select(f => CSharpSyntaxTree.ParseText(f.Content, parseOptions, path: f.RelativePath))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName: "EmittedCodeCompilesTests-emit",
            syntaxTrees: trees,
            references: ConsumerReferenceSet.Assemblies,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return compilation;
    }
}
