// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Resilience;
using Com.Daml.Ledger.Api.V2.Admin;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using CommandState = Canton.Ledger.Abstractions.CommandState;
using CommandStatus = Canton.Ledger.Abstractions.CommandStatus;
using Party = Daml.Runtime.Data.Party;
using PartyDetails = Canton.Ledger.Abstractions.PartyDetails;

namespace Canton.Ledger.Grpc.Client.Tests;

[Collection(nameof(AdminClientActivitySourceIsolation))]
public sealed class AdminClientUpdateTests : IDisposable
{
    private readonly LedgerClientOptions _options = new() { GrpcAddress = "https://localhost:5001" };
    private readonly GrpcChannel _channel;
    private readonly PartyManagementService.PartyManagementServiceClient _partyService;
    private readonly UserManagementService.UserManagementServiceClient _userService;
    private readonly CommandInspectionService.CommandInspectionServiceClient _commandInspectionService;

    public AdminClientUpdateTests()
    {
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);
        var callInvoker = Substitute.For<CallInvoker>();
        _partyService = Substitute.ForPartsOf<PartyManagementService.PartyManagementServiceClient>(callInvoker);
        _userService = Substitute.ForPartsOf<UserManagementService.UserManagementServiceClient>(callInvoker);
        _commandInspectionService =
            Substitute.ForPartsOf<CommandInspectionService.CommandInspectionServiceClient>(callInvoker);
    }

    public void Dispose() => _channel.Dispose();

    private AdminClient CreateClient() =>
        new(
            _options,
            _channel,
            _partyService,
            _userService,
            new StaticTokenProvider("test-token"),
            commandInspectionService: _commandInspectionService);

    private void EnableRetry() =>
        _options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 2, Delay = TimeSpan.Zero };

    private static AsyncUnaryCall<TResponse> UnaryResponse<TResponse>(TResponse response) =>
        new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    private static AsyncUnaryCall<TResponse> Unavailable<TResponse>()
    {
        var exception = new RpcException(new Status(StatusCode.Unavailable, "down"));
        return new(
            Task.FromException<TResponse>(exception),
            Task.FromResult(new Metadata()),
            () => exception.Status,
            () => new Metadata(),
            () => { });
    }

    [Fact]
    public async Task UpdateUser_sends_only_the_set_properties_in_the_field_mask()
    {
        UpdateUserRequest? captured = null;
        _userService
            .UpdateUserAsync(Arg.Do<UpdateUserRequest>(r => captured = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(new UpdateUserResponse { User = new User { Id = "alice", PrimaryParty = "alice::1220" } }));

        var result = await CreateClient().UpdateUserAsync(
            "alice",
            new UserUpdate
            {
                PrimaryParty = new Party("alice::1220"),
                IsDeactivated = true,
                Annotations = new Dictionary<string, string> { ["team"] = "ledger" },
            },
            cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be(new UserDetails("alice", new Party("alice::1220")));
        captured!.UpdateMask.Paths.Should().Equal("primary_party", "is_deactivated", "metadata.annotations");
        captured.User.Id.Should().Be("alice");
        captured.User.PrimaryParty.Should().Be("alice::1220");
        captured.User.IsDeactivated.Should().BeTrue();
        captured.User.Metadata.Annotations.Should().Equal(new Dictionary<string, string> { ["team"] = "ledger" });
    }

    [Fact]
    public async Task UpdateUser_clears_the_primary_party_with_an_empty_value_under_the_primary_party_path()
    {
        UpdateUserRequest? captured = null;
        _userService
            .UpdateUserAsync(Arg.Do<UpdateUserRequest>(r => captured = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(new UpdateUserResponse { User = new User { Id = "alice" } }));

        var result = await CreateClient().UpdateUserAsync(
            "alice", new UserUpdate { ClearPrimaryParty = true }, cancellationToken: TestContext.Current.CancellationToken);

        result.PrimaryParty.Should().BeNull();
        captured!.UpdateMask.Paths.Should().Equal("primary_party");
        captured.User.PrimaryParty.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateUser_sends_the_identity_provider_id()
    {
        UpdateUserRequest? captured = null;
        _userService
            .UpdateUserAsync(Arg.Do<UpdateUserRequest>(r => captured = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(new UpdateUserResponse { User = new User { Id = "alice" } }));

        await CreateClient().UpdateUserAsync(
            "alice", new UserUpdate { IsDeactivated = false }, "idp-1", TestContext.Current.CancellationToken);

        captured!.User.IdentityProviderId.Should().Be("idp-1");
        captured.UpdateMask.Paths.Should().Equal("is_deactivated");
    }

    [Fact]
    public async Task UpdateUser_rejects_an_update_that_changes_nothing()
    {
        var act = () => CreateClient().UpdateUserAsync("alice", new UserUpdate(), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ArgumentException>()).WithParameterName("update");
    }

    [Fact]
    public async Task UpdateUser_rejects_an_update_that_both_sets_and_clears_the_primary_party()
    {
        var update = new UserUpdate { PrimaryParty = new Party("alice::1220"), ClearPrimaryParty = true };

        var act = () => CreateClient().UpdateUserAsync("alice", update, cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ArgumentException>()).WithParameterName("update");
    }

    [Fact]
    public async Task UpdateUser_is_not_retried_on_Unavailable_when_Retry_is_enabled()
    {
        EnableRetry();
        _userService
            .UpdateUserAsync(Arg.Any<UpdateUserRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Unavailable<UpdateUserResponse>());

        var act = () => CreateClient().UpdateUserAsync(
            "alice", new UserUpdate { IsDeactivated = true }, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>();
        _ = _userService.Received(1).UpdateUserAsync(
            Arg.Any<UpdateUserRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteUser_sends_the_user_and_identity_provider_ids()
    {
        DeleteUserRequest? captured = null;
        _userService
            .DeleteUserAsync(Arg.Do<DeleteUserRequest>(r => captured = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(new DeleteUserResponse()));

        await CreateClient().DeleteUserAsync("alice", "idp-1", TestContext.Current.CancellationToken);

        captured!.UserId.Should().Be("alice");
        captured.IdentityProviderId.Should().Be("idp-1");
    }

    [Fact]
    public async Task DeleteUser_is_not_retried_on_Unavailable_when_Retry_is_enabled()
    {
        EnableRetry();
        _userService
            .DeleteUserAsync(Arg.Any<DeleteUserRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Unavailable<DeleteUserResponse>());

        var act = () => CreateClient().DeleteUserAsync("alice", cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>();
        _ = _userService.Received(1).DeleteUserAsync(
            Arg.Any<DeleteUserRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateUserIdentityProviderId_sends_source_and_target_and_is_not_retried()
    {
        EnableRetry();
        UpdateUserIdentityProviderIdRequest? captured = null;
        _userService
            .UpdateUserIdentityProviderIdAsync(Arg.Do<UpdateUserIdentityProviderIdRequest>(r => captured = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Unavailable<UpdateUserIdentityProviderIdResponse>());

        var act = () => CreateClient().UpdateUserIdentityProviderIdAsync("alice", null, "idp-2", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>();
        captured!.UserId.Should().Be("alice");
        captured.SourceIdentityProviderId.Should().BeEmpty();
        captured.TargetIdentityProviderId.Should().Be("idp-2");
        _ = _userService.Received(1).UpdateUserIdentityProviderIdAsync(
            Arg.Any<UpdateUserIdentityProviderIdRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdatePartyDetails_sends_annotations_under_the_local_metadata_path_and_returns_the_details()
    {
        UpdatePartyDetailsRequest? captured = null;
        _partyService
            .UpdatePartyDetailsAsync(Arg.Do<UpdatePartyDetailsRequest>(r => captured = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(new UpdatePartyDetailsResponse
            {
                PartyDetails = new Com.Daml.Ledger.Api.V2.Admin.PartyDetails { Party = "alice::1220", IsLocal = true },
            }));

        var result = await CreateClient().UpdatePartyDetailsAsync(
            new Party("alice::1220"),
            new PartyUpdate { Annotations = new Dictionary<string, string> { ["owner"] = "ops" } },
            cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be(new PartyDetails(new Party("alice::1220"), true));
        captured!.UpdateMask.Paths.Should().Equal("local_metadata.annotations");
        captured.PartyDetails.Party.Should().Be("alice::1220");
        captured.PartyDetails.LocalMetadata.Annotations.Should().Equal(new Dictionary<string, string> { ["owner"] = "ops" });
    }

    [Fact]
    public async Task UpdatePartyDetails_is_not_retried_on_Unavailable_when_Retry_is_enabled()
    {
        EnableRetry();
        _partyService
            .UpdatePartyDetailsAsync(Arg.Any<UpdatePartyDetailsRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Unavailable<UpdatePartyDetailsResponse>());

        var act = () => CreateClient().UpdatePartyDetailsAsync(
            new Party("alice::1220"),
            new PartyUpdate { Annotations = new Dictionary<string, string>() },
            cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>();
        _ = _partyService.Received(1).UpdatePartyDetailsAsync(
            Arg.Any<UpdatePartyDetailsRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdatePartyDetails_rejects_an_update_that_changes_nothing()
    {
        var act = () => CreateClient().UpdatePartyDetailsAsync(
            new Party("alice::1220"), new PartyUpdate(), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ArgumentException>()).WithParameterName("update");
    }

    [Fact]
    public async Task UpdatePartyIdentityProviderId_sends_party_source_and_target_and_is_not_retried()
    {
        EnableRetry();
        UpdatePartyIdentityProviderIdRequest? captured = null;
        _partyService
            .UpdatePartyIdentityProviderIdAsync(Arg.Do<UpdatePartyIdentityProviderIdRequest>(r => captured = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Unavailable<UpdatePartyIdentityProviderIdResponse>());

        var act = () => CreateClient().UpdatePartyIdentityProviderIdAsync(
            new Party("alice::1220"), "idp-1", null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>();
        captured!.Party.Should().Be("alice::1220");
        captured.SourceIdentityProviderId.Should().Be("idp-1");
        captured.TargetIdentityProviderId.Should().BeEmpty();
        _ = _partyService.Received(1).UpdatePartyIdentityProviderIdAsync(
            Arg.Any<UpdatePartyIdentityProviderIdRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCommandStatus_sends_the_filter_and_maps_each_status()
    {
        GetCommandStatusRequest? captured = null;
        var started = Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
        _commandInspectionService
            .GetCommandStatusAsync(Arg.Do<GetCommandStatusRequest>(r => captured = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(new GetCommandStatusResponse
            {
                CommandStatus =
                {
                    new Com.Daml.Ledger.Api.V2.Admin.CommandStatus
                    {
                        Started = started,
                        State = Com.Daml.Ledger.Api.V2.Admin.CommandState.Pending,
                        Completion = new Com.Daml.Ledger.Api.V2.Completion { CommandId = "cmd-1" },
                        SynchronizerId = "sync::1220",
                    },
                },
            }));

        var result = await CreateClient().GetCommandStatusAsync(
            "cmd-", CommandState.Pending, 5, TestContext.Current.CancellationToken);

        captured!.CommandIdPrefix.Should().Be("cmd-");
        captured.State.Should().Be(Com.Daml.Ledger.Api.V2.Admin.CommandState.Pending);
        captured.Limit.Should().Be(5u);
        result.Should().ContainSingle().Which.Should().Be(new CommandStatus(
            "cmd-1", CommandState.Pending, new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero), null, "sync::1220"));
    }

    [Fact]
    public async Task GetCommandStatus_rejects_a_negative_limit()
    {
        var act = () => CreateClient().GetCommandStatusAsync(limit: -1, cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ArgumentOutOfRangeException>()).WithParameterName("limit");
    }
}
