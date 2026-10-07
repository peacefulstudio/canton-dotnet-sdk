// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Daml.Runtime.Serialization;

internal static class BindingAssemblyLoader
{
    private const string HostDepsFilesKey = "APP_CONTEXT_DEPS_FILES";
    private const char HostDepsFilesSeparator = ';';
    private const string RuntimeLibraryName = "Daml.Runtime";

    public static void LoadHostBindings() =>
        Load(
            DepsFilesOf(AppContext.GetData(HostDepsFilesKey) as string),
            name => Assembly.Load(name),
            assembly => RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle));

    internal static IReadOnlyList<string> DepsFilesOf(string? hostValue) =>
        hostValue?.Split(HostDepsFilesSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];

    internal static void Load(
        IEnumerable<string> depsFiles, Func<string, Assembly> loadAssembly, Action<Assembly> runModuleConstructor)
    {
        var assemblyNames = depsFiles.SelectMany(DependentAssemblyNames).Distinct(StringComparer.Ordinal);
        foreach (var assemblyName in assemblyNames)
        {
            TryInitialise(assemblyName, loadAssembly, runModuleConstructor);
        }
    }

    private static void TryInitialise(
        string assemblyName, Func<string, Assembly> loadAssembly, Action<Assembly> runModuleConstructor)
    {
        try
        {
            runModuleConstructor(loadAssembly(assemblyName));
        }
        catch (Exception failure) when (failure is IOException or BadImageFormatException or TypeInitializationException)
        {
            return;
        }
    }

    private static IEnumerable<string> DependentAssemblyNames(string depsFile)
    {
        JsonDocument document;
        try
        {
            using var stream = File.OpenRead(depsFile);
            document = JsonDocument.Parse(stream);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }

        using (document)
        {
            return AssemblyNamesDependingOnRuntime(document.RootElement).ToList();
        }
    }

    private static IEnumerable<string> AssemblyNamesDependingOnRuntime(JsonElement depsRoot)
    {
        if (!TryGetObject(depsRoot, "targets", out var targets))
        {
            yield break;
        }

        foreach (var target in targets.EnumerateObject().Where(target => target.Value.ValueKind == JsonValueKind.Object))
        {
            foreach (var library in target.Value.EnumerateObject())
            {
                if (!DependsOnRuntime(library.Value) || !TryGetObject(library.Value, "runtime", out var runtimeFiles))
                {
                    continue;
                }

                foreach (var runtimeFile in runtimeFiles.EnumerateObject())
                {
                    yield return Path.GetFileNameWithoutExtension(runtimeFile.Name);
                }
            }
        }
    }

    private static bool DependsOnRuntime(JsonElement library) =>
        TryGetObject(library, "dependencies", out var dependencies)
        && dependencies.TryGetProperty(RuntimeLibraryName, out _);

    private static bool TryGetObject(JsonElement parent, string name, out JsonElement value)
    {
        value = default;
        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out value)
            && value.ValueKind == JsonValueKind.Object;
    }
}
