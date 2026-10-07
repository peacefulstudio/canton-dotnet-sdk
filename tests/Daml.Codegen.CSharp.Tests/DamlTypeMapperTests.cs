// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using Daml.Runtime.Data;
using Daml.Runtime.Stdlib;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class DamlTypeMapperTests
{
    private const string LocalPackageId = "pkg-id";
    private const string CrossPackageId = "other-pkg";
    private const string StdlibPackageId = "stdlib-pkg";

    private static DamlPackage Package(string name, params DamlModule[] modules) =>
        new()
        {
            PackageId = LocalPackageId,
            Name = name,
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = modules,
            DependencyReferences = []
        };

    private static DamlModule EmptyModule(string name) =>
        new() { Name = name, Templates = [], DataTypes = [], Interfaces = [] };

    private static DamlTypeMapper Mapper(params DamlPackage[] dependencies)
    {
        var resolution = RealResolution.Of(
            Package("test-package", EmptyModule("Test.Module")),
            new CodeGenOptions { NamespacePrefix = "Test.Package" },
            dependencies);
        return new DamlTypeMapper(resolution.Context, resolution.Resolver);
    }

    private static DamlPrimitiveType Prim(DamlPrimitive primitive) => new(primitive);

    private static DamlTypeApp App(DamlPrimitive constructor, params DamlType[] arguments) =>
        new(Prim(constructor), arguments);

    private static DamlPackage StdlibPackage() =>
        new()
        {
            PackageId = StdlibPackageId,
            Name = "daml-stdlib",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

    [Fact]
    public void MapType_maps_text_primitive_to_string()
    {
        Mapper().MapType(Prim(DamlPrimitive.Text)).Should().Be("string");
    }

    [Theory]
    [InlineData(DamlPrimitive.Bool, "bool")]
    [InlineData(DamlPrimitive.Int64, "long")]
    [InlineData(DamlPrimitive.Numeric, "decimal")]
    [InlineData(DamlPrimitive.Date, "global::System.DateOnly")]
    [InlineData(DamlPrimitive.Timestamp, "global::System.DateTimeOffset")]
    public void MapType_maps_primitives_to_their_clr_types(DamlPrimitive primitive, string expected)
    {
        Mapper().MapType(Prim(primitive)).Should().Be(expected);
    }

    [Fact]
    public void MapType_wraps_list_argument_in_ireadonlylist()
    {
        Mapper().MapType(App(DamlPrimitive.List, Prim(DamlPrimitive.Int64)))
            .Should().Be("global::System.Collections.Generic.IReadOnlyList<long>");
    }

    [Fact]
    public void MapType_renders_optional_argument_as_nullable()
    {
        Mapper().MapType(App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)))
            .Should().Be("string?");
    }

    [Fact]
    public void MapType_renders_genmap_as_ireadonlydictionary()
    {
        Mapper().MapType(App(DamlPrimitive.GenMap, Prim(DamlPrimitive.Text), Prim(DamlPrimitive.Int64)))
            .Should().Be("global::System.Collections.Generic.IReadOnlyDictionary<string, long>");
    }

    [Fact]
    public void MapType_renders_contract_id_argument()
    {
        Mapper().MapType(App(DamlPrimitive.ContractId, Prim(DamlPrimitive.Party)))
            .Should().Be("global::Daml.Runtime.Contracts.ContractId<global::Daml.Runtime.Data.Party>");
    }

    [Fact]
    public void MapType_resolves_cross_package_type_ref_through_the_resolver()
    {
        var mapper = Mapper(WidgetPackage());

        mapper.MapType(new DamlTypeRef(CrossPackageId, "Acme.Widgets", "Widget"))
            .Should().Be("global::Acme.Widgets.Widget");
    }

    [Fact]
    public void MapType_nests_optional_inside_list()
    {
        Mapper().MapType(App(DamlPrimitive.List, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))))
            .Should().Be("global::System.Collections.Generic.IReadOnlyList<string?>");
    }

    [Fact]
    public void MapType_nests_list_inside_optional()
    {
        Mapper().MapType(App(DamlPrimitive.Optional, App(DamlPrimitive.List, Prim(DamlPrimitive.Int64))))
            .Should().Be("global::System.Collections.Generic.IReadOnlyList<long>?");
    }

    [Fact]
    public void MapType_rejects_excessively_deep_types_before_managed_stack_overflow()
    {
        var type = Enumerable.Range(0, 300)
            .Aggregate((DamlType)Prim(DamlPrimitive.Text), (inner, _) => App(DamlPrimitive.List, inner));

        Mapper().Invoking(m => m.MapType(type))
            .Should().Throw<InvalidDataException>()
            .WithMessage("*depth*");
    }

    [Fact]
    public void MapType_nests_optional_inside_a_genmap_value()
    {
        Mapper().MapType(App(DamlPrimitive.GenMap, Prim(DamlPrimitive.Text), App(DamlPrimitive.Optional, Prim(DamlPrimitive.Int64))))
            .Should().Be("global::System.Collections.Generic.IReadOnlyDictionary<string, long?>");
    }

    [Fact]
    public void MapType_resolves_a_cross_package_argument_inside_a_contract_id()
    {
        var mapper = Mapper(WidgetPackage());

        mapper.MapType(App(DamlPrimitive.ContractId, new DamlTypeRef(CrossPackageId, "Acme.Widgets", "Widget")))
            .Should().Be("global::Daml.Runtime.Contracts.ContractId<global::Acme.Widgets.Widget>");
    }

    [Fact]
    public void MapType_emits_the_wrapper_at_both_levels_of_a_nested_optional()
    {
        Mapper().MapType(App(DamlPrimitive.Optional, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))))
            .Should().Be("global::Daml.Runtime.Stdlib.Optional<global::Daml.Runtime.Stdlib.Optional<string>>");
    }

    [Fact]
    public void MapType_emits_the_wrapper_at_both_levels_of_a_nested_optional_over_a_type_variable()
    {
        Mapper().MapType(App(DamlPrimitive.Optional, App(DamlPrimitive.Optional, new DamlTypeVar("a"))))
            .Should().Be("global::Daml.Runtime.Stdlib.Optional<global::Daml.Runtime.Stdlib.Optional<TA>>");
    }

    [Fact]
    public void MapType_emits_the_wrapper_for_a_nested_optional_passed_to_an_emitted_generic()
    {
        Mapper(BoxPackage()).MapType(
                new DamlTypeApp(
                    new DamlTypeRef(CrossPackageId, "Acme.Shapes", "Box"),
                    [App(DamlPrimitive.Optional, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)))]))
            .Should().Be("global::Acme.Shapes.Box<global::Daml.Runtime.Stdlib.Optional<global::Daml.Runtime.Stdlib.Optional<string>>>");
    }

    [Fact]
    public void ToValue_emits_the_chain_for_a_nested_optional_passed_to_an_emitted_generic()
    {
        Mapper(BoxPackage()).ToValue(
                new DamlTypeApp(
                    new DamlTypeRef(CrossPackageId, "Acme.Shapes", "Box"),
                    [App(DamlPrimitive.Optional, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)))]),
                "Field")
            .Should().Be(
                "Field.ToRecord(__t0 => (global::Daml.Runtime.Data.DamlValue)(__t0.ToChainValue(__optional1 => "
                + "__optional1.ToChainValue(__optional2 => new global::Daml.Runtime.Data.DamlText(__optional2)))))");
    }

    [Fact]
    public void MapType_refuses_a_nested_optional_passed_to_a_generic_that_wraps_the_parameter()
    {
        var act = () => Mapper(CratePackage()).MapType(
            new DamlTypeApp(
                new DamlTypeRef(CrossPackageId, "Acme.Shapes", "Crate"),
                [App(DamlPrimitive.Optional, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)))]));

        act.Should().Throw<CodegenException>()
            .WithMessage("*Optional as the 'a' type argument of Acme.Shapes:Crate*")
            .WithMessage("*one array level short*");
    }

    [Fact]
    public void ToValue_refuses_a_single_optional_passed_to_a_generic_that_wraps_the_parameter()
    {
        var act = () => Mapper(CratePackage()).ToValue(
            new DamlTypeApp(
                new DamlTypeRef(CrossPackageId, "Acme.Shapes", "Crate"),
                [App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))]),
            "Field");

        act.Should().Throw<CodegenException>()
            .WithMessage("*Optional as the 'a' type argument of Acme.Shapes:Crate*");
    }

    [Fact]
    public void FromValue_refuses_a_single_optional_passed_to_a_generic_that_wraps_the_parameter()
    {
        var act = () => Mapper(CratePackage()).FromValue(
            new DamlTypeApp(
                new DamlTypeRef(CrossPackageId, "Acme.Shapes", "Crate"),
                [App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))]),
            "value");

        act.Should().Throw<CodegenException>()
            .WithMessage("*Optional as the 'a' type argument of Acme.Shapes:Crate*");
    }

    [Fact]
    public void MapType_keeps_a_single_optional_passed_to_a_generic_that_does_not_wrap_the_parameter()
    {
        Mapper(BoxPackage()).MapType(
                new DamlTypeApp(
                    new DamlTypeRef(CrossPackageId, "Acme.Shapes", "Box"),
                    [App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))]))
            .Should().Be("global::Acme.Shapes.Box<global::Daml.Runtime.Stdlib.Optional<string>>");
    }

    [Fact]
    public void ToValue_serializes_every_level_of_a_nested_optional_through_the_chain_encoding()
    {
        Mapper().ToValue(App(DamlPrimitive.Optional, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))), "MaybeMaybeNote")
            .Should().Be("MaybeMaybeNote.ToChainValue(__optional0 => __optional0.ToChainValue(__optional1 => new global::Daml.Runtime.Data.DamlText(__optional1)))");
    }

    [Fact]
    public void FromValue_deserializes_every_level_of_a_nested_optional_through_the_chain_encoding()
    {
        Mapper().FromValue(App(DamlPrimitive.Optional, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))), "value")
            .Should().Contain("global::Daml.Runtime.Stdlib.Optional<global::Daml.Runtime.Stdlib.Optional<string>>.FromChainValue(")
            .And.Contain("Optional<string>.FromChainValue(");
    }

    [Fact]
    public void MapType_emits_the_wrapper_for_an_optional_genmap_key()
    {
        Mapper().MapType(App(
                DamlPrimitive.GenMap,
                App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)),
                Prim(DamlPrimitive.Int64)))
            .Should().Be("global::System.Collections.Generic.IReadOnlyDictionary<global::Daml.Runtime.Stdlib.Optional<string>, long>");
    }

    [Fact]
    public void FromValue_deserializes_an_optional_genmap_key_through_the_wrapper()
    {
        Mapper().FromValue(
                App(
                    DamlPrimitive.GenMap,
                    App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)),
                    Prim(DamlPrimitive.Int64)),
                "value")
            .Should().Be(
                "(global::System.Collections.Generic.IReadOnlyDictionary<global::Daml.Runtime.Stdlib.Optional<string>, long>)value.As<global::Daml.Runtime.Data.DamlGenMap>().Entries.ToDictionary("
                + "kv => global::Daml.Runtime.Stdlib.Optional<string>.FromValue(kv.Key, __optional1 => __optional1.As<global::Daml.Runtime.Data.DamlText>().Value), "
                + "kv => kv.Value.As<global::Daml.Runtime.Data.DamlInt64>().Value)");
    }

    [Fact]
    public void FromValue_never_emits_a_nullable_key_selector_for_an_optional_genmap_key()
    {
        Mapper().FromValue(
                App(
                    DamlPrimitive.GenMap,
                    App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)),
                    Prim(DamlPrimitive.Int64)),
                "value")
            .Should().NotContain(": null");
    }

    [Fact]
    public void ToValue_serializes_an_optional_genmap_key_through_the_flat_wrapper()
    {
        Mapper().ToValue(
                App(
                    DamlPrimitive.GenMap,
                    App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)),
                    Prim(DamlPrimitive.Int64)),
                "Registry")
            .Should().Be(
                "new global::Daml.Runtime.Data.DamlGenMap(Registry.Select(kv => ("
                + "(global::Daml.Runtime.Data.DamlValue)kv.Key.ToValue(__optional1 => new global::Daml.Runtime.Data.DamlText(__optional1)), "
                + "(global::Daml.Runtime.Data.DamlValue)new global::Daml.Runtime.Data.DamlInt64(kv.Value))).ToList())");
    }

    [Fact]
    public void FromValue_deserializes_a_nested_optional_genmap_key_through_the_chain_encoding()
    {
        Mapper().FromValue(
                App(
                    DamlPrimitive.GenMap,
                    App(DamlPrimitive.Optional, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))),
                    Prim(DamlPrimitive.Int64)),
                "value")
            .Should().Be(
                "(global::System.Collections.Generic.IReadOnlyDictionary<global::Daml.Runtime.Stdlib.Optional<global::Daml.Runtime.Stdlib.Optional<string>>, long>)value.As<global::Daml.Runtime.Data.DamlGenMap>().Entries.ToDictionary("
                + "kv => global::Daml.Runtime.Stdlib.Optional<global::Daml.Runtime.Stdlib.Optional<string>>.FromChainValue(kv.Key, __optional1 => "
                + "global::Daml.Runtime.Stdlib.Optional<string>.FromChainValue(__optional1, __optional2 => __optional2.As<global::Daml.Runtime.Data.DamlText>().Value)), "
                + "kv => kv.Value.As<global::Daml.Runtime.Data.DamlInt64>().Value)");
    }

    [Fact]
    public void FromValue_keeps_an_optional_genmap_value_nullable()
    {
        Mapper().FromValue(
                App(
                    DamlPrimitive.GenMap,
                    Prim(DamlPrimitive.Text),
                    App(DamlPrimitive.Optional, Prim(DamlPrimitive.Int64))),
                "value")
            .Should().Be(
                "(global::System.Collections.Generic.IReadOnlyDictionary<string, long?>)value.As<global::Daml.Runtime.Data.DamlGenMap>().Entries.ToDictionary("
                + "kv => kv.Key.As<global::Daml.Runtime.Data.DamlText>().Value, "
                + "kv => kv.Value.AsOptional().HasValue ? kv.Value.AsOptional().Value!.As<global::Daml.Runtime.Data.DamlInt64>().Value : null)");
    }

    [Fact]
    public void DamlTypeMapper_handles_the_nested_chain_encoding_in_all_three_methods()
    {
        var mapper = Mapper();
        var chained = new DamlWrappedOptional(Prim(DamlPrimitive.Text), OptionalEncoding.NestedChain);

        mapper.MapType(chained).Should().Be("global::Daml.Runtime.Stdlib.Optional<string>");
        mapper.ToValue(chained, "Note").Should().Contain("ToChainValue(");
        mapper.FromValue(chained, "value").Should().Contain("FromChainValue(");
    }

    [Fact]
    public void ToValue_serializes_int64_primitive()
    {
        Mapper().ToValue(Prim(DamlPrimitive.Int64), "Amount")
            .Should().Be("new global::Daml.Runtime.Data.DamlInt64(Amount)");
    }

    [Fact]
    public void ToValue_serializes_list_container()
    {
        Mapper().ToValue(App(DamlPrimitive.List, Prim(DamlPrimitive.Text)), "Items")
            .Should().Be("new global::Daml.Runtime.Data.DamlList(Items.Select(x => (global::Daml.Runtime.Data.DamlValue)new global::Daml.Runtime.Data.DamlText(x)).ToList())");
    }

    [Fact]
    public void ToValue_serializes_optional_container()
    {
        Mapper().ToValue(App(DamlPrimitive.Optional, Prim(DamlPrimitive.Int64)), "Maybe")
            .Should().Be("Maybe is { } __Maybe ? new global::Daml.Runtime.Data.DamlOptional(new global::Daml.Runtime.Data.DamlInt64(__Maybe)) : global::Daml.Runtime.Data.DamlOptional.None");
    }

    [Fact]
    public void ToValue_rejects_excessively_deep_types_before_managed_stack_overflow()
    {
        var type = Enumerable.Range(0, 300)
            .Aggregate((DamlType)Prim(DamlPrimitive.Text), (inner, _) => App(DamlPrimitive.List, inner));

        Mapper().Invoking(m => m.ToValue(type, "Items"))
            .Should().Throw<InvalidDataException>()
            .WithMessage("*depth*");
    }

    [Fact]
    public void ToValue_serializes_parametric_stdlib_type_through_the_stub()
    {
        var package = StdlibPackage();
        var either = new DamlTypeApp(
            new DamlTypeRef(StdlibPackageId, "DA.Types", "Either"),
            [Prim(DamlPrimitive.Text), Prim(DamlPrimitive.Int64)]);

        Mapper(package).ToValue(either, "Choice")
            .Should().Be("Choice.ToValue(__t0 => (global::Daml.Runtime.Data.DamlValue)(new global::Daml.Runtime.Data.DamlText(__t0)), __t1 => (global::Daml.Runtime.Data.DamlValue)(new global::Daml.Runtime.Data.DamlInt64(__t1)))");
    }

    [Fact]
    public void FromValue_deserializes_int64_primitive()
    {
        Mapper().FromValue(Prim(DamlPrimitive.Int64), "value")
            .Should().Be("value.As<global::Daml.Runtime.Data.DamlInt64>().Value");
    }

    [Fact]
    public void FromValue_deserializes_optional_container()
    {
        Mapper().FromValue(App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)), "value")
            .Should().Be("value.AsOptional().HasValue ? value.AsOptional().Value!.As<global::Daml.Runtime.Data.DamlText>().Value : null");
    }

    [Fact]
    public void FromValue_deserializes_a_list_of_optionals()
    {
        Mapper().FromValue(App(DamlPrimitive.List, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))), "value")
            .Should().Be("(global::System.Collections.Generic.IReadOnlyList<string?>)value.As<global::Daml.Runtime.Data.DamlList>().Values.Select(x => x.AsOptional().HasValue ? x.AsOptional().Value!.As<global::Daml.Runtime.Data.DamlText>().Value : null).ToList()");
    }

    [Fact]
    public void FromValue_casts_a_nested_genmap_of_genmap_to_the_declared_ireadonlydictionary()
    {
        Mapper().FromValue(App(DamlPrimitive.GenMap, Prim(DamlPrimitive.Party), App(DamlPrimitive.GenMap, Prim(DamlPrimitive.Text), Prim(DamlPrimitive.Int64))), "value")
            .Should().Be("(global::System.Collections.Generic.IReadOnlyDictionary<global::Daml.Runtime.Data.Party, global::System.Collections.Generic.IReadOnlyDictionary<string, long>>)value.As<global::Daml.Runtime.Data.DamlGenMap>().Entries.ToDictionary(kv => global::Daml.Runtime.Data.Party.FromDamlValue(kv.Key.As<global::Daml.Runtime.Data.DamlParty>()), kv => (global::System.Collections.Generic.IReadOnlyDictionary<string, long>)kv.Value.As<global::Daml.Runtime.Data.DamlGenMap>().Entries.ToDictionary(kv => kv.Key.As<global::Daml.Runtime.Data.DamlText>().Value, kv => kv.Value.As<global::Daml.Runtime.Data.DamlInt64>().Value))");
    }

    [Fact]
    public void FromValue_casts_a_nested_textmap_of_textmap_to_the_declared_ireadonlydictionary()
    {
        Mapper().FromValue(App(DamlPrimitive.TextMap, App(DamlPrimitive.TextMap, Prim(DamlPrimitive.Int64))), "value")
            .Should().Be("(global::System.Collections.Generic.IReadOnlyDictionary<string, global::System.Collections.Generic.IReadOnlyDictionary<string, long>>)value.As<global::Daml.Runtime.Data.DamlTextMap>().Values.ToDictionary(kv => kv.Key, kv => (global::System.Collections.Generic.IReadOnlyDictionary<string, long>)kv.Value.As<global::Daml.Runtime.Data.DamlTextMap>().Values.ToDictionary(kv => kv.Key, kv => kv.Value.As<global::Daml.Runtime.Data.DamlInt64>().Value))");
    }

    [Fact]
    public void FromValue_rejects_excessively_deep_types_before_managed_stack_overflow()
    {
        var type = Enumerable.Range(0, 300)
            .Aggregate((DamlType)Prim(DamlPrimitive.Text), (inner, _) => App(DamlPrimitive.List, inner));

        Mapper().Invoking(m => m.FromValue(type, "value"))
            .Should().Throw<InvalidDataException>()
            .WithMessage("*depth*");
    }

    [Fact]
    public void FromValue_deserializes_parametric_stdlib_type_through_the_stub()
    {
        var package = StdlibPackage();
        var either = new DamlTypeApp(
            new DamlTypeRef(StdlibPackageId, "DA.Types", "Either"),
            [Prim(DamlPrimitive.Text), Prim(DamlPrimitive.Int64)]);

        Mapper(package).FromValue(either, "value")
            .Should().Be("global::Daml.Runtime.Stdlib.Either<string, long>.FromValue(value, __v0 => __v0.As<global::Daml.Runtime.Data.DamlText>().Value, __v1 => __v1.As<global::Daml.Runtime.Data.DamlInt64>().Value)");
    }

    [Fact]
    public void ToValue_serializes_set_through_the_conversion_table()
    {
        var package = StdlibPackage();
        var set = new DamlTypeApp(
            new DamlTypeRef(StdlibPackageId, "DA.Set.Types", "Set"),
            [Prim(DamlPrimitive.Text)]);

        Mapper(package).ToValue(set, "Members")
            .Should().Be("Members.ToRecord(__t0 => (global::Daml.Runtime.Data.DamlValue)(new global::Daml.Runtime.Data.DamlText(__t0)))");
    }

    [Fact]
    public void FromValue_deserializes_set_through_the_conversion_table()
    {
        var package = StdlibPackage();
        var set = new DamlTypeApp(
            new DamlTypeRef(StdlibPackageId, "DA.Set.Types", "Set"),
            [Prim(DamlPrimitive.Text)]);

        Mapper(package).FromValue(set, "value")
            .Should().Be("global::Daml.Runtime.Stdlib.Set<string>.FromRecord(value.As<global::Daml.Runtime.Data.DamlRecord>(), __v0 => __v0.As<global::Daml.Runtime.Data.DamlText>().Value)");
    }

    [Fact]
    public void ToValue_serializes_nonempty_through_the_conversion_table()
    {
        var package = StdlibPackage();
        var nonEmpty = new DamlTypeApp(
            new DamlTypeRef(StdlibPackageId, "DA.NonEmpty.Types", "NonEmpty"),
            [Prim(DamlPrimitive.Int64)]);

        Mapper(package).ToValue(nonEmpty, "Items")
            .Should().Be("Items.ToRecord(__t0 => (global::Daml.Runtime.Data.DamlValue)(new global::Daml.Runtime.Data.DamlInt64(__t0)))");
    }

    [Fact]
    public void FromValue_deserializes_nonempty_through_the_conversion_table()
    {
        var package = StdlibPackage();
        var nonEmpty = new DamlTypeApp(
            new DamlTypeRef(StdlibPackageId, "DA.NonEmpty.Types", "NonEmpty"),
            [Prim(DamlPrimitive.Int64)]);

        Mapper(package).FromValue(nonEmpty, "value")
            .Should().Be("global::Daml.Runtime.Stdlib.NonEmpty<long>.FromRecord(value.As<global::Daml.Runtime.Data.DamlRecord>(), __v0 => __v0.As<global::Daml.Runtime.Data.DamlInt64>().Value)");
    }

    [Fact]
    public void FromValue_handles_type_var_with_a_runtime_stub()
    {
        Mapper().FromValue(new DamlTypeVar("a"), "value")
            .Should().Be("global::Daml.Runtime.Stdlib.GenericStub.NotImplemented<TA>(\"a\")");
    }

    private static DamlPackage PackageWithGenericType(string module, string name, DamlDataTypeDefinition definition) =>
        new()
        {
            PackageId = CrossPackageId,
            Name = "acme",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = module,
                    Templates = [],
                    Interfaces = [],
                    DataTypes =
                    [
                        new DamlDataType
                        {
                            Name = name,
                            TypeParams = ["a"],
                            Definition = definition,
                        }
                    ],
                }
            ],
            DependencyReferences = [],
        };

    private static DamlPackage WidgetPackage() =>
        PackageWithDataTypes(("Acme.Widgets", "Widget", new DamlRecordDefinition([])));

    private static DamlPackage CratePackage() =>
            PackageWithGenericType(
                "Acme.Shapes",
                "Crate",
                new DamlRecordDefinition(
                    [new DamlFieldDefinition("item", App(DamlPrimitive.Optional, new DamlTypeVar("a")))]));

    private static DamlPackage BoxPackage() =>
            PackageWithGenericType(
                "Acme.Shapes",
                "Box",
                new DamlRecordDefinition([new DamlFieldDefinition("item", new DamlTypeVar("a"))]));

    private static DamlTypeApp GenericAppOfText(string module, string name) =>
        new(new DamlTypeRef(CrossPackageId, module, name), [Prim(DamlPrimitive.Text)]);

    private static DamlPackage PackageWithDataTypes(params (string Module, string Name, DamlDataTypeDefinition Definition)[] declarations) =>
        new()
        {
            PackageId = CrossPackageId,
            Name = "acme",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = declarations
                .GroupBy(declaration => declaration.Module)
                .Select(moduleDeclarations => new DamlModule
                {
                    Name = moduleDeclarations.Key,
                    Templates = [],
                    Interfaces = [],
                    DataTypes = moduleDeclarations
                        .Select(declaration => new DamlDataType { Name = declaration.Name, Definition = declaration.Definition })
                        .ToList(),
                })
                .ToList(),
            DependencyReferences = [],
        };

    private static readonly DamlEnumDefinition Colours = new(["Red", "Blue"]);

    [Fact]
    public void ToValue_converts_a_cross_package_enum_through_its_qualified_extensions_class()
    {
        var package = PackageWithDataTypes(("Acme.Palette", "Colour", Colours));

        Mapper(package).ToValue(new DamlTypeRef(CrossPackageId, "Acme.Palette", "Colour"), "Shade")
            .Should().Be("global::Acme.Palette.ColourExtensions.ToDamlEnum(Shade)");
    }

    [Fact]
    public void ToValue_serializes_a_cross_package_variant_through_ToVariant()
    {
        var shape = new DamlVariantDefinition([new DamlVariantConstructor("Circle", Prim(DamlPrimitive.Text))]);
        var package = PackageWithDataTypes(("Acme.Shapes", "Shape", shape));

        Mapper(package).ToValue(new DamlTypeRef(CrossPackageId, "Acme.Shapes", "Shape"), "Figure")
            .Should().Be("Figure.ToVariant()");
    }

    [Fact]
    public void ToValue_leaves_a_cross_package_ref_unclassified_when_its_module_is_absent_from_the_package()
    {
        var package = PackageWithDataTypes(("Acme.Palette", "Colour", Colours));

        Mapper(package).ToValue(new DamlTypeRef(CrossPackageId, "Acme.Absent", "Colour"), "Shade")
            .Should().Be("Shade.ToRecord()");
    }

    [Fact]
    public void ToValue_leaves_a_cross_package_ref_unclassified_when_its_package_is_absent_from_the_dar()
    {
        Mapper().ToValue(new DamlTypeRef(CrossPackageId, "Acme.Palette", "Colour"), "Shade")
            .Should().Be("Shade.ToRecord()");
    }

    [Fact]
    public void ToValue_classifies_a_cross_package_enum_by_the_exact_module_when_another_module_name_extends_it()
    {
        var package = PackageWithDataTypes(
            ("Acme.Palette", "Colour", new DamlRecordDefinition([])),
            ("Acme.Palette.Extended", "Colour", Colours));

        Mapper(package).ToValue(new DamlTypeRef(CrossPackageId, "Acme.Palette.Extended", "Colour"), "Shade")
            .Should().Be("global::Acme.Palette.Extended.ColourExtensions.ToDamlEnum(Shade)");
        Mapper(package).ToValue(new DamlTypeRef(CrossPackageId, "Acme.Palette", "Colour"), "Shade")
            .Should().Be("Shade.ToRecord()");
    }

    [Fact]
    public void ToValue_serializes_a_user_generic_record_through_converter_lambdas()
    {
        var record = new DamlRecordDefinition([new DamlFieldDefinition("value", new DamlTypeVar("a"))]);
        var package = PackageWithGenericType("Acme.Boxes", "Box", record);

        Mapper(package).ToValue(GenericAppOfText("Acme.Boxes", "Box"), "Payload")
            .Should().Be("Payload.ToRecord(__t0 => (global::Daml.Runtime.Data.DamlValue)(new global::Daml.Runtime.Data.DamlText(__t0)))");
    }

    [Fact]
    public void FromValue_deserializes_a_user_generic_record_through_converter_lambdas()
    {
        var record = new DamlRecordDefinition([new DamlFieldDefinition("value", new DamlTypeVar("a"))]);
        var package = PackageWithGenericType("Acme.Boxes", "Box", record);

        Mapper(package).FromValue(GenericAppOfText("Acme.Boxes", "Box"), "value")
            .Should().Be("global::Acme.Boxes.Box<string>.FromRecord(value.As<global::Daml.Runtime.Data.DamlRecord>(), __v0 => __v0.As<global::Daml.Runtime.Data.DamlText>().Value, null)");
    }

    [Fact]
    public void ToValue_serializes_a_user_generic_variant_through_converter_lambdas()
    {
        var variant = new DamlVariantDefinition([new DamlVariantConstructor("Wrap", new DamlTypeVar("a"))]);
        var package = PackageWithGenericType("Acme.Choices", "Choice", variant);

        Mapper(package).ToValue(GenericAppOfText("Acme.Choices", "Choice"), "Payload")
            .Should().Be("Payload.ToVariant(__t0 => (global::Daml.Runtime.Data.DamlValue)(new global::Daml.Runtime.Data.DamlText(__t0)))");
    }

    [Fact]
    public void FromValue_deserializes_a_user_generic_variant_through_converter_lambdas()
    {
        var variant = new DamlVariantDefinition([new DamlVariantConstructor("Wrap", new DamlTypeVar("a"))]);
        var package = PackageWithGenericType("Acme.Choices", "Choice", variant);

        Mapper(package).FromValue(GenericAppOfText("Acme.Choices", "Choice"), "value")
            .Should().Be("global::Acme.Choices.Choice<string>.FromVariant(value.As<global::Daml.Runtime.Data.DamlVariant>(), __v0 => __v0.As<global::Daml.Runtime.Data.DamlText>().Value, null)");
    }

    [Fact]
    public void FromJson_reads_a_plain_record_type_ref_through_the_constrained_generic_overload()
    {
        var package = PackageWithDataTypes(("Acme.Widgets", "Widget", new DamlRecordDefinition([])));

        Mapper(package).FromJson(new DamlTypeRef(CrossPackageId, "Acme.Widgets", "Widget"), "json", "context")
            .Should().Be("global::Acme.Widgets.Widget.__ReadDamlLfJson(json, context)");
    }

    [Fact]
    public void FromJson_reads_a_plain_variant_type_ref_through_the_constrained_generic_overload()
    {
        var variant = new DamlVariantDefinition([new DamlVariantConstructor("Circle", Prim(DamlPrimitive.Text))]);
        var package = PackageWithDataTypes(("Acme.Shapes", "Shape", variant));

        Mapper(package).FromJson(new DamlTypeRef(CrossPackageId, "Acme.Shapes", "Shape"), "json", "context")
            .Should().Be("global::Acme.Shapes.Shape.__ReadDamlLfJson(json, context)");
    }

    [Fact]
    public void FromJson_reads_an_instantiated_generic_record_through_its_injected_reader_overload()
    {
        var record = new DamlRecordDefinition([new DamlFieldDefinition("value", new DamlTypeVar("a"))]);
        var package = PackageWithGenericType("Acme.Boxes", "Box", record);

        Mapper(package).FromJson(GenericAppOfText("Acme.Boxes", "Box"), "json", "context")
            .Should().Be(
                "global::Acme.Boxes.Box<string>.__ReadDamlLfJson(json, context, "
                + "(__json0, __ctx0) => global::Daml.Runtime.Serialization.DamlLfJsonDecoders.ReadText(__json0, __ctx0), null)");
    }

    private static DamlRecordDefinition BoxRecord() =>
        new([new DamlFieldDefinition("value", new DamlTypeVar("a"))]);

    private static DamlTypeApp BoxOf(DamlType argument) =>
        new(new DamlTypeRef(CrossPackageId, "Acme.Boxes", "Box"), [argument]);

    private static DamlPackage AcmeBoxPackage() =>
        PackageWithGenericType("Acme.Boxes", "Box", BoxRecord());

    private static readonly Dictionary<string, string> EnclosingTypeVariables = new()
    {
        ["a"] = "convertTA",
        ["b"] = "convertTB",
        ["absent:a"] = "absentTA",
        ["absent:b"] = "absentTB",
    };

    [Fact]
    public void FromValue_passes_None_as_the_absence_of_an_Optional_instantiation_of_a_generic_record()
    {
        Mapper(AcmeBoxPackage()).FromValue(BoxOf(App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))), "value")
            .Should().EndWith(", global::Daml.Runtime.Data.DamlOptional.None)");
    }

    [Fact]
    public void FromValue_passes_None_as_the_absence_of_a_nested_Optional_instantiation_of_a_generic_record()
    {
        var nested = App(DamlPrimitive.Optional, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)));

        Mapper(AcmeBoxPackage()).FromValue(BoxOf(nested), "value")
            .Should().EndWith(", global::Daml.Runtime.Data.DamlOptional.None)");
    }

    [Fact]
    public void FromValue_forwards_the_absence_parameter_of_the_enclosing_type_variable_it_instantiates()
    {
        Mapper(AcmeBoxPackage()).FromValue(BoxOf(new DamlTypeVar("b")), "value", EnclosingTypeVariables)
            .Should().EndWith("convertTB(__v0), absentTB)");
    }

    private static DamlTypeApp ChoiceOf(DamlType argument) =>
        new(new DamlTypeRef(CrossPackageId, "Acme.Choices", "Choice"), [argument]);

    private static DamlPackage AcmeChoicePackage() =>
        PackageWithGenericType(
            "Acme.Choices",
            "Choice",
            new DamlVariantDefinition([new DamlVariantConstructor("Wrap", new DamlTypeVar("a"))]));

    [Fact]
    public void FromValue_passes_None_as_the_absence_of_an_Optional_instantiation_of_a_generic_variant()
    {
        Mapper(AcmeChoicePackage()).FromValue(ChoiceOf(App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))), "value")
            .Should().EndWith(", global::Daml.Runtime.Data.DamlOptional.None)");
    }

    [Fact]
    public void FromValue_forwards_the_absence_parameter_of_the_enclosing_type_variable_a_generic_variant_instantiates()
    {
        Mapper(AcmeChoicePackage()).FromValue(ChoiceOf(new DamlTypeVar("b")), "value", EnclosingTypeVariables)
            .Should().EndWith("convertTB(__v0), absentTB)");
    }

    [Fact]
    public void FromJson_passes_None_as_the_absence_of_an_Optional_instantiation_of_a_generic_variant()
    {
        Mapper(AcmeChoicePackage()).FromJson(ChoiceOf(App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))), "json", "context")
            .Should().EndWith("global::Daml.Runtime.Data.DamlOptional.None)");
    }

    [Fact]
    public void FromJson_forwards_the_absence_parameter_of_the_enclosing_type_variable_a_generic_variant_instantiates()
    {
        var readers = new Dictionary<string, string>
        {
            ["a"] = "readTA",
            ["b"] = "readTB",
            ["absent:a"] = "absentTA",
            ["absent:b"] = "absentTB",
        };

        Mapper(AcmeChoicePackage()).FromJson(ChoiceOf(new DamlTypeVar("b")), "json", "context", readers)
            .Should().EndWith("readTB(__json0, __ctx0), absentTB)");
    }

    [Fact]
    public void FromJson_passes_None_as_the_absence_of_an_Optional_instantiation_of_a_generic_record()
    {
        Mapper(AcmeBoxPackage()).FromJson(BoxOf(App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))), "json", "context")
            .Should().EndWith(
                "global::Daml.Runtime.Serialization.DamlLfJsonDecoders.ReadOptional(__json0, __ctx0, "
                + "(__json1, __ctx1) => global::Daml.Runtime.Serialization.DamlLfJsonDecoders.ReadText(__json1, __ctx1)), "
                + "global::Daml.Runtime.Data.DamlOptional.None)");
    }

    [Fact]
    public void FromJson_forwards_the_absence_parameter_of_the_enclosing_type_variable_it_instantiates()
    {
        var readers = new Dictionary<string, string>
        {
            ["a"] = "readTA",
            ["b"] = "readTB",
            ["absent:a"] = "absentTA",
            ["absent:b"] = "absentTB",
        };

        Mapper(AcmeBoxPackage()).FromJson(BoxOf(new DamlTypeVar("b")), "json", "context", readers)
            .Should().EndWith("readTB(__json0, __ctx0), absentTB)");
    }

    [Fact]
    public void FromValue_passes_one_absence_per_component_of_a_Tuple2()
    {
        var package = StdlibPackage();
        var tuple = new DamlTypeApp(
            new DamlTypeRef(StdlibPackageId, "DA.Types", "Tuple2"),
            [Prim(DamlPrimitive.Text), App(DamlPrimitive.Optional, Prim(DamlPrimitive.Int64))]);

        Mapper(package).FromValue(tuple, "key")
            .Should().Be(
                "global::Daml.Runtime.Stdlib.Tuple2<string, global::Daml.Runtime.Stdlib.Optional<long>>.FromRecord("
                + "key.As<global::Daml.Runtime.Data.DamlRecord>(), "
                + "__v0 => __v0.As<global::Daml.Runtime.Data.DamlText>().Value, null, "
                + "__v1 => global::Daml.Runtime.Stdlib.Optional<long>.FromValue(__v1, __optional1 => __optional1.As<global::Daml.Runtime.Data.DamlInt64>().Value), "
                + "global::Daml.Runtime.Data.DamlOptional.None)");
    }

    [Fact]
    public void FromJson_passes_one_absence_per_component_of_a_Tuple3()
    {
        var package = StdlibPackage();
        var tuple = new DamlTypeApp(
            new DamlTypeRef(StdlibPackageId, "DA.Types", "Tuple3"),
            [Prim(DamlPrimitive.Text), App(DamlPrimitive.Optional, Prim(DamlPrimitive.Int64)), Prim(DamlPrimitive.Bool)]);

        Mapper(package).FromJson(tuple, "json", "context")
            .Should().Be(
                "global::Daml.Runtime.Serialization.DamlLfJsonDecoders.ReadTuple3(json, context, "
                + "(__json0, __ctx0) => global::Daml.Runtime.Serialization.DamlLfJsonDecoders.ReadText(__json0, __ctx0), null, "
                + "(__json0, __ctx0) => global::Daml.Runtime.Serialization.DamlLfJsonDecoders.ReadOptional(__json0, __ctx0, (__json1, __ctx1) => global::Daml.Runtime.Serialization.DamlLfJsonDecoders.ReadInt64(__json1, __ctx1)), "
                + "global::Daml.Runtime.Data.DamlOptional.None, "
                + "(__json0, __ctx0) => global::Daml.Runtime.Serialization.DamlLfJsonDecoders.ReadBool(__json0, __ctx0), null)");
    }

    [Fact]
    public void FromJson_reads_an_instantiated_generic_variant_through_its_injected_reader_overload()
    {
        var variant = new DamlVariantDefinition([new DamlVariantConstructor("Wrap", new DamlTypeVar("a"))]);
        var package = PackageWithGenericType("Acme.Choices", "Choice", variant);

        Mapper(package).FromJson(GenericAppOfText("Acme.Choices", "Choice"), "json", "context")
            .Should().Be(
                "global::Acme.Choices.Choice<string>.__ReadDamlLfJson(json, context, "
                + "(__json0, __ctx0) => global::Daml.Runtime.Serialization.DamlLfJsonDecoders.ReadText(__json0, __ctx0), null)");
    }

    [Fact]
    public void ToValue_maps_a_type_var_field_to_its_injected_converter_delegate()
    {
        var delegates = new Dictionary<string, string> { ["a"] = "convertTA" };

        Mapper().ToValue(new DamlTypeVar("a"), "Value", delegates)
            .Should().Be("convertTA(Value)");
    }

    [Fact]
    public void FromValue_maps_a_type_var_field_to_its_injected_converter_delegate()
    {
        var delegates = new Dictionary<string, string> { ["a"] = "convertTA" };

        Mapper().FromValue(new DamlTypeVar("a"), "value", delegates)
            .Should().Be("convertTA(value)");
    }

    [Fact]
    public void FromValue_falls_back_to_the_stub_for_a_type_var_absent_from_the_delegate_map()
    {
        var delegates = new Dictionary<string, string> { ["b"] = "convertTB" };

        Mapper().FromValue(new DamlTypeVar("a"), "value", delegates)
            .Should().Be("global::Daml.Runtime.Stdlib.GenericStub.NotImplemented<TA>(\"a\")");
    }

    [Fact]
    public void FromValue_emits_the_throwing_stub_for_a_higher_kinded_object_mapped_type()
    {
        var higherKinded = new DamlTypeApp(new DamlTypeVar("f"), [new DamlTypeVar("a")]);

        Mapper().FromValue(higherKinded, "value")
            .Should().Be("global::Daml.Runtime.Stdlib.GenericStub.NotImplemented<object>(\"value\")");
    }

    [Fact]
    public void FromValue_throws_codegen_exception_for_an_unclassifiable_type_ref_application()
    {
        var unresolvable = new DamlTypeApp(
            new DamlTypeRef(CrossPackageId, "Acme.Widgets", "Widget"),
            [Prim(DamlPrimitive.Text)]);

        Mapper().Invoking(m => m.FromValue(unresolvable, "value"))
            .Should().Throw<CodegenException>()
            .WithMessage("*Widget*");
    }

    private static readonly IReadOnlyDictionary<Type, IReadOnlyList<DamlType>> SubtypeRepresentatives =
        new Dictionary<Type, IReadOnlyList<DamlType>>
        {
            [typeof(DamlPrimitiveType)] = [Prim(DamlPrimitive.Text)],
            [typeof(DamlTypeApp)] = [App(DamlPrimitive.List, Prim(DamlPrimitive.Int64))],
            [typeof(DamlTypeRef)] = [new DamlTypeRef(LocalPackageId, "Test.Module", "Widget")],
            [typeof(DamlTypeVar)] = [new DamlTypeVar("a")],
            [typeof(DamlWrappedOptional)] =
            [
                .. Enum.GetValues<OptionalEncoding>()
                    .Select(encoding => new DamlWrappedOptional(Prim(DamlPrimitive.Text), encoding)),
            ],
            [typeof(DamlListType)] = [new DamlListType(Prim(DamlPrimitive.Int64))],
            [typeof(DamlOptionalType)] = [new DamlOptionalType(Prim(DamlPrimitive.Text))],
            [typeof(DamlTextMapType)] = [new DamlTextMapType(Prim(DamlPrimitive.Int64))],
            [typeof(DamlGenMapType)] =
            [
                new DamlGenMapType(Prim(DamlPrimitive.Text), Prim(DamlPrimitive.Int64)),
            ],
            [typeof(DamlContractIdType)] =
            [
                new DamlContractIdType(new DamlTypeRef(LocalPackageId, "Test.Module", "Widget")),
            ],
        };

    /// <summary>
    /// Both assemblies that can declare a concrete <see cref="DamlType"/> subtype: the neutral
    /// model's, and the emitter's — which owns <see cref="DamlWrappedOptional"/> since it moved
    /// out of the public model. Enumerating the model assembly alone would let the moved node
    /// silently drop out of the drift guard, which would then pass while exercising fewer
    /// subtypes.
    /// </summary>
    private static IEnumerable<Type> ConcreteDamlTypeSubtypes() =>
        new[] { typeof(DamlType).Assembly, typeof(OptionalRepresentation).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false } && typeof(DamlType).IsAssignableFrom(t));

    public static IEnumerable<object[]> EveryDamlTypeSubtype() =>
        SubtypeRepresentatives.Values.SelectMany(types => types).Select(type => new object[] { type });

    [Fact]
    public void DamlTypeMapper_every_parametric_stdlib_type_has_a_conversion_table_entry()
    {
        Mapper().StdlibConversionKeys.Should().BeEquivalentTo(StdlibPackages.ParametricStdlibTypes);
    }

    [Fact]
    public void DamlTypeMapper_every_parametric_stdlib_type_has_a_stdlib_mapping()
    {
        foreach (var (module, name) in StdlibPackages.ParametricStdlibTypes)
        {
            StdlibPackages.MapStdlibType(module, name)
                .Should().NotBeNullOrEmpty(
                    "ParametricStdlibTypes entry ({0}, {1}) must have a MapStdlibType result or codegen throws at emit time",
                    module, name);
        }
    }

    private static readonly IReadOnlyList<(string Module, string Type)> StdlibMappingKeys =
        StdlibPackages.ParametricStdlibTypes
            .Select(p => (Module: p.Module, Type: p.Name))
            .Concat(new[]
            {
                (Module: "DA.Date.Types", Type: "DayOfWeek"),
                (Module: "DA.Date.Types", Type: "Month"),
                (Module: "DA.Time.Types", Type: "RelTime"),
            })
            .ToList();

    public static IEnumerable<object[]> EveryStdlibMappingReturn() =>
        StdlibMappingKeys
            .Select(key => StdlibPackages.MapStdlibType(key.Module, key.Type))
            .Distinct()
            .Select(returned => new object[] { returned! });

    private static string StripGenericArity(string typeName)
    {
        var backtick = typeName.IndexOf('`');
        return backtick < 0 ? typeName : typeName[..backtick];
    }

    [Fact]
    public void DamlTypeMapper_every_stdlib_mapping_return_theory_has_cases()
    {
        EveryStdlibMappingReturn().Should().NotBeEmpty(
            "a wholesale null regression in MapStdlibType would otherwise empty the theory and pass silently");

        StdlibPackages.MapStdlibType("DA.Date.Types", "DayOfWeek").Should().NotBeNull(
            "StdlibMappingKeys entry (DA.Date.Types, DayOfWeek) must have a MapStdlibType result");
        StdlibPackages.MapStdlibType("DA.Time.Types", "RelTime").Should().NotBeNull(
            "StdlibMappingKeys entry (DA.Time.Types, RelTime) must have a MapStdlibType result");
    }

    [Theory]
    [MemberData(nameof(EveryStdlibMappingReturn))]
    public void DamlTypeMapper_every_stdlib_mapping_return_resolves_to_a_public_runtime_type(string? returnedTypeName)
    {
        returnedTypeName.Should().NotBeNull(
            "a key in StdlibMappingKeys returned null from MapStdlibType, so the switch and the guarded key set have drifted apart");

        var publicStdlibTypeNames = typeof(RelTime).Assembly.GetExportedTypes()
            .Where(t => t.Namespace == Daml.Runtime.RuntimeNamespaces.Stdlib)
            .Select(t => StripGenericArity(t.Name))
            .ToHashSet();

        publicStdlibTypeNames.Should().Contain(returnedTypeName,
            "MapStdlibType returns {0} as a C# reference into {1}; a renamed runtime record must fail loudly here instead of drifting into broken generated code",
            returnedTypeName, Daml.Runtime.RuntimeNamespaces.Stdlib);
    }

    [Fact]
    public void DamlTypeMapper_drift_guard_covers_every_concrete_subtype_discovered_by_reflection()
    {
        ConcreteDamlTypeSubtypes()
            .Should().BeEquivalentTo(SubtypeRepresentatives.Keys,
                "every concrete DamlType subtype needs a representative so the mapper drift-guard exercises it");
    }

    [Fact]
    public void DamlTypeMapper_drift_guard_theory_exercises_every_optional_encoding()
    {
        WrappedOptionalTheoryCases().Select(wrapped => wrapped.Encoding)
            .Should().BeEquivalentTo(
                Enum.GetValues<OptionalEncoding>(),
                "the encoding axis is invisible to the reflection drift guard, which walks DamlType "
                + "subtypes only, so an encoding the theory omits reaches no mapper method at all");
    }

    [Fact]
    public void DamlTypeMapper_emits_a_distinct_converter_pair_for_every_optional_encoding()
    {
        var wrappers = WrappedOptionalTheoryCases().ToList();
        wrappers.Should().HaveCountGreaterThan(1,
            "one representative would make the uniqueness assertions below hold for free");

        var mapper = Mapper();
        wrappers.Select(wrapped => mapper.ToValue(wrapped, "Field"))
            .Should().OnlyHaveUniqueItems(
                "two encodings sharing a serializer means one of them is being written in the other's "
                + "wire form, which compiles and is only rejected by the participant");
        wrappers.Select(wrapped => mapper.FromValue(wrapped, "value"))
            .Should().OnlyHaveUniqueItems(
                "two encodings sharing a deserializer means one of them is being read in the other's "
                + "wire form, which compiles and silently yields a different Optional");
    }

    private static IEnumerable<DamlWrappedOptional> WrappedOptionalTheoryCases() =>
        EveryDamlTypeSubtype().Select(row => row[0]).OfType<DamlWrappedOptional>();

    [Theory]
    [MemberData(nameof(EveryDamlTypeSubtype))]
    public void DamlTypeMapper_every_subtype_is_handled_by_all_three_methods_without_throwing(DamlType type)
    {
        var mapper = Mapper();

        mapper.Invoking(m => m.MapType(type)).Should().NotThrow<NotSupportedException>();
        mapper.Invoking(m => m.ToValue(type, "field")).Should().NotThrow<NotSupportedException>();
        mapper.Invoking(m => m.FromValue(type, "value")).Should().NotThrow<NotSupportedException>();
    }

    [Fact]
    public void MapType_emits_the_wrapper_for_an_optional_over_a_type_variable()
    {
        Mapper().MapType(App(DamlPrimitive.Optional, new DamlTypeVar("a")))
            .Should().Be("global::Daml.Runtime.Stdlib.Optional<TA>");
    }

    [Fact]
    public void MapsToReferenceType_places_an_optional_the_wrapper_carries_as_a_reference_type()
    {
        var mapper = Mapper();
        var nested = App(DamlPrimitive.Optional, App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)));

        mapper.MapType(nested).Should().Be("global::Daml.Runtime.Stdlib.Optional<global::Daml.Runtime.Stdlib.Optional<string>>");
        mapper.MapsToReferenceType(nested).Should().BeTrue();
    }

    [Fact]
    public void MapsToReferenceType_places_an_optional_nullable_syntax_carries_as_a_non_reference_type()
    {
        var mapper = Mapper();
        var flat = App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text));

        mapper.MapType(flat).Should().Be("string?");
        mapper.MapsToReferenceType(flat).Should().BeFalse();
    }

    [Fact]
    public void MapType_emits_the_wrapper_for_an_optional_argument_to_an_emitted_generic()
    {
        var mapper = Mapper(BoxPackage());

        mapper.MapType(new DamlTypeApp(
                new DamlTypeRef(CrossPackageId, "Acme.Shapes", "Box"),
                [App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))]))
            .Should().Be("global::Acme.Shapes.Box<global::Daml.Runtime.Stdlib.Optional<string>>");
    }

    [Fact]
    public void MapType_keeps_a_flat_optional_on_nullable_syntax()
    {
        Mapper().MapType(App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text)))
            .Should().Be("string?");
    }

    [Fact]
    public void ToValue_serializes_a_wrapped_optional_through_the_runtime_wrapper()
    {
        Mapper().ToValue(App(DamlPrimitive.Optional, new DamlTypeVar("a")), "Note")
            .Should().Be("Note.ToValue(__optional0 => global::Daml.Runtime.Stdlib.GenericStub.NotImplemented<global::Daml.Runtime.Data.DamlValue>(\"__optional0\"))");
    }

    [Fact]
    public void FromValue_deserializes_a_wrapped_optional_through_the_runtime_wrapper()
    {
        Mapper(BoxPackage()).FromValue(
                new DamlTypeApp(
                    new DamlTypeRef(CrossPackageId, "Acme.Shapes", "Box"),
                    [App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))]),
                "value")
            .Should().Contain("Optional<string>.FromValue(")
            .And.Contain("__optional");
    }

    [Fact]
    public void MapType_ToValue_and_FromValue_agree_on_which_optionals_are_wrapped()
    {
        var damlType = new DamlTypeApp(
            new DamlTypeRef(CrossPackageId, "Acme.Shapes", "Box"),
            [App(DamlPrimitive.Optional, Prim(DamlPrimitive.Text))]);
        var mapper = Mapper(BoxPackage());

        mapper.MapType(damlType).Should().Contain("Optional<string>");
        mapper.ToValue(damlType, "Boxed").Should().Contain("ToValue(");
        mapper.FromValue(damlType, "value").Should().Contain("Optional<string>.FromValue(");
    }

    [Fact]
    public void DamlTypeMapper_emits_the_wrapper_rather_than_the_object_fallback_for_a_wrapped_optional_node()
    {
        var wrapped = new DamlWrappedOptional(Prim(DamlPrimitive.Text), OptionalEncoding.Flat);
        var mapper = Mapper();

        mapper.MapType(wrapped).Should().Be("global::Daml.Runtime.Stdlib.Optional<string>");
        mapper.ToValue(wrapped, "Note").Should().Be("Note.ToValue(__optional0 => new global::Daml.Runtime.Data.DamlText(__optional0))");
        mapper.FromValue(wrapped, "value")
            .Should().Be("global::Daml.Runtime.Stdlib.Optional<string>.FromValue(value, __optional0 => __optional0.As<global::Daml.Runtime.Data.DamlText>().Value)");
    }

    /// <summary>
    /// The catalog's <see cref="DamlPrimitiveDisposition.SignatureOnly"/> rows, each with
    /// its catalog Daml-LF arity: the structural type-formers that are legal in type
    /// signatures but have no C# data-position mapping, so both their bare and their
    /// applied forms must fail loudly at every data-position entry point.
    /// </summary>
    public static TheoryData<DamlPrimitive, int> SignatureOnlyRows()
    {
        var rows = new TheoryData<DamlPrimitive, int>();
        foreach (var row in DamlPrimitiveCatalog.Rows.Where(
                     row => row.Disposition == DamlPrimitiveDisposition.SignatureOnly))
        {
            rows.Add(row.Primitive!.Value, row.Arity);
        }

        return rows;
    }

    /// <summary>
    /// The <see cref="DamlPrimitive"/> identities of the
    /// <see cref="DamlPrimitiveDisposition.SignatureOnly"/> rows, for the bare-form probes.
    /// </summary>
    public static TheoryData<DamlPrimitive> SignatureOnlyPrimitives()
    {
        var rows = new TheoryData<DamlPrimitive>();
        foreach (var row in DamlPrimitiveCatalog.Rows.Where(
                     row => row.Disposition == DamlPrimitiveDisposition.SignatureOnly))
        {
            rows.Add(row.Primitive!.Value);
        }

        return rows;
    }

    /// <summary>
    /// The applied shape a signature position actually carries for the row's arity
    /// (<c>Update X</c>, <c>Arrow A B</c>). Arity-0 formers are probed applied to a single
    /// argument too: whatever shape arrives, an application of a signature-only builtin in
    /// a data position must never silently map to <c>object</c>.
    /// </summary>
    private static DamlTypeApp AppliedSignatureOnly(DamlPrimitive primitive, int arity) =>
        arity switch
        {
            2 => App(primitive, Prim(DamlPrimitive.Text), Prim(DamlPrimitive.Int64)),
            _ => App(primitive, Prim(DamlPrimitive.Text)),
        };

    [Theory]
    [MemberData(nameof(SignatureOnlyRows))]
    public void MapType_applied_signature_only_builtin_throws_not_supported_naming_the_builtin(
        DamlPrimitive primitive,
        int arity)
    {
        var act = () => Mapper().MapType(AppliedSignatureOnly(primitive, arity));

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"*{primitive}*signature-only*",
                "an applied signature-only builtin in a data position must fail loudly, naming the builtin — " +
                "before the builtin-catalog milestone's follow-up it silently fell through to the 'object' " +
                "catch-all, and no cataloged builtin shape may reach that fallback");
    }

    [Theory]
    [InlineData(DamlPrimitive.List)]
    [InlineData(DamlPrimitive.Optional)]
    [InlineData(DamlPrimitive.TextMap)]
    [InlineData(DamlPrimitive.GenMap)]
    [InlineData(DamlPrimitive.ContractId)]
    public void MapType_type_constructor_applied_to_the_wrong_arity_throws_rather_than_mapping_to_object(
        DamlPrimitive constructor)
    {
        var act = () => Mapper().MapType(App(constructor));

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"*{constructor}*",
                "a cataloged builtin applied with the wrong argument count is still a cataloged builtin shape — " +
                "it must fail loudly instead of silently falling through to the 'object' catch-all");
    }

    [Theory]
    [InlineData(DamlPrimitive.List)]
    [InlineData(DamlPrimitive.Optional)]
    [InlineData(DamlPrimitive.TextMap)]
    [InlineData(DamlPrimitive.GenMap)]
    [InlineData(DamlPrimitive.ContractId)]
    public void ToValue_type_constructor_applied_to_the_wrong_arity_throws_rather_than_emitting_a_stub(
        DamlPrimitive constructor)
    {
        var act = () => Mapper().ToValue(App(constructor), "Field");

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"*{constructor}*",
                "serializing a constructor applied with the wrong argument count must fail loudly — " +
                "the pre-migration ToValue arms were arity-unconstrained, so a wrong-arity application " +
                "either silently emitted a placeholder or threw an unnamed ArgumentOutOfRangeException " +
                "while indexing the argument");
    }

    [Theory]
    [MemberData(nameof(SignatureOnlyRows))]
    public void ToValue_applied_signature_only_builtin_throws_not_supported_naming_the_builtin(
        DamlPrimitive primitive,
        int arity)
    {
        var act = () => Mapper().ToValue(AppliedSignatureOnly(primitive, arity), "Field");

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"*{primitive}*signature-only*",
                "serializing a data position typed with an applied signature-only builtin must fail loudly " +
                "instead of silently emitting a GenericStub.NotImplemented placeholder");
    }

    [Theory]
    [MemberData(nameof(SignatureOnlyRows))]
    public void FromValue_applied_signature_only_builtin_throws_not_supported_naming_the_builtin(
        DamlPrimitive primitive,
        int arity)
    {
        var act = () => Mapper().FromValue(AppliedSignatureOnly(primitive, arity), "value");

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"*{primitive}*signature-only*",
                "deserializing a data position typed with an applied signature-only builtin must fail loudly " +
                "instead of silently emitting a GenericStub.NotImplemented placeholder");
    }

    [Theory]
    [MemberData(nameof(SignatureOnlyPrimitives))]
    public void ClassifyCollection_bare_signature_only_builtin_throws_not_supported_naming_the_builtin(
        DamlPrimitive primitive)
    {
        var act = () => Mapper().ClassifyCollection(Prim(primitive));

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"*{primitive}*signature-only*",
                "classifying the collection shape of a data position typed with a bare signature-only " +
                "builtin must fail loudly — returning None would let the emitter keep walking into a " +
                "member it can never map");
    }

    [Theory]
    [MemberData(nameof(SignatureOnlyRows))]
    public void ClassifyCollection_applied_signature_only_builtin_throws_not_supported_naming_the_builtin(
        DamlPrimitive primitive,
        int arity)
    {
        var act = () => Mapper().ClassifyCollection(AppliedSignatureOnly(primitive, arity));

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"*{primitive}*signature-only*",
                "classifying the collection shape of a data position typed with an applied signature-only " +
                "builtin must fail loudly — returning None would let the emitter keep walking into a " +
                "member it can never map");
    }

    [Theory]
    [MemberData(nameof(SignatureOnlyPrimitives))]
    public void MapType_bare_signature_only_builtin_throws_not_supported_naming_the_builtin(DamlPrimitive primitive)
    {
        var act = () => Mapper().MapType(Prim(primitive));

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"*{primitive}*signature-only*",
                "the bare-primitive switches carry the signature-only arms for MapType; this pins the " +
                "message so a bare builtin in a data position is never confused with a mapping gap");
    }

    [Theory]
    [MemberData(nameof(SignatureOnlyPrimitives))]
    public void ToValue_bare_signature_only_builtin_throws_not_supported_naming_the_builtin(DamlPrimitive primitive)
    {
        var act = () => Mapper().ToValue(Prim(primitive), "Field");

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"*{primitive}*signature-only*",
                "the bare-primitive switches carry the signature-only arms for ToValue; this pins the " +
                "message so a bare builtin in a data position is never confused with a mapping gap");
    }

    [Theory]
    [MemberData(nameof(SignatureOnlyPrimitives))]
    public void FromValue_bare_signature_only_builtin_throws_not_supported_naming_the_builtin(DamlPrimitive primitive)
    {
        var act = () => Mapper().FromValue(Prim(primitive), "value");

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"*{primitive}*signature-only*",
                "the bare-primitive switches carry the signature-only arms for FromValue; this pins the " +
                "message so a bare builtin in a data position is never confused with a mapping gap");
    }

    [Theory]
    [MemberData(nameof(SignatureOnlyPrimitives))]
    public void FromJson_bare_signature_only_builtin_throws_not_supported_naming_the_builtin(DamlPrimitive primitive)
    {
        var act = () => Mapper().FromJson(Prim(primitive), "json", "context");

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"*{primitive}*signature-only*",
                "a JSON data position typed with a bare signature-only builtin fails at codegen time rather " +
                "than emitting a runtime DamlLfJsonDecoders.ReadUnsupported call");
    }

    [Theory]
    [MemberData(nameof(SignatureOnlyRows))]
    public void FromJson_applied_signature_only_builtin_throws_not_supported_naming_the_builtin(
        DamlPrimitive primitive,
        int arity)
    {
        var act = () => Mapper().FromJson(AppliedSignatureOnly(primitive, arity), "json", "context");

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"*{primitive}*signature-only*",
                "a JSON data position typed with an applied signature-only builtin (Update X, Arrow A B) fails " +
                "at codegen time rather than emitting a runtime DamlLfJsonDecoders.ReadUnsupported call");
    }
}
