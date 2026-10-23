// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class DamlValueConverterThirtyEightIntegerDigitNumericTests
{
    [Fact]
    public void ToProtoValue_writes_a_38_digit_integer_numeric_with_the_bare_trailing_dot()
    {
        DamlNumeric.TryParseCanonical("99999999999999999999999999999999999999.", out var value).Should().BeTrue();

        DamlValueConverter.ToProtoValue(value).Numeric.Should().Be("99999999999999999999999999999999999999.");
    }
}
