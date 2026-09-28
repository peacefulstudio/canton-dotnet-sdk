// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using Daml.Runtime.Streams;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins the <see cref="System.Text.Json"/> contract for
/// <see cref="InterfaceStreamEvent{TInterface, TView}"/>: a <c>"$case"</c>-discriminated object
/// that reads back as the arm that wrote it. The contract is a CLR round trip, not a Daml-LF wire
/// decode, so what the tests assert is that a value survives the trip.
/// <see cref="InterfaceStreamEvent{TInterface, TView}"/>'s own converter is zero-config, but an
/// arm carrying a <see cref="DamlValue"/> field (<c>Exercised</c>'s choice argument and result,
/// and a keyed <c>Created</c>/<c>Assigned</c>'s <see cref="ContractKey.Value"/>) round-trips only
/// once <see cref="DamlValueJsonConverter"/> is added to the options, matching
/// <see cref="ContractStreamEventJsonTests"/>.
/// </summary>
public class InterfaceStreamEventJsonTests
{
    private static readonly JsonSerializerOptions Options =
        new JsonSerializerOptions { Converters = { new DamlValueJsonConverter() } }.AddDamlConverters();

    private static readonly ContractId<TestInterface> Id = new("c1");
    private static readonly SynchronizerId Synchronizer = new("sync");
    private static readonly EquatableArray<Party> Witnesses = [new Party("alice")];
    private static readonly Identifier InterfaceId = new("pkg", "M", "TestInterface");

    [Fact]
    public void Created_reads_its_own_write_back_without_a_key()
    {
        InterfaceStreamEvent<TestInterface, TestView> value = new InterfaceStreamEvent<TestInterface, TestView>.Created(
            Id, new TestView("bronze"), null, LedgerOffset.At(1), Synchronizer, Witnesses);

        var written = JsonSerializer.Serialize(value);

        JsonSerializer.Deserialize<InterfaceStreamEvent<TestInterface, TestView>>(written).Should().Be(value);
    }

    [Fact]
    public void Created_reads_its_own_write_back_with_a_key()
    {
        InterfaceStreamEvent<TestInterface, TestView> value = new InterfaceStreamEvent<TestInterface, TestView>.Created(
            Id,
            new TestView("bronze"),
            new ContractKey(new DamlText("savings"), InterfaceId) { KeyHash = "hash-1" },
            LedgerOffset.At(1),
            Synchronizer,
            Witnesses);

        var written = JsonSerializer.Serialize(value, Options);

        JsonSerializer.Deserialize<InterfaceStreamEvent<TestInterface, TestView>>(written, Options).Should().Be(value);
    }

    [Fact]
    public void Archived_reads_its_own_write_back()
    {
        InterfaceStreamEvent<TestInterface, TestView> value = new InterfaceStreamEvent<TestInterface, TestView>.Archived(
            Id, LedgerOffset.At(2), Synchronizer, Witnesses);

        var written = JsonSerializer.Serialize(value);

        JsonSerializer.Deserialize<InterfaceStreamEvent<TestInterface, TestView>>(written).Should().Be(value);
    }

    [Fact]
    public void Exercised_reads_its_own_write_back()
    {
        InterfaceStreamEvent<TestInterface, TestView> value = new InterfaceStreamEvent<TestInterface, TestView>.Exercised(
            Id, new ChoiceName("Accept"), new DamlText("go"), new DamlText("done"), true, LedgerOffset.At(3), Synchronizer, Witnesses);

        var written = JsonSerializer.Serialize(value, Options);

        JsonSerializer.Deserialize<InterfaceStreamEvent<TestInterface, TestView>>(written, Options).Should().Be(
            value,
            "DamlUnit is a poor fixture for this assertion: its canonical Daml-LF encoding is the "
            + "same empty object an empty DamlRecord writes, and DamlValueJsonConverter has no type "
            + "hint to tell the two apart on read — a documented gap, not this PR's to close");
    }

