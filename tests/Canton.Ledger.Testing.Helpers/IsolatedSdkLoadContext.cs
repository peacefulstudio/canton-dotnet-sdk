// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Runtime.Loader;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// A collectible load context holding its own copy of the test assembly's SDK assemblies, so a scenario
/// that runs in it meets a fresh generated type registry and bindings no other test has touched. Nothing a
/// scenario loads or registers reaches the default context, so such tests are order-independent and leak no
/// state into the rest of the process.
/// </summary>
public sealed class IsolatedSdkLoadContext : AssemblyLoadContext, IDisposable
{
    private readonly HashSet<string> _unavailableAssemblies;

    /// <summary>Creates a context whose by-name resolution refuses <paramref name="unavailableAssemblies"/>.</summary>
    /// <param name="unavailableAssemblies">
    /// Simple names of assemblies that fail to load by name, as a binding the host's dependency manifest lists
    /// but cannot supply would. Loading one by explicit path still works.
    /// </param>
    public IsolatedSdkLoadContext(params string[] unavailableAssemblies)
        : base("isolated-sdk", isCollectible: true)
    {
        _unavailableAssemblies = [.. unavailableAssemblies];
    }

    /// <summary>
    /// Loads a copy of <paramref name="scenarioHost"/>'s assembly into this context and awaits its static
    /// <c>Task&lt;string&gt;</c> method <paramref name="scenarioMethod"/>, returning the report it answers.
    /// </summary>
    /// <param name="scenarioHost">A type of the assembly to copy, declaring the scenario method.</param>
    /// <param name="scenarioMethod">The name of the parameterless static method to run in this context.</param>
    public async Task<string> RunAsync(Type scenarioHost, string scenarioMethod)
    {
        ArgumentNullException.ThrowIfNull(scenarioHost);
        var assembly = LoadFromAssemblyPath(scenarioHost.Assembly.Location);
        var scenario = assembly.GetType(scenarioHost.FullName!, throwOnError: true)!
            .GetMethod(scenarioMethod, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(scenarioHost.FullName, scenarioMethod);

        return await ((Task<string>)scenario.Invoke(null, null)!).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is not { } simpleName)
        {
            return null;
        }

        if (_unavailableAssemblies.Contains(simpleName))
        {
            throw new FileNotFoundException($"Assembly '{simpleName}' is unavailable in this context.", simpleName);
        }

        var path = Path.Combine(AppContext.BaseDirectory, simpleName + ".dll");
        return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
    }

    /// <inheritdoc />
    public void Dispose() => Unload();
}
