// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

public class DeduplicationPeriodJsonTests
{
    [Fact]
    public void Offset_declared_as_its_base_type_writes_the_case_discriminator_and_reads_back()
    {
        DeduplicationPeriod value = new DeduplicationPeriod.Offset(LedgerOffset.At(42));

        var written = JsonSerializer.Serialize(value);

        written.Should().Be("""{"$case":"Offset","Start":42}""");
        JsonSerializer.Deserialize<DeduplicationPeriod>(written).Should().Be(value);
    }

    [Fact]
    public void Offset_at_participant_begin_reads_back_as_participant_begin()
    {
        DeduplicationPeriod value = new DeduplicationPeriod.Offset(LedgerOffset.Begin);

        var written = JsonSerializer.Serialize(value);

        JsonSerializer.Deserialize<DeduplicationPeriod>(written).Should().Be(value);
    }

    [Fact]
    public void Duration_declared_as_its_base_type_writes_the_case_discriminator_and_reads_back()
    {
        DeduplicationPeriod value = new DeduplicationPeriod.Duration(TimeSpan.FromMinutes(5));

        var written = JsonSerializer.Serialize(value);

        written.Should().Be("""{"$case":"Duration","Length":"00:05:00"}""");
        JsonSerializer.Deserialize<DeduplicationPeriod>(written).Should().Be(value);
    }

    [Fact]
    public void DeduplicationPeriod_reads_back_on_options_that_register_the_daml_converters()
    {
        var options = new JsonSerializerOptions().AddDamlConverters();
        DeduplicationPeriod value = new DeduplicationPeriod.Offset(LedgerOffset.At(7));

        var written = JsonSerializer.Serialize(value, options);

        written.Should().Be("""{"$case":"Offset","Start":7}""");
        JsonSerializer.Deserialize<DeduplicationPeriod>(written, options).Should().Be(value);
    }

    [Fact]
    public void DeduplicationPeriod_refuses_an_unknown_case()
    {
        var act = () => JsonSerializer.Deserialize<DeduplicationPeriod>("""{"$case":"Forever"}""");

        act.Should().Throw<JsonException>().WithMessage("*Forever*");
    }

    [Fact]
    public void DeduplicationPeriod_refuses_a_payload_missing_the_discriminator()
    {
        var act = () => JsonSerializer.Deserialize<DeduplicationPeriod>("""{"Start":42}""");

        act.Should().Throw<JsonException>().WithMessage("*$case*");
    }
}
