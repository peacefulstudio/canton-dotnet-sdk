// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Telemetry;
using Canton.Ledger.Kernel.Wire;
using Com.Daml.Ledger.Api.V2;
using Com.Daml.Ledger.Api.V2.Admin;
using Com.Daml.Ledger.Api.V2.Testing;
using Daml.Runtime.Data;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using CommandState = Canton.Ledger.Abstractions.CommandState;
using CommandStatus = Canton.Ledger.Abstractions.CommandStatus;
using HashFunction = Canton.Ledger.Abstractions.HashFunction;
using IdentityProviderConfig = Canton.Ledger.Abstractions.IdentityProviderConfig;
using PackageDetails = Canton.Ledger.Abstractions.PackageDetails;
using PackageStatus = Canton.Ledger.Abstractions.PackageStatus;
using PartyDetails = Canton.Ledger.Abstractions.PartyDetails;
using VettedPackage = Canton.Ledger.Abstractions.VettedPackage;
using VettedPackagesChange = Canton.Ledger.Abstractions.VettedPackagesChange;
using WireIdentityProviderConfig = Com.Daml.Ledger.Api.V2.Admin.IdentityProviderConfig;
using WireVettedPackages = Com.Daml.Ledger.Api.V2.VettedPackages;
using WireVettedPackagesChange = Com.Daml.Ledger.Api.V2.Admin.VettedPackagesChange;
using WireHashFunction = Com.Daml.Ledger.Api.V2.HashFunction;
using WirePackageStatus = Com.Daml.Ledger.Api.V2.PackageStatus;

namespace Canton.Ledger.Grpc.Client;

/// <summary>
/// Implementation of the Canton participant admin client using gRPC.
/// </summary>
internal sealed partial class AdminClient : IAdminClient
{
    internal const int MaxPagesPerPaginatedCall = 10_000;

    private const int PageSize = 100;

    private static readonly ActivitySource ActivitySource = LedgerActivitySource.Create<AdminClient>();

    private readonly GrpcChannel _channel;
    private readonly PartyManagementService.PartyManagementServiceClient _partyService;
    private readonly UserManagementService.UserManagementServiceClient _userService;
    private readonly PackageManagementService.PackageManagementServiceClient _packageManagementService;
    private readonly PackageService.PackageServiceClient _packageService;
    private readonly CommandInspectionService.CommandInspectionServiceClient _commandInspectionService;
    private readonly IdentityProviderConfigService.IdentityProviderConfigServiceClient _identityProviderConfigService;
    private readonly ParticipantPruningService.ParticipantPruningServiceClient _pruningService;
    private readonly TimeService.TimeServiceClient _timeService;
    private readonly LedgerClientOptions _options;
    private readonly ITokenProvider? _tokenProvider;
    private readonly LedgerCallInvoker _invoker;
    private readonly ILogger<AdminClient> _logger;

    internal AdminClient(
        IOptions<LedgerClientOptions> options,
        LedgerChannelProvider channels,
        ITokenProvider tokenProvider,
        ILogger<AdminClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(tokenProvider);

        _options = options.Value;
        _tokenProvider = tokenProvider;
        _logger = logger ?? NullLogger<AdminClient>.Instance;
        _invoker = new LedgerCallInvoker(_options, _tokenProvider);

        _channel = channels.Channel;

        _partyService = new PartyManagementService.PartyManagementServiceClient(_channel);
        _userService = new UserManagementService.UserManagementServiceClient(_channel);
        _packageManagementService = new PackageManagementService.PackageManagementServiceClient(_channel);
        _packageService = new PackageService.PackageServiceClient(_channel);
        _commandInspectionService = new CommandInspectionService.CommandInspectionServiceClient(_channel);
        _identityProviderConfigService = new IdentityProviderConfigService.IdentityProviderConfigServiceClient(_channel);
        _pruningService = new ParticipantPruningService.ParticipantPruningServiceClient(_channel);
        _timeService = new TimeService.TimeServiceClient(_channel);

        CallContextHelper.LogStartupDiagnostics(
            _logger, _tokenProvider, _options.GrpcAddress, nameof(AdminClient), "AddAdminClient");
    }

    internal AdminClient(
        LedgerClientOptions options,
        GrpcChannel channel,
        PartyManagementService.PartyManagementServiceClient partyService,
        UserManagementService.UserManagementServiceClient userService,
        ITokenProvider? tokenProvider = null,
        PackageManagementService.PackageManagementServiceClient? packageManagementService = null,
        PackageService.PackageServiceClient? packageService = null,
        ILogger<AdminClient>? logger = null,
        CommandInspectionService.CommandInspectionServiceClient? commandInspectionService = null,
        IdentityProviderConfigService.IdentityProviderConfigServiceClient? identityProviderConfigService = null,
        ParticipantPruningService.ParticipantPruningServiceClient? pruningService = null,
        TimeService.TimeServiceClient? timeService = null)
    {
        _options = options;
        _channel = channel;
        _partyService = partyService;
        _userService = userService;
        _packageManagementService = packageManagementService ?? new PackageManagementService.PackageManagementServiceClient(channel);
        _packageService = packageService ?? new PackageService.PackageServiceClient(channel);
        _commandInspectionService = commandInspectionService ?? new CommandInspectionService.CommandInspectionServiceClient(channel);
        _identityProviderConfigService = identityProviderConfigService ?? new IdentityProviderConfigService.IdentityProviderConfigServiceClient(channel);
        _pruningService = pruningService ?? new ParticipantPruningService.ParticipantPruningServiceClient(channel);
        _timeService = timeService ?? new TimeService.TimeServiceClient(channel);
        _tokenProvider = tokenProvider;
        _logger = logger ?? NullLogger<AdminClient>.Instance;
        _invoker = new LedgerCallInvoker(options, tokenProvider);
    }

    /// <inheritdoc />
    public Task<string> GetParticipantIdAsync(CancellationToken cancellationToken = default) =>
        SurfaceLedgerErrorsAsync(_invoker.InvokeTracedAsync<AdminClient, GetParticipantIdResponse, string>(
            ActivitySource,
            PartyManagementService.Descriptor,
            "GetParticipantId",
            (headers, deadline, token) => _partyService.GetParticipantIdAsync(new GetParticipantIdRequest(), headers, deadline, token),
            response => response.ParticipantId,
            cancellationToken));

