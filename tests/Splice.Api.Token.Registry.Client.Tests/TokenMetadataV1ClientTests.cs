// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Splice.Api.Token.Registry.Client.TokenMetadataV1;
using Xunit;

namespace Splice.Api.Token.Registry.Client.Tests;

public class TokenMetadataV1ClientTests
{
    private const string InstrumentJson =
        """
        {"id":"Amulet","name":"Amulet","symbol":"AMT","totalSupply":"1000.5","decimals":10,"supportedApis":{"splice-api-token-metadata-v1":1}}
        """;

    [Fact]
    public async Task GetRegistryInfo_gets_the_info_route_and_decodes_the_admin_and_supported_apis()
    {
        var (api, handler) = RegistryClientFixture.Resolve<ITokenMetadataV1Api>(
            HttpStatusCode.OK,
            """{"adminId":"DSO::1220","supportedApis":{"splice-api-token-metadata-v1":1,"splice-api-token-allocation-v2":2}}""");

        var info = await api.GetRegistryInfo(TestContext.Current.CancellationToken);

        handler.Method.Should().Be(HttpMethod.Get);
        handler.RequestUri!.ToString().Should().Be("https://registry.example/registry/metadata/v1/info");
        info.AdminId.Should().Be("DSO::1220");
        info.SupportedApis.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["splice-api-token-metadata-v1"] = 1,
            ["splice-api-token-allocation-v2"] = 2,
        });
    }

    [Fact]
    public async Task ListInstruments_gets_the_instruments_route_with_the_default_page_size()
    {
        var (api, handler) = RegistryClientFixture.Resolve<ITokenMetadataV1Api>(
            HttpStatusCode.OK,
            $$"""{"instruments":[{{InstrumentJson}}],"nextPageToken":"next-1"}""");

        var page = await api.ListInstruments(cancellationToken: TestContext.Current.CancellationToken);

        handler.Method.Should().Be(HttpMethod.Get);
        handler.RequestUri!.ToString().Should().Be("https://registry.example/registry/metadata/v1/instruments?pageSize=25");
        page.NextPageToken.Should().Be("next-1");
        page.Instruments.Should().ContainSingle().Which.Symbol.Should().Be("AMT");
    }

    [Fact]
    public async Task GetInstrument_gets_the_route_of_the_instrument_and_decodes_it()
    {
        var (api, handler) = RegistryClientFixture.Resolve<ITokenMetadataV1Api>(HttpStatusCode.OK, InstrumentJson);

        var instrument = await api.GetInstrument("Amulet", TestContext.Current.CancellationToken);

        handler.RequestUri!.ToString().Should().Be("https://registry.example/registry/metadata/v1/instruments/Amulet");
        instrument.Id.Should().Be("Amulet");
        instrument.TotalSupply.Should().Be("1000.5");
        instrument.Decimals.Should().Be(10);
    }

    [Fact]
    public async Task GetInstrument_surfaces_an_unknown_instrument_as_an_api_exception_carrying_the_status()
    {
        var (api, _) = RegistryClientFixture.Resolve<ITokenMetadataV1Api>(HttpStatusCode.NotFound, """{"error":"no such instrument"}""");

        var failure = await api.Invoking(a => a.GetInstrument("Nope", TestContext.Current.CancellationToken))
            .Should().ThrowAsync<Refit.ApiException>();

        failure.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
