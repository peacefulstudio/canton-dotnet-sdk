// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

public class TransportStatusJsonTests
{
    private static readonly JsonSerializerOptions Registered = new JsonSerializerOptions().AddDamlConverters();

    private sealed record Failure(string Message, TransportStatus Status);

    private sealed record MaybeFailure(string Message, TransportStatus? Status);

    public static TheoryData<TransportStatus, string> EveryArm => new()
    {
        { new TransportStatus.Grpc(GrpcStatusCode.Unavailable), """{"$case":"Grpc","StatusCode":14}""" },
        { new TransportStatus.Http(HttpStatusCode.NotFound), """{"$case":"Http","StatusCode":404}""" },
        { new TransportStatus.NoResponse(), """{"$case":"NoResponse"}""" },
        { new TransportStatus.UndecodableBody(), """{"$case":"UndecodableBody"}""" },
    };

    [Theory]
    [MemberData(nameof(EveryArm))]
    public void TransportStatus_writes_each_arm_with_a_case_discriminator(TransportStatus value, string json)
    {
        JsonSerializer.Serialize(value).Should().Be(json);
    }

    [Theory]
    [MemberData(nameof(EveryArm))]
    public void TransportStatus_reads_each_arm_back_from_its_discriminated_shape(TransportStatus value, string json)
    {
        JsonSerializer.Deserialize<TransportStatus>(json).Should().Be(value);
    }

    [Fact]
    public void TransportStatus_reads_an_HTTP_status_the_BCL_enum_does_not_name()
    {
        JsonSerializer.Deserialize<TransportStatus>("""{"$case":"Http","StatusCode":499}""")
            .Should().Be(new TransportStatus.Http((HttpStatusCode)499));
    }

    [Fact]
    public void TransportStatus_writes_the_discriminated_shape_for_an_arm_typed_variable_once_registered()
    {
        var value = new TransportStatus.Grpc(GrpcStatusCode.Aborted);

        var json = JsonSerializer.Serialize(value, Registered);

        json.Should().Be("""{"$case":"Grpc","StatusCode":10}""");
        JsonSerializer.Deserialize<TransportStatus>(json, Registered).Should().Be(value);
    }

    [Fact]
    public void TransportStatus_refuses_an_unknown_case()
    {
        var act = () => JsonSerializer.Deserialize<TransportStatus>("""{"$case":"Smtp"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("*TransportStatus names an unknown case \"Smtp\"*Grpc, Http, NoResponse, UndecodableBody*");
    }

    [Fact]
    public void TransportStatus_refuses_an_object_without_a_discriminator()
    {
        var act = () => JsonSerializer.Deserialize<TransportStatus>("""{"StatusCode":14}""");

        act.Should().Throw<JsonException>().WithMessage("*TransportStatus is missing the \"$case\" discriminator*");
    }

    [Fact]
    public void TransportStatus_refuses_a_payload_that_omits_a_member_the_slot_forbids_a_null_in_under_AddDamlConverters()
    {
        var act = () => JsonSerializer.Deserialize<Failure>("""{"Message":"boom"}""", Registered);

        act.Should().Throw<JsonException>().WithMessage("*Status*");
    }

    [Fact]
    public void TransportStatus_reads_an_omitted_member_as_absent_where_the_slot_permits_one()
    {
        var failure = JsonSerializer.Deserialize<MaybeFailure>("""{"Message":"boom"}""", Registered);

        failure!.Status.Should().BeNull();
    }
}
