// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

public class ExerciseOutcomeJsonTests
{
    [Fact]
    public void One_declared_as_its_base_type_writes_the_case_discriminator_and_reads_back()
    {
        ExerciseOutcome<long> value = new ExerciseOutcome<long>.One(42);

        var written = JsonSerializer.Serialize(value);

        written.Should().Be("""{"$case":"One","Result":42}""");
        JsonSerializer.Deserialize<ExerciseOutcome<long>>(written).Should().Be(value);
    }

    [Fact]
    public void None_declared_as_its_base_type_writes_the_case_discriminator_and_reads_back()
    {
        ExerciseOutcome<long> value = new ExerciseOutcome<long>.None();

        var written = JsonSerializer.Serialize(value);

        written.Should().Be("""{"$case":"None"}""");
        JsonSerializer.Deserialize<ExerciseOutcome<long>>(written).Should().Be(value);
    }

    [Fact]
    public void Many_declared_as_its_base_type_writes_the_case_discriminator_and_reads_back()
    {
        ExerciseOutcome<long> value = new ExerciseOutcome<long>.Many(["c1", "c2"]);

        var written = JsonSerializer.Serialize(value);

        written.Should().Be("""{"$case":"Many","ContractIds":["c1","c2"],"Count":2}""");
        JsonSerializer.Deserialize<ExerciseOutcome<long>>(written).Should().Be(value);
    }

    [Fact]
    public void DamlError_declared_as_its_base_type_writes_the_case_discriminator_and_reads_back()
    {
        ExerciseOutcome<long> value = new ExerciseOutcome<long>.DamlError(
            DamlErrorCategory.ContentionOnSharedResources,
            "LOCAL_VERDICT_LOCKED_CONTRACTS",
            "contract is locked",
            new Dictionary<string, string> { ["category"] = "10" });

        var written = JsonSerializer.Serialize(value);

        written.Should().Be(
            """{"$case":"DamlError","Category":2,"ErrorId":"LOCAL_VERDICT_LOCKED_CONTRACTS","Message":"contract is locked","Metadata":{"category":"10"}}""");
        JsonSerializer.Deserialize<ExerciseOutcome<long>>(written).Should().Be(value);
    }

    [Fact]
    public void InfraError_declared_as_its_base_type_writes_the_case_discriminator_and_reads_back()
    {
        ExerciseOutcome<long> value = new ExerciseOutcome<long>.InfraError(
            new TransportStatus.Http(System.Net.HttpStatusCode.BadGateway),
            "bad gateway",
            DamlErrorCategory.TransientServerFailure);

        var written = JsonSerializer.Serialize(value);

        written.Should().Be(
            """{"$case":"InfraError","Status":{"$case":"Http","StatusCode":502},"Message":"bad gateway","Category":1}""");
        JsonSerializer.Deserialize<ExerciseOutcome<long>>(written).Should().Be(value);
    }

    [Fact]
    public void InfraError_carrying_an_exception_writes_without_it_and_reads_back_without_it()
    {
        ExerciseOutcome<long> value = new ExerciseOutcome<long>.InfraError(
            new TransportStatus.Grpc(GrpcStatusCode.Unavailable),
            "unavailable",
            DamlErrorCategory.TransientServerFailure,
            ThrownException("transport reset"));

        var written = JsonSerializer.Serialize(value);
        var read = JsonSerializer.Deserialize<ExerciseOutcome<long>>(written);

        written.Should().NotContain("SourceException");
        read.Should().Be(new ExerciseOutcome<long>.InfraError(
            new TransportStatus.Grpc(GrpcStatusCode.Unavailable),
            "unavailable",
            DamlErrorCategory.TransientServerFailure));
    }

    [Fact]
    public void CommittedUndecodable_carrying_an_exception_writes_without_it()
    {
        ExerciseOutcome<long> value = new ExerciseOutcome<long>.CommittedUndecodable(
            "update-1", "response could not be decoded", ThrownException("bad payload"));

        var written = JsonSerializer.Serialize(value);

        written.Should().Be(
            """{"$case":"CommittedUndecodable","UpdateId":"update-1","Message":"response could not be decoded"}""");
    }

    [Fact]
    public void CommittedUndecodable_reads_back_without_the_exception()
    {
        ExerciseOutcome<long> value = new ExerciseOutcome<long>.CommittedUndecodable(
            "update-1", "response could not be decoded", ThrownException("bad payload"));

        var read = JsonSerializer.Deserialize<ExerciseOutcome<long>>(JsonSerializer.Serialize(value));

        read.Should().Be(new ExerciseOutcome<long>.CommittedUndecodable("update-1", "response could not be decoded"));
    }

    [Fact]
    public void CommittedUndecodable_without_an_update_id_or_exception_reads_its_own_write_back()
    {
        ExerciseOutcome<long> value = new ExerciseOutcome<long>.CommittedUndecodable(null, "response could not be decoded");

        var written = JsonSerializer.Serialize(value);

        written.Should().Be("""{"$case":"CommittedUndecodable","UpdateId":null,"Message":"response could not be decoded"}""");
        JsonSerializer.Deserialize<ExerciseOutcome<long>>(written).Should().Be(value);
    }

    [Fact]
    public void CommittedUndecodable_reads_back_on_options_that_register_the_daml_converters()
    {
        var options = new JsonSerializerOptions().AddDamlConverters();
        ExerciseOutcome<long> value = new ExerciseOutcome<long>.CommittedUndecodable(
            "update-1", "response could not be decoded", ThrownException("bad payload"));

        var read = JsonSerializer.Deserialize<ExerciseOutcome<long>>(JsonSerializer.Serialize(value, options), options);

        read.Should().Be(new ExerciseOutcome<long>.CommittedUndecodable("update-1", "response could not be decoded"));
    }

    [Fact]
    public void CommittedUndecodable_reads_back_under_the_strict_options()
    {
        ExerciseOutcome<long> value = new ExerciseOutcome<long>.CommittedUndecodable(
            "update-1", "response could not be decoded", ThrownException("bad payload"));

        var written = JsonSerializer.Serialize(value, JsonSerializerOptions.Strict);

        written.Should().Be(
            """{"$case":"CommittedUndecodable","UpdateId":"update-1","Message":"response could not be decoded"}""");
        JsonSerializer.Deserialize<ExerciseOutcome<long>>(written, JsonSerializerOptions.Strict).Should().Be(
            new ExerciseOutcome<long>.CommittedUndecodable("update-1", "response could not be decoded"));
    }

    [Fact]
    public void ExerciseOutcome_reads_back_on_options_that_register_the_daml_converters()
    {
        var options = new JsonSerializerOptions().AddDamlConverters();
        ExerciseOutcome<long> value = new ExerciseOutcome<long>.One(42);

        var written = JsonSerializer.Serialize(value, options);

        written.Should().Be("""{"$case":"One","Result":42}""");
        JsonSerializer.Deserialize<ExerciseOutcome<long>>(written, options).Should().Be(value);
    }

    [Fact]
    public void ExerciseOutcome_refuses_an_unknown_case()
    {
        var act = () => JsonSerializer.Deserialize<ExerciseOutcome<long>>("""{"$case":"Cancelled"}""");

        act.Should().Throw<JsonException>().WithMessage("*Cancelled*");
    }

    private static Exception ThrownException(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException caught)
        {
            return caught;
        }
    }
}
