// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Xunit;

namespace Canton.Ledger.Abstractions.Tests;

public sealed class ExternalSigningEnumNumbersTests
{
    [Fact]
    public void HashingSchemeVersion_V4_equals_the_wire_number_4()
    {
        ((int)HashingSchemeVersion.V4).Should().Be(4);
    }

    [Fact]
    public void SigningKeySpec_MlDsa65_equals_the_wire_number_5()
    {
        ((int)SigningKeySpec.MlDsa65).Should().Be(5);
    }

    [Fact]
    public void SigningAlgorithm_MlDsa65_equals_the_wire_number_4()
    {
        ((int)SigningAlgorithm.MlDsa65).Should().Be(4);
    }
}
