// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Streams;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Streams;

public class WireOffsetTests
{
    [Fact]
    public void FromProto_reads_the_unset_zero_as_absent()
    {
        var offset = WireOffset.FromProto(0L);

        offset.IsAbsent.Should().BeTrue();
        offset.IsMalformed.Should().BeFalse();
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(42L)]
    [InlineData(long.MaxValue)]
    public void FromProto_reads_a_positive_value_as_that_offset(long wire)
    {
        var offset = WireOffset.FromProto(wire);

        offset.IsAbsent.Should().BeFalse();
        offset.IsMalformed.Should().BeFalse();
        offset.Value.Should().Be(wire);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void FromProto_reads_a_negative_value_as_malformed(long wire)
    {
        var offset = WireOffset.FromProto(wire);

        offset.IsMalformed.Should().BeTrue();
        offset.IsAbsent.Should().BeFalse();
    }

    [Fact]
    public void FromText_reads_a_missing_text_as_absent()
    {
        WireOffset.FromText(null).IsAbsent.Should().BeTrue();
    }

    [Fact]
    public void FromText_reads_the_zero_text_as_absent()
    {
        WireOffset.FromText("0").IsAbsent.Should().BeTrue();
    }

    [Theory]
    [InlineData("1", 1L)]
    [InlineData("42", 42L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    public void FromText_reads_a_decimal_text_as_that_offset(string wire, long expected)
    {
        var offset = WireOffset.FromText(wire);

        offset.IsAbsent.Should().BeFalse();
        offset.IsMalformed.Should().BeFalse();
        offset.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("4 2")]
    [InlineData("+7")]
    [InlineData("9223372036854775808")]
    public void FromText_reads_anything_but_an_unsigned_decimal_as_malformed(string wire)
    {
        WireOffset.FromText(wire).IsMalformed.Should().BeTrue();
    }

    [Fact]
    public void ToMalformedResponse_names_the_field_and_the_offending_wire_text()
    {
        var exception = WireOffset.FromText("-1").ToMalformedResponse("unassigned event offset");

        exception.Should().BeOfType<MalformedResponseException>();
        exception.Detail.Should().Be("unassigned event offset '-1' is not a valid ledger offset");
    }

    [Fact]
    public void ToMalformedResponse_renders_a_negative_proto_value_as_its_decimal_text()
    {
        var exception = WireOffset.FromProto(-5L).ToMalformedResponse("created event offset");

        exception.Detail.Should().Be("created event offset '-5' is not a valid ledger offset");
    }

    [Fact]
    public void FromRequiredText_reads_a_missing_text_as_malformed_because_the_field_must_be_present()
    {
        var offset = WireOffset.FromRequiredText(null);

        offset.IsMalformed.Should().BeTrue();
        offset.IsAbsent.Should().BeFalse();
        offset.ToMalformedResponse("transaction offset").Detail
            .Should().Be("transaction offset '' is not a valid ledger offset");
    }

    [Theory]
    [InlineData("0", 0L)]
    [InlineData("42", 42L)]
    public void FromRequiredText_reads_a_decimal_text_as_that_offset(string wire, long expected)
    {
        var offset = WireOffset.FromRequiredText(wire);

        offset.IsMalformed.Should().BeFalse();
        offset.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    public void FromRequiredText_reads_anything_but_an_unsigned_decimal_as_malformed(string wire)
    {
        WireOffset.FromRequiredText(wire).IsMalformed.Should().BeTrue();
    }
}