    [Fact]
    public void Exercised_writes_the_choice_argument_and_result_at_their_canonical_DamlLF_shape()
    {
        InterfaceStreamEvent<TestInterface, TestView> value = new InterfaceStreamEvent<TestInterface, TestView>.Exercised(
            Id, new ChoiceName("Accept"), DamlUnit.Instance, new DamlText("done"), true, LedgerOffset.At(3), Synchronizer, Witnesses);

        var json = JsonSerializer.Serialize(value, Options);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("$case").GetString().Should().Be("Exercised");
        document.RootElement.GetProperty("ChoiceArgument").ValueKind.Should().Be(JsonValueKind.Object);
        document.RootElement.GetProperty("ChoiceArgument").EnumerateObject().Should().BeEmpty(
            "DamlUnit's canonical Daml-LF encoding is an empty object, and Exercised leaves the "
            + "choice argument at wire level rather than decoding it");
        document.RootElement.GetProperty("ExerciseResult").GetString().Should().Be("done");
    }

    [Fact]
    public void Assigned_reads_its_own_write_back_with_a_key()
    {
        InterfaceStreamEvent<TestInterface, TestView> value = new InterfaceStreamEvent<TestInterface, TestView>.Assigned(
            Id,
            new TestView("bronze"),
            new ContractKey(new DamlText("savings"), InterfaceId) { KeyHash = "hash-1" },
            LedgerOffset.At(4),
            new SynchronizerId("src"),
            new SynchronizerId("tgt"),
            "reassignment-1",
            7L,
            Witnesses);

        var written = JsonSerializer.Serialize(value, Options);

        JsonSerializer.Deserialize<InterfaceStreamEvent<TestInterface, TestView>>(written, Options).Should().Be(value);
    }

    [Fact]
    public void Unassigned_reads_its_own_write_back()
    {
        InterfaceStreamEvent<TestInterface, TestView> value = new InterfaceStreamEvent<TestInterface, TestView>.Unassigned(
            Id, LedgerOffset.At(5), new SynchronizerId("src"), new SynchronizerId("tgt"), "reassignment-1", 7L, Witnesses);

        var written = JsonSerializer.Serialize(value);

        JsonSerializer.Deserialize<InterfaceStreamEvent<TestInterface, TestView>>(written).Should().Be(value);
    }

    [Fact]
    public void Checkpoint_reads_its_own_write_back()
    {
        InterfaceStreamEvent<TestInterface, TestView> value = new InterfaceStreamEvent<TestInterface, TestView>.Checkpoint(LedgerOffset.At(6));

        var written = JsonSerializer.Serialize(value);

        JsonSerializer.Deserialize<InterfaceStreamEvent<TestInterface, TestView>>(written).Should().Be(value);
    }

    [Fact]
    public void StreamError_reads_its_own_write_back_with_full_fields()
    {
        InterfaceStreamEvent<TestInterface, TestView> value = new InterfaceStreamEvent<TestInterface, TestView>.StreamError(
            new TransportStatus.Grpc(GrpcStatusCode.Aborted), "the stream authorization is stale", DamlErrorCategory.ContentionOnSharedResources, "STALE_STREAM_AUTHORIZATION");

        var written = JsonSerializer.Serialize(value);

        JsonSerializer.Deserialize<InterfaceStreamEvent<TestInterface, TestView>>(written).Should().Be(value);
    }

    [Fact]
    public void StreamError_reads_its_own_write_back_with_only_the_required_fields()
    {
        InterfaceStreamEvent<TestInterface, TestView> value = new InterfaceStreamEvent<TestInterface, TestView>.StreamError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "unavailable");

        var written = JsonSerializer.Serialize(value);

