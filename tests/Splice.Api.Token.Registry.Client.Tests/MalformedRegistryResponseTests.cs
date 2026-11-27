// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Splice.Api.Token.Registry.Client.AllocationV2;
using Splice.Api.Token.Registry.Client.TokenMetadataV1;
using Xunit;

namespace Splice.Api.Token.Registry.Client.Tests;

public class MalformedRegistryResponseTests
{
    public static TheoryData<string> MalformedBodies =>
    [
        """{"adminId":"DSO::1220","supportedApis":""",
        "<html><body>gateway error</body></html>",
        """["DSO::1220"]""",
        """{"adminId":"DSO::1220","supportedApis":{"splice-api-token-metadata-v1":"one"}}""",
    ];

    [Theory]
    [MemberData(nameof(MalformedBodies))]
    public async Task GetRegistryInfo_surfaces_a_malformed_success_body_as_an_api_exception(string body)
    {
        var (api, _) = RegistryClientFixture.Resolve<ITokenMetadataV1Api>(HttpStatusCode.OK, body);

        var failure = await api.Invoking(a => a.GetRegistryInfo(TestContext.Current.CancellationToken))
            .Should().ThrowAsync<Refit.ApiException>();

        failure.Which.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetSettlementFactory_surfaces_a_disclosed_contract_of_the_wrong_shape_as_an_api_exception()
    {
        var (api, _) = RegistryClientFixture.Resolve<IAllocationV2Api>(
            HttpStatusCode.OK,
            """{"factoryId":"factory-1","choiceContext":{"choiceContextData":{"values":{}},"disclosedContracts":"00ab"}}""");

        var failure = await api.Invoking(a => a.GetSettlementFactory(new GetFactoryRequest(), TestContext.Current.CancellationToken))
            .Should().ThrowAsync<Refit.ApiException>();

        failure.Which.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
