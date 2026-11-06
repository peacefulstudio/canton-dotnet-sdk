// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Splice.Api.Token.Registry.Client.AllocationV2;
using Xunit;

namespace Splice.Api.Token.Registry.Client.Tests;

public class AllocationV2ClientTests
{
    private const string FactoryResponse =
        """
        {"factoryId":"factory-1","choiceContext":{"choiceContextData":{"values":{}},"disclosedContracts":[{"templateId":"pkg:Mod:Ent","contractId":"00ab","createdEventBlob":"AQID","synchronizerId":"sync::1220"}]}}
        """;

    private const string ChoiceContextResponse =
        """
        {"choiceContextData":{"values":{"k":{"tag":"AV_Bool","value":true}}},"disclosedContracts":[]}
        """;

    [Fact]
    public async Task GetSettlementFactory_posts_the_choice_arguments_to_the_settlement_factory_route()
    {
        var (api, handler) = RegistryClientFixture.Resolve<IAllocationV2Api>(HttpStatusCode.OK, FactoryResponse);
        using var arguments = JsonDocument.Parse("""{"settlement":"s1"}""");

        await api.GetSettlementFactory(new GetFactoryRequest { ChoiceArguments = arguments.RootElement.Clone() }, TestContext.Current.CancellationToken);

        handler.Method.Should().Be(HttpMethod.Post);
        handler.RequestUri!.ToString().Should().Be("https://registry.example/registry/allocation/v2/settlement-factory");
        handler.RequestBody.Should().Be("""{"choiceArguments":{"settlement":"s1"},"excludeDebugFields":false}""");
    }

    [Fact]
    public async Task GetSettlementFactory_decodes_the_factory_id_and_the_disclosed_contracts()
    {
        var (api, _) = RegistryClientFixture.Resolve<IAllocationV2Api>(HttpStatusCode.OK, FactoryResponse);

        var factory = await api.GetSettlementFactory(new GetFactoryRequest { ChoiceArguments = new object() }, TestContext.Current.CancellationToken);

        factory.FactoryId.Should().Be("factory-1");
        var disclosed = factory.ChoiceContext.DisclosedContracts.Should().ContainSingle().Subject;
        disclosed.TemplateId.Should().Be("pkg:Mod:Ent");
        disclosed.ContractId.Should().Be("00ab");
        disclosed.CreatedEventBlob.Should().Be("AQID");
        disclosed.SynchronizerId.Should().Be("sync::1220");
    }

    [Theory]
    [InlineData("withdraw")]
    [InlineData("cancel")]
    public async Task GetAllocationChoiceContext_posts_to_the_choice_route_of_the_allocation(string choice)
    {
        var (api, handler) = RegistryClientFixture.Resolve<IAllocationV2Api>(HttpStatusCode.OK, ChoiceContextResponse);
        var request = new GetChoiceContextRequest();

        var context = choice == "withdraw"
            ? await api.GetAllocationWithdrawContext("alloc-1", request, TestContext.Current.CancellationToken)
            : await api.GetAllocationCancelContext("alloc-1", request, TestContext.Current.CancellationToken);

        handler.Method.Should().Be(HttpMethod.Post);
        handler.RequestUri!.ToString().Should().Be($"https://registry.example/registry/allocations/v2/alloc-1/choice-contexts/{choice}");
        handler.RequestBody.Should().Be("""{"excludeDebugFields":false}""");
        context.DisclosedContracts.Should().BeEmpty();
        context.ChoiceContextData.Should().BeOfType<JsonElement>()
            .Which.GetProperty("values").GetProperty("k").GetProperty("tag").GetString().Should().Be("AV_Bool");
    }
}
