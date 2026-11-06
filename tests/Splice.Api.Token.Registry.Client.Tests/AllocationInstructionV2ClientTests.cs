// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Splice.Api.Token.Registry.Client.AllocationInstructionV2;
using Xunit;

namespace Splice.Api.Token.Registry.Client.Tests;

public class AllocationInstructionV2ClientTests
{
    [Fact]
    public async Task GetAllocationFactory_posts_the_choice_arguments_to_the_allocation_factory_route()
    {
        var (api, handler) = RegistryClientFixture.Resolve<IAllocationInstructionV2Api>(
            HttpStatusCode.OK,
            """{"factoryId":"factory-2","choiceContext":{"choiceContextData":{"values":{}},"disclosedContracts":[]}}""");

        var factory = await api.GetAllocationFactory(
            new GetFactoryRequest { ChoiceArguments = new object(), ExcludeDebugFields = true },
            TestContext.Current.CancellationToken);

        handler.Method.Should().Be(HttpMethod.Post);
        handler.RequestUri!.ToString().Should().Be("https://registry.example/registry/allocation-instruction/v2/allocation-factory");
        handler.RequestBody.Should().Be("""{"choiceArguments":{},"excludeDebugFields":true}""");
        factory.FactoryId.Should().Be("factory-2");
    }
}
