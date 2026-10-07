// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Daml.Runtime.Stdlib;
using Daml.Testing.StjRoundTrip;
using Xunit;

namespace Daml.Runtime.Tests;

public class StjSweepScopeTests
{
    [Fact]
    public void StjSweepScope_ValueTypes_drops_the_types_that_are_not_value_types()
    {
        Type[] exported =
        [
            typeof(Plain),
            typeof(StaticHolder),
            typeof(IThing),
            typeof(PlainConverter),
            typeof(PlainException),
            typeof(PlainAttribute),
            typeof(PlainHandler),
            typeof(PlainExtensions),
        ];

        var valueTypes = StjSweepScope.ValueTypes(exported, new HashSet<Type>());

        valueTypes.Should().Equal(typeof(Plain));
    }

    [Fact]
    public void StjSweepScope_ValueTypes_drops_the_explicitly_named_types()
    {
        Type[] exported = [typeof(Plain), typeof(Descriptor<>), typeof(Descriptor<>.Plumbing)];

        var valueTypes = StjSweepScope.ValueTypes(exported, new HashSet<Type> { typeof(Descriptor<>), typeof(Descriptor<>.Plumbing) });

        valueTypes.Should().Equal(typeof(Plain));
    }

    [Fact]
    public void StjSweepScope_StaleExclusions_reports_a_named_type_that_is_not_exported()
    {
        Type[] exported = [typeof(Plain), typeof(Descriptor<>)];

        var stale = StjSweepScope.StaleExclusions(exported, new HashSet<Type> { typeof(Descriptor<>), typeof(Pair) });

        stale.Should().Equal(typeof(Pair));
    }

    [Fact]
    public void StjSweepScope_CasesWithAsSelfArms_yields_one_case_per_instance_declared_as_its_key()
    {
        var samples = new Dictionary<Type, object[]>
        {
            [typeof(Plain)] = [new Plain(1)],
            [typeof(Pair)] = [new Pair("a", 2), new Pair("b", 3)],
        };

        var cases = StjSweepScope.CasesWithAsSelfArms(samples);

        cases.Select(c => (c.Id, c.DeclaredType)).Should().Equal(
            ("Plain", typeof(Plain)),
            ("Pair~1", typeof(Pair)),
            ("Pair~2", typeof(Pair)));
    }

    [Fact]
    public void StjSweepScope_CasesWithAsSelfArms_numbers_the_instances_of_one_arm_and_yields_as_self_cases_for_arms()
    {
        var samples = new Dictionary<Type, object[]>
        {
            [typeof(Opt<string>)] = [new Opt<string>.Some("x"), new Opt<string>.Some("y"), new Opt<string>.Nothing()],
        };

        var cases = StjSweepScope.CasesWithAsSelfArms(samples);

        cases.Select(c => (c.Id, c.DeclaredType)).Should().Equal(
            ("Opt<String>/Some~1", typeof(Opt<string>)),
            ("Opt<String>/Some~1/as-self", typeof(Opt<string>.Some)),
            ("Opt<String>/Some~2", typeof(Opt<string>)),
            ("Opt<String>/Some~2/as-self", typeof(Opt<string>.Some)),
            ("Opt<String>/Nothing", typeof(Opt<string>)),
            ("Opt<String>/Nothing/as-self", typeof(Opt<string>.Nothing)));
    }

    [Fact]
    public void StjSweepScope_CasesWithAsSelfArms_rejects_an_instance_not_assignable_to_its_key()
    {
        var samples = new Dictionary<Type, object[]> { [typeof(Plain)] = [new Pair("a", 2)] };

        var build = () => StjSweepScope.CasesWithAsSelfArms(samples);

        build.Should().Throw<ArgumentException>().WithMessage("*Pair*not assignable*Plain*");
    }

    [Fact]
    public void StjSweepScope_CasesWithAsSelfArms_rejects_a_duplicate_case_id()
    {
        var samples = new Dictionary<Type, object[]>
        {
            [typeof(First.Marker)] = [new First.Marker()],
            [typeof(Second.Marker)] = [new Second.Marker()],
        };

        var build = () => StjSweepScope.CasesWithAsSelfArms(samples);

        build.Should().Throw<ArgumentException>().WithMessage("*duplicate*Marker*");
    }

    [Fact]
    public void StjSweepScope_Uncovered_reports_a_value_type_with_no_sample_by_full_name()
    {
        var cases = StjSweepScope.CasesWithAsSelfArms(new Dictionary<Type, object[]> { [typeof(Plain)] = [new Plain(1)] });

        var uncovered = StjSweepScope.Uncovered([typeof(Plain), typeof(Pair)], cases);

        uncovered.Select(type => type.FullName).Should().Equal(typeof(Pair).FullName);
    }

    [Fact]
    public void StjSweepScope_Uncovered_lets_a_closed_instantiation_cover_its_open_definition()
    {
        var cases = StjSweepScope.CasesWithAsSelfArms(
            new Dictionary<Type, object[]> { [typeof(Wrapper<string>)] = [new Wrapper<string>("x")] });

        var uncovered = StjSweepScope.Uncovered([typeof(Wrapper<>)], cases);

        uncovered.Should().BeEmpty();
    }

