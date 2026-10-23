// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Outcomes;
using Xunit;
using RpcException = Grpc.Core.RpcException;
using Status = Grpc.Core.Status;
using StatusCode = Grpc.Core.StatusCode;

namespace Canton.Ledger.Grpc.Client.Tests;

public class DamlErrorParserOffsetErrorTests
{
    [Fact]
    public void Parse_reports_an_offset_after_the_ledger_end_as_a_seek_after_end_error_over_OutOfRange()
    {
        var rejection = LedgerClientTestFixtures.MakeDamlRpcException(
            "OFFSET_AFTER_LEDGER_END",
            "Offset 9999 is after ledger end 42",
            "InvalidGivenCurrentSystemStateSeekAfterEnd",
            StatusCode.OutOfRange);

        var parsed = DamlErrorParser.Parse(rejection);

        var structured = parsed.Should().BeOfType<ParsedLedgerError.Structured>().Subject;
        structured.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.OutOfRange));
        structured.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateSeekAfterEnd);
        structured.ErrorId.Should().Be("OFFSET_AFTER_LEDGER_END");
        structured.Message.Should().Be("Offset 9999 is after ledger end 42");
    }

    [Fact]
    public void Parse_reports_a_pruned_offset_as_a_failed_precondition_that_is_not_a_seek_after_end_error()
    {
        var rejection = LedgerClientTestFixtures.MakeDamlRpcException(
            "PARTICIPANT_PRUNED_DATA_ACCESSED",
            "Transactions request from 5 precedes pruned offset 10",
            "InvalidGivenCurrentSystemStateOther",
            StatusCode.FailedPrecondition);

        var parsed = DamlErrorParser.Parse(rejection);

        var structured = parsed.Should().BeOfType<ParsedLedgerError.Structured>().Subject;
        structured.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.FailedPrecondition));
        structured.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateOther);
        structured.ErrorId.Should().Be("PARTICIPANT_PRUNED_DATA_ACCESSED");
    }

    [Fact]
    public void Parse_carries_an_unstructured_OutOfRange_status_through_with_no_category()
    {
        var rejection = new RpcException(new Status(StatusCode.OutOfRange, "offset beyond end"));

        var parsed = DamlErrorParser.Parse(rejection);

        var unstructured = parsed.Should().BeOfType<ParsedLedgerError.Unstructured>().Subject;
        unstructured.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.OutOfRange));
        unstructured.Category.Should().BeNull();
        unstructured.Message.Should().Be("offset beyond end");
    }
}
