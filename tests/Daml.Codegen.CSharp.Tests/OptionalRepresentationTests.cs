// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// The rewriter over the typed nodes: every arm carries its children's path state — an
/// Optional's child is parented by an Optional, a GenMap's key takes the flat wrapper —
/// so the same scenarios the legacy application spellings pinned hold unchanged for
/// <see cref="DamlOptionalType"/>, <see cref="DamlListType"/>, <see cref="DamlGenMapType"/>
/// and the other nodes the readers deliver.
/// </summary>
public class OptionalRepresentationTests
{
    private const string LocalPackageId = "local-pkg";
    private const string EmittedPackageId = "emitted-pkg";
    private const string StdlibPackageId = "stdlib-pkg";

    private sealed class StubResolver : ICrossPackageResolver
    {
        public string Resolve(DamlTypeRef typeRef, PackageEmitContext context) => "Resolved";

        public IReadOnlySet<string> DiscoveredExternalPackageIds => new HashSet<string>();

        public DamlPackage? LookupPackage(string packageId) => null;
    }

    private static DamlPackage LocalPackage(params DamlModule[] modules) =>
        new()
        {
            PackageId = LocalPackageId,
            Name = "local",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = modules,
            DependencyReferences = []
        };

    private static DamlType Rewrite(DamlType type) =>
        OptionalRepresentation.Rewrite(type, LocalPackage(), new StubResolver());

    private sealed class DeclaringResolver(params DamlPackage[] packages) : ICrossPackageResolver
    {
        private readonly IReadOnlyDictionary<string, DamlPackage> _packages =
            packages.ToDictionary(package => package.PackageId);

        public string Resolve(DamlTypeRef typeRef, PackageEmitContext context) => "Resolved";

        public IReadOnlySet<string> DiscoveredExternalPackageIds => new HashSet<string>();

        public DamlPackage? LookupPackage(string packageId) =>
            _packages.TryGetValue(packageId, out var package) ? package : null;
    }

    private static DamlDataType Generic(string name, params DamlFieldDefinition[] fields) =>
        new()
        {
            Name = name,
            TypeParams = ["a"],
            Definition = new DamlRecordDefinition(fields),
        };

    private static DamlModule ShapesModule(params DamlDataType[] dataTypes) =>
        new() { Name = "Acme.Shapes", Templates = [], Interfaces = [], DataTypes = dataTypes };

    private static DamlPackage EmittedPackage(params DamlDataType[] dataTypes) =>
        new()
        {
            PackageId = EmittedPackageId,
            Name = "acme",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [ShapesModule(dataTypes)],
            DependencyReferences = []
        };

    private static DamlPackage StdlibPackage(params DamlDataType[] dataTypes) =>
        new()
        {
            PackageId = StdlibPackageId,
            Name = "daml-stdlib",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [new DamlModule
            {
                Name = "DA.Types",
                Templates = [],
                Interfaces = [],
                DataTypes = dataTypes,
            }],
            DependencyReferences = []
        };

    private static DamlType RewriteAgainst(DamlType type, params DamlDataType[] declarations) =>
        OptionalRepresentation.Rewrite(type, LocalPackage(), new DeclaringResolver(EmittedPackage(declarations)));

    private static DamlType RewriteAgainstStdlib(DamlType type, params DamlDataType[] declarations) =>
        OptionalRepresentation.Rewrite(type, LocalPackage(), new DeclaringResolver(StdlibPackage(declarations)));

    private static DamlFieldDefinition Field(string name, DamlType type) => new(name, type);

    private static DamlPrimitiveType Prim(DamlPrimitive primitive) => new(primitive);

    private static DamlTypeApp Emitted(string name, params DamlType[] arguments) =>
        new(new DamlTypeRef(EmittedPackageId, "Acme.Shapes", name), arguments);

    private static DamlTypeApp StdlibEither(params DamlType[] arguments) =>
        new(new DamlTypeRef(StdlibPackageId, "DA.Types", "Either"), arguments);

    [Fact]
    public void Rewrite_leaves_a_flat_optional_typed_node_as_a_nullable_type()
    {
        Rewrite(new DamlOptionalType(Prim(DamlPrimitive.Text)))
            .Should().Be(new DamlOptionalType(Prim(DamlPrimitive.Text)));
    }