    /// <inheritdoc />
    public Task<PartyDetails> AllocatePartyAsync(
        string partyIdHint,
        SynchronizerId? synchronizerId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(partyIdHint);

        return SurfaceLedgerErrorsAsync(AllocatePartyCoreAsync(partyIdHint, synchronizerId, cancellationToken));
    }

    private async Task<PartyDetails> AllocatePartyCoreAsync(
        string partyIdHint,
        SynchronizerId? synchronizerId,
        CancellationToken cancellationToken)
    {
        LogAllocatingParty(_logger, partyIdHint);

        var request = new AllocatePartyRequest { PartyIdHint = partyIdHint };
        if (synchronizerId is { } synchronizer)
            request.SynchronizerId = synchronizer.Value;

        var details = await _invoker.InvokeTracedAsync<AdminClient, AllocatePartyResponse, PartyDetails>(
            ActivitySource,
            PartyManagementService.Descriptor,
            "AllocateParty",
            (headers, deadline, token) => _partyService.AllocatePartyAsync(request, headers, deadline, token),
            response => FromProtoPartyDetails(response.PartyDetails),
            cancellationToken,
            configureActivity: activity => activity.SetPartyOrContractTag(_options, LedgerActivityTagNames.CantonPartyIdHint, partyIdHint)).ConfigureAwait(false);

        LogPartyAllocated(_logger, details.Party.Value);
        return details;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Allocating party with hint: {PartyIdHint}")]
    private static partial void LogAllocatingParty(ILogger logger, string partyIdHint);

    [LoggerMessage(Level = LogLevel.Information, Message = "Party allocated: {PartyId}")]
    private static partial void LogPartyAllocated(ILogger logger, string partyId);

    /// <inheritdoc />
    public Task<IReadOnlyList<PartyDetails>> GetPartiesAsync(
        IEnumerable<Party> parties,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parties);

        var request = new GetPartiesRequest();
        request.Parties.AddRange(parties.Select(party => party.Value));

        return SurfaceLedgerErrorsAsync(_invoker.InvokeTracedAsync<AdminClient, GetPartiesResponse, IReadOnlyList<PartyDetails>>(
            ActivitySource,
            PartyManagementService.Descriptor,
            "GetParties",
            (headers, deadline, token) => _partyService.GetPartiesAsync(request, headers, deadline, token),
            response => response.PartyDetails.Select(FromProtoPartyDetails).ToList(),
            cancellationToken));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PartyDetails>> ListKnownPartiesAsync(
        CancellationToken cancellationToken = default)
    {
        var request = new ListKnownPartiesRequest { PageSize = PageSize };

        return SurfaceLedgerErrorsAsync(_invoker.ExecuteTracedAsync<AdminClient, IReadOnlyList<PartyDetails>>(
            ActivitySource,
            PartyManagementService.Descriptor,
            "ListKnownParties",
            (activity, token) => FetchAllPagesAsync(
                activity,
                "ListKnownParties",
                async pageToken =>
                {
                    request.PageToken = pageToken;
                    return await _invoker.InvokeAsync(
                        (headers, deadline, callToken) => _partyService.ListKnownPartiesAsync(request, headers, deadline, callToken),
                        token).ConfigureAwait(false);
                },
                response => response.NextPageToken,
                response => response.PartyDetails.Select(FromProtoPartyDetails)),
            cancellationToken));
    }

    /// <inheritdoc />
    public Task<UserDetails> CreateUserAsync(
        string userId,
        Party? primaryParty,
        IEnumerable<UserRight>? rights = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        return SurfaceLedgerErrorsAsync(CreateUserCoreAsync(userId, primaryParty, rights, cancellationToken));
    }

    private async Task<UserDetails> CreateUserCoreAsync(
        string userId,
        Party? primaryParty,
        IEnumerable<UserRight>? rights,
        CancellationToken cancellationToken)
    {
        LogCreatingUser(_logger, userId);

        var user = new User { Id = userId, PrimaryParty = primaryParty?.Value ?? string.Empty };
        var request = new CreateUserRequest { User = user };
        if (rights != null)
            request.Rights.AddRange(rights.Select(ToProtoRight));

        var details = await _invoker.InvokeTracedAsync<AdminClient, CreateUserResponse, UserDetails>(
            ActivitySource,
            UserManagementService.Descriptor,
            "CreateUser",
            (headers, deadline, token) => _userService.CreateUserAsync(request, headers, deadline, token),
            response => FromProtoUser(response.User),
            cancellationToken,
            configureActivity: activity => activity?.SetTag(LedgerActivityTagNames.CantonUserId, userId)).ConfigureAwait(false);

        LogUserCreated(_logger, userId);
        return details;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Creating user: {UserId}")]
    private static partial void LogCreatingUser(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User created: {UserId}")]
    private static partial void LogUserCreated(ILogger logger, string userId);

    /// <inheritdoc />
    public Task<UserDetails?> GetUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);

        return SurfaceLedgerErrorsAsync(GetUserCoreAsync(userId, cancellationToken));
    }

    private async Task<UserDetails?> GetUserCoreAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            return await _invoker.InvokeTracedAsync<AdminClient, GetUserResponse, UserDetails?>(
                ActivitySource,
                UserManagementService.Descriptor,
                "GetUser",
                (headers, deadline, token) => _userService.GetUserAsync(new GetUserRequest { UserId = userId }, headers, deadline, token),
                response => FromProtoUser(response.User),
                cancellationToken,
                isExpectedFailure: IsNotFound).ConfigureAwait(false);
        }
        catch (RpcException ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task GrantUserRightsAsync(
        string userId,
        IEnumerable<UserRight> rights,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(rights);

        return SurfaceLedgerErrorsAsync(GrantUserRightsCoreAsync(userId, rights, cancellationToken));
    }

    private async Task GrantUserRightsCoreAsync(
        string userId,
        IEnumerable<UserRight> rights,
        CancellationToken cancellationToken)
    {
        var request = new GrantUserRightsRequest { UserId = userId };
        request.Rights.AddRange(rights.Select(ToProtoRight));

        await _invoker.InvokeTracedAsync<AdminClient, GrantUserRightsResponse>(
            ActivitySource,
            UserManagementService.Descriptor,
            "GrantUserRights",
            (headers, deadline, token) => _userService.GrantUserRightsAsync(request, headers, deadline, token),
            cancellationToken,
            configureActivity: activity => activity?.SetTag(LedgerActivityTagNames.CantonUserId, userId)).ConfigureAwait(false);

        LogRightsGranted(_logger, userId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rights granted to user {UserId}")]
    private static partial void LogRightsGranted(ILogger logger, string userId);

    /// <inheritdoc />
    public Task RevokeUserRightsAsync(
        string userId,
        IEnumerable<UserRight> rights,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(rights);

        return SurfaceLedgerErrorsAsync(RevokeUserRightsCoreAsync(userId, rights, cancellationToken));
    }

    private async Task RevokeUserRightsCoreAsync(
        string userId,
        IEnumerable<UserRight> rights,
        CancellationToken cancellationToken)
    {
        var request = new RevokeUserRightsRequest { UserId = userId };
        request.Rights.AddRange(rights.Select(ToProtoRight));

        await _invoker.InvokeTracedAsync<AdminClient, RevokeUserRightsResponse>(
            ActivitySource,
            UserManagementService.Descriptor,
            "RevokeUserRights",
            (headers, deadline, token) => _userService.RevokeUserRightsAsync(request, headers, deadline, token),
            cancellationToken,
            configureActivity: activity => activity?.SetTag(LedgerActivityTagNames.CantonUserId, userId)).ConfigureAwait(false);

        LogRightsRevoked(_logger, userId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rights revoked from user {UserId}")]
    private static partial void LogRightsRevoked(ILogger logger, string userId);

    /// <inheritdoc />
    public Task<IReadOnlyList<UserRight>?> ListUserRightsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);

        return SurfaceLedgerErrorsAsync(ListUserRightsCoreAsync(userId, cancellationToken));
    }

    private async Task<IReadOnlyList<UserRight>?> ListUserRightsCoreAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _invoker.InvokeTracedAsync<AdminClient, ListUserRightsResponse, IReadOnlyList<UserRight>?>(
                ActivitySource,
                UserManagementService.Descriptor,
                "ListUserRights",
                (headers, deadline, token) => _userService.ListUserRightsAsync(new ListUserRightsRequest { UserId = userId }, headers, deadline, token),
                response => response.Rights.Select(FromProtoRight).ToList(),
                cancellationToken,
                configureActivity: activity => activity?.SetTag(LedgerActivityTagNames.CantonUserId, userId),
                isExpectedFailure: IsNotFound).ConfigureAwait(false);
        }
        catch (RpcException ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UserDetails>> ListUsersAsync(
        CancellationToken cancellationToken = default)
    {
        var request = new ListUsersRequest { PageSize = PageSize };

        return SurfaceLedgerErrorsAsync(_invoker.ExecuteTracedAsync<AdminClient, IReadOnlyList<UserDetails>>(
            ActivitySource,
            UserManagementService.Descriptor,
            "ListUsers",
            (activity, token) => FetchAllPagesAsync(
                activity,
                "ListUsers",
                async pageToken =>
                {
                    request.PageToken = pageToken;
                    return await _invoker.InvokeAsync(
                        (headers, deadline, callToken) => _userService.ListUsersAsync(request, headers, deadline, callToken),
                        token).ConfigureAwait(false);
                },
                response => response.NextPageToken,
                response => response.Users.Select(FromProtoUser)),
            cancellationToken));
    }

    /// <inheritdoc />
    public Task<UserDetails> UpdateUserAsync(
        string userId,
        UserUpdate update,
        string? identityProviderId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(update);

        var request = new UpdateUserRequest
        {
            User = new User
            {
                Id = userId,
                PrimaryParty = update.PrimaryParty?.Value ?? string.Empty,
                IsDeactivated = update.IsDeactivated ?? false,
                PrimaryPartyAuthentication = update.PrimaryPartyAuthentication ?? false,
                IdentityProviderId = identityProviderId ?? string.Empty,
                Metadata = ToProtoMetadata(update.Annotations),
            },
            UpdateMask = new FieldMask { Paths = { update.UpdatePaths() } },
        };

        return SurfaceLedgerErrorsAsync(_invoker.InvokeTracedAsync<AdminClient, UpdateUserResponse, UserDetails>(
            ActivitySource,
            UserManagementService.Descriptor,
            "UpdateUser",
            (headers, deadline, token) => _userService.UpdateUserAsync(request, headers, deadline, token),
            response => FromProtoUser(response.User),
            cancellationToken,
            configureActivity: activity => activity?.SetTag(LedgerActivityTagNames.CantonUserId, userId),
            replayable: false));
    }

    /// <inheritdoc />
    public Task DeleteUserAsync(
        string userId,
        string? identityProviderId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var request = new DeleteUserRequest { UserId = userId, IdentityProviderId = identityProviderId ?? string.Empty };
        return SurfaceLedgerErrorsAsync(_invoker.InvokeTracedAsync<AdminClient, DeleteUserResponse>(
            ActivitySource,
            UserManagementService.Descriptor,
            "DeleteUser",
            (headers, deadline, token) => _userService.DeleteUserAsync(request, headers, deadline, token),
            cancellationToken,
            configureActivity: activity => activity?.SetTag(LedgerActivityTagNames.CantonUserId, userId),
            replayable: false));
    }

    /// <inheritdoc />
    public Task UpdateUserIdentityProviderIdAsync(
        string userId,
        string? sourceIdentityProviderId,
        string? targetIdentityProviderId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var request = new UpdateUserIdentityProviderIdRequest
        {
            UserId = userId,
            SourceIdentityProviderId = sourceIdentityProviderId ?? string.Empty,
            TargetIdentityProviderId = targetIdentityProviderId ?? string.Empty,
        };
        return SurfaceLedgerErrorsAsync(_invoker.InvokeTracedAsync<AdminClient, UpdateUserIdentityProviderIdResponse>(
            ActivitySource,
            UserManagementService.Descriptor,
            "UpdateUserIdentityProviderId",
            (headers, deadline, token) => _userService.UpdateUserIdentityProviderIdAsync(request, headers, deadline, token),
            cancellationToken,
            configureActivity: activity => activity?.SetTag(LedgerActivityTagNames.CantonUserId, userId),
            replayable: false));
    }

    /// <inheritdoc />
    public Task<PartyDetails> UpdatePartyDetailsAsync(
        Party party,
        PartyUpdate update,
        string? identityProviderId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var request = new UpdatePartyDetailsRequest
        {
            PartyDetails = new Com.Daml.Ledger.Api.V2.Admin.PartyDetails
            {
                Party = party.Value,
                IdentityProviderId = identityProviderId ?? string.Empty,
                LocalMetadata = ToProtoMetadata(update.Annotations),
            },
            UpdateMask = new FieldMask { Paths = { update.UpdatePaths() } },
        };

        return SurfaceLedgerErrorsAsync(_invoker.InvokeTracedAsync<AdminClient, UpdatePartyDetailsResponse, PartyDetails>(
            ActivitySource,
            PartyManagementService.Descriptor,
            "UpdatePartyDetails",
            (headers, deadline, token) => _partyService.UpdatePartyDetailsAsync(request, headers, deadline, token),
            response => FromProtoPartyDetails(response.PartyDetails),
            cancellationToken,
            replayable: false));
    }

    /// <inheritdoc />
    public Task UpdatePartyIdentityProviderIdAsync(
        Party party,
        string? sourceIdentityProviderId,
        string? targetIdentityProviderId,
        CancellationToken cancellationToken = default)
    {
        var request = new UpdatePartyIdentityProviderIdRequest
        {
            Party = party.Value,
            SourceIdentityProviderId = sourceIdentityProviderId ?? string.Empty,
            TargetIdentityProviderId = targetIdentityProviderId ?? string.Empty,
        };
        return SurfaceLedgerErrorsAsync(_invoker.InvokeTracedAsync<AdminClient, UpdatePartyIdentityProviderIdResponse>(
            ActivitySource,
            PartyManagementService.Descriptor,
            "UpdatePartyIdentityProviderId",
            (headers, deadline, token) => _partyService.UpdatePartyIdentityProviderIdAsync(request, headers, deadline, token),
            cancellationToken,
            replayable: false));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CommandStatus>> GetCommandStatusAsync(
        string commandIdPrefix = "",
        CommandState state = CommandState.Unspecified,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandIdPrefix);
        ArgumentOutOfRangeException.ThrowIfNegative(limit ?? 0, nameof(limit));

        var request = new GetCommandStatusRequest
        {
            CommandIdPrefix = commandIdPrefix,
            State = ToProtoCommandState(state),
            Limit = (uint)(limit ?? 0),
        };
        return SurfaceLedgerErrorsAsync(_invoker.InvokeTracedAsync<AdminClient, GetCommandStatusResponse, IReadOnlyList<CommandStatus>>(
            ActivitySource,
            CommandInspectionService.Descriptor,
            "GetCommandStatus",
            (headers, deadline, token) => _commandInspectionService.GetCommandStatusAsync(request, headers, deadline, token),
            response => response.CommandStatus.Select(FromProtoCommandStatus).ToList(),
            cancellationToken));
    }

    private static ObjectMeta? ToProtoMetadata(IReadOnlyDictionary<string, string>? annotations)
    {
        if (annotations is null)
            return null;

        var metadata = new ObjectMeta();
        foreach (var (key, value) in annotations)
            metadata.Annotations[key] = value;
        return metadata;
    }

    private static Com.Daml.Ledger.Api.V2.Admin.CommandState ToProtoCommandState(CommandState state) => state switch
    {
        CommandState.Unspecified => Com.Daml.Ledger.Api.V2.Admin.CommandState.Unspecified,
        CommandState.Pending => Com.Daml.Ledger.Api.V2.Admin.CommandState.Pending,
        CommandState.Succeeded => Com.Daml.Ledger.Api.V2.Admin.CommandState.Succeeded,
        CommandState.Failed => Com.Daml.Ledger.Api.V2.Admin.CommandState.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown command state."),
    };

    private static CommandState FromProtoCommandState(Com.Daml.Ledger.Api.V2.Admin.CommandState state) => state switch
    {
        Com.Daml.Ledger.Api.V2.Admin.CommandState.Pending => CommandState.Pending,
        Com.Daml.Ledger.Api.V2.Admin.CommandState.Succeeded => CommandState.Succeeded,
        Com.Daml.Ledger.Api.V2.Admin.CommandState.Failed => CommandState.Failed,
        _ => CommandState.Unspecified,
    };

    private static CommandStatus FromProtoCommandStatus(Com.Daml.Ledger.Api.V2.Admin.CommandStatus status) =>
        new(
            status.Completion?.CommandId ?? string.Empty,
            FromProtoCommandState(status.State),
            status.Started?.ToDateTimeOffset(),
            status.Completed?.ToDateTimeOffset(),
            status.SynchronizerId);

    /// <inheritdoc />
    public Task<IReadOnlyList<PackageDetails>> ListKnownPackagesAsync(
        CancellationToken cancellationToken = default) =>
        SurfaceLedgerErrorsAsync(_invoker.InvokeTracedAsync<AdminClient, ListKnownPackagesResponse, IReadOnlyList<PackageDetails>>(
            ActivitySource,
            PackageManagementService.Descriptor,
            "ListKnownPackages",
            (headers, deadline, token) => _packageManagementService.ListKnownPackagesAsync(new ListKnownPackagesRequest(), headers, deadline, token),
            response => response.PackageDetails
                .Select(p => new PackageDetails(
                    p.PackageId,
                    p.Name,
                    p.Version,
                    p.PackageSize <= long.MaxValue
                        ? (long)p.PackageSize
                        : throw new InvalidOperationException(
                            $"Package '{p.PackageId}' reports a size of {p.PackageSize} bytes, which exceeds the supported maximum of {long.MaxValue}."),
                    (p.KnownSince ?? throw new InvalidOperationException(
                        $"Package '{p.PackageId}' is missing the required known_since timestamp.")).ToDateTimeOffset()))
                .ToList(),
            cancellationToken));

    /// <inheritdoc />
    public Task<IdentityProviderConfig> CreateIdentityProviderConfigAsync(
        IdentityProviderConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var request = new CreateIdentityProviderConfigRequest { IdentityProviderConfig = ToProtoIdentityProviderConfig(config) };
        return SurfaceLedgerErrorsAsync(_invoker.InvokeTracedAsync<AdminClient, CreateIdentityProviderConfigResponse, IdentityProviderConfig>(
            ActivitySource,
            IdentityProviderConfigService.Descriptor,
            "CreateIdentityProviderConfig",
            (headers, deadline, token) => _identityProviderConfigService.CreateIdentityProviderConfigAsync(request, headers, deadline, token),
            response => FromProtoIdentityProviderConfig(response.IdentityProviderConfig),
            cancellationToken,
            replayable: false));
    }

    /// <inheritdoc />
    public Task<IdentityProviderConfig?> GetIdentityProviderConfigAsync(
        string identityProviderId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityProviderId);

        return SurfaceLedgerErrorsAsync(GetIdentityProviderConfigCoreAsync(identityProviderId, cancellationToken));
    }

    private async Task<IdentityProviderConfig?> GetIdentityProviderConfigCoreAsync(
        string identityProviderId, CancellationToken cancellationToken)
    {
        var request = new GetIdentityProviderConfigRequest { IdentityProviderId = identityProviderId };
        try
        {
            return await _invoker.InvokeTracedAsync<AdminClient, GetIdentityProviderConfigResponse, IdentityProviderConfig?>(
                ActivitySource,
                IdentityProviderConfigService.Descriptor,
                "GetIdentityProviderConfig",
                (headers, deadline, token) => _identityProviderConfigService.GetIdentityProviderConfigAsync(request, headers, deadline, token),
                response => FromProtoIdentityProviderConfig(response.IdentityProviderConfig),
                cancellationToken,
                isExpectedFailure: IsNotFound).ConfigureAwait(false);
        }
        catch (RpcException ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IdentityProviderConfig>> ListIdentityProviderConfigsAsync(
        CancellationToken cancellationToken = default) =>
        SurfaceLedgerErrorsAsync(_invoker.InvokeTracedAsync<AdminClient, ListIdentityProviderConfigsResponse, IReadOnlyList<IdentityProviderConfig>>(
            ActivitySource,
            IdentityProviderConfigService.Descriptor,
            "ListIdentityProviderConfigs",
            (headers, deadline, token) => _identityProviderConfigService.ListIdentityProviderConfigsAsync(new ListIdentityProviderConfigsRequest(), headers, deadline, token),
            response => response.IdentityProviderConfigs.Select(FromProtoIdentityProviderConfig).ToList(),
            cancellationToken));

    /// <inheritdoc />
    public Task<IdentityProviderConfig> UpdateIdentityProviderConfigAsync(
        string identityProviderId,
        IdentityProviderConfigUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityProviderId);
        ArgumentNullException.ThrowIfNull(update);

        var request = new UpdateIdentityProviderConfigRequest
        {
            IdentityProviderConfig = new WireIdentityProviderConfig
            {
                IdentityProviderId = identityProviderId,
                IsDeactivated = update.IsDeactivated ?? false,
                Issuer = update.Issuer ?? string.Empty,
                JwksUrl = update.JwksUrl ?? string.Empty,
                Audience = update.Audience ?? string.Empty,
            },
            UpdateMask = new FieldMask { Paths = { update.UpdatePaths() } },
        };
        return SurfaceLedgerErrorsAsync(_invoker.InvokeTracedAsync<AdminClient, UpdateIdentityProviderConfigResponse, IdentityProviderConfig>(
            ActivitySource,
            IdentityProviderConfigService.Descriptor,
            "UpdateIdentityProviderConfig",
            (headers, deadline, token) => _identityProviderConfigService.UpdateIdentityProviderConfigAsync(request, headers, deadline, token),
            response => FromProtoIdentityProviderConfig(response.IdentityProviderConfig),
            cancellationToken,
            replayable: false));
    }

    /// <inheritdoc />
    public Task DeleteIdentityProviderConfigAsync(
        string identityProviderId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityProviderId);

        var request = new DeleteIdentityProviderConfigRequest { IdentityProviderId = identityProviderId };
        return SurfaceLedgerErrorsAsync(_invoker.InvokeTracedAsync<AdminClient, DeleteIdentityProviderConfigResponse>(
            ActivitySource,
            IdentityProviderConfigService.Descriptor,
            "DeleteIdentityProviderConfig",
            (headers, deadline, token) => _identityProviderConfigService.DeleteIdentityProviderConfigAsync(request, headers, deadline, token),
            cancellationToken,
            replayable: false));
    }

    /// <inheritdoc />
    public Task<VettedPackagesUpdateResult> UpdateVettedPackagesAsync(
        IReadOnlyList<VettedPackagesChange> changes,
        bool dryRun = false,
        SynchronizerId? synchronizerId = null,
        ExpectedTopologySerial? expectedTopologySerial = null,
        VettingOverrides safetyOverrides = VettingOverrides.None,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var request = new UpdateVettedPackagesRequest
        {
            DryRun = dryRun,
            SynchronizerId = synchronizerId?.Value ?? string.Empty,
        };
        request.Changes.AddRange(changes.Select(ToProtoVettedPackagesChange));
        if (expectedTopologySerial is not null)
            request.ExpectedTopologySerial = ToProtoPriorTopologySerial(expectedTopologySerial);
        request.UpdateVettedPackagesForceFlags.AddRange(ToProtoForceFlags(safetyOverrides));

        return SurfaceLedgerErrorsAsync(_invoker.InvokeTracedAsync<AdminClient, UpdateVettedPackagesResponse, VettedPackagesUpdateResult>(
            ActivitySource,
            PackageManagementService.Descriptor,
            "UpdateVettedPackages",
            (headers, deadline, token) => _packageManagementService.UpdateVettedPackagesAsync(request, headers, deadline, token),
            response => new VettedPackagesUpdateResult(
                FromProtoVettedPackages(response.PastVettedPackages),
                FromProtoVettedPackages(response.NewVettedPackages)),
            cancellationToken,
            replayable: false));
    }

    /// <inheritdoc />
    public Task PruneAsync(
        long pruneUpTo,
        string? submissionId = null,
        bool pruneAllDivulgedContracts = false,
        CancellationToken cancellationToken = default)
    {
        var request = new PruneRequest
        {
            PruneUpTo = pruneUpTo,
            SubmissionId = submissionId ?? string.Empty,
            PruneAllDivulgedContracts = pruneAllDivulgedContracts,
        };
        return SurfaceLedgerErrorsAsync(_invoker.InvokeTracedAsync<AdminClient, PruneResponse>(
            ActivitySource,
            ParticipantPruningService.Descriptor,
            "Prune",
            (headers, deadline, token) => _pruningService.PruneAsync(request, headers, deadline, token),
            cancellationToken,
            replayable: false));
    }

    private static WireIdentityProviderConfig ToProtoIdentityProviderConfig(IdentityProviderConfig config) =>
        new()
        {
            IdentityProviderId = config.IdentityProviderId,
            IsDeactivated = config.IsDeactivated,
            Issuer = config.Issuer,
            JwksUrl = config.JwksUrl,
            Audience = config.Audience,
        };

    private static IdentityProviderConfig FromProtoIdentityProviderConfig(WireIdentityProviderConfig config) =>
        new(config.IdentityProviderId, config.IsDeactivated, config.Issuer, config.JwksUrl, config.Audience);

    private static VettedPackagesRef ToProtoPackageSelector(PackageSelector reference) =>
        new()
        {
            PackageId = reference.PackageId,
            PackageName = reference.PackageName,
            PackageVersion = reference.PackageVersion,
        };

    private static WireVettedPackagesChange ToProtoVettedPackagesChange(VettedPackagesChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        switch (change)
        {
            case VettedPackagesChange.Vet vet:
                var vetOperation = new WireVettedPackagesChange.Types.Vet();
                vetOperation.Packages.AddRange(vet.Packages.Select(ToProtoPackageSelector));
                if (vet.ValidFromInclusive is { } from)
                    vetOperation.NewValidFromInclusive = Timestamp.FromDateTimeOffset(from);
                if (vet.ValidUntilExclusive is { } until)
                    vetOperation.NewValidUntilExclusive = Timestamp.FromDateTimeOffset(until);
                return new WireVettedPackagesChange { Vet = vetOperation };
            case VettedPackagesChange.Unvet unvet:
                var unvetOperation = new WireVettedPackagesChange.Types.Unvet();
                unvetOperation.Packages.AddRange(unvet.Packages.Select(ToProtoPackageSelector));
                return new WireVettedPackagesChange { Unvet = unvetOperation };
            default:
                throw new NotSupportedException($"Unknown vetted packages change '{change.GetType().Name}'.");
        }
    }

    private static PriorTopologySerial ToProtoPriorTopologySerial(ExpectedTopologySerial expected) =>
        expected.Prior is { } prior
            ? new PriorTopologySerial { Prior = prior }
            : new PriorTopologySerial { NoPrior = new Empty() };

    private static IEnumerable<UpdateVettedPackagesForceFlag> ToProtoForceFlags(VettingOverrides flags)
    {
        if (flags.HasFlag(VettingOverrides.AllowVetIncompatibleUpgrades))
            yield return UpdateVettedPackagesForceFlag.AllowVetIncompatibleUpgrades;
        if (flags.HasFlag(VettingOverrides.AllowUnvettedDependencies))
            yield return UpdateVettedPackagesForceFlag.AllowUnvettedDependencies;
    }

    private static VettedPackagesSnapshot? FromProtoVettedPackages(WireVettedPackages? snapshot) =>
        snapshot is null
            ? null
            : new VettedPackagesSnapshot(
                snapshot.Packages.Select(package => new VettedPackageEntry(
                    package.PackageId,
                    package.PackageName,
                    package.PackageVersion,
                    package.ValidFromInclusive?.ToDateTimeOffset(),
                    package.ValidUntilExclusive?.ToDateTimeOffset())).ToList(),
                snapshot.ParticipantId,
                snapshot.SynchronizerId,
                snapshot.TopologySerial);

    /// <inheritdoc />
    public Task<PackageArchive> GetPackageAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        return SurfaceLedgerErrorsAsync(_invoker.InvokeTracedAsync<AdminClient, GetPackageResponse, PackageArchive>(
            ActivitySource,
            PackageService.Descriptor,
            "GetPackage",
            (headers, deadline, token) => _packageService.GetPackageAsync(new GetPackageRequest { PackageId = packageId }, headers, deadline, token),
            response => new PackageArchive(
                response.ArchivePayload.Memory,
                response.Hash,
                MapHashFunction(response.HashFunction)),
            cancellationToken,
            configureActivity: activity => activity?.SetTag(LedgerActivityTagNames.DamlPackageId, packageId)));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListPackagesAsync(CancellationToken cancellationToken = default) =>
        SurfaceLedgerErrorsAsync(_invoker.InvokeTracedAsync<AdminClient, ListPackagesResponse, IReadOnlyList<string>>(
            ActivitySource,
            PackageService.Descriptor,
            "ListPackages",
            (headers, deadline, token) => _packageService.ListPackagesAsync(new ListPackagesRequest(), headers, deadline, token),
            response => response.PackageIds.ToList(),
            cancellationToken));

    /// <inheritdoc />
    public Task<PackageStatus> GetPackageStatusAsync(string packageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        return SurfaceLedgerErrorsAsync(_invoker.InvokeTracedAsync<AdminClient, GetPackageStatusResponse, PackageStatus>(
            ActivitySource,
            PackageService.Descriptor,
            "GetPackageStatus",
            (headers, deadline, token) => _packageService.GetPackageStatusAsync(new GetPackageStatusRequest { PackageId = packageId }, headers, deadline, token),
            response => MapPackageStatus(response.PackageStatus),
            cancellationToken,
            configureActivity: activity => activity?.SetTag(LedgerActivityTagNames.DamlPackageId, packageId)));
    }

    private static PackageStatus MapPackageStatus(WirePackageStatus status) => status switch
    {
        WirePackageStatus.Unspecified => PackageStatus.Unspecified,
        WirePackageStatus.Registered => PackageStatus.Registered,
        _ => PackageStatus.Unrecognized,
    };

    /// <inheritdoc />
    public Task<DateTimeOffset> GetTimeAsync(CancellationToken cancellationToken = default) =>
        SurfaceLedgerErrorsAsync(_invoker.InvokeTracedAsync<AdminClient, GetTimeResponse, DateTimeOffset>(
            ActivitySource,
            TimeService.Descriptor,
            "GetTime",
            (headers, deadline, token) => _timeService.GetTimeAsync(new GetTimeRequest(), headers, deadline, token),
            response => response.CurrentTime.ToDateTimeOffset(),
            cancellationToken));

    /// <inheritdoc />
    public Task SetTimeAsync(
        DateTimeOffset currentTime,
        DateTimeOffset newTime,
        CancellationToken cancellationToken = default)
    {
        var request = new SetTimeRequest
        {
            CurrentTime = Timestamp.FromDateTimeOffset(currentTime),
            NewTime = Timestamp.FromDateTimeOffset(newTime),
        };

        return SurfaceLedgerErrorsAsync(_invoker.InvokeTracedAsync<AdminClient, Empty>(
            ActivitySource,
            TimeService.Descriptor,
            "SetTime",
            (headers, deadline, token) => _timeService.SetTimeAsync(request, headers, deadline, token),
            cancellationToken,
            replayable: false));
    }

    private static HashFunction MapHashFunction(WireHashFunction hashFunction) => hashFunction switch
    {
        WireHashFunction.Sha256 => HashFunction.Sha256,
        _ => HashFunction.Unrecognized,
    };

    /// <inheritdoc />
    public Task<IReadOnlyList<VettedPackage>> ListVettedPackagesAsync(
        IEnumerable<string>? packageNamePrefixes = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ListVettedPackagesRequest();

        var prefixes = packageNamePrefixes?.ToList();
        if (prefixes is { Count: > 0 })
            request.PackageMetadataFilter = new PackageMetadataFilter { PackageNamePrefixes = { prefixes } };

        return SurfaceLedgerErrorsAsync(_invoker.ExecuteTracedAsync<AdminClient, IReadOnlyList<VettedPackage>>(
            ActivitySource,
            PackageService.Descriptor,
            "ListVettedPackages",
            (activity, token) => FetchAllPagesAsync(
                activity,
                "ListVettedPackages",
                async pageToken =>
                {
                    request.PageToken = pageToken;
                    return await _invoker.InvokeAsync(
                        (headers, deadline, callToken) => _packageService.ListVettedPackagesAsync(request, headers, deadline, callToken),
                        token).ConfigureAwait(false);
                },
                response => response.NextPageToken,
                response => response.VettedPackages.SelectMany(group =>
                    group.Packages.Select(p => new VettedPackage(
                        p.PackageId,
                        p.PackageName,
                        p.PackageVersion,
                        group.ParticipantId,
                        new SynchronizerId(group.SynchronizerId))))),
            cancellationToken));
    }

    private static async Task<IReadOnlyList<TItem>> FetchAllPagesAsync<TResponse, TItem>(
        Activity? activity,
        string grpcMethodName,
        Func<string, Task<TResponse>> fetchPage,
        Func<TResponse, string> readNextPageToken,
        Func<TResponse, IEnumerable<TItem>> readItems)
    {
        var items = new List<TItem>();
        var pageToken = string.Empty;
        var seenPageTokens = new HashSet<string>(StringComparer.Ordinal);
        var fetchedPages = 0;

        do
        {
            var response = await fetchPage(pageToken).ConfigureAwait(false);
            fetchedPages++;
            var nextPageToken = readNextPageToken(response);

            if (nextPageToken.Length > 0 && !seenPageTokens.Add(nextPageToken))
            {
                var error = new InvalidOperationException(
                    $"{grpcMethodName} pagination is not progressing: the server returned the page token '{nextPageToken}' that was already used earlier in this call.");
                activity.RecordException(error);
                throw error;
            }

            items.AddRange(readItems(response));
            pageToken = nextPageToken;

            if (pageToken.Length > 0 && fetchedPages >= MaxPagesPerPaginatedCall)
            {
                var error = new InvalidOperationException(
                    $"{grpcMethodName} pagination did not complete after {MaxPagesPerPaginatedCall} pages; aborting instead of following an unbounded page-token stream.");
                activity.RecordException(error);
                throw error;
            }
        } while (pageToken.Length > 0);

        return items;
    }

    /// <inheritdoc />
    public Task UploadDarAsync(
        byte[] darFile,
        string? submissionId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrEmpty(darFile);

        return SurfaceLedgerErrorsAsync(UploadDarCoreAsync(darFile, submissionId, synchronizerId: null, cancellationToken));
    }

    /// <inheritdoc />
    public Task UploadDarAsync(
        byte[] darFile,
        SynchronizerId synchronizerId,
        string? submissionId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrEmpty(darFile);

        return SurfaceLedgerErrorsAsync(UploadDarCoreAsync(darFile, submissionId, synchronizerId, cancellationToken));
    }

    private async Task UploadDarCoreAsync(
        byte[] darFile,
        string? submissionId,
        SynchronizerId? synchronizerId,
        CancellationToken cancellationToken)
    {
        LogUploadingDar(_logger, darFile.Length);

        var request = new UploadDarFileRequest
        {
            DarFile = ByteString.CopyFrom(darFile),
            SubmissionId = submissionId ?? string.Empty
        };
        if (synchronizerId is { } synchronizer)
            request.SynchronizerId = synchronizer.Value;

        await _invoker.InvokeTracedAsync<AdminClient, UploadDarFileResponse>(
            ActivitySource,
            PackageManagementService.Descriptor,
            "UploadDarFile",
            (headers, deadline, token) => _packageManagementService.UploadDarFileAsync(request, headers, deadline, token),
            cancellationToken,
            configureActivity: activity => activity?.SetTag(LedgerActivityTagNames.CantonSubmissionId, submissionId)).ConfigureAwait(false);

        LogDarUploaded(_logger, darFile.Length);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Uploading DAR file ({DarSize} bytes)")]
    private static partial void LogUploadingDar(ILogger logger, int darSize);

    [LoggerMessage(Level = LogLevel.Information, Message = "DAR file uploaded ({DarSize} bytes)")]
    private static partial void LogDarUploaded(ILogger logger, int darSize);

    /// <inheritdoc />
    public Task ValidateDarAsync(
        byte[] darFile,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrEmpty(darFile);

        return SurfaceLedgerErrorsAsync(ValidateDarCoreAsync(darFile, synchronizerId: null, cancellationToken));
    }

    /// <inheritdoc />
    public Task ValidateDarAsync(
        byte[] darFile,
        SynchronizerId synchronizerId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrEmpty(darFile);

        return SurfaceLedgerErrorsAsync(ValidateDarCoreAsync(darFile, synchronizerId, cancellationToken));
    }

    private async Task ValidateDarCoreAsync(byte[] darFile, SynchronizerId? synchronizerId, CancellationToken cancellationToken)
    {
        var request = new ValidateDarFileRequest { DarFile = ByteString.CopyFrom(darFile) };
        if (synchronizerId is { } synchronizer)
            request.SynchronizerId = synchronizer.Value;

        await _invoker.InvokeTracedAsync<AdminClient, ValidateDarFileResponse>(
            ActivitySource,
            PackageManagementService.Descriptor,
            "ValidateDarFile",
            (headers, deadline, token) => _packageManagementService.ValidateDarFileAsync(request, headers, deadline, token),
            cancellationToken).ConfigureAwait(false);

        LogDarValidated(_logger, darFile.Length);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "DAR file validated ({DarSize} bytes)")]
    private static partial void LogDarValidated(ILogger logger, int darSize);

    private static bool IsNotFound(RpcException exception) => exception.StatusCode == StatusCode.NotFound;

    private static async Task<T> SurfaceLedgerErrorsAsync<T>(Task<T> call)
    {
        try
        {
            return await call.ConfigureAwait(false);
        }
        catch (RpcException rejection)
        {
            throw DamlErrorParser.Parse(rejection).ToException();
        }
    }

    private static async Task SurfaceLedgerErrorsAsync(Task call)
    {
        try
        {
            await call.ConfigureAwait(false);
        }
        catch (RpcException rejection)
        {
            throw DamlErrorParser.Parse(rejection).ToException();
        }
    }

    private static void ThrowIfNullOrEmpty(byte[] darFile)
    {
        ArgumentNullException.ThrowIfNull(darFile);
        if (darFile.Length == 0)
            throw new ArgumentException("DAR file must not be empty.", nameof(darFile));
    }

    internal static Right ToProtoRight(UserRight right) => right switch
    {
        UserRight.ActAs actAs => new Right { CanActAs = new Right.Types.CanActAs { Party = actAs.Party.Value } },
        UserRight.ReadAs readAs => new Right { CanReadAs = new Right.Types.CanReadAs { Party = readAs.Party.Value } },
        UserRight.ParticipantAdmin => new Right { ParticipantAdmin = new Right.Types.ParticipantAdmin() },
        UserRight.IdentityProviderAdmin => new Right { IdentityProviderAdmin = new Right.Types.IdentityProviderAdmin() },
        UserRight.ReadAsAnyParty => new Right { CanReadAsAnyParty = new Right.Types.CanReadAsAnyParty() },
        UserRight.ExecuteAs executeAs => new Right { CanExecuteAs = new Right.Types.CanExecuteAs { Party = executeAs.Party.Value } },
        UserRight.ExecuteAsAnyParty => new Right { CanExecuteAsAnyParty = new Right.Types.CanExecuteAsAnyParty() },
        _ => throw new NotSupportedException($"Unknown right type: {right.GetType().Name}")
    };

    internal static UserRight FromProtoRight(Right right) => right.KindCase switch
    {
        Right.KindOneofCase.ParticipantAdmin => new UserRight.ParticipantAdmin(),
        Right.KindOneofCase.CanActAs => new UserRight.ActAs(new Party(right.CanActAs.Party)),
        Right.KindOneofCase.CanReadAs => new UserRight.ReadAs(new Party(right.CanReadAs.Party)),
        Right.KindOneofCase.IdentityProviderAdmin => new UserRight.IdentityProviderAdmin(),
        Right.KindOneofCase.CanReadAsAnyParty => new UserRight.ReadAsAnyParty(),
        Right.KindOneofCase.CanExecuteAs => new UserRight.ExecuteAs(new Party(right.CanExecuteAs.Party)),
        Right.KindOneofCase.CanExecuteAsAnyParty => new UserRight.ExecuteAsAnyParty(),
        _ => throw new NotSupportedException($"Unknown right kind: {right.KindCase}")
    };

    internal static UserDetails FromProtoUser(User user) =>
        new(user.Id, string.IsNullOrEmpty(user.PrimaryParty) ? null : new Party(user.PrimaryParty));

    private static PartyDetails FromProtoPartyDetails(Com.Daml.Ledger.Api.V2.Admin.PartyDetails details) =>
        new(new Party(details.Party), details.IsLocal);

    /// <summary>
    /// Creates a <see cref="CallInvoker"/> bound to this client's channel for driving raw generated
    /// gRPC stubs — services or overloads the typed surface does not cover — through the client's own
    /// authentication, deadline, and retry plumbing: construct any generated stub over it, e.g.
    /// <c>new PartyManagementService.PartyManagementServiceClient(client.CreateCallInvoker())</c>,
    /// and call it without building any <see cref="CallOptions"/> by hand.
    /// </summary>
    /// <remarks>
    /// A bearer token is resolved from the configured
    /// <see cref="Canton.Ledger.Abstractions.ITokenProvider"/> on every call;
    /// <see cref="Canton.Ledger.Abstractions.ITokenProvider.None"/> sends no
    /// <c>authorization</c> header, and a caller-supplied <c>authorization</c> metadata entry wins
    /// over the resolved token. Unary calls carry the configured
    /// <see cref="LedgerClientOptions.Timeout"/> as a per-attempt deadline when the caller sets none
    /// and run through the configured <see cref="LedgerClientOptions.Retry"/> pipeline, with auth
    /// headers and deadline recomputed on each attempt; a caller-supplied deadline is kept verbatim.
    /// Streaming calls attach auth headers but carry no default deadline — a server stream may
    /// legitimately outlive any unary budget — and are never retried. Because retried unary calls
    /// only surface the winning attempt, <c>AsyncUnaryCall&lt;TResponse&gt;.ResponseHeadersAsync</c>
    /// resolves only once the response itself is available — headers from a failed attempt never
    /// leak, but callers awaiting headers ahead of the body will wait for the body. The invoker
    /// runs on the channel the container owns, so it stays valid until the container that resolved
    /// this client is disposed; dispose the container, not the invoker.
    /// </remarks>
    /// <returns>A <see cref="CallInvoker"/> that authenticated raw stubs can be constructed over.</returns>
    /// <exception cref="ObjectDisposedException">The container that resolved this client has been disposed.</exception>
    public CallInvoker CreateCallInvoker() =>
        new AuthenticatedCallInvoker(_channel.CreateCallInvoker(), _invoker, _logger);
}
