// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Resilience;
using Com.Daml.Ledger.Api.V2.Admin;
using Daml.Runtime.Data;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

[Collection(nameof(AdminClientActivitySourceIsolation))]
public sealed class AdminClientWriteReplayTests : IDisposable
{
    private readonly LedgerClientOptions _options = new()
    {
        GrpcAddress = "https://localhost:5001",
        Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 3, Delay = TimeSpan.Zero },
    };

    private readonly GrpcChannel _channel;
    private readonly PartyManagementService.PartyManagementServiceClient _partyService;
    private readonly UserManagementService.UserManagementServiceClient _userService;
    private readonly PackageManagementService.PackageManagementServiceClient _packageManagementService;

    public AdminClientWriteReplayTests()
    {
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);
        var callInvoker = Substitute.For<CallInvoker>();
        _partyService = Substitute.ForPartsOf<PartyManagementService.PartyManagementServiceClient>(callInvoker);
        _userService = Substitute.ForPartsOf<UserManagementService.UserManagementServiceClient>(callInvoker);
        _packageManagementService = Substitute.ForPartsOf<PackageManagementService.PackageManagementServiceClient>(callInvoker);
    }

    public void Dispose() => _channel.Dispose();

    private AdminClient CreateClient() =>
        new(
            _options,
            _channel,
            _partyService,
            _userService,
            new StaticTokenProvider("test-token"),
            packageManagementService: _packageManagementService);

    private static AsyncUnaryCall<TResponse> Faulted<TResponse>(StatusCode code)
    {
        var exception = new RpcException(new Status(code, "boom"));
        return new(
            Task.FromException<TResponse>(exception),
            Task.FromResult(new Metadata()),
            () => exception.Status,
            () => new Metadata(),
            () => { });
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    public async Task AllocateParty_is_sent_once_when_Retry_is_enabled_and_the_participant_is_transiently_unavailable(StatusCode transient)
    {
        _partyService
            .AllocatePartyAsync(Arg.Any<AllocatePartyRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<AllocatePartyResponse>(transient));

        var act = () => CreateClient().AllocatePartyAsync("alice", cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>();
        _ = _partyService.Received(1).AllocatePartyAsync(
            Arg.Any<AllocatePartyRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    public async Task CreateUser_is_sent_once_when_Retry_is_enabled_and_the_participant_is_transiently_unavailable(StatusCode transient)
    {
        _userService
            .CreateUserAsync(Arg.Any<CreateUserRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<CreateUserResponse>(transient));

        var act = () => CreateClient().CreateUserAsync("alice", null, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>();
        _ = _userService.Received(1).CreateUserAsync(
            Arg.Any<CreateUserRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    public async Task GrantUserRights_is_sent_once_when_Retry_is_enabled_and_the_participant_is_transiently_unavailable(StatusCode transient)
    {
        _userService
            .GrantUserRightsAsync(Arg.Any<GrantUserRightsRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<GrantUserRightsResponse>(transient));

        var act = () => CreateClient().GrantUserRightsAsync(
            "alice", [new UserRight.ParticipantAdmin()], TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>();
        _ = _userService.Received(1).GrantUserRightsAsync(
            Arg.Any<GrantUserRightsRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    public async Task RevokeUserRights_is_sent_once_when_Retry_is_enabled_and_the_participant_is_transiently_unavailable(StatusCode transient)
    {
        _userService
            .RevokeUserRightsAsync(Arg.Any<RevokeUserRightsRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<RevokeUserRightsResponse>(transient));

        var act = () => CreateClient().RevokeUserRightsAsync(
            "alice", [new UserRight.ParticipantAdmin()], TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>();
        _ = _userService.Received(1).RevokeUserRightsAsync(
            Arg.Any<RevokeUserRightsRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    public async Task UploadDar_is_sent_once_when_Retry_is_enabled_and_the_participant_is_transiently_unavailable(StatusCode transient)
    {
        _packageManagementService
            .UploadDarFileAsync(Arg.Any<UploadDarFileRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Faulted<UploadDarFileResponse>(transient));

        var act = () => CreateClient().UploadDarAsync([1, 2, 3], cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>();
        _ = _packageManagementService.Received(1).UploadDarFileAsync(
            Arg.Any<UploadDarFileRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }
}