    [Fact]
    public void Rewrite_wraps_an_optional_typed_node_over_a_type_variable()
    {
        Rewrite(new DamlOptionalType(new DamlTypeVar("a")))
            .Should().Be(new DamlWrappedOptional(new DamlTypeVar("a"), OptionalEncoding.Flat));
    }

    [Fact]
    public void Rewrite_routes_the_legacy_optional_application_spelling_to_the_same_wrapper_decision_as_its_typed_node()
    {
        var legacy = new DamlTypeApp(Prim(DamlPrimitive.Optional), [new DamlTypeVar("a")]);
        var node = new DamlOptionalType(new DamlTypeVar("a"));

        Rewrite(legacy).Should().Be(Rewrite(node),
            "a wrapping decision replaces the whole Optional with the wrapper node regardless of " +
            "spelling, so the legacy application and its typed node rewrite to the identical tree — " +
            "a hand-built model that never passed a reader is rewritten exactly like a reader-fed one");
    }

    [Fact]
    public void Rewrite_wraps_an_optional_typed_node_passed_to_an_emitted_generic_in_the_flat_encoding()
    {
        Rewrite(Emitted("Box", new DamlOptionalType(Prim(DamlPrimitive.Text))))
            .Should().Be(Emitted("Box", new DamlWrappedOptional(Prim(DamlPrimitive.Text), OptionalEncoding.Flat)));
    }

    [Fact]
    public void Rewrite_keeps_the_outer_optional_typed_node_nullable_around_an_emitted_generic()
    {
        var damlType = new DamlOptionalType(Emitted("Box", new DamlOptionalType(Prim(DamlPrimitive.Text))));

        Rewrite(damlType).Should().Be(new DamlOptionalType(
            Emitted("Box", new DamlWrappedOptional(Prim(DamlPrimitive.Text), OptionalEncoding.Flat))));
    }

    [Fact]
    public void Rewrite_leaves_an_optional_under_a_list_alone_even_inside_an_optional()
    {
        var damlType = new DamlOptionalType(new DamlListType(new DamlOptionalType(Prim(DamlPrimitive.Text))));

        Rewrite(damlType).Should().Be(damlType);
    }

    [Fact]
    public void Rewrite_leaves_an_optional_typed_node_under_a_list_alone()
    {
        var damlType = new DamlListType(new DamlOptionalType(Prim(DamlPrimitive.Text)));

        Rewrite(damlType).Should().Be(damlType);
    }

    [Fact]
    public void Rewrite_wraps_both_levels_of_a_chain_in_the_nested_encoding()
    {
        Rewrite(new DamlOptionalType(new DamlOptionalType(Prim(DamlPrimitive.Text))))
            .Should().Be(new DamlWrappedOptional(
                new DamlWrappedOptional(Prim(DamlPrimitive.Text), OptionalEncoding.NestedChain),
                OptionalEncoding.NestedChain));
    }

    [Fact]
    public void Rewrite_wraps_every_level_of_a_three_deep_chain()
    {
        var damlType = new DamlOptionalType(
            new DamlOptionalType(new DamlOptionalType(Prim(DamlPrimitive.Text))));

        Rewrite(damlType).Should().Be(new DamlWrappedOptional(
            new DamlWrappedOptional(
                new DamlWrappedOptional(Prim(DamlPrimitive.Text), OptionalEncoding.NestedChain),
                OptionalEncoding.NestedChain),
            OptionalEncoding.NestedChain));
    }

    [Fact]
    public void Rewrite_wraps_a_chain_over_a_type_variable_at_every_level()
    {
        Rewrite(new DamlOptionalType(new DamlOptionalType(new DamlTypeVar("a"))))
            .Should().Be(new DamlWrappedOptional(
                new DamlWrappedOptional(new DamlTypeVar("a"), OptionalEncoding.NestedChain),
                OptionalEncoding.NestedChain));
    }

    [Fact]
    public void Rewrite_wraps_a_chain_passed_to_an_emitted_generic_as_a_chain_not_as_flat()
    {
        var damlType = Emitted(
            "Box",
            new DamlOptionalType(new DamlOptionalType(Prim(DamlPrimitive.Text))));

        Rewrite(damlType).Should().Be(Emitted(
            "Box",
            new DamlWrappedOptional(
                new DamlWrappedOptional(Prim(DamlPrimitive.Text), OptionalEncoding.NestedChain),
                OptionalEncoding.NestedChain)));
    }

