// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Grpc.Client;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class ThreeDeepOptionalTests
{
    private static readonly Party Owner = new("alice::1220");

    private static readonly Optional<Optional<Optional<string>>> OuterAbsent =
        new Optional<Optional<Optional<string>>>.None();

    private static readonly Optional<Optional<Optional<string>>> MiddleAbsent =
        new Optional<Optional<Optional<string>>>.Some(new Optional<Optional<string>>.None());

    private static readonly Optional<Optional<Optional<string>>> InnerAbsent =
        new Optional<Optional<Optional<string>>>.Some(
            new Optional<Optional<string>>.Some(new Optional<string>.None()));

    private static readonly Optional<Optional<Optional<string>>> Present =
        new Optional<Optional<Optional<string>>>.Some(
            new Optional<Optional<string>>.Some(new Optional<string>.Some("deep")));

    public static TheoryData<string, string> WireForms() => new()
    {
        { "outer absent", "[]" },
        { "middle absent", "[[]]" },
        { "inner absent", "[[[]]]" },
        { "present", """[[["deep"]]]""" },
    };

    private static Optional<Optional<Optional<string>>> State(string name) => name switch
    {
        "outer absent" => OuterAbsent,
        "middle absent" => MiddleAbsent,
        "inner absent" => InnerAbsent,
        "present" => Present,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "not a three-deep state"),
    };

    private static ThreeDeepOptionals Sample(string state) => new(Owner, State(state), Present);

    [Fact]
    public void DeepNote_is_emitted_as_a_three_deep_Optional_wrapper_chain()
    {
        var property = typeof(ThreeDeepOptionals).GetProperty(nameof(ThreeDeepOptionals.DeepNote))!;

        property.PropertyType.Should().Be<Optional<Optional<Optional<string>>>>();
    }

    [Theory]
    [MemberData(nameof(WireForms))]
    public void DeepNote_is_written_as_one_array_level_per_Optional(string state, string wire)
    {
        var written = DamlJsonSerializer.Serialize(Sample(state).ToRecord().GetRequiredField("deepNote"));

        written.Should().Be(wire);
    }

    [Theory]
    [InlineData("outer absent")]
    [InlineData("middle absent")]
    [InlineData("inner absent")]
    [InlineData("present")]
    public void DeepNote_round_trips_through_the_gRPC_converter(string state)
    {
        var original = Sample(state);

        var viaProto = DamlValueConverter.FromProtoValue(DamlValueConverter.ToProtoValue(original.ToRecord()));
        var decoded = ThreeDeepOptionals.FromRecord((DamlRecord)viaProto);

        decoded.Should().Be(original);
    }

    [Theory]
    [MemberData(nameof(WireForms))]
    public void DeepNote_round_trips_through_the_JSON_writer_and_reader(string state, string wire)
    {
        var original = Sample(state);

        var json = DamlJsonSerializer.Serialize(original.ToRecord());
        var decoded = ThreeDeepOptionals.FromRecord(DamlLfJsonReader.ReadRecord<ThreeDeepOptionals>(json));

        decoded.Should().Be(original);
        json.Should().Contain($"\"deepNote\":{wire}");
    }

    [Theory]
    [MemberData(nameof(WireForms))]
    public void DeepNote_reads_the_array_form_the_ledger_returns(string state, string wire)
    {
        var json = $$"""{"owner":"alice::1220","deepNote":{{wire}},"tailDeepNote":[[["deep"]]]}""";

        var decoded = ThreeDeepOptionals.FromRecord(DamlLfJsonReader.ReadRecord<ThreeDeepOptionals>(json));

        decoded.Should().Be(Sample(state));
    }

    [Fact]
    public void ThreeDeepOptionals_decodes_an_omitted_trailing_three_deep_None_as_outer_absent()
    {
        var decoded = ThreeDeepOptionals.FromRecord(
            DamlLfJsonReader.ReadRecord<ThreeDeepOptionals>("""{"owner":"alice::1220","deepNote":[[["deep"]]]}"""));

        decoded.DeepNote.Should().Be(Present);
        decoded.TailDeepNote.Should().Be(OuterAbsent);
    }

    [Fact]
    public void ThreeDeepOptionals_still_rejects_null_for_a_three_deep_Optional()
    {
        var act = () => DamlLfJsonReader.ReadRecord<ThreeDeepOptionals>(
            """{"owner":"alice::1220","deepNote":null,"tailDeepNote":[]}""");

        act.Should().Throw<JsonException>();
    }
}
