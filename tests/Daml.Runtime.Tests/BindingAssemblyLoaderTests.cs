// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using AwesomeAssertions;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

public sealed class BindingAssemblyLoaderTests : IDisposable
{
    private const string DepsJsonWithTwoBindingsAndOneBystander = """
        {
          "runtimeTarget": {"name": ".NETCoreApp,Version=v10.0"},
          "targets": {
            ".NETCoreApp,Version=v10.0": {
              "Acme.App/1.0.0": {
                "dependencies": {"Acme.Bindings.Billing": "1.0.0", "Newtonsoft.Json": "13.0.3"},
                "runtime": {"Acme.App.dll": {}}
              },
              "Acme.Bindings.Billing/1.0.0": {
                "dependencies": {"Daml.Runtime": "0.6.0"},
                "runtime": {"lib/net10.0/Acme.Bindings.Billing.dll": {}}
              },
              "Acme.Bindings.Shipping/2.1.0": {
                "dependencies": {"Daml.Runtime": "0.6.0", "Acme.Bindings.Billing": "1.0.0"},
                "runtime": {"lib/net10.0/Acme.Bindings.Shipping.dll": {}, "lib/net10.0/Acme.Bindings.ShippingExtras.dll": {}}
              },
              "Daml.Runtime/0.6.0": {
                "runtime": {"lib/net10.0/Daml.Runtime.dll": {}}
              },
              "Newtonsoft.Json/13.0.3": {
                "runtime": {"lib/net6.0/Newtonsoft.Json.dll": {}}
              },
              "Acme.AnalyzersOnly/1.0.0": {
                "dependencies": {"Daml.Runtime": "0.6.0"}
              }
            }
          }
        }
        """;