    [Fact]
    public void StjSweepScope_Uncovered_covers_an_arm_by_an_instance_under_its_base_normalised_to_the_open_nested_definition()
    {
        var cases = StjSweepScope.CasesWithAsSelfArms(
            new Dictionary<Type, object[]> { [typeof(Opt<string>)] = [new Opt<string>.Some("x"), new Opt<string>.Nothing()] });

        var uncovered = StjSweepScope.Uncovered(
            [typeof(Opt<>), typeof(Opt<>.Some), typeof(Opt<>.Nothing)], cases);

        uncovered.Should().BeEmpty();
    }

    [Fact]
    public void StjSweepScope_Uncovered_reports_an_arm_no_instance_was_sampled_for()
    {
        var cases = StjSweepScope.CasesWithAsSelfArms(
            new Dictionary<Type, object[]> { [typeof(Opt<string>)] = [new Opt<string>.Some("x")] });

        var uncovered = StjSweepScope.Uncovered(
            [typeof(Opt<>), typeof(Opt<>.Some), typeof(Opt<>.Nothing)], cases);

        uncovered.Should().Equal(typeof(Opt<>.Nothing));
    }

    [Fact]
    public void StjSweepScope_OutOfScopeKeys_reports_a_key_that_is_not_a_value_type_in_scope()
    {
        var cases = StjSweepScope.CasesWithAsSelfArms(
            new Dictionary<Type, object[]> { [typeof(Plain)] = [new Plain(1)], [typeof(Pair)] = [new Pair("a", 2)] });

        var outOfScope = StjSweepScope.OutOfScopeKeys([typeof(Plain)], cases);

        outOfScope.Should().Equal(typeof(Pair));
    }

    [Fact]
    public void StjSweepScope_ArmsWithNoFullyPopulatedSample_reports_an_arm_whose_only_sample_leaves_a_member_null()
    {
        var cases = StjSweepScope.CasesWithAsSelfArms(new Dictionary<Type, object[]>
        {
            [typeof(Tagged)] = [new Tagged("a", null)],
        });

        var sparse = StjSweepScope.ArmsWithNoFullyPopulatedSample(cases, new HashSet<string>());

        sparse.Should().Equal("Tagged");
    }

    [Fact]
    public void StjSweepScope_ArmsWithNoFullyPopulatedSample_accepts_an_arm_with_one_populated_sample_among_sparse_ones()
    {
        var cases = StjSweepScope.CasesWithAsSelfArms(new Dictionary<Type, object[]>
        {
            [typeof(Tagged)] = [new Tagged("a", null), new Tagged("b", "note")],
        });

        var sparse = StjSweepScope.ArmsWithNoFullyPopulatedSample(cases, new HashSet<string>());

        sparse.Should().BeEmpty();
    }

    [Fact]
    public void StjSweepScope_ArmsWithNoFullyPopulatedSample_reports_an_arm_whose_only_sample_holds_None_in_an_Optional_member()
    {
        var cases = StjSweepScope.CasesWithAsSelfArms(new Dictionary<Type, object[]>
        {
            [typeof(WithOptionalMember)] = [new WithOptionalMember("a", new Optional<string>.None())],
        });

        var sparse = StjSweepScope.ArmsWithNoFullyPopulatedSample(cases, new HashSet<string>());

        sparse.Should().Equal("WithOptionalMember");
    }

    [Fact]
    public void StjSweepScope_ArmsWithNoFullyPopulatedSample_accepts_an_Optional_member_holding_Some_next_to_one_holding_None()
    {
        var cases = StjSweepScope.CasesWithAsSelfArms(new Dictionary<Type, object[]>
        {
            [typeof(WithOptionalMember)] =
            [
                new WithOptionalMember("a", new Optional<string>.None()),
                new WithOptionalMember("b", new Optional<string>.Some("note")),
            ],
        });

        var sparse = StjSweepScope.ArmsWithNoFullyPopulatedSample(cases, new HashSet<string>());

        sparse.Should().BeEmpty();
    }

    [Fact]
    public void StjSweepScope_ArmsWithNoFullyPopulatedSample_accepts_a_Some_wrapping_None_because_only_the_outer_Optional_is_examined()
    {
        var cases = StjSweepScope.CasesWithAsSelfArms(new Dictionary<Type, object[]>
        {
            [typeof(WithNestedOptionalMember)] =
            [
                new WithNestedOptionalMember(new Optional<Optional<string>>.Some(new Optional<string>.None())),
            ],
        });

        var sparse = StjSweepScope.ArmsWithNoFullyPopulatedSample(cases, new HashSet<string>());

        sparse.Should().BeEmpty();
    }