    [Fact]
    public void Rewrite_does_not_carry_the_chain_context_across_an_emitted_generic()
    {
        var damlType = new DamlOptionalType(
            Emitted("Box", new DamlOptionalType(Prim(DamlPrimitive.Text))));

        Rewrite(damlType).Should().Be(new DamlOptionalType(
            Emitted("Box", new DamlWrappedOptional(Prim(DamlPrimitive.Text), OptionalEncoding.Flat))));
    }

    [Fact]
    public void Rewrite_wraps_an_optional_typed_node_passed_to_a_stdlib_generic_in_the_flat_encoding()
    {
        var damlType = StdlibEither(
            Prim(DamlPrimitive.Text), new DamlOptionalType(Prim(DamlPrimitive.Text)));

        RewriteAgainstStdlib(damlType).Should().Be(StdlibEither(
            Prim(DamlPrimitive.Text),
            new DamlWrappedOptional(Prim(DamlPrimitive.Text), OptionalEncoding.Flat)));
    }

    [Fact]
    public void Rewrite_wraps_a_chain_passed_to_a_stdlib_generic_as_a_chain_not_as_flat()
    {
        var damlType = StdlibEither(
            Prim(DamlPrimitive.Text),
            new DamlOptionalType(new DamlOptionalType(Prim(DamlPrimitive.Text))));

        RewriteAgainstStdlib(damlType).Should().Be(StdlibEither(
            Prim(DamlPrimitive.Text),
            new DamlWrappedOptional(
                new DamlWrappedOptional(Prim(DamlPrimitive.Text), OptionalEncoding.NestedChain),
                OptionalEncoding.NestedChain)));
    }

    [Fact]
    public void Rewrite_keeps_the_outer_optional_typed_node_nullable_around_a_stdlib_generic()
    {
        var damlType = new DamlOptionalType(
            StdlibEither(Prim(DamlPrimitive.Text), new DamlOptionalType(Prim(DamlPrimitive.Text))));

        RewriteAgainstStdlib(damlType).Should().Be(new DamlOptionalType(
            StdlibEither(
                Prim(DamlPrimitive.Text),
                new DamlWrappedOptional(Prim(DamlPrimitive.Text), OptionalEncoding.Flat))));
    }

    [Fact]
    public void Rewrite_rejects_an_optional_typed_node_passed_to_a_stdlib_generic_that_wraps_that_parameter()
    {
        var wrappingEither = Generic("Either", Field("item", new DamlOptionalType(new DamlTypeVar("a"))));

        var act = () => RewriteAgainstStdlib(
            StdlibEither(new DamlOptionalType(Prim(DamlPrimitive.Text)), Prim(DamlPrimitive.Int64)),
            wrappingEither);

        act.Should().Throw<CodegenException>()
            .WithMessage("*Optional as the 'a' type argument of DA.Types:Either*")
            .WithMessage("*one array level short*");
    }

    [Fact]
    public void Rewrite_is_idempotent()
    {
        var damlType = Emitted("Box", new DamlOptionalType(Prim(DamlPrimitive.Text)));

        Rewrite(Rewrite(damlType)).Should().Be(Rewrite(damlType));
    }

    [Fact]
    public void Rewrite_is_idempotent_over_an_optional_type_variable()
    {
        var damlType = new DamlOptionalType(new DamlTypeVar("a"));

        Rewrite(Rewrite(damlType)).Should().Be(Rewrite(damlType));
    }

    [Fact]
    public void Rewrite_is_idempotent_over_a_nested_optional_chain()
    {
        var damlType = new DamlOptionalType(new DamlOptionalType(Prim(DamlPrimitive.Text)));

        Rewrite(Rewrite(damlType)).Should().Be(Rewrite(damlType));
    }

    [Fact]
    public void Rewrite_marks_an_unrewritten_optional_under_a_chain_wrapper_as_a_chain()
    {
        var damlType = new DamlWrappedOptional(
            new DamlOptionalType(Prim(DamlPrimitive.Text)), OptionalEncoding.NestedChain);

        Rewrite(damlType).Should().Be(new DamlWrappedOptional(
            new DamlWrappedOptional(Prim(DamlPrimitive.Text), OptionalEncoding.NestedChain),
            OptionalEncoding.NestedChain));
    }