    private readonly string _directory = Directory.CreateTempSubdirectory("binding-loader-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string DepsFile(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static readonly Assembly AnyLoadedAssembly = typeof(BindingAssemblyLoaderTests).Assembly;

    [Fact]
    public void Load_loads_every_runtime_library_that_depends_on_Daml_Runtime_by_the_names_of_its_runtime_files()
    {
        var requested = new List<string>();

        BindingAssemblyLoader.Load(
            [DepsFile("app.deps.json", DepsJsonWithTwoBindingsAndOneBystander)],
            name =>
            {
                requested.Add(name);
                return AnyLoadedAssembly;
            },
            _ => { });

        requested.Should().Equal("Acme.Bindings.Billing", "Acme.Bindings.Shipping", "Acme.Bindings.ShippingExtras");
    }

    [Fact]
    public void Load_runs_the_module_constructor_of_each_loaded_assembly()
    {
        var initialised = new List<Assembly>();

        BindingAssemblyLoader.Load(
            [DepsFile("app.deps.json", DepsJsonWithTwoBindingsAndOneBystander)],
            _ => AnyLoadedAssembly,
            initialised.Add);

        initialised.Should().HaveCount(3).And.OnlyContain(assembly => assembly == AnyLoadedAssembly);
    }

    [Theory]
    [InlineData(typeof(FileNotFoundException))]
    [InlineData(typeof(FileLoadException))]
    [InlineData(typeof(BadImageFormatException))]
    public void Load_skips_a_library_that_fails_to_load_and_still_loads_the_rest(Type failure)
    {
        var loaded = new List<string>();

        BindingAssemblyLoader.Load(
            [DepsFile("app.deps.json", DepsJsonWithTwoBindingsAndOneBystander)],
            name =>
            {
                if (name == "Acme.Bindings.Billing")
                {
                    throw (Exception)Activator.CreateInstance(failure)!;
                }

                loaded.Add(name);
                return AnyLoadedAssembly;
            },
            _ => { });

        loaded.Should().Equal("Acme.Bindings.Shipping", "Acme.Bindings.ShippingExtras");
    }

    [Fact]
    public void Load_skips_a_library_whose_module_constructor_throws_and_still_initialises_the_rest()
    {
        var loaded = new List<string>();

        BindingAssemblyLoader.Load(
            [DepsFile("app.deps.json", DepsJsonWithTwoBindingsAndOneBystander)],
            name =>
            {
                loaded.Add(name);
                return AnyLoadedAssembly;
            },
            _ =>
            {
                if (loaded.Count == 1)
                {
                    throw new TypeInitializationException("<Module>", new InvalidOperationException("boom"));
                }
            });

        loaded.Should().Equal("Acme.Bindings.Billing", "Acme.Bindings.Shipping", "Acme.Bindings.ShippingExtras");
    }

    [Fact]
    public void Load_loads_a_library_listed_by_several_deps_files_once()
    {
        var requested = new List<string>();

        BindingAssemblyLoader.Load(
            [DepsFile("a.deps.json", DepsJsonWithTwoBindingsAndOneBystander), DepsFile("b.deps.json", DepsJsonWithTwoBindingsAndOneBystander)],
            name =>
            {
                requested.Add(name);
                return AnyLoadedAssembly;
            },
            _ => { });

        requested.Should().Equal("Acme.Bindings.Billing", "Acme.Bindings.Shipping", "Acme.Bindings.ShippingExtras");
    }

    [Fact]
    public void Load_ignores_a_deps_file_that_does_not_exist_or_is_not_json()
    {
        var requested = new List<string>();

        BindingAssemblyLoader.Load(
            [Path.Combine(_directory, "absent.deps.json"), DepsFile("broken.deps.json", "{ not json"), DepsFile("empty.deps.json", "{}")],
            name =>
            {
                requested.Add(name);
                return AnyLoadedAssembly;
            },
            _ => { });

        requested.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{"targets": []}""")]
    [InlineData("""{"targets": {".NETCoreApp,Version=v10.0": []}}""")]
    [InlineData("""{"targets": {".NETCoreApp,Version=v10.0": {"Acme.Odd/1.0.0": []}}}""")]
    [InlineData("""{"targets": {".NETCoreApp,Version=v10.0": {"Acme.Odd/1.0.0": {"dependencies": []}}}}""")]
    [InlineData("""{"targets": {".NETCoreApp,Version=v10.0": {"Acme.Odd/1.0.0": {"dependencies": {"Daml.Runtime": "0.6.0"}, "runtime": []}}}}""")]
    public void Load_ignores_a_deps_file_whose_shape_is_not_the_one_the_host_writes_and_still_reads_the_others(string oddDocument)
    {
        var requested = new List<string>();

        BindingAssemblyLoader.Load(
            [DepsFile("odd.deps.json", oddDocument), DepsFile("app.deps.json", DepsJsonWithTwoBindingsAndOneBystander)],
            name =>
            {
                requested.Add(name);
                return AnyLoadedAssembly;
            },
            _ => { });

        requested.Should().Equal("Acme.Bindings.Billing", "Acme.Bindings.Shipping", "Acme.Bindings.ShippingExtras");
    }

    [Fact]
    public void Load_with_no_deps_files_loads_nothing()
    {
        var requested = new List<string>();

        BindingAssemblyLoader.Load(
            [],
            name =>
            {
                requested.Add(name);
                return AnyLoadedAssembly;
            },
            _ => { });

        requested.Should().BeEmpty();
    }

    [Fact]
    public void DepsFilesOf_splits_the_host_value_on_semicolons_and_drops_blanks()
    {
        var files = BindingAssemblyLoader.DepsFilesOf("/app/app.deps.json;;/shared/Microsoft.NETCore.App.deps.json");

        files.Should().Equal("/app/app.deps.json", "/shared/Microsoft.NETCore.App.deps.json");
    }

    [Fact]
    public void DepsFilesOf_a_host_that_published_nothing_is_empty()
    {
        BindingAssemblyLoader.DepsFilesOf(null).Should().BeEmpty();
        BindingAssemblyLoader.DepsFilesOf(string.Empty).Should().BeEmpty();
    }
}
