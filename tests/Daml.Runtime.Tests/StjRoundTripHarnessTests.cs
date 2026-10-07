// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Daml.Testing.StjRoundTrip;
using Xunit;

namespace Daml.Runtime.Tests;

public class StjRoundTripHarnessTests
{
    [Theory]
    [InlineData("Default")]
    [InlineData("AddDamlConverters")]
    public void StjRoundTripHarness_accepts_a_sample_that_reads_back_equivalent_equal_and_hash_equal(string legName)
    {
        var roundTripCase = new StjRoundTripCase("Pair", typeof(Pair), new Pair("alice", 7));

        var check = () => StjRoundTrip.AssertReadsBackEquivalentEqualAndHashEqual(
            roundTripCase, Enum.Parse<StjOptionsLeg>(legName));

        check.Should().NotThrow();
    }

    [Fact]
    public void StjRoundTripHarness_fails_a_list_member_compared_by_reference_on_equality_alone()
    {
        var roundTripCase = new StjRoundTripCase("ReferenceList", typeof(ReferenceList), new ReferenceList([1, 2]));

        var check = () => StjRoundTrip.AssertReadsBackEquivalentEqualAndHashEqual(roundTripCase, StjOptionsLeg.Default);

        check.Should().Throw<StjRoundTripFailure>().WithMessage("ReferenceList [Default] equality:*");
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("AddDamlConverters")]
    public void StjRoundTripHarness_accepts_a_sample_with_no_members_that_reads_back_equal_and_hash_equal(string legName)
    {
        var roundTripCase = new StjRoundTripCase("Absent", typeof(Absent), new Absent());

        var check = () => StjRoundTrip.AssertReadsBackEquivalentEqualAndHashEqual(
            roundTripCase, Enum.Parse<StjOptionsLeg>(legName));

        check.Should().NotThrow();
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("AddDamlConverters")]
    public void StjRoundTripHarness_compares_a_byte_memory_member_by_its_contents(string legName)
    {
        var roundTripCase = new StjRoundTripCase("ByteBlob", typeof(ByteBlob), new ByteBlob(new byte[] { 1, 2, 3, 250 }));

        var check = () => StjRoundTrip.AssertReadsBackEquivalentEqualAndHashEqual(
            roundTripCase, Enum.Parse<StjOptionsLeg>(legName));

        check.Should().NotThrow();
    }

    [Fact]
    public void StjRoundTripHarness_fails_a_dropped_member_of_an_arm_declared_as_its_base_on_equivalence_alone()
    {
        var sample = new FacetHolder(new Narrow(5, "kept"));
        var roundTripCase = new StjRoundTripCase("FacetHolder", typeof(FacetHolder), sample);

        var check = () => StjRoundTrip.AssertReadsBackEquivalentEqualAndHashEqual(roundTripCase, StjOptionsLeg.Default);

        check.Should().Throw<StjRoundTripFailure>().WithMessage("FacetHolder [Default] equivalence:*");
        new FacetHolder(new Narrow(5, string.Empty)).Should().Be(sample);
    }

    [Fact]
    public void StjRoundTripHarness_fails_a_hash_code_that_disagrees_with_Equals_on_the_hash_check_alone()
    {
        var roundTripCase = new StjRoundTripCase("IdentityHash", typeof(IdentityHash), new IdentityHash("alice"));

        var check = () => StjRoundTrip.AssertReadsBackEquivalentEqualAndHashEqual(roundTripCase, StjOptionsLeg.Default);

        check.Should().Throw<StjRoundTripFailure>().WithMessage("IdentityHash [Default] hash code:*");
    }

    [Fact]
    public void StjRoundTripHarness_fails_a_dropped_member_that_Equals_ignores_on_equivalence_alone()
    {
        var sample = new LabelOnlyVault("front", "combination");
        var roundTripCase = new StjRoundTripCase("LabelOnlyVault", typeof(LabelOnlyVault), sample);

        var check = () => StjRoundTrip.AssertReadsBackEquivalentEqualAndHashEqual(roundTripCase, StjOptionsLeg.Default);

        check.Should().Throw<StjRoundTripFailure>().WithMessage("LabelOnlyVault [Default] equivalence:*");
        new LabelOnlyVault("front", string.Empty).Should().Be(sample);
    }

