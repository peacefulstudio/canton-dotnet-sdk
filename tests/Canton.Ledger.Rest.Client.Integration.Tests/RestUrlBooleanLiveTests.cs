// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Grpc.Client.Integration.Tests;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// LocalNet conformance coverage for how the participant reads a boolean query parameter. Refit's
/// default formatter spells a boolean <c>True</c> or <c>False</c>, where <c>JsonUrlParameterFormatter</c>
/// spells it <c>true</c> or <c>false</c>; the formatter's boolean spelling is needed only while the
/// participant refuses the capitalised spelling.
/// <para>
/// The same DAR is uploaded under three spellings of <c>vetAllPackages</c>. The typed client's
/// spelling must be accepted. A spelling that is not a boolean at all must be refused, which proves the
/// participant parses the parameter rather than ignoring it, so what the third leg shows is not vacuous.
/// The capitalised spelling is the one Refit's default would put on the wire: it is accepted today,
/// which means the participant already reads it and the formatter has nothing left to compensate for.
/// That leg is the retirement instrument, and it would start failing only if the participant began
/// refusing the capitalised spelling.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public class RestUrlBooleanLiveTests
{
    private const string UploadDarPath = "/v2/dars";

    [Fact]
    [Trait("Retires", nameof(JsonUrlParameterFormatter))]
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
            "the spelling Refit's default formatter produces must be accepted for the formatter to have "
            + "nothing left to compensate for");
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