    [Fact]
    public void StjSweepScope_ArmsWithNoFullyPopulatedSample_requires_a_JsonIgnore_member_to_be_populated()
    {
        var cases = StjSweepScope.CasesWithAsSelfArms(new Dictionary<Type, object[]>
        {
            [typeof(WithIgnoredMember)] = [new WithIgnoredMember("a")],
        });

        var sparse = StjSweepScope.ArmsWithNoFullyPopulatedSample(cases, new HashSet<string>());

        sparse.Should().Equal("WithIgnoredMember");
    }

    [Fact]
    public void StjSweepScope_ArmsWithNoFullyPopulatedSample_accepts_a_JsonIgnore_member_that_is_populated()
    {
        var cases = StjSweepScope.CasesWithAsSelfArms(new Dictionary<Type, object[]>
        {
            [typeof(WithIgnoredMember)] = [new WithIgnoredMember("a") { Transient = "kept" }],
        });

        var sparse = StjSweepScope.ArmsWithNoFullyPopulatedSample(cases, new HashSet<string>());

        sparse.Should().BeEmpty();
    }

    [Fact]
    public void StjSweepScope_ArmsWithNoFullyPopulatedSample_skips_an_exempt_arm()
    {
        var cases = StjSweepScope.CasesWithAsSelfArms(new Dictionary<Type, object[]>
        {
            [typeof(Tagged)] = [new Tagged("a", null)],
        });

        var sparse = StjSweepScope.ArmsWithNoFullyPopulatedSample(cases, new HashSet<string> { "Tagged" });

        sparse.Should().BeEmpty();
    }

    [Fact]
    public void StjSweepScope_StaleExemptions_reports_an_exemption_naming_no_arm_or_an_arm_already_populated()
    {
        var cases = StjSweepScope.CasesWithAsSelfArms(new Dictionary<Type, object[]>
        {
            [typeof(Tagged)] = [new Tagged("a", "note")],
            [typeof(Plain)] = [new Plain(1)],
        });

        var stale = StjSweepScope.StaleExemptions(cases, new HashSet<string> { "Tagged", "Missing", "Plain" });

        stale.Should().BeEquivalentTo("Tagged", "Missing", "Plain");
    }

    [Fact]
    public void StjSweepScope_StaleExemptions_keeps_an_exemption_that_still_excuses_a_sparse_arm()
    {
        var cases = StjSweepScope.CasesWithAsSelfArms(new Dictionary<Type, object[]>
        {
            [typeof(Tagged)] = [new Tagged("a", null)],
        });

        var stale = StjSweepScope.StaleExemptions(cases, new HashSet<string> { "Tagged" });

        stale.Should().BeEmpty();
    }

    [Fact]
    public void StjSweepScope_StaleKnownBrokenKeys_reports_a_key_naming_no_case_or_no_leg()
    {
        var cases = StjSweepScope.CasesWithAsSelfArms(new Dictionary<Type, object[]> { [typeof(Plain)] = [new Plain(1)] });

        var stale = StjSweepScope.StaleKnownBrokenKeys(
            cases, ["Plain", "Plain@AddDamlConverters", "Plain@Sideways", "Missing", "Missing@Default"]);

        stale.Should().Equal("Plain@Sideways", "Missing", "Missing@Default");
    }

    [Fact]
    public void StjKnownBroken_RootCause_prefers_the_leg_entry_over_the_case_entry()
    {
        var list = new Dictionary<string, string>
        {
            ["Plain"] = "both legs",
            ["Plain@AddDamlConverters"] = "one leg",
        };

        StjKnownBroken.RootCause(list, "Plain", StjOptionsLeg.AddDamlConverters).Should().Be("one leg");
        StjKnownBroken.RootCause(list, "Plain", StjOptionsLeg.Default).Should().Be("both legs");
        StjKnownBroken.RootCause(list, "Pair", StjOptionsLeg.Default).Should().BeNull();
    }

    private sealed record Plain(int Value);

    private sealed record Pair(string Name, int Count);

    private sealed record Wrapper<T>(T Value);

    private sealed record Tagged(string Name, string? Note);

    private sealed record WithOptionalMember(string Name, Optional<string> Note);

    private sealed record WithNestedOptionalMember(Optional<Optional<string>> Note);

    private sealed record WithIgnoredMember(string Name)
    {
        [JsonIgnore]
        public string? Transient { get; init; }
    }

    private abstract record Opt<T>
    {
        public sealed record Some(T Value) : Opt<T>;

        public sealed record Nothing : Opt<T>;
    }

    private static class First
    {
        public sealed record Marker;
    }

    private static class Second
    {
        public sealed record Marker;
    }

    private static class StaticHolder;

    private interface IThing;

    private sealed class PlainConverter : JsonConverter<Plain>
    {
        public override Plain Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(0);

        public override void Write(Utf8JsonWriter writer, Plain value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value.Value);
    }

    private sealed class PlainException : Exception;

    [AttributeUsage(AttributeTargets.Class)]
    private sealed class PlainAttribute : Attribute;

    private delegate void PlainHandler();

    private sealed class PlainExtensions;

    private sealed class Descriptor<T>
    {
        public sealed class Plumbing;
    }
}