        JsonSerializer.Deserialize<InterfaceStreamEvent<TestInterface, TestView>>(written).Should().Be(value);
    }

    [Fact]
    public void StreamError_writes_and_reads_back_when_SourceException_is_set()
    {
        InterfaceStreamEvent<TestInterface, TestView> value = new InterfaceStreamEvent<TestInterface, TestView>.StreamError(
            new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "unavailable", DamlErrorCategory.TransientServerFailure, "STREAM_UNAVAILABLE", new InvalidOperationException("transport reset"));

        var written = JsonSerializer.Serialize(value);
        var read = JsonSerializer.Deserialize<InterfaceStreamEvent<TestInterface, TestView>>(written);

        read.Should().Be(
            new InterfaceStreamEvent<TestInterface, TestView>.StreamError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "unavailable", DamlErrorCategory.TransientServerFailure, "STREAM_UNAVAILABLE"),
            "SourceException carries JsonIgnoreAttribute: a real caught exception has a TargetSite, "
            + "and System.Text.Json's reflection-based writer throws NotSupportedException trying to "
            + "serialize System.Reflection.MethodBase through it, so writing succeeds by dropping the "
            + "one member with no JSON-constructible shape rather than throwing on every StreamError "
            + "that carries one");
    }

    [Fact]
    public void StreamError_omits_SourceException_from_the_written_JSON()
    {
        InterfaceStreamEvent<TestInterface, TestView> value = new InterfaceStreamEvent<TestInterface, TestView>.StreamError(
            new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "unavailable", SourceException: new InvalidOperationException("transport reset"));

        var json = JsonSerializer.Serialize(value);

        using var document = JsonDocument.Parse(json);
        document.RootElement.TryGetProperty("SourceException", out _).Should().BeFalse();
    }

    [Fact]
    public void InterfaceStreamEvent_reads_back_under_options_that_disallow_unmapped_members()
    {
        var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        InterfaceStreamEvent<TestInterface, TestView> value = new InterfaceStreamEvent<TestInterface, TestView>.Archived(
            Id, LedgerOffset.At(2), Synchronizer, Witnesses);

        var written = JsonSerializer.Serialize(value, options);

        JsonSerializer.Deserialize<InterfaceStreamEvent<TestInterface, TestView>>(written, options).Should().Be(
            value,
            "the \"$case\" discriminator is not a member any arm declares, so a caller hardening "
            + "against unmapped members must not see it as one when the union's own converter reads "
            + "the arm back");
    }

    [Fact]
    public void Unclassified_reads_its_own_write_back_for_an_enumerated_kind()
    {
        InterfaceStreamEvent<TestInterface, TestView> value = new InterfaceStreamEvent<TestInterface, TestView>.Unclassified(
            LedgerOffset.At(7), UnclassifiedKind.DecodeFailure);

        var written = JsonSerializer.Serialize(value);

        JsonSerializer.Deserialize<InterfaceStreamEvent<TestInterface, TestView>>(written).Should().Be(value);
    }

    [Fact]
    public void Unclassified_reads_its_own_write_back_for_InterfaceViewUnavailable()
    {
        InterfaceStreamEvent<TestInterface, TestView> value = new InterfaceStreamEvent<TestInterface, TestView>.Unclassified(
            LedgerOffset.At(7), UnclassifiedKind.InterfaceViewUnavailable);

        var written = JsonSerializer.Serialize(value);

        JsonSerializer.Deserialize<InterfaceStreamEvent<TestInterface, TestView>>(written).Should().Be(
            value,
            "InterfaceViewUnavailable is the reason only this family reports, so it needs its own "
            + "coverage rather than inheriting the template family's enumerated-kind case");
    }

    [Fact]
    public void Unclassified_reads_its_own_write_back_for_Unknown_with_a_raw_descriptor()
    {
        InterfaceStreamEvent<TestInterface, TestView> value = new InterfaceStreamEvent<TestInterface, TestView>.Unclassified(
            LedgerOffset.At(7), UnclassifiedKind.Unknown, "TopologyEvent");

        var written = JsonSerializer.Serialize(value);

        JsonSerializer.Deserialize<InterfaceStreamEvent<TestInterface, TestView>>(written).Should().Be(value);
    }

    [Fact]
    public void Unclassified_reads_its_own_write_back_with_a_null_offset()
    {
        InterfaceStreamEvent<TestInterface, TestView> value = new InterfaceStreamEvent<TestInterface, TestView>.Unclassified(
            null, UnclassifiedKind.DecodeFailure);

        var written = JsonSerializer.Serialize(value);

        JsonSerializer.Deserialize<InterfaceStreamEvent<TestInterface, TestView>>(written).Should().Be(value);
    }

    [Fact]
    public void InterfaceStreamEvent_writes_the_case_discriminator_as_the_first_member()
    {
        InterfaceStreamEvent<TestInterface, TestView> value = new InterfaceStreamEvent<TestInterface, TestView>.Archived(
            Id, LedgerOffset.At(2), Synchronizer, Witnesses);

        var json = JsonSerializer.Serialize(value);

        using var document = JsonDocument.Parse(json);
        document.RootElement.EnumerateObject().First().Name.Should().Be("$case");
        document.RootElement.GetProperty("$case").GetString().Should().Be("Archived");
    }

    [Fact]
    public void InterfaceStreamEvent_refuses_a_payload_that_is_not_an_object()
    {
        var act = () => JsonSerializer.Deserialize<InterfaceStreamEvent<TestInterface, TestView>>("\"c\"");

        act.Should().Throw<JsonException>()
            .WithMessage(
                "*Expected object token for InterfaceStreamEvent<InterfaceStreamEventJsonTests.TestInterface, "
                + "InterfaceStreamEventJsonTests.TestView>*");
    }

    [Fact]
    public void InterfaceStreamEvent_refuses_a_payload_missing_the_discriminator()
    {
        var act = () => JsonSerializer.Deserialize<InterfaceStreamEvent<TestInterface, TestView>>("""{"Offset":6}""");

        act.Should().Throw<JsonException>()
            .WithMessage(
                "*InterfaceStreamEvent<InterfaceStreamEventJsonTests.TestInterface, "
                + "InterfaceStreamEventJsonTests.TestView> is missing the \"$case\" discriminator*");
    }

    [Fact]
    public void InterfaceStreamEvent_round_trips_under_a_property_naming_policy()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper };
        InterfaceStreamEvent<TestInterface, TestView> value =
            new InterfaceStreamEvent<TestInterface, TestView>.Checkpoint(LedgerOffset.At(6));

        var json = JsonSerializer.Serialize(value, options);

        using (var document = JsonDocument.Parse(json))
        {
            document.RootElement.GetProperty("$case").GetString().Should().Be(
                "Checkpoint", "the discriminator is inserted directly, not through a serialized "
                + "property, so a naming policy does not touch it — SNAKE_CASE_UPPER, unlike "
                + "CamelCase, actually rewrites \"$case\" (to \"$CASE\") if it were applied, so this "
                + "pins a policy that can catch that regression");
            document.RootElement.TryGetProperty("OFFSET", out _).Should().BeTrue(
                "the arm's own Offset property is written through the policy like any other member");
            document.RootElement.TryGetProperty("Offset", out _).Should().BeFalse(
                "the PascalCase name must not also appear once the policy has renamed it");
        }
        JsonSerializer.Deserialize<InterfaceStreamEvent<TestInterface, TestView>>(json, options).Should().Be(value);
    }

    [Fact]
    public void InterfaceStreamEvent_refuses_an_unknown_case()
    {
        var act = () => JsonSerializer.Deserialize<InterfaceStreamEvent<TestInterface, TestView>>("""{"$case":"Renamed"}""");

        act.Should().Throw<JsonException>()
            .WithMessage(
                "*InterfaceStreamEvent<InterfaceStreamEventJsonTests.TestInterface, "
                + "InterfaceStreamEventJsonTests.TestView> names an unknown case \"Renamed\"*Created, "
                + "Archived, Exercised, Assigned, Unassigned, Checkpoint, StreamError, Unclassified*");
    }

    public static IEnumerable<object[]> UnsupportedReferenceHandlers =>
    [
        [ReferenceHandler.Preserve],
        [ReferenceHandler.IgnoreCycles],
    ];

    [Theory]
    [MemberData(nameof(UnsupportedReferenceHandlers))]
    public void InterfaceStreamEvent_write_refuses_a_ReferenceHandler(ReferenceHandler handler)
    {
        var options = new JsonSerializerOptions { ReferenceHandler = handler };
        InterfaceStreamEvent<TestInterface, TestView> value =
            new InterfaceStreamEvent<TestInterface, TestView>.Checkpoint(LedgerOffset.At(6));

        var act = () => JsonSerializer.Serialize(value, options);

        act.Should().Throw<JsonException>().WithMessage(
            "Reference handling is not supported for "
            + "InterfaceStreamEvent<InterfaceStreamEventJsonTests.TestInterface, "
            + "InterfaceStreamEventJsonTests.TestView>: this Daml union's converter reaches its arm's "
            + "payload through a nested JsonSerializer call that starts its own independent reference "
            + "resolver, so JsonSerializerOptions.ReferenceHandler.Preserve would silently duplicate "
            + "an aliased object under a colliding \"$id\" and ReferenceHandler.IgnoreCycles would not "
            + "recognize a cycle through this type at all. Serialize or deserialize a value containing "
            + "InterfaceStreamEvent<InterfaceStreamEventJsonTests.TestInterface, "
            + "InterfaceStreamEventJsonTests.TestView> with JsonSerializerOptions.ReferenceHandler left "
            + "null.");
    }

    [Theory]
    [MemberData(nameof(UnsupportedReferenceHandlers))]
    public void InterfaceStreamEvent_read_refuses_a_ReferenceHandler(ReferenceHandler handler)
    {
        var options = new JsonSerializerOptions { ReferenceHandler = handler };

        var act = () => JsonSerializer.Deserialize<InterfaceStreamEvent<TestInterface, TestView>>(
            """{"$case":"Checkpoint","Offset":6}""", options);

        act.Should().Throw<JsonException>().WithMessage(
            "Reference handling is not supported for "
            + "InterfaceStreamEvent<InterfaceStreamEventJsonTests.TestInterface, "
            + "InterfaceStreamEventJsonTests.TestView>: this Daml union's converter reaches its arm's "
            + "payload through a nested JsonSerializer call that starts its own independent reference "
            + "resolver, so JsonSerializerOptions.ReferenceHandler.Preserve would silently duplicate "
            + "an aliased object under a colliding \"$id\" and ReferenceHandler.IgnoreCycles would not "
            + "recognize a cycle through this type at all. Serialize or deserialize a value containing "
            + "InterfaceStreamEvent<InterfaceStreamEventJsonTests.TestInterface, "
            + "InterfaceStreamEventJsonTests.TestView> with JsonSerializerOptions.ReferenceHandler left "
            + "null.");
    }

    [Fact]
    public void InterfaceStreamEvent_writes_the_case_discriminator_when_the_static_type_is_the_concrete_Checkpoint_arm()
    {
        InterfaceStreamEvent<TestInterface, TestView>.Checkpoint value = new(LedgerOffset.At(6));

        var json = JsonSerializer.Serialize(value, Options);

        json.Should().Be(
            """{"$case":"Checkpoint","Offset":6}""",
            "a caller holding the concrete arm type — not the declared-abstract "
            + "InterfaceStreamEvent<TestInterface, TestView> — must still get the discriminated "
            + "shape once InterfaceStreamEventJsonConverterFactory is registered: its "
            + "[JsonConverter] attribute lives on InterfaceStreamEvent<TInterface, TView> and, "
            + "unlike its CanConvert, is not inherited by JsonSerializer's converter resolution "
            + "when the arm type itself is what gets looked up, so only AddDamlConverters's "
            + "explicit registration — not the attribute — reaches this arm-typed lookup");
    }

    [Fact]
    public void InterfaceStreamEvent_arm_typed_write_reads_back_through_the_base_type()
    {
        InterfaceStreamEvent<TestInterface, TestView>.Checkpoint value = new(LedgerOffset.At(6));

        var written = JsonSerializer.Serialize(value, Options);

        JsonSerializer.Deserialize<InterfaceStreamEvent<TestInterface, TestView>>(written, Options).Should().Be(value);
    }

    [Fact]
    public void InterfaceStreamEvent_arm_typed_write_on_bare_options_falls_back_to_the_plain_reflection_shape()
    {
        InterfaceStreamEvent<TestInterface, TestView>.Checkpoint value = new(LedgerOffset.At(6));

        var json = JsonSerializer.Serialize(value);

        json.Should().Be(
            """{"Offset":6}""",
            "InterfaceStreamEventJsonConverterFactory is reached for an arm-typed lookup only "
            + "through JsonSerializerOptions.Converters, e.g. via AddDamlConverters, because "
            + "[JsonConverter] on InterfaceStreamEvent<TInterface, TView> is not inherited by "
            + "InterfaceStreamEvent<TestInterface, TestView>.Checkpoint; bare options carry "
            + "neither the attribute on the arm nor the registration, so the arm keeps writing its "
            + "own plain members with no \"$case\", exactly as it did before this factory started "
            + "matching arm types at all");
    }

    private interface TestInterface : IDamlInterface, IHasView<TestView>
    {
        static Identifier IDamlInterface.InterfaceId => new("pkg", "M", "TestInterface");
        static string IDamlInterface.PackageId => "pkg";
        static string IDamlInterface.PackageName => "test";
        static Version IDamlInterface.PackageVersion => new(0, 1, 0);
        static DamlTypeDescriptor IDamlType.DamlTypeId =>
            new(new Identifier("pkg", "M", "TestInterface"), DamlTypeKind.Interface, "test");
    }

    private sealed record TestView(string Tier) : IDamlRecord<TestView>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(new DamlField("tier", new DamlText(Tier)));

        public static TestView FromRecord(DamlRecord record) =>
            new((record.GetField("tier") as DamlText)?.Value ?? string.Empty);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            throw new NotSupportedException();
    }
}
