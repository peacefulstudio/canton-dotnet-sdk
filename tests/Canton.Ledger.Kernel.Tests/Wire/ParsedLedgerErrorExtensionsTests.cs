// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Wire;

public class ParsedLedgerErrorExtensionsTests
{
    [Fact]
    public void ToException_carries_a_structured_error_as_a_classified_rejection()
    {
        var parsed = new ParsedLedgerError.Structured(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "PACKAGE_NOT_FOUND",
            "the package is unknown",
            new Dictionary<string, string> { ["category"] = "11" },
            new TransportStatus.Http(HttpStatusCode.NotFound));

        var exception = parsed.ToException();

        exception.Message.Should().Be("the package is unknown");
        exception.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing);
        exception.ErrorId.Should().Be("PACKAGE_NOT_FOUND");
        exception.Metadata.Should().Equal(new Dictionary<string, string> { ["category"] = "11" });
        exception.Status.Should().BeNull();
        exception.CommitState.Should().Be(CommitState.NotCommitted);
    }

    [Fact]
    public void ToException_carries_an_unstructured_error_as_a_transport_failure_of_unknown_commit_state()
    {
        var parsed = new ParsedLedgerError.Unstructured(
            "the participant is unreachable",
            new TransportStatus.Http(HttpStatusCode.ServiceUnavailable),
            DamlErrorCategory.TransientServerFailure);

        var exception = parsed.ToException();

        exception.Message.Should().Be("the participant is unreachable");
        exception.Status.Should().Be(new TransportStatus.Http(HttpStatusCode.ServiceUnavailable));
        exception.Category.Should().Be(DamlErrorCategory.TransientServerFailure);
        exception.ErrorId.Should().BeNull();
        exception.Metadata.Should().BeNull();
        exception.CommitState.Should().Be(CommitState.Unknown);
    }
}
