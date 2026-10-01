// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Resilience;
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
using PackageStatus = Canton.Ledger.Abstractions.PackageStatus;
using WirePackageStatus = Com.Daml.Ledger.Api.V2.PackageStatus;

namespace Canton.Ledger.Grpc.Client.Tests;

[Collection(nameof(AdminClientActivitySourceIsolation))]
public sealed class AdminClientPackageAndTimeReadTests : IDisposable
{
    private readonly LedgerClientOptions _options = new() { GrpcAddress = "https://localhost:5001" };
    private readonly GrpcChannel _channel;
    private readonly PackageService.PackageServiceClient _packageService;
    private readonly TimeService.TimeServiceClient _timeService;

    public AdminClientPackageAndTimeReadTests()
    {
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);
        var callInvoker = Substitute.For<CallInvoker>();
        _packageService = Substitute.ForPartsOf<PackageService.PackageServiceClient>(callInvoker);
        _timeService = Substitute.ForPartsOf<TimeService.TimeServiceClient>(callInvoker);
    }

    public void Dispose() => _channel.Dispose();

    private AdminClient CreateClient() =>
        new(
            _options,
            _channel,
            Substitute.ForPartsOf<PartyManagementService.PartyManagementServiceClient>(Substitute.For<CallInvoker>()),
            Substitute.ForPartsOf<UserManagementService.UserManagementServiceClient>(Substitute.For<CallInvoker>()),
            new StaticTokenProvider("test-token"),
            packageService: _packageService,
            timeService: _timeService);

    private static AsyncUnaryCall<TResponse> UnaryResponse<TResponse>(TResponse response) =>
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

    [Fact]
    public async Task ListPackages_returns_the_package_ids_the_participant_serves()
    {
        _packageService
            .ListPackagesAsync(Arg.Any<ListPackagesRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(new ListPackagesResponse { PackageIds = { "pkg-a", "pkg-b" } }));

        var result = await CreateClient().ListPackagesAsync(TestContext.Current.CancellationToken);

        result.Should().Equal("pkg-a", "pkg-b");
    }

    [Fact]
    public async Task ListPackages_throws_LedgerOperationException_carrying_the_status_of_a_rejection()
    {
        _packageService
            .ListPackagesAsync(Arg.Any<ListPackagesRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns<AsyncUnaryCall<ListPackagesResponse>>(_ =>
                throw new RpcException(new Status(StatusCode.PermissionDenied, "not allowed")));

        var act = () => CreateClient().ListPackagesAsync(TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Message.Should().Be("not allowed");
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.PermissionDenied));
    }

    [Theory]
    [InlineData(WirePackageStatus.Registered, PackageStatus.Registered)]
    [InlineData(WirePackageStatus.Unspecified, PackageStatus.Unspecified)]
    [InlineData((WirePackageStatus)42, PackageStatus.Unrecognized)]
    public async Task GetPackageStatus_maps_the_wire_status(WirePackageStatus wire, PackageStatus expected)
    {
        GetPackageStatusRequest? captured = null;
        _packageService
            .GetPackageStatusAsync(
                Arg.Do<GetPackageStatusRequest>(request => captured = request),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(new GetPackageStatusResponse { PackageStatus = wire }));

        var result = await CreateClient().GetPackageStatusAsync("pkg-id-1", TestContext.Current.CancellationToken);

        result.Should().Be(expected);
        captured!.PackageId.Should().Be("pkg-id-1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetPackageStatus_throws_ArgumentException_when_packageId_null_or_whitespace(string? packageId)
    {
        var act = () => CreateClient().GetPackageStatusAsync(packageId!, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetTime_returns_the_current_time_of_the_participant()
    {
        _timeService
            .GetTimeAsync(Arg.Any<GetTimeRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(new GetTimeResponse
            {
                CurrentTime = Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero)),
            }));

        var result = await CreateClient().GetTimeAsync(TestContext.Current.CancellationToken);

        result.Should().Be(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task GetTime_retries_a_transient_Unavailable_when_Retry_is_enabled()
    {
        _options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 2, Delay = TimeSpan.Zero };
        _timeService
            .GetTimeAsync(Arg.Any<GetTimeRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(
                Faulted<GetTimeResponse>(new RpcException(new Status(StatusCode.Unavailable, "down"))),
                UnaryResponse(new GetTimeResponse { CurrentTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UnixEpoch) }));

        var result = await CreateClient().GetTimeAsync(TestContext.Current.CancellationToken);

        result.Should().Be(DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public async Task SetTime_sends_the_current_and_the_new_time()
    {
        SetTimeRequest? captured = null;
        _timeService
            .SetTimeAsync(
                Arg.Do<SetTimeRequest>(request => captured = request),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(new Empty()));

        await CreateClient().SetTimeAsync(
            new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 30, 13, 0, 0, TimeSpan.Zero),
            TestContext.Current.CancellationToken);

        captured!.CurrentTime.ToDateTimeOffset().Should().Be(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
        captured.NewTime.ToDateTimeOffset().Should().Be(new DateTimeOffset(2026, 9, 30, 13, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task SetTime_is_sent_once_when_Retry_is_enabled_and_the_participant_is_transiently_unavailable()
    {
        _options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 3, Delay = TimeSpan.Zero };
        _timeService
            .SetTimeAsync(Arg.Any<SetTimeRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(
                Faulted<Empty>(new RpcException(new Status(StatusCode.Unavailable, "down"))),
                UnaryResponse(new Empty()));

        var act = () => CreateClient().SetTimeAsync(
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        _ = _timeService.Received(1).SetTimeAsync(
            Arg.Any<SetTimeRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetTime_throws_LedgerOperationException_carrying_the_status_of_a_rejection()
    {
        _timeService
            .SetTimeAsync(Arg.Any<SetTimeRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns<AsyncUnaryCall<Empty>>(_ =>
                throw new RpcException(new Status(StatusCode.Unimplemented, "wall-clock ledger")));

        var act = () => CreateClient().SetTimeAsync(
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unimplemented));
    }
}
