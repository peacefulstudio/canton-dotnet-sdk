// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Telemetry;
using Canton.Ledger.Testing.Helpers;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestAdminClientUserIdPrivacyTests : IDisposable
{
    private const string AcceptedBody =
        """{"user": {"id": "carol-user"}, "partyDetails": {"party": "carol::1220", "isLocal": true}, "rights": []}""";

    private const string RejectedBody =
        """{"code": "INVALID_ARGUMENT", "cause": "party carol::1220 is unknown", "grpcCodeValue": 3, "errorCategory": 8, "context": {}}""";

    private readonly RestLedgerClientOptions _options = new() { HttpAddress = "http://localhost:7575" };
    private readonly CapturingLoggerFactory _logs = new();
    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories) factory.Dispose();
        _logs.Dispose();
    }

    private RestAdminClient ClientAnswering(HttpStatusCode status, string body)
    {
        var factory = new StubHttpClientFactory(new RecordingHttpHandler().WithResponse(status, body));
        _factories.Add(factory);
        return new RestAdminClient(factory, _logs.CreateLogger<RestAdminClient>(), Options.Create(_options));
    }

    private IEnumerable<string> InformationMessages() =>
        _logs.Records.Where(r => r.Level == LogLevel.Information).Select(r => r.Message);

    private IEnumerable<string> DebugMessages() =>
        _logs.Records.Where(r => r.Level == LogLevel.Debug).Select(r => r.Message);

    [Fact]
    public async Task AllocatePartyAsync_logs_the_attempt_without_the_hint_by_default()
    {
        await ClientAnswering(HttpStatusCode.OK, AcceptedBody)
            .AllocatePartyAsync("carol", cancellationToken: TestContext.Current.CancellationToken);

        DebugMessages().Should().Equal("Allocating party");
    }

    [Fact]
    public async Task AllocatePartyAsync_logs_the_hint_of_the_attempt_when_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;

        await ClientAnswering(HttpStatusCode.OK, AcceptedBody)
            .AllocatePartyAsync("carol", cancellationToken: TestContext.Current.CancellationToken);

        DebugMessages().Should().Equal("Allocating party with hint: carol");
    }

    [Fact]
    public async Task CreateUserAsync_logs_the_attempt_without_the_user_id_by_default()
    {
        await ClientAnswering(HttpStatusCode.OK, AcceptedBody)
            .CreateUserAsync("carol-user", primaryParty: null, cancellationToken: TestContext.Current.CancellationToken);

        DebugMessages().Should().Equal("Creating user");
    }

    [Fact]
    public async Task CreateUserAsync_logs_the_user_id_of_the_attempt_when_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;

        await ClientAnswering(HttpStatusCode.OK, AcceptedBody)
            .CreateUserAsync("carol-user", primaryParty: null, cancellationToken: TestContext.Current.CancellationToken);

        DebugMessages().Should().Equal("Creating user: carol-user");
    }

    public static TheoryData<string> OperationsTaggingTheUserId => new()
    {
        "CreateUserAsync",
        "GetUserAsync",
        "GrantUserRightsAsync",
        "RevokeUserRightsAsync",
        "ListUserRightsAsync",
        "UpdateUserAsync",
        "DeleteUserAsync",
        "UpdateUserIdentityProviderIdAsync",
    };

    private static Task Invoke(IAdminClient client, string operation, CancellationToken token) => operation switch
    {
        "CreateUserAsync" => client.CreateUserAsync("carol-user", new Party("carol::1220"), cancellationToken: token),
        "GetUserAsync" => client.GetUserAsync("carol-user", token),
        "GrantUserRightsAsync" => client.GrantUserRightsAsync("carol-user", [new UserRight.ParticipantAdmin()], token),
        "RevokeUserRightsAsync" => client.RevokeUserRightsAsync("carol-user", [new UserRight.ParticipantAdmin()], token),
        "ListUserRightsAsync" => client.ListUserRightsAsync("carol-user", token),
        "UpdateUserAsync" => client.UpdateUserAsync("carol-user", new UserUpdate { IsDeactivated = true }, cancellationToken: token),
        "DeleteUserAsync" => client.DeleteUserAsync("carol-user", cancellationToken: token),
        "UpdateUserIdentityProviderIdAsync" => client.UpdateUserIdentityProviderIdAsync("carol-user", null, "idp-2", token),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
    };

    [Theory]
    [MemberData(nameof(OperationsTaggingTheUserId))]
    public async Task User_operation_leaves_the_user_id_off_the_span_by_default(string operation)
    {
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.RestAdminClient);

        await Invoke(ClientAnswering(HttpStatusCode.OK, AcceptedBody), operation, TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.OperationName.Should().Be($"RestAdminClient.{operation}");
        activity.GetTagItem(LedgerActivityTagNames.CantonUserId).Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(OperationsTaggingTheUserId))]
    public async Task User_operation_tags_the_user_id_when_opted_in(string operation)
    {
        _options.EmitPartyAndContractSpanTags = true;
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.RestAdminClient);

        await Invoke(ClientAnswering(HttpStatusCode.OK, AcceptedBody), operation, TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.OperationName.Should().Be($"RestAdminClient.{operation}");
        activity.GetTagItem(LedgerActivityTagNames.CantonUserId).Should().Be("carol-user");
    }

    [Fact]
    public async Task AllocatePartyAsync_logs_the_allocation_without_the_party_id_by_default()
    {
        await ClientAnswering(HttpStatusCode.OK, AcceptedBody)
            .AllocatePartyAsync("carol", cancellationToken: TestContext.Current.CancellationToken);

        InformationMessages().Should().Equal("Party allocated");
    }

    [Fact]
    public async Task AllocatePartyAsync_logs_the_party_id_when_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;

        await ClientAnswering(HttpStatusCode.OK, AcceptedBody)
            .AllocatePartyAsync("carol", cancellationToken: TestContext.Current.CancellationToken);

        InformationMessages().Should().Equal("Party allocated: carol::1220");
    }

    [Fact]
    public async Task CreateUserAsync_logs_the_creation_without_the_user_id_by_default()
    {
        await ClientAnswering(HttpStatusCode.OK, AcceptedBody)
            .CreateUserAsync("carol-user", primaryParty: null, cancellationToken: TestContext.Current.CancellationToken);

        InformationMessages().Should().Equal("User created");
    }

    [Fact]
    public async Task CreateUserAsync_logs_the_user_id_when_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;

        await ClientAnswering(HttpStatusCode.OK, AcceptedBody)
            .CreateUserAsync("carol-user", primaryParty: null, cancellationToken: TestContext.Current.CancellationToken);

        InformationMessages().Should().Equal("User created: carol-user");
    }

    [Fact]
    public async Task GrantUserRightsAsync_logs_the_grant_without_the_user_id_by_default()
    {
        await ClientAnswering(HttpStatusCode.OK, AcceptedBody)
            .GrantUserRightsAsync("carol-user", [new UserRight.ParticipantAdmin()], TestContext.Current.CancellationToken);

        InformationMessages().Should().Equal("Rights granted");
    }

    [Fact]
    public async Task GrantUserRightsAsync_logs_the_user_id_when_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;

        await ClientAnswering(HttpStatusCode.OK, AcceptedBody)
            .GrantUserRightsAsync("carol-user", [new UserRight.ParticipantAdmin()], TestContext.Current.CancellationToken);

        InformationMessages().Should().Equal("Rights granted to user carol-user");
    }

    [Fact]
    public async Task RevokeUserRightsAsync_logs_the_revocation_without_the_user_id_by_default()
    {
        await ClientAnswering(HttpStatusCode.OK, AcceptedBody)
            .RevokeUserRightsAsync("carol-user", [new UserRight.ParticipantAdmin()], TestContext.Current.CancellationToken);

        InformationMessages().Should().Equal("Rights revoked");
    }

    [Fact]
    public async Task RevokeUserRightsAsync_logs_the_user_id_when_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;

        await ClientAnswering(HttpStatusCode.OK, AcceptedBody)
            .RevokeUserRightsAsync("carol-user", [new UserRight.ParticipantAdmin()], TestContext.Current.CancellationToken);

        InformationMessages().Should().Equal("Rights revoked from user carol-user");
    }

    [Fact]
    public async Task A_failed_call_carries_the_http_status_in_its_span_status_by_default()
    {
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.RestAdminClient);
        var client = ClientAnswering(HttpStatusCode.BadRequest, RejectedBody);

        var act = () => client.GrantUserRightsAsync("carol-user", [new UserRight.ParticipantAdmin()], TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.Status.Should().Be(System.Diagnostics.ActivityStatusCode.Error);
        activity.StatusDescription.Should().Be("400");
        activity.GetTagItem(RestActivityHandler.ErrorType).Should().Be("Daml.Ledger.Abstractions.LedgerOperationException");
    }

    [Fact]
    public async Task A_failed_call_carries_the_server_message_in_its_span_status_when_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.RestAdminClient);
        var client = ClientAnswering(HttpStatusCode.BadRequest, RejectedBody);

        var act = () => client.GrantUserRightsAsync("carol-user", [new UserRight.ParticipantAdmin()], TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.Status.Should().Be(System.Diagnostics.ActivityStatusCode.Error);
        activity.StatusDescription.Should().Be("party carol::1220 is unknown");
    }

    private const string StuckPageBody = """{"partyDetails": [], "nextPageToken": "party::alice-page-token"}""";

    [Fact]
    public async Task A_repeated_page_token_stays_off_the_span_status_by_default()
    {
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.RestAdminClient);
        var client = ClientAnswering(HttpStatusCode.OK, StuckPageBody);

        var act = () => client.ListKnownPartiesAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.StatusDescription.Should().Be("InvalidOperationException");
        activity.GetTagItem(RestActivityHandler.ErrorType).Should().Be("System.InvalidOperationException");
    }

    [Fact]
    public async Task A_repeated_page_token_reaches_the_span_status_when_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.RestAdminClient);
        var client = ClientAnswering(HttpStatusCode.OK, StuckPageBody);

        var act = () => client.ListKnownPartiesAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        capture.Activities.Should().ContainSingle().Subject.StatusDescription.Should().Be(
            "ListKnownParties pagination is not progressing: the server returned the page token 'party::alice-page-token' that was already used earlier in this call.");
    }

    [Fact]
    public async Task An_undecodable_body_is_reported_by_its_status_kind_in_the_span_status_by_default()
    {
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.RestAdminClient);
        var client = ClientAnswering(HttpStatusCode.OK, """{"partyDetails": "party::alice is not an object"}""");

        var act = () => client.AllocatePartyAsync("alice", cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
        capture.Activities.Should().ContainSingle().Subject.StatusDescription.Should().Be("UndecodableBody");
    }

    [Fact]
    public async Task An_undecodable_body_reaches_the_span_status_when_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.RestAdminClient);
        var client = ClientAnswering(HttpStatusCode.OK, """{"partyDetails": "party::alice is not an object"}""");

        var act = () => client.AllocatePartyAsync("alice", cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        capture.Activities.Should().ContainSingle().Subject.StatusDescription.Should().Be(thrown.Message);
    }

    private async Task<Exception?> ExceptionLoggedForAnUndecodableBody()
    {
        var client = ClientAnswering(HttpStatusCode.OK, """{"partyDetails": "party::alice is not an object"}""");

        var act = () => client.AllocatePartyAsync("alice", cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
        return _logs.Records
            .Single(r => r.Level == LogLevel.Error && r.Message.StartsWith("The participant answered successfully", StringComparison.Ordinal))
            .Exception;
    }

    [Fact]
    public async Task An_undecodable_body_logs_only_the_exception_type_by_default()
    {
        var exception = await ExceptionLoggedForAnUndecodableBody();

        exception!.Message.Should().Be("System.Text.Json.JsonException");
    }

    [Fact]
    public async Task An_undecodable_body_logs_the_original_exception_when_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;

        var exception = await ExceptionLoggedForAnUndecodableBody();

        exception.Should().BeOfType<System.Text.Json.JsonException>();
    }
}
