// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;
using AllocationInstructionV2Wire = Splice.Api.Token.Registry.Client.AllocationInstructionV2.DisclosedContract;
using AllocationV2Wire = Splice.Api.Token.Registry.Client.AllocationV2.DisclosedContract;

namespace Splice.Api.Token.Registry.Client.Tests;

public class DisclosedContractMappingTests
{
    [Fact]
    public void ToDisclosedContract_carries_the_contract_id_template_blob_and_synchronizer_of_the_wire_contract()
    {
        var wire = new AllocationV2Wire
        {
            ContractId = "00ab",
            TemplateId = "0123abcd:Splice.Amulet:Amulet",
            CreatedEventBlob = "AQID",
            SynchronizerId = "global-domain::1220",
        };

        var disclosed = wire.ToDisclosedContract();

        disclosed.ContractId.Should().Be("00ab");
        disclosed.TemplateId.ToString().Should().Be("0123abcd:Splice.Amulet:Amulet");
        disclosed.CreatedEventBlob.ToArray().Should().Equal(1, 2, 3);
        disclosed.SynchronizerId.Should().NotBeNull();
        disclosed.SynchronizerId!.Value.ToString().Should().Be("global-domain::1220");
    }

    [Fact]
    public void ToDisclosedContract_leaves_the_synchronizer_unset_when_the_wire_contract_carries_none()
    {
        var wire = new AllocationV2Wire { ContractId = "00cd", TemplateId = "pkg:Mod:Ent", CreatedEventBlob = "BAUG" };

        wire.ToDisclosedContract().SynchronizerId.Should().BeNull();
    }

    [Fact]
    public void ToDisclosedContract_maps_the_contracts_of_both_allocation_namespaces_alike()
    {
        var v2Instruction = new AllocationInstructionV2Wire { ContractId = "01", TemplateId = "pkg:Mod:Ent", CreatedEventBlob = "AQ==" };
        var v2Allocation = new AllocationV2Wire { ContractId = "02", TemplateId = "pkg:Mod:Ent", CreatedEventBlob = "Ag==" };

        v2Instruction.ToDisclosedContract().CreatedEventBlob.ToArray().Should().Equal(1);
        v2Allocation.ToDisclosedContract().CreatedEventBlob.ToArray().Should().Equal(2);
    }

    [Fact]
    public void ToDisclosedContract_refuses_an_empty_created_event_blob()
    {
        var wire = new AllocationV2Wire { ContractId = "00ab", TemplateId = "pkg:Mod:Ent", CreatedEventBlob = "" };

        var act = () => wire.ToDisclosedContract();

        act.Should().Throw<FormatException>().WithMessage("*createdEventBlob*");
    }

    [Fact]
    public void ToDisclosedContract_refuses_a_created_event_blob_that_is_not_base64()
    {
        var wire = new AllocationV2Wire { ContractId = "00ab", TemplateId = "pkg:Mod:Ent", CreatedEventBlob = "***" };

        var act = () => wire.ToDisclosedContract();

        act.Should().Throw<FormatException>();
    }
}
