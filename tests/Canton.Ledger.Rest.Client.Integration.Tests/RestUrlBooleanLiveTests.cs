// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Grpc.Client.Integration.Tests;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// LocalNet conformance coverage for how the participant reads a boolean query parameter. The typed
/// client sends a boolean as Refit's <c>DefaultUrlParameterFormatter</c> spells it, <c>True</c> or
/// <c>False</c>, where JSON and the specification spell it <c>true</c> or <c>false</c>; that is correct
/// only while the participant reads the parameter case-insensitively.
/// <para>
/// The same DAR is uploaded under three spellings of <c>vetAllPackages</c>. The typed client's
/// spelling must be accepted. A spelling that is not a boolean at all must be refused, which proves the
/// participant parses the parameter rather than ignoring it, so what the third leg shows is not vacuous.
/// The capitalised spelling is sent literally and must be accepted: the day the participant refuses it,
/// the typed client needs a URL parameter formatter that spells booleans lowercase again.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public class RestUrlBooleanLiveTests
{
    private const string UploadDarPath = "/v2/dars";

    [Fact]
    public async Task A_boolean_query_parameter_is_parsed_by_the_participant_and_read_in_either_capitalisation()
    {
        await using var lane = await RestLiveLane.OpenAsync(TestContext.Current.CancellationToken);
        using var wireClient = lane.CreateWireLevelClient();

        await UploadThroughTypedClientAsync(lane, TestContext.Current.CancellationToken);
        var notABoolean = await UploadAsync(wireClient, "banana", TestContext.Current.CancellationToken);
        var capitalised = await UploadAsync(wireClient, "True", TestContext.Current.CancellationToken);

        notABoolean.Should().Be(
            HttpStatusCode.BadRequest,
            "a value that is not a boolean must be refused, or the capitalised leg below would show only "
            + "that the participant ignores the parameter");
        capitalised.Should().Be(
            HttpStatusCode.OK,
            "the typed client sends Refit's default capitalised spelling, so the participant must read it");
    }

    private static async Task UploadThroughTypedClientAsync(RestLiveLane lane, CancellationToken cancellationToken)
    {
        await using var dar = File.OpenRead(RichTypesDar.Path);
        await lane.Api<Raw.IDarApi>().UploadDar(dar, vetAllPackages: true, cancellationToken: cancellationToken);
    }

    private static async Task<HttpStatusCode> UploadAsync(
        HttpClient wireClient, string vetAllPackages, CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(await File.ReadAllBytesAsync(RichTypesDar.Path, cancellationToken));
        content.Headers.ContentType = new("application/octet-stream");
        using var response = await wireClient.PostAsync(
            new Uri($"{UploadDarPath}?vetAllPackages={vetAllPackages}", UriKind.Relative), content, cancellationToken);
        return response.StatusCode;
    }
}
