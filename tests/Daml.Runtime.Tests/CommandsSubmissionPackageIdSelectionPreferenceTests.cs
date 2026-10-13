// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Runtime.Tests;

public class CommandsSubmissionPackageIdSelectionPreferenceTests
{
    private static CommandsSubmission Submission() =>
        CommandsSubmission.Single(new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create()));

    [Fact]
    public void PackageIdSelectionPreference_is_unset_by_default()
    {
        Submission().PackageIdSelectionPreference.Should().BeNull();
    }

    [Fact]
    public void WithPackageIdSelectionPreference_keeps_the_package_ids_in_order()
    {
        var preferred = Submission().WithPackageIdSelectionPreference("pkg-b", "pkg-a");

        preferred.PackageIdSelectionPreference.Should().Equal("pkg-b", "pkg-a");
    }

    [Fact]
    public void WithPackageIdSelectionPreference_with_no_package_ids_clears_the_preference()
    {
        var cleared = Submission().WithPackageIdSelectionPreference("pkg-a").WithPackageIdSelectionPreference();

        cleared.PackageIdSelectionPreference.Should().BeNull();
    }

    [Fact]
    public void WithPackageIdSelectionPreference_with_null_clears_the_preference()
    {
        var cleared = Submission().WithPackageIdSelectionPreference("pkg-a").WithPackageIdSelectionPreference(null);

        cleared.PackageIdSelectionPreference.Should().BeNull();
    }

    [Fact]
    public void PackageIdSelectionPreference_is_copied_from_the_array_the_caller_supplied()
    {
        var supplied = new[] { "pkg-a" };
        var preferred = Submission().WithPackageIdSelectionPreference(supplied);

        supplied[0] = "pkg-z";

        preferred.PackageIdSelectionPreference.Should().Equal("pkg-a");
    }

    [Fact]
    public void WithPackageIdSelectionPreference_rejects_a_blank_package_id()
    {
        var act = () => Submission().WithPackageIdSelectionPreference("pkg-a", " ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PackageIdSelectionPreference_takes_part_in_submission_equality()
    {
        var plain = Submission();
        var preferred = plain.WithPackageIdSelectionPreference("pkg-a");

        preferred.Should().NotBe(plain);
        preferred.Should().Be(plain.WithPackageIdSelectionPreference("pkg-a"));
        preferred.GetHashCode().Should().Be(plain.WithPackageIdSelectionPreference("pkg-a").GetHashCode());
        preferred.GetHashCode().Should().NotBe(plain.GetHashCode());
        preferred.Should().NotBe(plain.WithPackageIdSelectionPreference("pkg-b"));
    }
}
