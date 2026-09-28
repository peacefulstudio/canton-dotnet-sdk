// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Runtime.Tests;

public class CommandsSubmissionDeduplicationPeriodTests
{
    private static CommandsSubmission Submission() =>
        CommandsSubmission.Single(new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create()));

    [Fact]
    public void WithDeduplicationPeriod_sets_the_period()
    {
        var submission = Submission();

        var result = submission.WithDeduplicationPeriod(
            new DeduplicationPeriod.Duration(TimeSpan.FromMinutes(5)));

        result.DeduplicationPeriod.Should().BeOfType<DeduplicationPeriod.Duration>()
            .Which.Length.Should().Be(TimeSpan.FromSeconds(300));
        submission.DeduplicationPeriod.Should().BeNull();
    }

    [Fact]
    public void WithDeduplicationPeriod_null_clears_the_period()
    {
        var submission = Submission()
            .WithDeduplicationPeriod(new DeduplicationPeriod.Offset(LedgerOffset.At(42)));

        var result = submission.WithDeduplicationPeriod(null);

        result.DeduplicationPeriod.Should().BeNull();
    }

    [Fact]
    public void WithDeduplicationPeriod_rejects_a_negative_duration()
    {
        var submission = Submission();

        var act = () => submission.WithDeduplicationPeriod(
            new DeduplicationPeriod.Duration(TimeSpan.FromSeconds(-1)));

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("DeduplicationPeriod");
    }

    [Fact]
    public void CommandsSubmission_with_equal_periods_compares_equal()
    {
        var left = Submission().WithDeduplicationPeriod(
            new DeduplicationPeriod.Duration(TimeSpan.FromSeconds(90)));
        var right = Submission().WithDeduplicationPeriod(
            new DeduplicationPeriod.Duration(TimeSpan.FromMinutes(1.5)));

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void CommandsSubmission_with_different_periods_compares_unequal()
    {
        var byDuration = Submission().WithDeduplicationPeriod(
            new DeduplicationPeriod.Duration(TimeSpan.FromSeconds(90)));
        var byOffset = Submission().WithDeduplicationPeriod(
            new DeduplicationPeriod.Offset(LedgerOffset.At(90)));
        var unset = Submission();

        byDuration.Should().NotBe(byOffset);
        byDuration.Should().NotBe(unset);
        unset.Should().NotBe(byOffset);
    }

    [Fact]
    public void DeduplicationPeriod_init_rejects_a_negative_duration()
    {
        var act = () => Submission() with
        {
            DeduplicationPeriod = new DeduplicationPeriod.Duration(TimeSpan.FromTicks(-1)),
        };

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("DeduplicationPeriod");
    }
}
