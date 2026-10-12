// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Wire;

public class ParsedLedgerErrorExtensionsTests
{
    [Fact]
    public void WithMessage_rewords_a_structured_error_and_keeps_everything_else()
    {
        var reworded = new ParsedLedgerError.Structured(
                DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
                "PACKAGE_NOT_FOUND",
                "the package is unknown",
                new Dictionary<string, string> { ["category"] = "11" },
                new TransportStatus.Http(HttpStatusCode.NotFound))
            .WithMessage("the package is unknown (narrow the window)");

        var structured = reworded.Should().BeOfType<ParsedLedgerError.Structured>().Subject;
        structured.Message.Should().Be("the package is unknown (narrow the window)");
        structured.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing);
        structured.ErrorId.Should().Be("PACKAGE_NOT_FOUND");
        structured.Metadata.Should().Equal(new Dictionary<string, string> { ["category"] = "11" });
        structured.Status.Should().Be(new TransportStatus.Http(HttpStatusCode.NotFound));
    }

    [Fact]
    public void WithMessage_rewords_an_unstructured_error_and_keeps_its_status_and_category()
    {
        var reworded = new ParsedLedgerError.Unstructured(
                "unavailable",
                new TransportStatus.Grpc(GrpcStatusCode.Unavailable),
                DamlErrorCategory.TransientServerFailure)
            .WithMessage("Status(StatusCode=\"Unavailable\", Detail=\"\")");

        var unstructured = reworded.Should().BeOfType<ParsedLedgerError.Unstructured>().Subject;
        unstructured.Message.Should().Be("Status(StatusCode=\"Unavailable\", Detail=\"\")");
        unstructured.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        unstructured.Category.Should().Be(DamlErrorCategory.TransientServerFailure);
    }
}
