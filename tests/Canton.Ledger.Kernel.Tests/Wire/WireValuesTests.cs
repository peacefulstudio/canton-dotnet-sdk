// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Wire;

public class WireValuesTests
{
    [Fact]
    public void ContractId_keeps_the_wire_string_verbatim()
    {
        WireValues.ContractId<ProbeTemplate>("00abc123").Value.Should().Be("00abc123");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ContractId_refuses_a_missing_or_blank_wire_string_as_a_MissingRequiredField(string? wireContractId)
    {
        var refusal = () => WireValues.ContractId<ProbeTemplate>(wireContractId);

        refusal.Should().Throw<MalformedResponseException>().Which.Message.Should().Be(
            "Malformed response from ledger: the event has no contract_id, "
            + "though the Ledger API marks the field as required.");
    }

    [Theory]
    [InlineData(0UL, 0L)]
    [InlineData(7UL, 7L)]
    [InlineData(9223372036854775807UL, 9223372036854775807L)]
    public void ReassignmentCounter_converts_a_value_that_fits_a_signed_64_bit_counter(ulong wire, long expected)
    {
        WireValues.ReassignmentCounter(wire).Should().Be(expected);
    }

    [Theory]
    [InlineData(9223372036854775808UL)]
    [InlineData(ulong.MaxValue)]
    public void ReassignmentCounter_refuses_a_value_above_the_signed_64_bit_range_as_a_malformed_response(ulong wire)
    {
        var refusal = () => WireValues.ReassignmentCounter(wire);

        refusal.Should().Throw<MalformedResponseException>()
            .Which.Detail.Should().Be($"reassignment counter {wire} exceeds the signed 64-bit range.");
    }

    private sealed class ProbeTemplate : IDamlType
    {
        public static DamlTypeDescriptor DamlTypeId { get; } =
            new(new Identifier("pkg-probe", "Probe.Module", "ProbeEntity"), DamlTypeKind.Template, "probe-package");
    }
}
