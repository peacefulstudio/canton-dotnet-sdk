// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Resilience;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using PartyDetails = Canton.Ledger.Abstractions.PartyDetails;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestAdminClientTests : IDisposable
{
    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    private RestAdminClient RetryingClientWith(RecordingHttpHandler transport)
    {
        var retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 3, Delay = TimeSpan.Zero };
        var retryHandler = new RestRetryHandler(Options.Create(new RestLedgerClientOptions
        {
            HttpAddress = "http://localhost:7575",
            Retry = retry,
        }))
        {
            InnerHandler = transport,
        };
        return ClientWith(retryHandler);
    }

    private RestAdminClient ClientWith(HttpMessageHandler handler)
    {
        var factory = new StubHttpClientFactory(handler);
        _factories.Add(factory);
        return new RestAdminClient(factory);
    }

    [Fact]
    public async Task GetParticipantIdAsync_is_replayed_by_the_opt_in_retry_pipeline()
    {
        var transport = new RecordingHttpHandler()
            .WithTransportException(new HttpRequestException("connection refused"));
        IAdminClient client = RetryingClientWith(transport);

        var act = () => client.GetParticipantIdAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
        transport.Requests.Should().HaveCount(4);
    }

    [Fact]
    public async Task AllocatePartyAsync_is_never_replayed_by_the_opt_in_retry_pipeline()
    {
        var transport = new RecordingHttpHandler()
            .WithTransportException(new HttpRequestException("connection reset after the request was sent"));
        IAdminClient client = RetryingClientWith(transport);

        var act = () => client.AllocatePartyAsync("alice", cancellationToken: TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.Status.Should().Be(new TransportStatus.NoResponse());
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task GetParticipantIdAsync_reads_the_participant_id_from_v2_parties_participant_id()
    {
        var transport = new RecordingHttpHandler()
            .WithResponse(HttpStatusCode.OK, """{"participantId": "participant1::1220abcd"}""");
        IAdminClient client = ClientWith(transport);

        var participantId = await client.GetParticipantIdAsync(TestContext.Current.CancellationToken);

        participantId.Should().Be("participant1::1220abcd");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/parties/participant-id");
    }

    [Fact]
    public async Task AllocatePartyAsync_posts_the_hint_and_synchronizer_to_v2_parties_and_maps_the_allocated_party()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """{"partyDetails": {"party": "alice::1220abcd", "isLocal": true}}""");
        IAdminClient client = ClientWith(transport);

        var details = await client.AllocatePartyAsync(
            "alice", new SynchronizerId("global-domain::1220ef"), TestContext.Current.CancellationToken);

        details.Should().Be(new PartyDetails(new Party("alice::1220abcd"), IsLocal: true));
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/parties");
        transport.LastRequestBody.Should().Be("""{"partyIdHint":"alice","synchronizerId":"global-domain::1220ef"}""");
    }

    [Fact]
    public async Task GetPartiesAsync_reads_each_party_from_its_own_v2_parties_route_and_skips_an_unknown_one()
    {
        var transport = new RecordingHttpHandler()
            .WithResponseForPath(
                "/v2/parties/alice%3A%3A1220ab",
                HttpStatusCode.OK,
                """{"partyDetails": [{"party": "alice::1220ab", "isLocal": true}]}""")
            .WithResponseForPath(
                "/v2/parties/ghost%3A%3A1220ff",
                HttpStatusCode.OK,
                """{"partyDetails": []}""")
            .WithResponseForPath(
                "/v2/parties/bob%3A%3A1220cd",
                HttpStatusCode.OK,
                """{"partyDetails": [{"party": "bob::1220cd", "isLocal": false}]}""");
        IAdminClient client = ClientWith(transport);

        var details = await client.GetPartiesAsync(
            [new Party("alice::1220ab"), new Party("ghost::1220ff"), new Party("bob::1220cd")],
            TestContext.Current.CancellationToken);

        details.Should().Equal(
            new PartyDetails(new Party("alice::1220ab"), IsLocal: true),
            new PartyDetails(new Party("bob::1220cd"), IsLocal: false));
        transport.Requests.Select(request => request.PathAndQuery).Should().Equal(
            "/v2/parties/alice%3A%3A1220ab",
            "/v2/parties/ghost%3A%3A1220ff",
            "/v2/parties/bob%3A%3A1220cd");
    }

    [Fact]
    public async Task ListKnownPartiesAsync_reads_a_page_without_a_partyDetails_field_as_no_parties()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{}");
        IAdminClient client = ClientWith(transport);

        var parties = await client.ListKnownPartiesAsync(TestContext.Current.CancellationToken);

        parties.Should().BeEmpty();
    }

    [Fact]
    public async Task ListKnownPartiesAsync_follows_the_page_token_of_v2_parties_until_it_is_empty()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, """{"partyDetails": [{"party": "alice::1220ab", "isLocal": true}], "nextPageToken": "page-2"}"""),
            (HttpStatusCode.OK, """{"partyDetails": [{"party": "bob::1220cd", "isLocal": false}], "nextPageToken": ""}"""));
        IAdminClient client = ClientWith(transport);

        var parties = await client.ListKnownPartiesAsync(TestContext.Current.CancellationToken);

        parties.Should().Equal(
            new PartyDetails(new Party("alice::1220ab"), IsLocal: true),
            new PartyDetails(new Party("bob::1220cd"), IsLocal: false));
        transport.Requests.Select(request => request.PathAndQuery).Should().Equal(
            "/v2/parties?pageSize=100",
            "/v2/parties?pageSize=100&pageToken=page-2");
    }

    [Fact]
    public async Task ListKnownPartiesAsync_throws_instead_of_looping_when_the_participant_repeats_a_page_token()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, """{"partyDetails": [], "nextPageToken": "stuck"}"""));
        IAdminClient client = ClientWith(transport);

        var act = () => client.ListKnownPartiesAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "ListKnownParties pagination is not progressing: the server returned the page token 'stuck' that was already used earlier in this call.");
        transport.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task AllocatePartyAsync_leaves_the_synchronizer_off_the_request_when_none_is_given()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """{"partyDetails": {"party": "alice::1220abcd", "isLocal": false}}""");
        IAdminClient client = ClientWith(transport);

        var details = await client.AllocatePartyAsync("", cancellationToken: TestContext.Current.CancellationToken);

        details.Should().Be(new PartyDetails(new Party("alice::1220abcd"), IsLocal: false));
        transport.LastRequestBody.Should().Be("""{"partyIdHint":""}""");
    }

    [Fact]
    public async Task CreateUserAsync_posts_the_user_and_its_rights_to_v2_users_and_maps_the_created_user()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """{"user": {"id": "carol", "primaryParty": "alice::1220ab", "isDeactivated": false}}""");
        IAdminClient client = ClientWith(transport);

        var user = await client.CreateUserAsync(
            "carol",
            new Party("alice::1220ab"),
            [new UserRight.ActAs(new Party("alice::1220ab")), new UserRight.ParticipantAdmin()],
            TestContext.Current.CancellationToken);

        user.Should().Be(new UserDetails("carol", new Party("alice::1220ab")));
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/users");
        transport.LastRequestBody.Should().Be(
            """{"user":{"id":"carol","primaryParty":"alice::1220ab"},"rights":[{"kind":{"CanActAs":{"value":{"party":"alice::1220ab"}}}},{"kind":{"ParticipantAdmin":{"value":{}}}}]}""");
    }

    [Fact]
    public async Task CreateUserAsync_sends_no_primary_party_and_no_rights_when_none_are_given()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """{"user": {"id": "carol", "primaryParty": ""}}""");
        IAdminClient client = ClientWith(transport);

        var user = await client.CreateUserAsync("carol", primaryParty: null, cancellationToken: TestContext.Current.CancellationToken);

        user.Should().Be(new UserDetails("carol", PrimaryParty: null));
        transport.LastRequestBody.Should().Be("""{"user":{"id":"carol"},"rights":[]}""");
    }

    [Fact]
    public async Task CreateUserAsync_is_never_replayed_by_the_opt_in_retry_pipeline()
    {
        var transport = new RecordingHttpHandler()
            .WithTransportException(new HttpRequestException("connection reset after the request was sent"));
        IAdminClient client = RetryingClientWith(transport);

        var act = () => client.CreateUserAsync("carol", primaryParty: null, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
        transport.Requests.Should().ContainSingle();
    }

    private const string UserNotFoundBody =
        """{"code": "USER_NOT_FOUND", "cause": "getting user failed for unknown user \"ghost\"", "grpcCodeValue": 5, "errorCategory": 11, "context": {}}""";

    [Fact]
    public async Task GetUserAsync_reads_the_user_from_v2_users_by_its_escaped_id()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """{"user": {"id": "carol@example", "primaryParty": "alice::1220ab"}}""");
        IAdminClient client = ClientWith(transport);

        var user = await client.GetUserAsync("carol@example", TestContext.Current.CancellationToken);

        user.Should().Be(new UserDetails("carol@example", new Party("alice::1220ab")));
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/users/carol%40example");
    }

    [Fact]
    public async Task GetUserAsync_returns_null_when_the_participant_answers_404()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.NotFound, UserNotFoundBody);
        IAdminClient client = ClientWith(transport);

        var user = await client.GetUserAsync("ghost", TestContext.Current.CancellationToken);

        user.Should().BeNull();
    }

    [Fact]
    public async Task GetUserAsync_reads_the_authenticated_user_when_the_id_is_empty()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """{"user": {"id": "ledger-api-user", "primaryParty": ""}}""");
        IAdminClient client = ClientWith(transport);

        var user = await client.GetUserAsync("", TestContext.Current.CancellationToken);

        user.Should().Be(new UserDetails("ledger-api-user", PrimaryParty: null));
        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/authenticated-user");
    }

    [Fact]
    public async Task GetUserAsync_surfaces_a_rejection_other_than_404_as_a_LedgerOperationException()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.Forbidden,
            """{"code": "PERMISSION_DENIED", "cause": "missing admin right", "grpcCodeValue": 7, "errorCategory": 7, "context": {}}""");
        IAdminClient client = ClientWith(transport);

        var act = () => client.GetUserAsync("carol", TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.Category.Should().Be(DamlErrorCategory.AuthorizationChecksFailed);
        thrown.Which.ErrorId.Should().Be("PERMISSION_DENIED");
    }

    private static readonly UserRight[] EveryKindOfRight =
    [
        new UserRight.ActAs(new Party("alice::1220ab")),
        new UserRight.ReadAs(new Party("bob::1220cd")),
        new UserRight.ParticipantAdmin(),
        new UserRight.IdentityProviderAdmin(),
        new UserRight.ReadAsAnyParty(),
        new UserRight.ExecuteAs(new Party("carol::1220ef")),
        new UserRight.ExecuteAsAnyParty(),
    ];

    private const string EveryKindOfRightJson =
        """[{"kind":{"CanActAs":{"value":{"party":"alice::1220ab"}}}},{"kind":{"CanReadAs":{"value":{"party":"bob::1220cd"}}}},{"kind":{"ParticipantAdmin":{"value":{}}}},{"kind":{"IdentityProviderAdmin":{"value":{}}}},{"kind":{"CanReadAsAnyParty":{"value":{}}}},{"kind":{"CanExecuteAs":{"value":{"party":"carol::1220ef"}}}},{"kind":{"CanExecuteAsAnyParty":{"value":{}}}}]""";

    [Fact]
    public async Task GrantUserRightsAsync_posts_every_kind_of_right_to_the_users_rights_route()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, """{"newlyGrantedRights": []}""");
        IAdminClient client = ClientWith(transport);

        await client.GrantUserRightsAsync("carol@example", EveryKindOfRight, TestContext.Current.CancellationToken);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/users/carol%40example/rights");
        transport.LastRequestBody.Should().Be($$"""{"userId":"carol@example","rights":{{EveryKindOfRightJson}}}""");
    }

    [Fact]
    public async Task RevokeUserRightsAsync_patches_the_users_rights_route_with_the_rights_to_revoke()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, """{"newlyRevokedRights": []}""");
        IAdminClient client = ClientWith(transport);

        await client.RevokeUserRightsAsync(
            "carol", [new UserRight.ReadAs(new Party("bob::1220cd"))], TestContext.Current.CancellationToken);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/users/carol/rights");
        transport.LastRequestBody.Should().Be(
            """{"userId":"carol","rights":[{"kind":{"CanReadAs":{"value":{"party":"bob::1220cd"}}}}]}""");
    }

    [Fact]
    public async Task GrantUserRightsAsync_is_never_replayed_by_the_opt_in_retry_pipeline()
    {
        var transport = new RecordingHttpHandler()
            .WithTransportException(new HttpRequestException("connection reset after the request was sent"));
        IAdminClient client = RetryingClientWith(transport);

        var act = () => client.GrantUserRightsAsync("carol", EveryKindOfRight, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task RevokeUserRightsAsync_is_never_replayed_by_the_opt_in_retry_pipeline()
    {
        var transport = new RecordingHttpHandler()
            .WithTransportException(new HttpRequestException("connection reset after the request was sent"));
        IAdminClient client = RetryingClientWith(transport);

        var act = () => client.RevokeUserRightsAsync("carol", EveryKindOfRight, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ListUserRightsAsync_maps_every_kind_of_right_the_participant_serves()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, $$"""{"rights": {{EveryKindOfRightJson}}}""");
        IAdminClient client = ClientWith(transport);

        var rights = await client.ListUserRightsAsync("carol@example", TestContext.Current.CancellationToken);

        rights.Should().Equal(EveryKindOfRight);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/users/carol%40example/rights");
    }

    [Fact]
    public async Task ListUserRightsAsync_returns_null_when_the_participant_answers_a_plain_404()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.NotFound, "The requested resource could not be found.");
        IAdminClient client = ClientWith(transport);

        var rights = await client.ListUserRightsAsync("ghost", TestContext.Current.CancellationToken);

        rights.Should().BeNull();
    }

    [Fact]
    public async Task ListUserRightsAsync_lists_the_authenticated_users_rights_when_the_id_is_empty()
    {
        var transport = new RecordingHttpHandler()
            .WithResponseForPath(
                "/v2/authenticated-user",
                HttpStatusCode.OK,
                """{"user": {"id": "ledger-api-user", "primaryParty": ""}}""")
            .WithResponseForPath(
                "/v2/users/ledger-api-user/rights",
                HttpStatusCode.OK,
                """{"rights": [{"kind": {"ParticipantAdmin": {"value": {}}}}]}""");
        IAdminClient client = ClientWith(transport);

        var rights = await client.ListUserRightsAsync("", TestContext.Current.CancellationToken);

        rights.Should().Equal(new UserRight.ParticipantAdmin());
        transport.Requests.Select(request => request.PathAndQuery).Should().Equal(
            "/v2/authenticated-user",
            "/v2/users/ledger-api-user/rights");
    }

    [Fact]
    public async Task ListUsersAsync_follows_the_page_token_of_v2_users_until_it_is_empty()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, """{"users": [{"id": "carol", "primaryParty": "alice::1220ab"}], "nextPageToken": "next page"}"""),
            (HttpStatusCode.OK, """{"users": [{"id": "dave", "primaryParty": ""}]}"""));
        IAdminClient client = ClientWith(transport);

        var users = await client.ListUsersAsync(TestContext.Current.CancellationToken);

        users.Should().Equal(
            new UserDetails("carol", new Party("alice::1220ab")),
            new UserDetails("dave", PrimaryParty: null));
        transport.Requests.Select(request => request.PathAndQuery).Should().Equal(
            "/v2/users?pageSize=100",
            "/v2/users?pageSize=100&pageToken=next%20page");
    }

    [Fact]
    public async Task ListKnownPackagesAsync_throws_NotSupportedException_without_calling_the_participant()
    {
        var transport = new RecordingHttpHandler();
        IAdminClient client = ClientWith(transport);

        var act = () => client.ListKnownPackagesAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<NotSupportedException>()).WithMessage(
            "ListKnownPackagesAsync is not available over the JSON Ledger API: it serves no route for PackageManagementService.ListKnownPackages. Use ListVettedPackagesAsync, or the gRPC IAdminClient registered by AddAdminClient.");
        transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ListPackagesAsync_reads_the_package_ids_from_v2_packages()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, """{"packageIds": ["1220aa", "1220bb"]}""");
        IAdminClient client = ClientWith(transport);

        var packageIds = await client.ListPackagesAsync(TestContext.Current.CancellationToken);

        packageIds.Should().Equal("1220aa", "1220bb");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/packages");
    }

    [Theory]
    [InlineData("PACKAGE_STATUS_REGISTERED", PackageStatus.Registered)]
    [InlineData("PACKAGE_STATUS_UNSPECIFIED", PackageStatus.Unspecified)]
    public async Task GetPackageStatusAsync_maps_the_served_status(string wireStatus, PackageStatus expected)
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, $$"""{"packageStatus": "{{wireStatus}}"}""");
        IAdminClient client = ClientWith(transport);

        var status = await client.GetPackageStatusAsync("1220 aa", TestContext.Current.CancellationToken);

        status.Should().Be(expected);
        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/packages/1220%20aa/status");
    }

    [Fact]
    public async Task GetPackageStatusAsync_reads_an_absent_status_as_unspecified()
    {
        IAdminClient client = ClientWith(new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{}"));

        var status = await client.GetPackageStatusAsync("1220aa", TestContext.Current.CancellationToken);

        status.Should().Be(PackageStatus.Unspecified);
    }

    [Fact]
    public async Task GetPackageStatusAsync_throws_ArgumentException_for_a_blank_package_id()
    {
        IAdminClient client = ClientWith(new RecordingHttpHandler());

        var act = () => client.GetPackageStatusAsync(" ", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetTimeAsync_and_SetTimeAsync_throw_NotSupportedException_without_calling_the_participant()
    {
        var transport = new RecordingHttpHandler();
        IAdminClient client = ClientWith(transport);
        var instant = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        var getTime = () => client.GetTimeAsync(TestContext.Current.CancellationToken);
        var setTime = () => client.SetTimeAsync(instant, instant.AddHours(1), TestContext.Current.CancellationToken);

        const string message =
            "The participant's TimeService is not available over the JSON Ledger API: it serves no route for GetTime or SetTime. Use the gRPC IAdminClient registered by AddAdminClient.";
        (await getTime.Should().ThrowAsync<NotSupportedException>()).WithMessage(message);
        (await setTime.Should().ThrowAsync<NotSupportedException>()).WithMessage(message);
        transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ListVettedPackagesAsync_posts_the_name_prefixes_to_v2_package_vetting_list_and_follows_the_page_token()
    {
        var transport = new RecordingHttpHandler().WithResponseSequence(
            (HttpStatusCode.OK, """
                {"vettedPackages": [{"packages": [{"packageId": "1220aa", "packageName": "splice-amulet", "packageVersion": "0.1.14"}],
                  "participantId": "participant1::1220ab", "synchronizerId": "global-domain::1220ef", "topologySerial": 3}],
                 "nextPageToken": "page-2"}
                """),
            (HttpStatusCode.OK, """
                {"vettedPackages": [{"packages": [{"packageId": "1220bb", "packageName": "splice-wallet", "packageVersion": "0.1.15"},
                                                  {"packageId": "1220cc", "packageName": "splice-util", "packageVersion": "0.1.4"}],
                  "participantId": "participant1::1220ab", "synchronizerId": "global-domain::1220ef"}],
                 "nextPageToken": ""}
                """));
        IAdminClient client = ClientWith(transport);

        var vetted = await client.ListVettedPackagesAsync(["splice-"], TestContext.Current.CancellationToken);

        vetted.Should().Equal(
            new VettedPackage("1220aa", "splice-amulet", "0.1.14", "participant1::1220ab", new SynchronizerId("global-domain::1220ef")),
            new VettedPackage("1220bb", "splice-wallet", "0.1.15", "participant1::1220ab", new SynchronizerId("global-domain::1220ef")),
            new VettedPackage("1220cc", "splice-util", "0.1.4", "participant1::1220ab", new SynchronizerId("global-domain::1220ef")));
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.Requests.Should().Equal(
            ("/v2/package-vetting/list", """{"packageMetadataFilter":{"packageNamePrefixes":["splice-"]}}"""),
            ("/v2/package-vetting/list", """{"packageMetadataFilter":{"packageNamePrefixes":["splice-"]},"pageToken":"page-2"}"""));
    }

    [Fact]
    public async Task ListVettedPackagesAsync_sends_no_metadata_filter_when_no_prefix_is_given()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, """{"vettedPackages": []}""");
        IAdminClient client = ClientWith(transport);

        var vetted = await client.ListVettedPackagesAsync([], TestContext.Current.CancellationToken);

        vetted.Should().BeEmpty();
        transport.LastRequestBody.Should().Be("{}");
    }

    [Fact]
    public async Task ListVettedPackagesAsync_is_replayed_by_the_opt_in_retry_pipeline()
    {
        var transport = new RecordingHttpHandler()
            .WithTransportException(new HttpRequestException("connection refused"));
        IAdminClient client = RetryingClientWith(transport);

        var act = () => client.ListVettedPackagesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
        transport.Requests.Should().HaveCount(4);
    }

    [Fact]
    public async Task GetPackageAsync_reads_the_archive_bytes_and_the_package_hash_header_from_v2_packages()
    {
        var transport = new RecordingHttpHandler()
            .WithBinaryResponse(HttpStatusCode.OK, [0x0A, 0x0B, 0x0C], "application/octet-stream")
            .WithResponseHeader("Canton-Package-Hash", "1220deadbeef");
        IAdminClient client = ClientWith(transport);

        var archive = await client.GetPackageAsync("1220aa", TestContext.Current.CancellationToken);

        archive.Should().Be(new PackageArchive(new byte[] { 0x0A, 0x0B, 0x0C }, "1220deadbeef", HashFunction.Sha256));
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/packages/1220aa");
        transport.LastRequest.Headers.Accept.Should().ContainSingle().Which.MediaType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task GetPackageAsync_surfaces_a_response_without_the_package_hash_header_as_an_undecodable_body()
    {
        var transport = new RecordingHttpHandler()
            .WithBinaryResponse(HttpStatusCode.OK, [0x0A], "application/octet-stream");
        IAdminClient client = ClientWith(transport);

        var act = () => client.GetPackageAsync("1220aa", TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.Which.Message.Should().Be(
            "Server returned a malformed package archive response body: Malformed response from ledger: The response carries no Canton-Package-Hash header.");
    }

    [Fact]
    public async Task GetPackageAsync_surfaces_an_unknown_package_as_a_LedgerOperationException()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.NotFound,
            """{"code": "PACKAGE_NOT_FOUND", "cause": "package 1220ff not found", "grpcCodeValue": 5, "errorCategory": 11, "context": {}}""");
        IAdminClient client = ClientWith(transport);

        var act = () => client.GetPackageAsync("1220ff", TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.ErrorId.Should().Be("PACKAGE_NOT_FOUND");
        thrown.Which.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing);
    }

    [Fact]
    public async Task UploadDarAsync_posts_the_dar_bytes_as_an_octet_stream_to_v2_dars_without_a_submission_id()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{}");
        IAdminClient client = ClientWith(transport);

        await client.UploadDarAsync([0x50, 0x4B, 0x03, 0x04], "upload-1", TestContext.Current.CancellationToken);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/dars");
        transport.LastRequest.Content!.Headers.ContentType!.MediaType.Should().Be("application/octet-stream");
        transport.LastRequestBytes.Should().Equal(0x50, 0x4B, 0x03, 0x04);
    }

    [Fact]
    public async Task UploadDarAsync_adds_the_synchronizerId_query_parameter_when_synchronizerId_provided()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{}");
        IAdminClient client = ClientWith(transport);

        await client.UploadDarAsync(
            [0x50, 0x4B, 0x03, 0x04],
            synchronizerId: new SynchronizerId("sync::ns1"),
            submissionId: null,
            cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/dars?synchronizerId=sync%3A%3Ans1");
    }

    [Fact]
    public async Task UploadDarAsync_omits_the_synchronizerId_query_parameter_when_synchronizerId_omitted()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{}");
        IAdminClient client = ClientWith(transport);

        await client.UploadDarAsync([0x50, 0x4B, 0x03, 0x04], cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/dars");
    }

    [Fact]
    public async Task UploadDarAsync_is_never_replayed_by_the_opt_in_retry_pipeline()
    {
        var transport = new RecordingHttpHandler()
            .WithTransportException(new HttpRequestException("connection reset after the request was sent"));
        IAdminClient client = RetryingClientWith(transport);

        var act = () => client.UploadDarAsync([0x50, 0x4B], cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ValidateDarAsync_posts_the_dar_bytes_as_an_octet_stream_to_v2_dars_validate()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{}");
        IAdminClient client = ClientWith(transport);

        await client.ValidateDarAsync([0x50, 0x4B, 0x03, 0x04], TestContext.Current.CancellationToken);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/dars/validate");
        transport.LastRequest.Content!.Headers.ContentType!.MediaType.Should().Be("application/octet-stream");
        transport.LastRequestBytes.Should().Equal(0x50, 0x4B, 0x03, 0x04);
    }

    [Fact]
    public async Task ValidateDarAsync_adds_the_synchronizerId_query_parameter_when_synchronizerId_provided()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{}");
        IAdminClient client = ClientWith(transport);

        await client.ValidateDarAsync(
            [0x50, 0x4B, 0x03, 0x04],
            synchronizerId: new SynchronizerId("sync::ns1"),
            cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/dars/validate?synchronizerId=sync%3A%3Ans1");
    }

    [Fact]
    public async Task ValidateDarAsync_omits_the_synchronizerId_query_parameter_when_synchronizerId_omitted()
    {
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{}");
        IAdminClient client = ClientWith(transport);

        await client.ValidateDarAsync([0x50, 0x4B, 0x03, 0x04], cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/dars/validate");
    }

    [Fact]
    public async Task ValidateDarAsync_surfaces_an_invalid_dar_as_a_LedgerOperationException()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.BadRequest,
            """{"code": "DAR_PARSE_ERROR", "cause": "Failed to parse the dar file content.", "grpcCodeValue": 3, "errorCategory": 8, "context": {}}""");
        IAdminClient client = ClientWith(transport);

        var act = () => client.ValidateDarAsync([0x00], TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.ErrorId.Should().Be("DAR_PARSE_ERROR");
        thrown.Which.Category.Should().Be(DamlErrorCategory.InvalidIndependentOfSystemState);
    }

    [Fact]
    public async Task GetUserAsync_records_a_RestAdminClient_span_tagged_with_the_user_id()
    {
        using var capture = ActivityCapture.Of("Canton.Ledger.Rest.Client.RestAdminClient");
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, """{"user": {"id": "carol"}}""");
        IAdminClient client = ClientWith(transport);

        await client.GetUserAsync("carol", TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.OperationName.Should().Be("RestAdminClient.GetUserAsync");
        activity.GetTagItem("canton.user_id").Should().Be("carol");
        activity.Status.Should().Be(ActivityStatusCode.Unset);
    }

    [Fact]
    public async Task GetPackageAsync_records_a_failed_RestAdminClient_span_tagged_with_the_package_id()
    {
        using var capture = ActivityCapture.Of("Canton.Ledger.Rest.Client.RestAdminClient");
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.NotFound,
            """{"code": "PACKAGE_NOT_FOUND", "cause": "package 1220ff not found", "grpcCodeValue": 5, "errorCategory": 11, "context": {}}""");
        IAdminClient client = ClientWith(transport);

        var act = () => client.GetPackageAsync("1220ff", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.OperationName.Should().Be("RestAdminClient.GetPackageAsync");
        activity.GetTagItem("daml.package_id").Should().Be("1220ff");
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem("error.type").Should().Be("Daml.Ledger.Abstractions.LedgerOperationException");
    }

    [Fact]
    public async Task GetUserAsync_leaves_the_span_of_an_unknown_user_unfailed()
    {
        using var capture = ActivityCapture.Of("Canton.Ledger.Rest.Client.RestAdminClient");
        var transport = new RecordingHttpHandler().WithResponse(HttpStatusCode.NotFound, UserNotFoundBody);
        IAdminClient client = ClientWith(transport);

        await client.GetUserAsync("ghost", TestContext.Current.CancellationToken);

        capture.Activities.Should().ContainSingle().Which.Status.Should().Be(ActivityStatusCode.Unset);
    }

    private (IAdminClient Client, CapturingLoggerFactory Logs) LoggingClientWith(RecordingHttpHandler transport)
    {
        var factory = new StubHttpClientFactory(transport);
        _factories.Add(factory);
        var logs = new CapturingLoggerFactory();
        return (new RestAdminClient(factory, logs.CreateLogger<RestAdminClient>()), logs);
    }

    private static IEnumerable<(LogLevel Level, string Message)> LevelsAndMessages(CapturingLoggerFactory logs) =>
        logs.Records.Select(record => (record.Level, record.Message));

    [Fact]
    public async Task AllocatePartyAsync_logs_the_hint_before_and_the_allocated_party_after()
    {
        var (client, logs) = LoggingClientWith(new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, """{"partyDetails": {"party": "alice::1220abcd", "isLocal": true}}"""));

        await client.AllocatePartyAsync("alice", cancellationToken: TestContext.Current.CancellationToken);

        LevelsAndMessages(logs).Should().Equal(
            (LogLevel.Debug, "Allocating party with hint: alice"),
            (LogLevel.Information, "Party allocated: alice::1220abcd"));
    }

    [Fact]
    public async Task CreateUserAsync_logs_the_user_before_and_after_creating_it()
    {
        var (client, logs) = LoggingClientWith(new RecordingHttpHandler().WithResponse(
            HttpStatusCode.OK, """{"user": {"id": "carol"}}"""));

        await client.CreateUserAsync("carol", primaryParty: null, cancellationToken: TestContext.Current.CancellationToken);

        LevelsAndMessages(logs).Should().Equal(
            (LogLevel.Debug, "Creating user: carol"),
            (LogLevel.Information, "User created: carol"));
    }

    [Fact]
    public async Task GrantUserRightsAsync_and_RevokeUserRightsAsync_log_the_user_whose_rights_changed()
    {
        var (client, logs) = LoggingClientWith(new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{}"));

        await client.GrantUserRightsAsync("carol", [new UserRight.ParticipantAdmin()], TestContext.Current.CancellationToken);
        await client.RevokeUserRightsAsync("carol", [new UserRight.ParticipantAdmin()], TestContext.Current.CancellationToken);

        LevelsAndMessages(logs).Should().Equal(
            (LogLevel.Information, "Rights granted to user carol"),
            (LogLevel.Information, "Rights revoked from user carol"));
    }

    [Fact]
    public async Task UploadDarAsync_and_ValidateDarAsync_log_the_dar_size()
    {
        var (client, logs) = LoggingClientWith(new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, "{}"));

        await client.UploadDarAsync([0x50, 0x4B, 0x03], cancellationToken: TestContext.Current.CancellationToken);
        await client.ValidateDarAsync([0x50, 0x4B], TestContext.Current.CancellationToken);

        LevelsAndMessages(logs).Should().Equal(
            (LogLevel.Debug, "Uploading DAR file (3 bytes)"),
            (LogLevel.Information, "DAR file uploaded (3 bytes)"),
            (LogLevel.Information, "DAR file validated (2 bytes)"));
    }
}
