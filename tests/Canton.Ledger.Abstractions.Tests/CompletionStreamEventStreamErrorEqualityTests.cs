// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Abstractions.Tests;

public class CompletionStreamEventStreamErrorEqualityTests
{
    private static CompletionStreamEvent.StreamError Sample(Exception? sourceException) =>
        new(
            new TransportStatus.Grpc(GrpcStatusCode.Unavailable),
            "down",
            DamlErrorCategory.TransientServerFailure,
            "STREAM_DOWN",
            sourceException);

    [Fact]
    public void StreamError_with_a_distinct_SourceException_is_equal_with_the_same_hash()
    {
        var first = Sample(new IOException("x"));
        var second = first with { SourceException = new IOException("x") };

        first.Equals(second).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void StreamError_with_and_without_a_SourceException_is_equal_with_the_same_hash()
    {
        var first = Sample(new IOException("x"));
        var second = Sample(null);

        first.Equals(second).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    public static TheoryData<string> ComparedMembers() =>
        new() { "Status", "Message", "Category", "ErrorId" };

    [Theory]
    [MemberData(nameof(ComparedMembers))]
    public void StreamError_differing_in_one_compared_member_is_unequal(string member)
    {
        var sample = Sample(null);
        var changed = member switch
        {
            "Status" => sample with { Status = new TransportStatus.Http(HttpStatusCode.ServiceUnavailable) },
            "Message" => sample with { Message = "restarted" },
            "Category" => sample with { Category = DamlErrorCategory.ContentionOnSharedResources },
            "ErrorId" => sample with { ErrorId = "STREAM_RESTARTED" },
            _ => throw new ArgumentOutOfRangeException(nameof(member), member, null),
        };

        sample.Equals(changed).Should().BeFalse();
    }

    [Fact]
    public void StreamError_is_unequal_to_null()
    {
        Sample(null).Equals(null).Should().BeFalse();
    }

    [Fact]
    public void StreamError_writes_to_JSON_without_the_SourceException_when_one_was_caught()
    {
        var json = JsonSerializer.Serialize(Sample(CaughtException()));

        json.Should().Be(
            """{"Status":{"$case":"Grpc","StatusCode":14},"Message":"down","Category":1,"ErrorId":"STREAM_DOWN"}""");
    }

    [Fact]
    public void StreamError_reads_back_equal_to_the_value_written_with_a_SourceException_attached()
    {
        var written = Sample(CaughtException());

        var read = JsonSerializer.Deserialize<CompletionStreamEvent.StreamError>(JsonSerializer.Serialize(written));

        read.Should().Be(Sample(null));
        read!.SourceException.Should().BeNull();
    }

    private static Exception CaughtException()
    {
        try
        {
            throw new IOException("x");
        }
        catch (IOException caught)
        {
            return caught;
        }
    }
}
