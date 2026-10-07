// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Com.Daml.Ledger.Api.V2;
using Com.Daml.Ledger.Api.V2.Admin;
using Com.Daml.Ledger.Api.V2.Testing;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

[Collection(nameof(AdminClientActivitySourceIsolation))]
public sealed class AdminClientFailureContractTests : IDisposable
{
    private readonly LedgerClientOptions _options = new() { GrpcAddress = "https://localhost:5001" };
    private readonly GrpcChannel _channel;
    private readonly PartyManagementService.PartyManagementServiceClient _partyService;
    private readonly UserManagementService.UserManagementServiceClient _userService;
    private readonly PackageManagementService.PackageManagementServiceClient _packageService;
    private readonly PackageService.PackageServiceClient _vettingService;
    private readonly CommandInspectionService.CommandInspectionServiceClient _commandInspectionService;
    private readonly TimeService.TimeServiceClient _timeService;
    private readonly IdentityProviderConfigService.IdentityProviderConfigServiceClient _idpService;

    public AdminClientFailureContractTests()
    {
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);
        var callInvoker = Substitute.For<CallInvoker>();
        _partyService = Substitute.ForPartsOf<PartyManagementService.PartyManagementServiceClient>(callInvoker);
        _userService = Substitute.ForPartsOf<UserManagementService.UserManagementServiceClient>(callInvoker);
        _packageService = Substitute.ForPartsOf<PackageManagementService.PackageManagementServiceClient>(callInvoker);
        _vettingService = Substitute.ForPartsOf<PackageService.PackageServiceClient>(callInvoker);
        _commandInspectionService = Substitute.ForPartsOf<CommandInspectionService.CommandInspectionServiceClient>(callInvoker);
        _timeService = Substitute.ForPartsOf<TimeService.TimeServiceClient>(callInvoker);
        _idpService = Substitute.ForPartsOf<IdentityProviderConfigService.IdentityProviderConfigServiceClient>(callInvoker);
    }

    public void Dispose() => _channel.Dispose();

    private AdminClient CreateClient(ITokenProvider? tokenProvider = null) =>
        new(
            _options,
            _channel,
            _partyService,
            _userService,
            tokenProvider ?? new StaticTokenProvider("test-token"),
            packageManagementService: _packageService,
            packageService: _vettingService,
            commandInspectionService: _commandInspectionService,
            timeService: _timeService,
            identityProviderConfigService: _idpService);

    [Fact]
    public async Task A_read_rejected_with_a_structured_error_carries_its_detail_and_did_not_commit()
    {
        var rejection = LedgerClientTestFixtures.MakeDamlRpcException(
            "PERMISSION_DENIED",
            "not allowed",
            "InvalidIndependentOfSystemState",
            StatusCode.PermissionDenied);
        StubGetParticipantId(rejection);

        var act = () => CreateClient().GetParticipantIdAsync(TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Message.Should().Be("not allowed");
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.PermissionDenied));
        thrown.Category.Should().Be(DamlErrorCategory.InvalidIndependentOfSystemState);
        thrown.ErrorId.Should().Be("PERMISSION_DENIED");
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeSameAs(rejection);
    }

    [Fact]
    public async Task A_read_that_gets_no_answer_did_not_commit()
    {
        var unavailable = new RpcException(new Status(StatusCode.Unavailable, "participant down"));
        StubGetParticipantId(unavailable);

        var act = () => CreateClient().GetParticipantIdAsync(TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeSameAs(unavailable);
    }

    [Fact]
    public async Task A_write_rejected_with_a_definite_category_did_not_commit()
    {
        var rejection = LedgerClientTestFixtures.MakeDamlRpcException(
            "USER_ALREADY_EXISTS",
            "user exists",
            "InvalidGivenCurrentSystemStateResourceExists",
            StatusCode.AlreadyExists);
        StubDeleteUser(rejection);

        var act = () => CreateClient().DeleteUserAsync("alice", cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.AlreadyExists));
        thrown.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists);
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeSameAs(rejection);
    }

    [Fact]
    public async Task A_write_rejected_with_a_request_state_unknown_category_may_have_committed()
    {
        var rejection = LedgerClientTestFixtures.MakeDamlRpcException(
            "REQUEST_TIME_OUT",
            "timed out",
            "DeadlineExceededRequestStateUnknown",
            StatusCode.DeadlineExceeded);
        StubDeleteUser(rejection);

        var act = () => CreateClient().DeleteUserAsync("alice", cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.DeadlineExceeded));
        thrown.CommitState.Should().Be(CommitState.Unknown);
    }

    [Fact]
    public async Task A_write_that_gets_no_answer_may_have_committed()
    {
        var unavailable = new RpcException(new Status(StatusCode.Unavailable, "connection dropped"));
        StubDeleteUser(unavailable);

        var act = () => CreateClient().DeleteUserAsync("alice", cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        thrown.CommitState.Should().Be(CommitState.Unknown);
        thrown.InnerException.Should().BeSameAs(unavailable);
    }

    [Fact]
    public async Task GetUserAsync_raises_a_not_committed_failure_for_a_status_other_than_NotFound()
    {
        var denied = new RpcException(new Status(StatusCode.PermissionDenied, "denied"));
        _userService
            .GetUserAsync(Arg.Any<GetUserRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Faulted<GetUserResponse>(denied));

        var act = () => CreateClient().GetUserAsync("alice", TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.PermissionDenied));
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeSameAs(denied);
    }

    [Fact]
    public async Task ListUserRightsAsync_raises_a_not_committed_failure_for_a_status_other_than_NotFound()
    {
        var denied = new RpcException(new Status(StatusCode.PermissionDenied, "denied"));
        _userService
            .ListUserRightsAsync(Arg.Any<ListUserRightsRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Faulted<ListUserRightsResponse>(denied));

        var act = () => CreateClient().ListUserRightsAsync("alice", TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.PermissionDenied));
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeSameAs(denied);
    }

    [Fact]
    public async Task GetIdentityProviderConfigAsync_raises_a_not_committed_failure_for_a_status_other_than_NotFound()
    {
        var denied = new RpcException(new Status(StatusCode.PermissionDenied, "denied"));
        _idpService
            .GetIdentityProviderConfigAsync(Arg.Any<GetIdentityProviderConfigRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Faulted<GetIdentityProviderConfigResponse>(denied));

        var act = () => CreateClient().GetIdentityProviderConfigAsync("idp-1", TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.PermissionDenied));
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeSameAs(denied);
    }

    [Fact]
    public async Task AllocatePartyAsync_raises_an_undecodable_body_failure_that_committed_when_the_response_names_no_party()
    {
        _partyService
            .AllocatePartyAsync(Arg.Any<AllocatePartyRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new AllocatePartyResponse()));

        var act = () => CreateClient().AllocatePartyAsync("alice", cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.Committed);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>().Which.Detail.Should().Be(
            "the response has no party_details, though the Ledger API marks the field as required.");
    }

    [Fact]
    public async Task CreateUserAsync_raises_an_undecodable_body_failure_that_committed_when_the_primary_party_is_blank()
    {
        _userService
            .CreateUserAsync(Arg.Any<CreateUserRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new CreateUserResponse { User = new User { Id = "alice", PrimaryParty = "   " } }));

        var act = () => CreateClient().CreateUserAsync("alice", primaryParty: null, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.Committed);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>()
            .Which.InnerException.Should().BeOfType<ArgumentException>();
    }

    [Fact]
    public async Task GetUserAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_response_names_no_user()
    {
        _userService
            .GetUserAsync(Arg.Any<GetUserRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new GetUserResponse()));

        var act = () => CreateClient().GetUserAsync("alice", TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>().Which.Detail.Should().Be(
            "the response has no user, though the Ledger API marks the field as required.");
    }

    [Fact]
    public async Task CreateIdentityProviderConfigAsync_raises_an_undecodable_body_failure_that_committed_when_the_response_has_no_config()
    {
        _idpService
            .CreateIdentityProviderConfigAsync(Arg.Any<CreateIdentityProviderConfigRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new CreateIdentityProviderConfigResponse()));

        var act = () => CreateClient().CreateIdentityProviderConfigAsync(
            new Canton.Ledger.Abstractions.IdentityProviderConfig("idp-1", false, "https://issuer", "https://jwks", "aud"),
            TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.Committed);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>().Which.Detail.Should().Be(
            "the response has no identity_provider_config, though the Ledger API marks the field as required.");
    }

    [Fact]
    public async Task GetIdentityProviderConfigAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_response_has_no_config()
    {
        _idpService
            .GetIdentityProviderConfigAsync(Arg.Any<GetIdentityProviderConfigRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new GetIdentityProviderConfigResponse()));

        var act = () => CreateClient().GetIdentityProviderConfigAsync("idp-1", TestContext.Current.CancellationToken);

        var thrown = await AssertUndecodableRead(act);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>().Which.Detail.Should().Be(
            "the response has no identity_provider_config, though the Ledger API marks the field as required.");
    }

    [Fact]
    public async Task UpdateIdentityProviderConfigAsync_raises_an_undecodable_body_failure_that_committed_when_the_response_has_no_config()
    {
        _idpService
            .UpdateIdentityProviderConfigAsync(Arg.Any<UpdateIdentityProviderConfigRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new UpdateIdentityProviderConfigResponse()));

        var act = () => CreateClient().UpdateIdentityProviderConfigAsync(
            "idp-1", new IdentityProviderConfigUpdate { IsDeactivated = true }, TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.Committed);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>().Which.Detail.Should().Be(
            "the response has no identity_provider_config, though the Ledger API marks the field as required.");
    }

    [Fact]
    public async Task ListKnownPackagesAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_a_package_has_no_known_since()
    {
        _packageService
            .ListKnownPackagesAsync(Arg.Any<ListKnownPackagesRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new ListKnownPackagesResponse
            {
                PackageDetails = { new Com.Daml.Ledger.Api.V2.Admin.PackageDetails { PackageId = "pkg-1", Name = "pkg", Version = "1.0.0" } },
            }));

        var act = () => CreateClient().ListKnownPackagesAsync(TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>().Which.Detail.Should().Be(
            "package 'pkg-1' has no known_since, though the Ledger API marks the field as required.");
    }

    [Fact]
    public async Task ListKnownPackagesAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_a_package_size_exceeds_the_signed_range()
    {
        _packageService
            .ListKnownPackagesAsync(Arg.Any<ListKnownPackagesRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new ListKnownPackagesResponse
            {
                PackageDetails =
                {
                    new Com.Daml.Ledger.Api.V2.Admin.PackageDetails
                    {
                        PackageId = "pkg-1",
                        Name = "pkg",
                        Version = "1.0.0",
                        PackageSize = ulong.MaxValue,
                        KnownSince = Timestamp.FromDateTimeOffset(DateTimeOffset.UnixEpoch),
                    },
                },
            }));

        var act = () => CreateClient().ListKnownPackagesAsync(TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>().Which.Detail.Should().Be(
            "package 'pkg-1' reports a size of 18446744073709551615 bytes, which exceeds the supported maximum of 9223372036854775807.");
    }

    [Fact]
    public async Task ListKnownPartiesAsync_lets_a_token_failure_before_a_later_page_escape_unchanged()
    {
        var tokenFailure = new FormatException("the token is not valid base64");
        var tokenProvider = Substitute.For<ITokenProvider>();
        tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("test-token"), Task.FromException<string>(tokenFailure));
        _partyService
            .ListKnownPartiesAsync(Arg.Any<ListKnownPartiesRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new ListKnownPartiesResponse { NextPageToken = "page-2" }));

        var act = () => CreateClient(tokenProvider).ListKnownPartiesAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<FormatException>()).Which.Should().BeSameAs(tokenFailure);
    }

    [Fact]
    public async Task ListKnownPackagesAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_known_since_is_out_of_range()
    {
        _packageService
            .ListKnownPackagesAsync(Arg.Any<ListKnownPackagesRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new ListKnownPackagesResponse
            {
                PackageDetails =
                {
                    new Com.Daml.Ledger.Api.V2.Admin.PackageDetails
                    {
                        PackageId = "pkg-1",
                        Name = "pkg",
                        Version = "1.0.0",
                        KnownSince = new Timestamp { Seconds = long.MaxValue },
                    },
                },
            }));

        var act = () => CreateClient().ListKnownPackagesAsync(TestContext.Current.CancellationToken);

        await AssertUndecodableRead(act);
    }

    [Fact]
    public async Task GetTimeAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_response_has_no_current_time()
    {
        _timeService
            .GetTimeAsync(Arg.Any<GetTimeRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new GetTimeResponse()));

        var act = () => CreateClient().GetTimeAsync(TestContext.Current.CancellationToken);

        var thrown = await AssertUndecodableRead(act);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>().Which.Detail.Should().Be(
            "the GetTime response has no current_time, though the Ledger API marks the field as required.");
    }

    [Fact]
    public async Task GetTimeAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_the_current_time_is_out_of_range()
    {
        _timeService
            .GetTimeAsync(Arg.Any<GetTimeRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new GetTimeResponse { CurrentTime = new Timestamp { Seconds = long.MaxValue } }));

        var act = () => CreateClient().GetTimeAsync(TestContext.Current.CancellationToken);

        await AssertUndecodableRead(act);
    }

    [Fact]
    public async Task GetCommandStatusAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_a_start_time_is_out_of_range()
    {
        _commandInspectionService
            .GetCommandStatusAsync(Arg.Any<GetCommandStatusRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new GetCommandStatusResponse
            {
                CommandStatus = { new Com.Daml.Ledger.Api.V2.Admin.CommandStatus { Started = new Timestamp { Seconds = long.MaxValue } } },
            }));

        var act = () => CreateClient().GetCommandStatusAsync(cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodableRead(act);
    }

    [Fact]
    public async Task UpdateVettedPackagesAsync_raises_an_undecodable_body_failure_that_committed_when_a_validity_bound_is_out_of_range()
    {
        _packageService
            .UpdateVettedPackagesAsync(Arg.Any<UpdateVettedPackagesRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new UpdateVettedPackagesResponse
            {
                NewVettedPackages = new VettedPackages
                {
                    Packages =
                    {
                        new Com.Daml.Ledger.Api.V2.VettedPackage { PackageId = "pkg-1", ValidFromInclusive = new Timestamp { Seconds = long.MaxValue } },
                    },
                },
            }));

        var act = () => CreateClient().UpdateVettedPackagesAsync([], cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.Committed);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>()
            .Which.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task A_dry_run_UpdateVettedPackagesAsync_that_gets_no_answer_did_not_commit()
    {
        var unavailable = new RpcException(new Status(StatusCode.Unavailable, "participant down"));
        StubUpdateVettedPackages(Faulted<UpdateVettedPackagesResponse>(unavailable));

        var act = () => CreateClient().UpdateVettedPackagesAsync([], dryRun: true, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeSameAs(unavailable);
    }

    [Fact]
    public async Task A_UpdateVettedPackagesAsync_that_is_not_a_dry_run_and_gets_no_answer_may_have_committed()
    {
        var unavailable = new RpcException(new Status(StatusCode.Unavailable, "participant down"));
        StubUpdateVettedPackages(Faulted<UpdateVettedPackagesResponse>(unavailable));

        var act = () => CreateClient().UpdateVettedPackagesAsync([], dryRun: false, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        thrown.CommitState.Should().Be(CommitState.Unknown);
    }

    [Fact]
    public async Task A_dry_run_UpdateVettedPackagesAsync_raises_an_undecodable_body_failure_that_did_not_commit()
    {
        StubUpdateVettedPackages(Answered(new UpdateVettedPackagesResponse
        {
            NewVettedPackages = new VettedPackages
            {
                Packages =
                {
                    new Com.Daml.Ledger.Api.V2.VettedPackage { PackageId = "pkg-1", ValidFromInclusive = new Timestamp { Seconds = long.MaxValue } },
                },
            },
        }));

        var act = () => CreateClient().UpdateVettedPackagesAsync([], dryRun: true, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
    }

    [Fact]
    public async Task ListVettedPackagesAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_a_group_has_no_synchronizer_id()
    {
        _vettingService
            .ListVettedPackagesAsync(Arg.Any<ListVettedPackagesRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new ListVettedPackagesResponse
            {
                VettedPackages = { new VettedPackages { Packages = { new Com.Daml.Ledger.Api.V2.VettedPackage { PackageId = "pkg-1" } } } },
            }));

        var act = () => CreateClient().ListVettedPackagesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await AssertUndecodableRead(act);
    }

    [Fact]
    public async Task ListUserRightsAsync_raises_an_undecodable_body_failure_that_did_not_commit_when_a_right_has_no_kind()
    {
        _userService
            .ListUserRightsAsync(Arg.Any<ListUserRightsRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Answered(new ListUserRightsResponse { Rights = { new Right() } }));

        var act = () => CreateClient().ListUserRightsAsync("alice", TestContext.Current.CancellationToken);

        var thrown = await AssertUndecodableRead(act);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>()
            .Which.InnerException.Should().BeOfType<NotSupportedException>();
    }

    private static async Task<LedgerOperationException> AssertUndecodableRead<T>(Func<Task<T>> act)
    {
        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>();
        return thrown;
    }

    private void StubGetParticipantId(RpcException failure) =>
        _partyService
            .GetParticipantIdAsync(Arg.Any<GetParticipantIdRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Faulted<GetParticipantIdResponse>(failure));

    private void StubUpdateVettedPackages(AsyncUnaryCall<UpdateVettedPackagesResponse> call) =>
        _packageService
            .UpdateVettedPackagesAsync(Arg.Any<UpdateVettedPackagesRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(call);

    private void StubDeleteUser(RpcException failure) =>
        _userService
            .DeleteUserAsync(Arg.Any<DeleteUserRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Faulted<DeleteUserResponse>(failure));

    private static AsyncUnaryCall<TResponse> Answered<TResponse>(TResponse response) =>
        new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    private static AsyncUnaryCall<TResponse> Faulted<TResponse>(RpcException exception) =>
        new(
            Task.FromException<TResponse>(exception),
            Task.FromResult(new Metadata()),
            () => exception.Status,
            () => exception.Trailers ?? new Metadata(),
            () => { });
}
