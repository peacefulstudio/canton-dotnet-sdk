// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class TruncatedActiveContractsPageHandlerTests
{
    private const string ActiveContractsPage =
        """{"activeContracts":[{"a":1},{"b":2},{"c":3}],"nextPageToken":""}""";

    [Fact]
    public async Task Keeps_the_rows_before_the_cut_whole_and_ends_inside_the_next_row()
    {
        var body = await SendAsync(new TruncatedActiveContractsPageHandler(rowsKept: 2), "/v2/state/active-contracts-page", ActiveContractsPage);

        Assert.Equal("""{"activeContracts":[{"a":1},{"b":2},{"c""", body);
    }

    [Fact]
    public async Task Cuts_inside_the_first_row_when_no_row_is_kept()
    {
        var body = await SendAsync(new TruncatedActiveContractsPageHandler(rowsKept: 0), "/v2/state/active-contracts-page", ActiveContractsPage);

        Assert.Equal("""{"activeContracts":[{"a""", body);
    }

    [Fact]
    public async Task Cuts_the_body_in_half_when_the_page_holds_no_row_to_cut_inside()
    {
        var body = await SendAsync(new TruncatedActiveContractsPageHandler(rowsKept: 0), "/v2/state/active-contracts-page", """{"activeContracts":[]}""");

        Assert.Equal("""{"activeCon""", body);
    }

    [Fact]
    public async Task Cuts_the_body_in_half_when_fewer_rows_exist_than_are_kept()
    {
        var body = await SendAsync(new TruncatedActiveContractsPageHandler(rowsKept: 3), "/v2/state/active-contracts-page", ActiveContractsPage);

        Assert.Equal(ActiveContractsPage[..32], body);
    }

    [Fact]
    public async Task Leaves_a_body_that_is_not_valid_json()
    {
        var body = await SendAsync(new TruncatedActiveContractsPageHandler(rowsKept: 1), "/v2/state/active-contracts-page", ActiveContractsPage);

        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(body));
    }

    [Fact]
    public async Task Leaves_the_response_of_any_other_path_whole()
    {
        var body = await SendAsync(new TruncatedActiveContractsPageHandler(rowsKept: 0), "/v2/state/ledger-end", """{"offset":5}""");

        Assert.Equal("""{"offset":5}""", body);
    }

    [Fact]
    public async Task Keeps_the_content_type_of_the_response()
    {
        var response = await SendResponseAsync(new TruncatedActiveContractsPageHandler(rowsKept: 0), "/v2/state/active-contracts-page", ActiveContractsPage);

        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
    }

    private static async Task<string> SendAsync(TruncatedActiveContractsPageHandler handler, string path, string responseBody)
    {
        using var response = await SendResponseAsync(handler, path, responseBody);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> SendResponseAsync(TruncatedActiveContractsPageHandler handler, string path, string responseBody)
    {
        handler.InnerHandler = new FixedResponseHandler(responseBody);
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://localhost:7575") };
        return await client.PostAsync(path, new StringContent("{}"), TestContext.Current.CancellationToken);
    }

    private sealed class FixedResponseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
            });
    }
}
