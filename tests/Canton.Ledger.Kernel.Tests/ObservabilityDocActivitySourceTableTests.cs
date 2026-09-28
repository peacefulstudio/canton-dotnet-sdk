// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Telemetry;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public class ObservabilityDocActivitySourceTableTests
{
    private const string DocFileName = "observability.md";

    private static string DocText() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, DocFileName));

    [Theory]
    [InlineData("Canton.Ledger.Grpc.Client.LedgerClient")]
    [InlineData("Canton.Ledger.Grpc.Client.AdminClient")]
    [InlineData("Canton.Ledger.Rest.Client.RestLedgerClient")]
    [InlineData("Canton.Ledger.Rest.Client.RestAdminClient")]
    [InlineData("Canton.Ledger.Pqs.Client.PqsClient")]
    public void Source_table_lists_every_well_known_source(string expectedName) =>
        DocText().Should().Contain(expectedName);

    [Fact]
    public void Source_table_row_count_matches_the_well_known_set()
    {
        var tableRows = DocText()
            .Split('\n')
            .Count(line => line.TrimStart().StartsWith("| `Canton.Ledger", StringComparison.Ordinal));

        tableRows.Should().Be(LedgerActivitySourceNames.All.Count);
    }
}
