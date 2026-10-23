// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class SubmitterInfoTests
{
    [Fact]
    public void Implicit_Party_conversion_should_yield_single_party_actAs()
    {
        SubmitterInfo info = new Party("alice");

        info.ActAs.Should().ContainSingle().Which.Should().Be(new Party("alice"));
        info.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_with_actAs_set_and_readAs_set_should_preserve_both()
    {
        var info = new SubmitterInfo(
            actAs: new HashSet<Party> { new("alice"), new("bob") },
            readAs: new HashSet<Party> { new("observer") });

        info.ActAs.Should().BeEquivalentTo(new[] { new Party("alice"), new Party("bob") });
        info.ReadAs.Should().ContainSingle().Which.Should().Be(new Party("observer"));
    }

    [Fact]
    public void Constructor_with_empty_actAs_should_throw_ArgumentException()
    {
        var act = () => new SubmitterInfo(new HashSet<Party>());

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ActAs*at least one party*")
            .And.ParamName.Should().Be("actAs");
    }

    [Fact]
    public void Constructor_with_null_actAs_should_throw_ArgumentNullException()
    {
        var act = () => new SubmitterInfo((IReadOnlySet<Party>)null!);

        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("actAs");
    }

    [Fact]
    public void Constructor_should_take_defensive_copy_of_actAs()
    {
        var input = new HashSet<Party> { new("alice") };
        var info = new SubmitterInfo(input);

        input.Add(new Party("bob"));

        info.ActAs.Should().ContainSingle().Which.Should().Be(new Party("alice"));
    }

    [Fact]
    public void Default_SubmitterInfo_ActAs_access_should_throw()
    {
        SubmitterInfo defaultValue = default;

        var act = () => defaultValue.ActAs;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*default (uninitialized) SubmitterInfo*");
    }

    [Fact]
    public void CommandsSubmission_WithSubmitter_should_set_actAs_and_readAs()
    {
        var submission = CommandsSubmission.Single(
            new CreateCommand(
                new Identifier("pkg", "Module", "Template"),
                new DamlRecord(null, [])));

        var submitter = new SubmitterInfo(
            actAs: new HashSet<Party> { new("alice"), new("bob") },
            readAs: new HashSet<Party> { new("observer") });

        var result = submission.WithSubmitter(submitter);

        result.ActAs.Should().BeEquivalentTo(new[] { new Party("alice"), new Party("bob") });
        result.ReadAs.Should().ContainSingle().Which.Should().Be(new Party("observer"));
    }

    [Fact]
    public void CommandsSubmission_WithSubmitter_with_no_readAs_should_leave_readAs_unset()
    {
        var submission = CommandsSubmission.Single(
            new CreateCommand(
                new Identifier("pkg", "Module", "Template"),
                new DamlRecord(null, [])));

        var submitter = new SubmitterInfo(new Party("alice"));

        var result = submission.WithSubmitter(submitter);

        result.ActAs.Should().ContainSingle().Which.Should().Be(new Party("alice"));
        result.ReadAs.Should().BeNull();
    }

    [Fact]
    public void CommandsSubmission_WithSubmitter_with_no_readAs_should_clear_existing_readAs()
    {
        var submission = CommandsSubmission.Single(
                new CreateCommand(
                    new Identifier("pkg", "Module", "Template"),
                    new DamlRecord(null, [])))
            .WithSubmitter(new SubmitterInfo(
                actAs: new HashSet<Party> { new("original-actAs") },
                readAs: new HashSet<Party> { new("observer") }));

        submission.ReadAs.Should().NotBeNull();

        var result = submission.WithSubmitter(new SubmitterInfo(new Party("alice")));

        result.ActAs.Should().ContainSingle().Which.Should().Be(new Party("alice"));
        result.ReadAs.Should().BeNull();
    }

    [Fact]
    public void CommandsSubmission_WithSubmitter_should_replace_pre_set_actAs_not_merge_with_it()
    {
        var submission = CommandsSubmission.Single(
                new CreateCommand(
                    new Identifier("pkg", "Module", "Template"),
                    new DamlRecord(null, [])))
            .WithActAs(new Party("alice"));

        var result = submission.WithSubmitter(new Party("bob"));

        result.ActAs.Should().ContainSingle().Which.Should().Be(new Party("bob"));
    }

    [Fact]
    public void SubmitterInfo_single_party_constructor_should_reject_default_Party()
    {
        var act = () => new SubmitterInfo(default(Party));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SubmitterInfo_multi_party_constructor_should_reject_default_Party_in_set()
    {
        var act = () => new SubmitterInfo(
            actAs: new HashSet<Party> { default, new("alice") });

        act.Should().Throw<ArgumentException>().WithMessage("*actAs*default*");
    }

    [Fact]
    public void SubmitterInfo_should_be_immutable_against_caller_set_mutation()
    {
        var input = new HashSet<Party> { new("alice"), new("bob") };
        var info = new SubmitterInfo(actAs: input);

        input.Add(new Party("eve"));

        info.ActAs.Should().HaveCount(2);
        info.ActAs.Should().Contain(new Party("alice")).And.Contain(new Party("bob"));
        info.ActAs.Should().NotContain(new Party("eve"));
    }

    [Fact]
    public void SubmitterInfo_should_compare_by_set_contents_not_by_reference()
    {
        var a = new SubmitterInfo(
            actAs: new HashSet<Party> { new("alice"), new("bob") },
            readAs: new HashSet<Party> { new("observer") });
        var b = new SubmitterInfo(
            actAs: new HashSet<Party> { new("bob"), new("alice") },
            readAs: new HashSet<Party> { new("observer") });

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());

        var differentReadAs = new SubmitterInfo(
            actAs: new HashSet<Party> { new("alice"), new("bob") },
            readAs: new HashSet<Party> { new("other") });
        a.Should().NotBe(differentReadAs);
    }

    [Fact]
    public void CommandsSubmission_WithSubmitter_via_implicit_Party_should_yield_single_party_submission()
    {
        var submission = CommandsSubmission.Single(
            new CreateCommand(
                new Identifier("pkg", "Module", "Template"),
                new DamlRecord(null, [])));

        var result = submission.WithSubmitter(new Party("alice"));

        result.ActAs.Should().ContainSingle().Which.Should().Be(new Party("alice"));
        result.ReadAs.Should().BeNull();
    }
}
