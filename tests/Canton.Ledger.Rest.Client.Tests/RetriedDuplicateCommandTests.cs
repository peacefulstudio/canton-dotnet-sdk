// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RetriedDuplicateCommandTests
{
    public static TheoryData<string?, string?, long?> Metadata => new()
    {
        { "true", "9663", 9663L },
        { null, "9663", 9663L },
        { "True", "9663", 9663L },
        { "false", "9663", null },
        { "FALSE", "9663", null },
        { "true", null, null },
        { "true", "0", null },
        { "true", "-5", null },
        { "true", "", null },
        { "true", "not-an-offset", null },
        { "true", "9663.5", null },
        { "true", "9223372036854775807", 9223372036854775807L },
        { "true", "9223372036854775808", null },
    };

    [Theory]
    [MemberData(nameof(Metadata))]
    public void TryReadCompletionOffset_reads_the_offset_of_an_accepted_command_only(
        string? accepted, string? completionOffset, long? expected)
    {
        var metadata = new Dictionary<string, string> { ["definite_answer"] = "true" };
        if (accepted is not null)
        {
            metadata["accepted"] = accepted;
        }
        if (completionOffset is not null)
        {
            metadata["completion_offset"] = completionOffset;
        }

        var found = RetriedDuplicateCommand.TryReadCompletionOffset(metadata, out var offset);

        found.Should().Be(expected is not null);
        offset.Should().Be(expected ?? 0L);
    }
}