    [Fact]
    public void StjRoundTripHarness_throws_for_a_declared_abstract_base_with_no_converter()
    {
        var roundTripCase = new StjRoundTripCase("Shape/Circle", typeof(Shape), new Circle(5));

        var check = () => StjRoundTrip.AssertReadsBackEquivalentEqualAndHashEqual(roundTripCase, StjOptionsLeg.Default);

        check.Should().Throw<StjRoundTripFailure>().WithMessage("Shape/Circle [Default] deserialize:*");
    }

    [Fact]
    public void StjRoundTripHarness_accepts_the_same_arm_declared_as_its_own_type()
    {
        var roundTripCase = new StjRoundTripCase("Shape/Circle/as-self", typeof(Circle), new Circle(5));

        var check = () => StjRoundTrip.AssertReadsBackEquivalentEqualAndHashEqual(roundTripCase, StjOptionsLeg.Default);

        check.Should().NotThrow();
    }

    [Fact]
    public void StjRoundTripHarness_compares_an_arm_by_its_runtime_type_so_a_lost_arm_member_fails()
    {
        var sample = new Marked(3, "stamped");
        var roundTripCase = new StjRoundTripCase("Token/Marked", typeof(Token), sample);
        var restored = JsonSerializer.Deserialize<Token>(JsonSerializer.Serialize<Token>(sample))!;

        var check = () => StjRoundTrip.AssertReadsBackEquivalentEqualAndHashEqual(roundTripCase, StjOptionsLeg.Default);

        restored.Should().BeEquivalentTo(sample as Token);
        check.Should().Throw<StjRoundTripFailure>().WithMessage("Token/Marked [Default] equivalence:*");
    }

    [Fact]
    public void StjRoundTripHarness_reports_a_failure_on_the_AddDamlConverters_leg_for_that_leg_alone()
    {
        var roundTripCase = new StjRoundTripCase("Careless", typeof(Careless), new Careless(null!));

        var defaultLeg = () => StjRoundTrip.AssertReadsBackEquivalentEqualAndHashEqual(roundTripCase, StjOptionsLeg.Default);
        var damlLeg = () => StjRoundTrip.AssertReadsBackEquivalentEqualAndHashEqual(roundTripCase, StjOptionsLeg.AddDamlConverters);

        defaultLeg.Should().NotThrow();
        damlLeg.Should().Throw<StjRoundTripFailure>().WithMessage("Careless [AddDamlConverters] serialize:*");
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("AddDamlConverters")]
    public void StjRoundTripHarness_accepts_a_populated_JsonIgnore_member_that_Equals_excludes(string legName)
    {
        var roundTripCase = new StjRoundTripCase(
            "DiagnosticAttached", typeof(DiagnosticAttached), new DiagnosticAttached("alice") { Diagnostic = "kept locally" });

        var check = () => StjRoundTrip.AssertReadsBackEquivalentEqualAndHashEqual(
            roundTripCase, Enum.Parse<StjOptionsLeg>(legName));

        check.Should().NotThrow();
    }

    [Fact]
    public void StjRoundTripHarness_fails_a_populated_JsonIgnore_member_that_Equals_compares_on_equality_alone()
    {
        var roundTripCase = new StjRoundTripCase(
            "DiagnosticCompared", typeof(DiagnosticCompared), new DiagnosticCompared("alice") { Diagnostic = "kept locally" });

        var check = () => StjRoundTrip.AssertReadsBackEquivalentEqualAndHashEqual(roundTripCase, StjOptionsLeg.Default);

        check.Should().Throw<StjRoundTripFailure>().WithMessage("DiagnosticCompared [Default] equality:*");
    }

    [Fact]
    public void StjRoundTripHarness_fails_a_populated_JsonIgnore_member_that_hashing_includes_on_the_hash_check_alone()
    {
        var roundTripCase = new StjRoundTripCase(
            "DiagnosticHashed", typeof(DiagnosticHashed), new DiagnosticHashed("alice") { Diagnostic = "kept locally" });

        var check = () => StjRoundTrip.AssertReadsBackEquivalentEqualAndHashEqual(roundTripCase, StjOptionsLeg.Default);

        check.Should().Throw<StjRoundTripFailure>().WithMessage("DiagnosticHashed [Default] hash code:*");
    }

