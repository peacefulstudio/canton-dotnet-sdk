// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Resilience;
using Com.Daml.Ledger.Api.V2.Admin;
using Daml.Runtime.Data;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using NSubstitute;
using Xunit;
using IdentityProviderConfig = Canton.Ledger.Abstractions.IdentityProviderConfig;
using VettedPackagesChange = Canton.Ledger.Abstractions.VettedPackagesChange;
using WireIdentityProviderConfig = Com.Daml.Ledger.Api.V2.Admin.IdentityProviderConfig;

namespace Canton.Ledger.Grpc.Client.Tests;

[Collection(nameof(AdminClientActivitySourceIsolation))]
public sealed class AdminClientIdpVettingPruneTests : IDisposable
{
    private readonly LedgerClientOptions _options = new() { GrpcAddress = "https://localhost:5001" };
    private readonly GrpcChannel _channel;
    private readonly IdentityProviderConfigService.IdentityProviderConfigServiceClient _idpService;
    private readonly PackageManagementService.PackageManagementServiceClient _packageManagementService;
    private readonly ParticipantPruningService.ParticipantPruningServiceClient _pruningService;

    public AdminClientIdpVettingPruneTests()
    {
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);
        var callInvoker = Substitute.For<CallInvoker>();
        _idpService = Substitute.ForPartsOf<IdentityProviderConfigService.IdentityProviderConfigServiceClient>(callInvoker);
        _packageManagementService = Substitute.ForPartsOf<PackageManagementService.PackageManagementServiceClient>(callInvoker);
        _pruningService = Substitute.ForPartsOf<ParticipantPruningService.ParticipantPruningServiceClient>(callInvoker);
    }

    public void Dispose() => _channel.Dispose();

    private AdminClient CreateClient() =>
        new(
            _options,
            _channel,
            Substitute.ForPartsOf<PartyManagementService.PartyManagementServiceClient>(Substitute.For<CallInvoker>()),
            Substitute.ForPartsOf<UserManagementService.UserManagementServiceClient>(Substitute.For<CallInvoker>()),
            new StaticTokenProvider("test-token"),
            packageManagementService: _packageManagementService,
            identityProviderConfigService: _idpService,
            pruningService: _pruningService);

    private void EnableRetry() =>
        _options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 2, Delay = TimeSpan.Zero };

    private static AsyncUnaryCall<TResponse> UnaryResponse<TResponse>(TResponse response) =>
        new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

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

    private static readonly IdentityProviderConfig Config = new("idp-1", false, "https://issuer.invalid", "https://issuer.invalid/jwks", "aud");

    private static WireIdentityProviderConfig WireConfig() =>
        new()
        {
            IdentityProviderId = "idp-1",
            IsDeactivated = false,
            Issuer = "https://issuer.invalid",
            JwksUrl = "https://issuer.invalid/jwks",
            Audience = "aud",
        };

    [Fact]
    public async Task CreateIdentityProviderConfig_sends_every_property_and_maps_the_stored_config()
    {
        CreateIdentityProviderConfigRequest? captured = null;
        _idpService
            .CreateIdentityProviderConfigAsync(Arg.Do<CreateIdentityProviderConfigRequest>(r => captured = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(new CreateIdentityProviderConfigResponse { IdentityProviderConfig = WireConfig() }));

        var result = await CreateClient().CreateIdentityProviderConfigAsync(Config, TestContext.Current.CancellationToken);

        result.Should().Be(Config);
        captured!.IdentityProviderConfig.Should().BeEquivalentTo(WireConfig());
    }

    [Fact]
    public async Task CreateIdentityProviderConfig_is_not_retried_on_Unavailable_when_Retry_is_enabled()
    {
        EnableRetry();
        _idpService
            .CreateIdentityProviderConfigAsync(Arg.Any<CreateIdentityProviderConfigRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Faulted<CreateIdentityProviderConfigResponse>(StatusCode.Unavailable));

        var act = () => CreateClient().CreateIdentityProviderConfigAsync(Config, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>();
        _ = _idpService.Received(1).CreateIdentityProviderConfigAsync(
            Arg.Any<CreateIdentityProviderConfigRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetIdentityProviderConfig_maps_the_config()
    {
        _idpService
            .GetIdentityProviderConfigAsync(Arg.Any<GetIdentityProviderConfigRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(new GetIdentityProviderConfigResponse { IdentityProviderConfig = WireConfig() }));

        var result = await CreateClient().GetIdentityProviderConfigAsync("idp-1", TestContext.Current.CancellationToken);

        result.Should().Be(Config);
    }

    [Fact]
    public async Task GetIdentityProviderConfig_returns_null_when_the_participant_reports_NotFound()
    {
        _idpService
            .GetIdentityProviderConfigAsync(Arg.Any<GetIdentityProviderConfigRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Faulted<GetIdentityProviderConfigResponse>(StatusCode.NotFound));

        var result = await CreateClient().GetIdentityProviderConfigAsync("idp-1", TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ListIdentityProviderConfigs_maps_every_config()
    {
        _idpService
            .ListIdentityProviderConfigsAsync(Arg.Any<ListIdentityProviderConfigsRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(new ListIdentityProviderConfigsResponse { IdentityProviderConfigs = { WireConfig() } }));

        var result = await CreateClient().ListIdentityProviderConfigsAsync(TestContext.Current.CancellationToken);

        result.Should().Equal(Config);
    }

    [Fact]
    public async Task UpdateIdentityProviderConfig_sends_only_the_set_properties_in_the_field_mask()
    {
        UpdateIdentityProviderConfigRequest? captured = null;
        _idpService
            .UpdateIdentityProviderConfigAsync(Arg.Do<UpdateIdentityProviderConfigRequest>(r => captured = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(new UpdateIdentityProviderConfigResponse { IdentityProviderConfig = WireConfig() }));

        var result = await CreateClient().UpdateIdentityProviderConfigAsync(
            "idp-1",
            new IdentityProviderConfigUpdate { Issuer = "https://issuer.invalid", IsDeactivated = true },
            TestContext.Current.CancellationToken);

        result.Should().Be(Config);
        captured!.UpdateMask.Paths.Should().Equal("is_deactivated", "issuer");
        captured.IdentityProviderConfig.IdentityProviderId.Should().Be("idp-1");
        captured.IdentityProviderConfig.Issuer.Should().Be("https://issuer.invalid");
        captured.IdentityProviderConfig.IsDeactivated.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateIdentityProviderConfig_rejects_an_update_that_changes_nothing()
    {
        var act = () => CreateClient().UpdateIdentityProviderConfigAsync(
            "idp-1", new IdentityProviderConfigUpdate(), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ArgumentException>()).WithParameterName("update");
    }

    [Fact]
    public async Task UpdateIdentityProviderConfig_is_not_retried_on_Unavailable_when_Retry_is_enabled()
    {
        EnableRetry();
        _idpService
            .UpdateIdentityProviderConfigAsync(Arg.Any<UpdateIdentityProviderConfigRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Faulted<UpdateIdentityProviderConfigResponse>(StatusCode.Unavailable));

        var act = () => CreateClient().UpdateIdentityProviderConfigAsync(
            "idp-1", new IdentityProviderConfigUpdate { Audience = "x" }, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>();
        _ = _idpService.Received(1).UpdateIdentityProviderConfigAsync(
            Arg.Any<UpdateIdentityProviderConfigRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteIdentityProviderConfig_sends_the_id_and_is_not_retried_when_Retry_is_enabled()
    {
        EnableRetry();
        DeleteIdentityProviderConfigRequest? captured = null;
        _idpService
            .DeleteIdentityProviderConfigAsync(Arg.Do<DeleteIdentityProviderConfigRequest>(r => captured = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Faulted<DeleteIdentityProviderConfigResponse>(StatusCode.Unavailable));

        var act = () => CreateClient().DeleteIdentityProviderConfigAsync("idp-1", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>();
        captured!.IdentityProviderId.Should().Be("idp-1");
        _ = _idpService.Received(1).DeleteIdentityProviderConfigAsync(
            Arg.Any<DeleteIdentityProviderConfigRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateVettedPackages_maps_vet_and_unvet_changes_and_the_request_options()
    {
        UpdateVettedPackagesRequest? captured = null;
        _packageManagementService
            .UpdateVettedPackagesAsync(Arg.Do<UpdateVettedPackagesRequest>(r => captured = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(new UpdateVettedPackagesResponse()));
        var from = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        await CreateClient().UpdateVettedPackagesAsync(
            [
                new VettedPackagesChange.Vet([new PackageSelector("pkg-1", "name", "1.0.0")], ValidFromInclusive: from),
                new VettedPackagesChange.Unvet([new PackageSelector("pkg-2")]),
            ],
            dryRun: true,
            synchronizerId: new SynchronizerId("sync::1220"),
            expectedTopologySerial: ExpectedTopologySerial.At(7),
            safetyOverrides: VettingOverrides.AllowUnvettedDependencies,
            cancellationToken: TestContext.Current.CancellationToken);

        captured!.DryRun.Should().BeTrue();
        captured.SynchronizerId.Should().Be("sync::1220");
        captured.ExpectedTopologySerial.Prior.Should().Be(7u);
        captured.UpdateVettedPackagesForceFlags.Should().Equal(UpdateVettedPackagesForceFlag.AllowUnvettedDependencies);
        captured.Changes[0].Vet.Packages.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new VettedPackagesRef { PackageId = "pkg-1", PackageName = "name", PackageVersion = "1.0.0" });
        captured.Changes[0].Vet.NewValidFromInclusive.Should().Be(Timestamp.FromDateTimeOffset(from));
        captured.Changes[0].Vet.NewValidUntilExclusive.Should().BeNull();
        captured.Changes[1].Unvet.Packages.Single().PackageId.Should().Be("pkg-2");
    }

    [Fact]
    public async Task UpdateVettedPackages_sends_NoPrior_when_no_serial_is_expected_to_exist()
    {
        UpdateVettedPackagesRequest? captured = null;
        _packageManagementService
            .UpdateVettedPackagesAsync(Arg.Do<UpdateVettedPackagesRequest>(r => captured = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(new UpdateVettedPackagesResponse()));

        await CreateClient().UpdateVettedPackagesAsync(
            [], expectedTopologySerial: ExpectedTopologySerial.NoPrior, cancellationToken: TestContext.Current.CancellationToken);

        captured!.ExpectedTopologySerial.SerialCase.Should().Be(Com.Daml.Ledger.Api.V2.PriorTopologySerial.SerialOneofCase.NoPrior);
    }

    [Fact]
    public async Task UpdateVettedPackages_maps_the_past_and_new_states()
    {
        _packageManagementService
            .UpdateVettedPackagesAsync(Arg.Any<UpdateVettedPackagesRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(new UpdateVettedPackagesResponse
            {
                NewVettedPackages = new Com.Daml.Ledger.Api.V2.VettedPackages
                {
                    ParticipantId = "participant::1220",
                    SynchronizerId = "sync::1220",
                    TopologySerial = 3,
                    Packages =
                    {
                        new Com.Daml.Ledger.Api.V2.VettedPackage { PackageId = "pkg-1", PackageName = "name", PackageVersion = "1.0.0" },
                    },
                },
            }));

        var result = await CreateClient().UpdateVettedPackagesAsync([], cancellationToken: TestContext.Current.CancellationToken);

        result.Past.Should().BeNull();
        result.New!.Packages.Should().Equal(new VettedPackageEntry("pkg-1", "name", "1.0.0", null, null));
        result.New.ParticipantId.Should().Be("participant::1220");
        result.New.SynchronizerId.Should().Be("sync::1220");
        result.New.TopologySerial.Should().Be(3u);
    }

    [Fact]
    public async Task UpdateVettedPackages_is_not_retried_on_Unavailable_when_Retry_is_enabled()
    {
        EnableRetry();
        _packageManagementService
            .UpdateVettedPackagesAsync(Arg.Any<UpdateVettedPackagesRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Faulted<UpdateVettedPackagesResponse>(StatusCode.Unavailable));

        var act = () => CreateClient().UpdateVettedPackagesAsync([], cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>();
        _ = _packageManagementService.Received(1).UpdateVettedPackagesAsync(
            Arg.Any<UpdateVettedPackagesRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Prune_sends_the_offset_submission_id_and_divulged_flag()
    {
        PruneRequest? captured = null;
        _pruningService
            .PruneAsync(Arg.Do<PruneRequest>(r => captured = r), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(UnaryResponse(new PruneResponse()));

        await CreateClient().PruneAsync(42, "sub-1", true, TestContext.Current.CancellationToken);

        captured!.PruneUpTo.Should().Be(42);
        captured.SubmissionId.Should().Be("sub-1");
        captured.PruneAllDivulgedContracts.Should().BeTrue();
    }

    [Fact]
    public async Task Prune_is_not_retried_on_Unavailable_when_Retry_is_enabled()
    {
        EnableRetry();
        _pruningService
            .PruneAsync(Arg.Any<PruneRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(Faulted<PruneResponse>(StatusCode.Unavailable));

        var act = () => CreateClient().PruneAsync(1, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>();
        _ = _pruningService.Received(1).PruneAsync(
            Arg.Any<PruneRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }
}
