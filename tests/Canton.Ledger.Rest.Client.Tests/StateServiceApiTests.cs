// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Rest.Client.Raw;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class StateServiceApiTests
{
    private static (IStateServiceApi Api, RecordingHttpHandler Transport) BuildApi()
    {
        var (api, transport) = RestApiFactory.Build<IStateServiceApi>();
        transport.WithResponse(HttpStatusCode.OK, """{"offset":"42"}""");
        return (api, transport);
    }

    [Fact]
    public async Task GetLedgerEnd_sends_each_synchronizer_filter_under_the_query_name_the_participant_reads()
    {
        var (api, transport) = BuildApi();

        await api.GetLedgerEnd(["global-domain::1220aa", "app-domain::1220bb"], TestContext.Current.CancellationToken);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be(
            "/v2/state/ledger-end?synchronizer_id=global-domain%3A%3A1220aa&synchronizer_id=app-domain%3A%3A1220bb");
    }

    [Fact]
    public async Task GetLedgerEnd_without_a_synchronizer_filter_sends_no_query()
    {
        var (api, transport) = BuildApi();

        await api.GetLedgerEnd(cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/state/ledger-end");
    }
}