    [Fact]
    public void StjRoundTripHarness_AssertStillFails_accepts_a_case_that_fails()
    {
        var roundTripCase = new StjRoundTripCase("Shape/Circle", typeof(Shape), new Circle(5));

        var check = () => StjRoundTrip.AssertStillFails(roundTripCase, StjOptionsLeg.Default, "Shape is abstract and has no converter");

        check.Should().NotThrow();
    }

    [Fact]
    public void StjRoundTripHarness_AssertStillFails_tells_the_reader_to_delete_the_entry_of_a_case_that_passes()
    {
        var roundTripCase = new StjRoundTripCase("Pair", typeof(Pair), new Pair("alice", 7));

        var check = () => StjRoundTrip.AssertStillFails(roundTripCase, StjOptionsLeg.AddDamlConverters, "Pair is lossy");

        check.Should().Throw<StjRoundTripFailure>().WithMessage(
            "Pair [AddDamlConverters] now reads back as itself, so its known-broken root cause no longer holds: Pair is lossy. Delete its known-broken entry.");
    }

    private sealed record DiagnosticAttached(string Name)
    {
        [JsonIgnore]
        public string? Diagnostic { get; init; }

        public bool Equals(DiagnosticAttached? other) => other is not null && Name == other.Name;

        public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);
    }

    private sealed record DiagnosticCompared(string Name)
    {
        [JsonIgnore]
        public string? Diagnostic { get; init; }
    }

    private sealed record DiagnosticHashed(string Name)
    {
        [JsonIgnore]
        public string? Diagnostic { get; init; }

        public bool Equals(DiagnosticHashed? other) => other is not null && Name == other.Name;

        public override int GetHashCode() => HashCode.Combine(Name, Diagnostic);
    }

    private sealed record Pair(string Name, int Count);

    private sealed record ByteBlob(ReadOnlyMemory<byte> Bytes)
    {
        public bool Equals(ByteBlob? other) => other is not null && Bytes.Span.SequenceEqual(other.Bytes.Span);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.AddBytes(Bytes.Span);
            return hash.ToHashCode();
        }
    }

    private sealed record FacetHolder(Facet Item);

    [JsonConverter(typeof(FacetConverter))]
    private abstract record Facet(int Id);

    private sealed record Narrow(int Id, string Extra) : Facet(Id)
    {
        public bool Equals(Narrow? other) => other is not null && Id == other.Id;

        public override int GetHashCode() => Id;
    }

    private sealed class FacetConverter : JsonConverter<Facet>
    {
        public override Facet Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new Narrow(reader.GetInt32(), string.Empty);

        public override void Write(Utf8JsonWriter writer, Facet value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value.Id);
    }

    private abstract record Presence;

    private sealed record Absent : Presence;

    private sealed record ReferenceList(IReadOnlyList<int> Items);

    private sealed record IdentityHash(string Name)
    {
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }

    [JsonConverter(typeof(LabelOnlyConverter))]
    private sealed record LabelOnlyVault(string Label, string Secret)
    {
        public bool Equals(LabelOnlyVault? other) => other is not null && Label == other.Label;

        public override int GetHashCode() => Label.GetHashCode(StringComparison.Ordinal);
    }

    private sealed class LabelOnlyConverter : JsonConverter<LabelOnlyVault>
    {
        public override LabelOnlyVault Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(reader.GetString()!, string.Empty);

        public override void Write(Utf8JsonWriter writer, LabelOnlyVault value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Label);
    }

    private abstract record Shape;

    private sealed record Circle(int Radius) : Shape;

    [JsonConverter(typeof(TokenConverter))]
    private abstract record Token(int Id);

    private sealed record Marked(int Id, string Label) : Token(Id);

    private sealed class TokenConverter : JsonConverter<Token>
    {
        public override Token Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new Marked(reader.GetInt32(), string.Empty);

        public override void Write(Utf8JsonWriter writer, Token value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value.Id);
    }

    private sealed record Careless(string Name);
}
