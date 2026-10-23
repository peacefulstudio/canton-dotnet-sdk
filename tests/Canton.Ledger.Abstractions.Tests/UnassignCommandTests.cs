// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Abstractions.Tests;

public class UnassignCommandTests
{
    public static TheoryData<string> BlankIds => new() { null!, "", "   ", "\t" };

    [Theory]
    [MemberData(nameof(BlankIds))]
    public void UnassignCommand_rejects_blank_contract_id(string contractId)
    {
        var construct = () => new UnassignCommand(contractId, (SynchronizerId)"src", (SynchronizerId)"tgt");

        construct.Should().Throw<ArgumentException>().WithParameterName("ContractId");
    }

    [Fact]
    public void UnassignCommand_rejects_blank_contract_id_through_with_expression()
    {
        var command = new UnassignCommand("00cid", (SynchronizerId)"src", (SynchronizerId)"tgt");

        var rewrite = () => command with { ContractId = " " };

        rewrite.Should().Throw<ArgumentException>().WithParameterName("ContractId");
    }
}
