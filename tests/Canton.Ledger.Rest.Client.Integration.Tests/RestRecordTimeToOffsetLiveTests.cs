// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Grpc.Client.Integration.Tests;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Peaceful.Canton.Localnet.Testing;
using Refit;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Integration.Tests;

/// <summary>
/// LocalNet conformance coverage for <c>GET /v2/state/convert-record-time-to-offset</c>. The participant
/// reads its parameters as <c>record-time</c> and <c>synchronizer-id</c>, where the proto-derived
/// specification would name them <c>recordTime</c> and <c>synchronizerId</c>, and it reads the record
/// time as an RFC 3339 timestamp, where Refit's default formatter spells a <see cref="DateTimeOffset"/>
/// as <c>11/04/2026 13:05:07 +00:00</c>.
/// <para>
/// The record time and synchronizer of a transaction the test just committed are converted three
/// ways. The typed client's request must be answered with an offset no later than the ledger end. The
/// same record time under the proto-derived names must be refused, which proves the participant
/// requires <c>record-time</c> rather than ignoring an unknown name. The same record time spelled as
/// Refit's default would spell it must be refused too: that leg is the retirement instrument for the
/// timestamp spelling of <c>JsonUrlParameterFormatter</c>, and it starts failing the day the participant
/// reads that spelling.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public class RestRecordTimeToOffsetLiveTests
{
    private const string ConvertRecordTimeToOffsetPath = "/v2/state/convert-record-time-to-offset";

    [Fact]
    [Trait("Retires", nameof(JsonUrlParameterFormatter))]
    public async Task A_record_time_converts_only_when_sent_as_an_RFC_3339_record_time_query_parameter()
    {
        await using var lane = await RestLiveLane.OpenAsync(TestContext.Current.CancellationToken);
        var owner = await NewOwnerAsync(lane, TestContext.Current.CancellationToken);
        using var wireClient = lane.CreateWireLevelClient();

        var committed = await CreateMarkerAsync(lane, owner, TestContext.Current.CancellationToken);
        var recordTime = committed.RecordTime
            ?? throw new InvalidOperationException($"transaction {committed.UpdateId} was served without a record time");

        var converted = await lane.Api<Raw.IStateServiceApi>().ConvertRecordTimeToOffset(
            recordTime, committed.SynchronizerId, TestContext.Current.CancellationToken);
        var ledgerEnd = await lane.Api<Raw.IStateServiceApi>().GetLedgerEnd(
            cancellationToken: TestContext.Current.CancellationToken);

        var protoDerivedNames = await ConvertAsync(
            wireClient,
            $"recordTime={Escaped(RfcSpelling(recordTime))}&synchronizerId={Escaped(committed.SynchronizerId)}",
            TestContext.Current.CancellationToken);
        var refitDefaultSpelling = await ConvertAsync(
            wireClient,
            $"record-time={Escaped(RefitDefaultSpelling(recordTime))}&synchronizer-id={Escaped(committed.SynchronizerId)}",
            TestContext.Current.CancellationToken);

        long.Parse(converted.Offset, CultureInfo.InvariantCulture).Should().BeInRange(
            1,
            long.Parse(ledgerEnd.Offset, CultureInfo.InvariantCulture),
            "a record time the synchronizer has delivered converts to an absolute offset the participant has "
            + "already reached");
        protoDerivedNames.Should().Be(
            HttpStatusCode.BadRequest,
            "the participant requires record-time, so a request naming it recordTime must be refused, or the "
            + "typed client's success above would not show which name the participant read");
        refitDefaultSpelling.Should().Be(
            HttpStatusCode.BadRequest,
            "the spelling Refit's default formatter produces must be refused for the formatter's timestamp "
            + "spelling to have something left to compensate for");
    }

    private static async Task<Party> NewOwnerAsync(RestLiveLane lane, CancellationToken cancellationToken)
    {
        var darOutcome = await lane.Fixture.UploadDarAsync(RichTypesDar.Path, cancellationToken);
        Assert.True(
            darOutcome is DarUploadOutcome.Uploaded or DarUploadOutcome.AlreadyKnown,
            $"Unexpected DAR upload outcome: {darOutcome}");

        var party = await lane.Fixture.AllocatePartyAsync(
            "rest-record-time-to-offset", cancellationToken: cancellationToken);
        await lane.GrantActAsAsync(party.PartyId, cancellationToken);
        return new Party(party.PartyId);
    }

    private static async Task<Raw.Transaction> CreateMarkerAsync(
        RestLiveLane lane, Party owner, CancellationToken cancellationToken)
    {
        var reply = await lane.Api<Raw.ICommandServiceApi>().SubmitAndWaitForTransaction(
            new Raw.SubmitAndWaitForTransactionRequest
            {
                Commands = RestCommandBuilder.BuildCommands(
                    CommandsSubmission.Single(CreateCommand.For(new Marker(owner)), owner), userId: null),
                TransactionFormat = RestSubscribeRequestBuilder.BuildTransactionFormat(owner),
            },
            cancellationToken);
        return reply.Transaction;
    }

    private static async Task<HttpStatusCode> ConvertAsync(
        HttpClient wireClient, string query, CancellationToken cancellationToken)
    {
        using var response = await wireClient.GetAsync(
            new Uri($"{ConvertRecordTimeToOffsetPath}?{query}", UriKind.Relative), cancellationToken);
        return response.StatusCode;
    }

    private static string RfcSpelling(DateTimeOffset recordTime) =>
        new JsonUrlParameterFormatter().Format(recordTime, typeof(DateTimeOffset), typeof(DateTimeOffset))!;

    private static string RefitDefaultSpelling(DateTimeOffset recordTime) =>
        new DefaultUrlParameterFormatter().Format(recordTime, typeof(DateTimeOffset), typeof(DateTimeOffset))!;

    private static string Escaped(string value) => Uri.EscapeDataString(value);
}
