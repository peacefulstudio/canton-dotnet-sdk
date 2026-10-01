// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Abstractions.Tests;

public class CreatedContractShapeTests
{
    [Fact]
    public void CreatedContract_exposes_no_offset_because_GetContract_never_populates_one()
    {
        var propertyNames = typeof(CreatedContract<>).GetProperties().Select(property => property.Name);

        propertyNames.Should().BeEquivalentTo("ContractId", "Payload", "Key", "WitnessParties");
    }
}