    [Fact]
    public void Rewrite_rejects_an_optional_typed_node_passed_to_an_emitted_generic_that_wraps_that_parameter()
    {
        var crate = Generic("Crate", Field("item", new DamlOptionalType(new DamlTypeVar("a"))));

        var act = () => RewriteAgainst(
            Emitted("Crate", new DamlOptionalType(Prim(DamlPrimitive.Text))), crate);

        act.Should().Throw<CodegenException>()
            .WithMessage("*Optional as the 'a' type argument of Acme.Shapes:Crate*")
            .WithMessage("*one array level short*");
    }

    [Fact]
    public void Rewrite_rejects_a_chain_passed_to_an_emitted_generic_that_wraps_that_parameter()
    {
        var crate = Generic("Crate", Field("item", new DamlOptionalType(new DamlTypeVar("a"))));
        var chain = new DamlOptionalType(new DamlOptionalType(Prim(DamlPrimitive.Text)));

        var act = () => RewriteAgainst(Emitted("Crate", chain), crate);

        act.Should().Throw<CodegenException>()
            .WithMessage("*Optional as the 'a' type argument of Acme.Shapes:Crate*")
            .WithMessage("*one array level short*");
    }

    [Fact]
    public void Rewrite_rejects_an_optional_passed_to_a_generic_declared_in_the_package_being_emitted()
    {
        var crate = Generic("Crate", Field("item", new DamlOptionalType(new DamlTypeVar("a"))));
        var selfReference = new DamlTypeApp(
            new DamlTypeRef(string.Empty, "Acme.Shapes", "Crate"),
            [new DamlOptionalType(Prim(DamlPrimitive.Text))]);

        var act = () => OptionalRepresentation.Rewrite(
            selfReference, LocalPackage(ShapesModule(crate)), new DeclaringResolver());

        act.Should().Throw<CodegenException>()
            .WithMessage("*Optional as the 'a' type argument of Acme.Shapes:Crate*");
    }

    [Fact]
    public void Rewrite_rejects_an_optional_reaching_a_wrapping_parameter_through_another_generic()
    {
        var crate = Generic("Crate", Field("item", new DamlOptionalType(new DamlTypeVar("a"))));
        var outer = Generic("Outer", Field("crate", Emitted("Crate", new DamlTypeVar("a"))));

        var act = () => RewriteAgainst(
            Emitted("Outer", new DamlOptionalType(Prim(DamlPrimitive.Text))), crate, outer);

        act.Should().Throw<CodegenException>()
            .WithMessage("*Optional as the 'a' type argument of Acme.Shapes:Outer*");
    }

    [Fact]
    public void Rewrite_rejects_an_optional_passed_to_a_generic_that_wraps_the_parameter_inside_a_list()
    {
        var shelf = Generic(
            "Shelf", Field("items", new DamlListType(new DamlOptionalType(new DamlTypeVar("a")))));

        var act = () => RewriteAgainst(
            Emitted("Shelf", new DamlOptionalType(Prim(DamlPrimitive.Text))), shelf);

        act.Should().Throw<CodegenException>()
            .WithMessage("*Optional as the 'a' type argument of Acme.Shapes:Shelf*");
    }

    [Fact]
    public void Rewrite_wraps_a_chain_passed_to_an_emitted_generic_whose_declaration_adds_no_optional_level()
    {
        var box = Generic("Box", Field("item", new DamlTypeVar("a")));
        var chain = new DamlOptionalType(new DamlOptionalType(Prim(DamlPrimitive.Text)));

        RewriteAgainst(Emitted("Box", chain), box).Should().Be(Emitted(
            "Box",
            new DamlWrappedOptional(
                new DamlWrappedOptional(Prim(DamlPrimitive.Text), OptionalEncoding.NestedChain),
                OptionalEncoding.NestedChain)));
    }

    [Fact]
    public void Rewrite_leaves_an_optional_passed_to_a_generic_that_wraps_only_a_list_of_the_parameter()
    {
        var shelf = Generic(
            "Shelf", Field("items", new DamlOptionalType(new DamlListType(new DamlTypeVar("a")))));

        RewriteAgainst(Emitted("Shelf", new DamlOptionalType(Prim(DamlPrimitive.Text))), shelf)
            .Should().Be(Emitted("Shelf", new DamlWrappedOptional(Prim(DamlPrimitive.Text), OptionalEncoding.Flat)));
    }

