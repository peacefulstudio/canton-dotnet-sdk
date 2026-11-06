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

    [Fact]
    public async Task ConvertRecordTimeToOffset_sends_its_parameters_under_the_query_names_the_participant_reads()
    {
        var (api, transport) = BuildApi();

        await api.ConvertRecordTimeToOffset(
            new DateTimeOffset(2026, 11, 4, 13, 5, 7, TimeSpan.Zero),
            "global-domain::1220aa",
            TestContext.Current.CancellationToken);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.RequestUri!.AbsolutePath.Should().Be("/v2/state/convert-record-time-to-offset");
        QueryNamesOf(transport.LastRequest.RequestUri).Should().Equal("record-time", "synchronizer-id");
    }

    [Theory]
    [InlineData(0, "2026-11-04T13:05:07Z")]
    [InlineData(1_234_567, "2026-11-04T13:05:07.1234567Z")]
    [InlineData(5_000_000, "2026-11-04T13:05:07.5Z")]
    public async Task ConvertRecordTimeToOffset_spells_the_record_time_as_an_RFC_3339_UTC_timestamp(
        long ticksPastTheSecond, string expectedRecordTime)
    {
        var (api, transport) = BuildApi();

        await api.ConvertRecordTimeToOffset(
            new DateTimeOffset(2026, 11, 4, 13, 5, 7, TimeSpan.Zero).AddTicks(ticksPastTheSecond),
            "global-domain::1220aa",
            TestContext.Current.CancellationToken);

        RecordTimeOf(transport.LastRequest!.RequestUri!).Should().Be(expectedRecordTime);
    }

    [Fact]
    public async Task ConvertRecordTimeToOffset_spells_a_record_time_with_an_offset_as_the_same_instant_in_UTC()
    {
        var (api, transport) = BuildApi();

        await api.ConvertRecordTimeToOffset(
            new DateTimeOffset(2026, 11, 4, 10, 5, 7, TimeSpan.FromHours(-3)),
            "global-domain::1220aa",
            TestContext.Current.CancellationToken);

        RecordTimeOf(transport.LastRequest!.RequestUri!).Should().Be("2026-11-04T13:05:07Z");
    }

    private static IEnumerable<string> QueryNamesOf(Uri requestUri) =>
        QueryPairsOf(requestUri).Select(pair => pair.Name);

    private static string RecordTimeOf(Uri requestUri) =>
        QueryPairsOf(requestUri).Single(pair => pair.Name == "record-time").Value;

    private static IEnumerable<(string Name, string Value)> QueryPairsOf(Uri requestUri) =>
        requestUri.Query.TrimStart('?')
            .Split('&')
            .Select(pair => pair.Split('=', 2))
            .Select(parts => (Uri.UnescapeDataString(parts[0]), Uri.UnescapeDataString(parts[1])));
}
