// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlJsonSerializerDeserializeRecordOverloadTests
{
    [Fact]
    public void DeserializeRecord_with_a_null_literal_applies_the_default_limits()
    {
        var record = DamlJsonSerializer.DeserializeRecord("""{"amount":"42"}""", null);

        record.GetRequiredField("amount").Should().Be(new DamlInt64(42));
    }

    [Fact]
    public void DeserializeRecord_with_no_second_argument_applies_the_default_limits()
    {
        var oversized = "{\"items\":[" + string.Join(",", Enumerable.Repeat("1", 100_001)) + "]}";

        var act = () => DamlJsonSerializer.DeserializeRecord(oversized);

        act.Should().Throw<System.Text.Json.JsonException>()
            .WithMessage("JSON array length 100001 exceeds the maximum supported JSON array length of 100000");
    }
}