    [Fact]
    public void Rewrite_terminates_on_a_generic_whose_declaration_references_itself()
    {
        var node = Generic(
            "Node", Field("next", new DamlOptionalType(Emitted("Node", new DamlTypeVar("a")))));

        RewriteAgainst(Emitted("Node", new DamlOptionalType(Prim(DamlPrimitive.Text))), node)
            .Should().Be(Emitted("Node", new DamlWrappedOptional(Prim(DamlPrimitive.Text), OptionalEncoding.Flat)));
    }

    [Fact]
    public void Rewrite_wraps_an_optional_in_gen_map_key_position()
    {
        var damlType = new DamlGenMapType(
            new DamlOptionalType(Prim(DamlPrimitive.Text)),
            Prim(DamlPrimitive.Int64));

        Rewrite(damlType).Should().Be(new DamlGenMapType(
            new DamlWrappedOptional(Prim(DamlPrimitive.Text), OptionalEncoding.Flat),
            Prim(DamlPrimitive.Int64)));
    }

    [Fact]
    public void Rewrite_routes_the_legacy_gen_map_application_spelling_to_the_same_key_rule_as_its_typed_node()
    {
        var legacy = new DamlTypeApp(
            Prim(DamlPrimitive.GenMap),
            [new DamlOptionalType(Prim(DamlPrimitive.Text)), Prim(DamlPrimitive.Int64)]);

        Rewrite(legacy).Should().Be(new DamlTypeApp(
            Prim(DamlPrimitive.GenMap),
            [
                new DamlWrappedOptional(Prim(DamlPrimitive.Text), OptionalEncoding.Flat),
                Prim(DamlPrimitive.Int64),
            ]),
            "the legacy application spelling routes to the same GenMap-key decision as its typed " +
            "node — the flat-wrapper rule a dictionary's notnull key parameter forces — while " +
            "preserving the spelling it arrived in");
    }

    [Fact]
    public void Rewrite_leaves_an_optional_in_gen_map_value_position_alone()
    {
        var damlType = new DamlGenMapType(
            Prim(DamlPrimitive.Text),
            new DamlOptionalType(Prim(DamlPrimitive.Text)));

        Rewrite(damlType).Should().Be(damlType);
    }

    [Fact]
    public void Rewrite_wraps_every_level_of_a_nested_optional_gen_map_key_as_a_chain()
    {
        var damlType = new DamlGenMapType(
            new DamlOptionalType(new DamlOptionalType(Prim(DamlPrimitive.Text))),
            Prim(DamlPrimitive.Int64));

        Rewrite(damlType).Should().Be(new DamlGenMapType(
            new DamlWrappedOptional(
                new DamlWrappedOptional(Prim(DamlPrimitive.Text), OptionalEncoding.NestedChain),
                OptionalEncoding.NestedChain),
            Prim(DamlPrimitive.Int64)));
    }

    [Fact]
    public void Rewrite_leaves_an_optional_under_a_list_in_gen_map_key_position_alone()
    {
        var damlType = new DamlGenMapType(
            new DamlListType(new DamlOptionalType(Prim(DamlPrimitive.Text))),
            Prim(DamlPrimitive.Int64));

        Rewrite(damlType).Should().Be(damlType);
    }

    [Fact]
    public void Rewrite_wraps_a_gen_map_key_under_an_outer_nullable_optional()
    {
        var damlType = new DamlOptionalType(new DamlGenMapType(
            new DamlOptionalType(Prim(DamlPrimitive.Text)),
            Prim(DamlPrimitive.Int64)));

        Rewrite(damlType).Should().Be(new DamlOptionalType(new DamlGenMapType(
            new DamlWrappedOptional(Prim(DamlPrimitive.Text), OptionalEncoding.Flat),
            Prim(DamlPrimitive.Int64))));
    }

    [Fact]
    public void Rewrite_is_idempotent_over_an_optional_gen_map_key()
    {
        var damlType = new DamlGenMapType(
            new DamlOptionalType(Prim(DamlPrimitive.Text)),
            Prim(DamlPrimitive.Int64));

        Rewrite(Rewrite(damlType)).Should().Be(Rewrite(damlType));
    }

    [Fact]
    public void Rewrite_rejects_a_type_tree_deeper_than_the_supported_bound()
    {
        DamlType damlType = Prim(DamlPrimitive.Text);
        for (var level = 0; level < 300; level++)
        {
            damlType = new DamlListType(damlType);
        }

        var act = () => Rewrite(damlType);

        act.Should().Throw<InvalidDataException>().WithMessage("*depth*");
    }
}
