// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Net;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Telemetry;
using Canton.Ledger.Kernel.Wire;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Canton.Ledger.Rest.Client;

internal sealed partial class RestAdminClient : IAdminClient
{
    private const int MaxPagesPerPaginatedCall = 10_000;
    private const int PageSize = 100;
    private const string ParticipantIdPath = "/v2/parties/participant-id";
    private const string PartiesPath = "/v2/parties";
    private const string UsersPath = "/v2/users";
    private const string AuthenticatedUserPath = "/v2/authenticated-user";
    private const string PackagesPath = "/v2/packages";
    private const string PackageHashHeader = "Canton-Package-Hash";
    private const string VettedPackagesPath = "/v2/package-vetting/list";
    private const string DarsPath = "/v2/dars";
    private const string ValidateDarPath = "/v2/dars/validate";

    private const string ListKnownPackagesUnsupported =
        "ListKnownPackagesAsync is not available over the JSON Ledger API: it serves no route for PackageManagementService.ListKnownPackages. Use ListVettedPackagesAsync, or the gRPC IAdminClient registered by AddAdminClient.";

    private static readonly ActivitySource ActivitySource = LedgerActivitySource.Create<RestAdminClient>();

    private readonly RestCallEnvelope _calls;
    private readonly ILogger<RestAdminClient> _logger;

    internal RestAdminClient(IHttpClientFactory httpClientFactory, ILogger<RestAdminClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        _logger = logger ?? NullLogger<RestAdminClient>.Instance;
        _calls = new RestCallEnvelope(httpClientFactory, _logger);
    }

    public Task<string> GetParticipantIdAsync(CancellationToken cancellationToken = default) =>
        TracedAsync(nameof(GetParticipantIdAsync), () => _calls.SendAsync<Raw.GetParticipantIdResponse, string>(
            Read(ParticipantIdPath, "participant id"),
            response => response.ParticipantId,
            timeout: null,
            cancellationToken));

    public Task<PartyDetails> AllocatePartyAsync(
        string partyIdHint,
        SynchronizerId? synchronizerId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(partyIdHint);

        return TracedAsync(nameof(AllocatePartyAsync), () => AllocatePartyCoreAsync(partyIdHint, synchronizerId, cancellationToken));
    }

    private async Task<PartyDetails> AllocatePartyCoreAsync(
        string partyIdHint,
        SynchronizerId? synchronizerId,
        CancellationToken cancellationToken)
    {
        LogAllocatingParty(_logger, partyIdHint);

        var request = new Raw.AllocatePartyRequest { PartyIdHint = partyIdHint };
        if (synchronizerId is { } synchronizer)
            request.SynchronizerId = synchronizer.Value;

        var details = await _calls.SendAsync<Raw.AllocatePartyResponse, PartyDetails>(
            Mutate(HttpMethod.Post, PartiesPath, request, "allocated party"),
            response => FromWire(response.PartyDetails),
            timeout: null,
            cancellationToken).ConfigureAwait(false);

        LogPartyAllocated(_logger, details.Party.Value);
        return details;
    }

    public Task<IReadOnlyList<PartyDetails>> GetPartiesAsync(
        IEnumerable<Party> parties,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parties);

        var requested = parties.ToList();
        return TracedAsync(nameof(GetPartiesAsync), () => GetPartiesCoreAsync(requested, cancellationToken));
    }

    private async Task<IReadOnlyList<PartyDetails>> GetPartiesCoreAsync(
        IReadOnlyList<Party> parties,
        CancellationToken cancellationToken)
    {
        var details = new List<PartyDetails>(parties.Count);
        foreach (var party in parties)
        {
            details.AddRange(await _calls.SendAsync<Raw.GetPartiesResponse, IEnumerable<PartyDetails>>(
                Read($"{PartiesPath}/{Uri.EscapeDataString(party.Value)}", "party details"),
                response => (response.PartyDetails ?? []).Select(FromWire),
                timeout: null,
                cancellationToken).ConfigureAwait(false));
        }

        return details;
    }

    public Task<IReadOnlyList<PartyDetails>> ListKnownPartiesAsync(CancellationToken cancellationToken = default) =>
        TracedAsync(nameof(ListKnownPartiesAsync), () => FetchAllPagesAsync(
            "ListKnownParties",
            pageToken => _calls.SendAsync<Raw.ListKnownPartiesResponse, Raw.ListKnownPartiesResponse>(
                Read(PagedPath(PartiesPath, pageToken), "known parties"),
                response => response,
                timeout: null,
                cancellationToken),
            response => response.NextPageToken,
            response => (response.PartyDetails ?? []).Select(FromWire)));

    public Task<UserDetails> CreateUserAsync(
        string userId,
        Party? primaryParty,
        IEnumerable<UserRight>? rights = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var user = new Raw.User { Id = userId };
        if (primaryParty is { } party)
            user.PrimaryParty = party.Value;

        var request = new Raw.CreateUserRequest { User = user, Rights = (rights ?? []).Select(ToWire).ToList() };
        return TracedAsync(
            nameof(CreateUserAsync),
            () => CreateUserCoreAsync(request, cancellationToken),
            (LedgerActivityTagNames.CantonUserId, userId));
    }

    private async Task<UserDetails> CreateUserCoreAsync(Raw.CreateUserRequest request, CancellationToken cancellationToken)
    {
        LogCreatingUser(_logger, request.User.Id);

        var details = await _calls.SendAsync<Raw.CreateUserResponse, UserDetails>(
            Mutate(HttpMethod.Post, UsersPath, request, "created user"),
            response => FromWire(response.User),
            timeout: null,
            cancellationToken).ConfigureAwait(false);

        LogUserCreated(_logger, request.User.Id);
        return details;
    }

    public Task<UserDetails?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);

        return TracedAsync(
            nameof(GetUserAsync),
            () => NullWhenNotFoundAsync(GetUserCoreAsync(userId, cancellationToken)),
            (LedgerActivityTagNames.CantonUserId, userId));
    }

    private Task<UserDetails?> GetUserCoreAsync(string userId, CancellationToken cancellationToken) =>
        _calls.SendAsync<Raw.GetUserResponse, UserDetails?>(
            Read(userId.Length == 0 ? AuthenticatedUserPath : UserPath(userId), "user"),
            response => FromWire(response.User),
            timeout: null,
            cancellationToken);

    public Task GrantUserRightsAsync(
        string userId,
        IEnumerable<UserRight> rights,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(rights);

        var request = new Raw.GrantUserRightsRequest { UserId = userId, Rights = rights.Select(ToWire).ToList() };
        return TracedAsync(
            nameof(GrantUserRightsAsync),
            () => GrantUserRightsCoreAsync(request, cancellationToken),
            (LedgerActivityTagNames.CantonUserId, userId));
    }

    private async Task<Raw.GrantUserRightsResponse> GrantUserRightsCoreAsync(
        Raw.GrantUserRightsRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _calls.SendAsync<Raw.GrantUserRightsResponse, Raw.GrantUserRightsResponse>(
            Mutate(HttpMethod.Post, UserRightsPath(request.UserId), request, "granted rights"),
            granted => granted,
            timeout: null,
            cancellationToken).ConfigureAwait(false);

        LogRightsGranted(_logger, request.UserId);
        return response;
    }

    public Task RevokeUserRightsAsync(
        string userId,
        IEnumerable<UserRight> rights,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(rights);

        var request = new Raw.RevokeUserRightsRequest { UserId = userId, Rights = rights.Select(ToWire).ToList() };
        return TracedAsync(
            nameof(RevokeUserRightsAsync),
            () => RevokeUserRightsCoreAsync(request, cancellationToken),
            (LedgerActivityTagNames.CantonUserId, userId));
    }

    private async Task<Raw.RevokeUserRightsResponse> RevokeUserRightsCoreAsync(
        Raw.RevokeUserRightsRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _calls.SendAsync<Raw.RevokeUserRightsResponse, Raw.RevokeUserRightsResponse>(
            Mutate(HttpMethod.Patch, UserRightsPath(request.UserId), request, "revoked rights"),
            revoked => revoked,
            timeout: null,
            cancellationToken).ConfigureAwait(false);

        LogRightsRevoked(_logger, request.UserId);
        return response;
    }

    public Task<IReadOnlyList<UserRight>?> ListUserRightsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);

        return TracedAsync(
            nameof(ListUserRightsAsync),
            () => NullWhenNotFoundAsync(ListUserRightsCoreAsync(userId, cancellationToken)),
            (LedgerActivityTagNames.CantonUserId, userId));
    }

    private async Task<IReadOnlyList<UserRight>?> ListUserRightsCoreAsync(string userId, CancellationToken cancellationToken)
    {
        var resolvedUserId = userId.Length == 0
            ? (await GetUserCoreAsync(userId, cancellationToken).ConfigureAwait(false))?.UserId ?? userId
            : userId;

        return await _calls.SendAsync<Raw.ListUserRightsResponse, IReadOnlyList<UserRight>?>(
            Read(UserRightsPath(resolvedUserId), "user rights"),
            response => (response.Rights ?? []).Select(FromWire).ToList(),
            timeout: null,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<UserDetails>> ListUsersAsync(CancellationToken cancellationToken = default) =>
        TracedAsync(nameof(ListUsersAsync), () => FetchAllPagesAsync(
            "ListUsers",
            pageToken => _calls.SendAsync<Raw.ListUsersResponse, Raw.ListUsersResponse>(
                Read(PagedPath(UsersPath, pageToken), "users"),
                response => response,
                timeout: null,
                cancellationToken),
            response => response.NextPageToken,
            response => (response.Users ?? []).Select(FromWire)));

    public Task<IReadOnlyList<PackageDetails>> ListKnownPackagesAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyList<PackageDetails>>(new NotSupportedException(ListKnownPackagesUnsupported));

    public Task<PackageArchive> GetPackageAsync(string packageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        return TracedAsync(
            nameof(GetPackageAsync),
            () => _calls.SendAsync(
                new RestCall(
                    HttpMethod.Get,
                    $"{PackagesPath}/{Uri.EscapeDataString(packageId)}",
                    Body: null,
                    MissingBody("package archive"),
                    MalformedBody("package archive"),
                    Accept: RestCallEnvelope.OctetStream),
                ReadPackageArchiveAsync,
                timeout: null,
                cancellationToken),
            (LedgerActivityTagNames.DamlPackageId, packageId));
    }

    private static async Task<PackageArchive> ReadPackageArchiveAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.Headers.TryGetValues(PackageHashHeader, out var hashes) || hashes.FirstOrDefault() is not { } hash)
            throw MalformedResponse.WithDetail($"The response carries no {PackageHashHeader} header.");

        var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return new PackageArchive(payload, hash, HashFunction.Sha256);
    }

    public Task<IReadOnlyList<VettedPackage>> ListVettedPackagesAsync(
        IEnumerable<string>? packageNamePrefixes = null,
        CancellationToken cancellationToken = default)
    {
        var prefixes = packageNamePrefixes?.ToList();
        var filter = prefixes is { Count: > 0 }
            ? new Raw.PackageMetadataFilter { PackageNamePrefixes = prefixes }
            : null;

        return TracedAsync(nameof(ListVettedPackagesAsync), () => FetchAllPagesAsync(
            "ListVettedPackages",
            pageToken => _calls.SendAsync<Raw.ListVettedPackagesResponse, Raw.ListVettedPackagesResponse>(
                new RestCall(
                    HttpMethod.Post,
                    VettedPackagesPath,
                    VettedPackagesPage(filter, pageToken),
                    MissingBody("vetted packages"),
                    MalformedBody("vetted packages")),
                response => response,
                timeout: null,
                cancellationToken),
            response => response.NextPageToken,
            response => (response.VettedPackages ?? []).SelectMany(FromWire)));
    }

    private static Raw.ListVettedPackagesRequest VettedPackagesPage(Raw.PackageMetadataFilter? filter, string pageToken)
    {
        var request = new Raw.ListVettedPackagesRequest();
        if (filter is not null)
            request.PackageMetadataFilter = filter;
        if (pageToken.Length > 0)
            request.PageToken = pageToken;
        return request;
    }

    public Task UploadDarAsync(
        byte[] darFile,
        string? submissionId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrEmpty(darFile);

        return TracedAsync(nameof(UploadDarAsync), () => UploadDarCoreAsync(darFile, synchronizerId: null, cancellationToken));
    }

    public Task UploadDarAsync(
        byte[] darFile,
        SynchronizerId synchronizerId,
        string? submissionId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrEmpty(darFile);

        return TracedAsync(nameof(UploadDarAsync), () => UploadDarCoreAsync(darFile, synchronizerId, cancellationToken));
    }

    private async Task<bool> UploadDarCoreAsync(byte[] darFile, SynchronizerId? synchronizerId, CancellationToken cancellationToken)
    {
        LogUploadingDar(_logger, darFile.Length);

        await _calls.SendAsync(
            new RestCall(HttpMethod.Post, DarPath(DarsPath, synchronizerId), darFile, MissingBody("DAR upload"), MalformedBody("DAR upload"), Replayable: false),
            IgnoreBodyAsync,
            timeout: null,
            cancellationToken).ConfigureAwait(false);

        LogDarUploaded(_logger, darFile.Length);
        return true;
    }

    public Task ValidateDarAsync(
        byte[] darFile,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrEmpty(darFile);

        return TracedAsync(nameof(ValidateDarAsync), () => ValidateDarCoreAsync(darFile, synchronizerId: null, cancellationToken));
    }

    public Task ValidateDarAsync(
        byte[] darFile,
        SynchronizerId synchronizerId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrEmpty(darFile);

        return TracedAsync(nameof(ValidateDarAsync), () => ValidateDarCoreAsync(darFile, synchronizerId, cancellationToken));
    }

    private async Task<bool> ValidateDarCoreAsync(byte[] darFile, SynchronizerId? synchronizerId, CancellationToken cancellationToken)
    {
        await _calls.SendAsync(
            new RestCall(HttpMethod.Post, DarPath(ValidateDarPath, synchronizerId), darFile, MissingBody("DAR validation"), MalformedBody("DAR validation")),
            IgnoreBodyAsync,
            timeout: null,
            cancellationToken).ConfigureAwait(false);

        LogDarValidated(_logger, darFile.Length);
        return true;
    }

    private static string DarPath(string path, SynchronizerId? synchronizerId) =>
        synchronizerId is { } synchronizer
            ? $"{path}?synchronizerId={Uri.EscapeDataString(synchronizer.Value)}"
            : path;

    private static async Task<T> TracedAsync<T>(
        string operation,
        Func<Task<T>> call,
        (string Name, string Value)? tag = null)
    {
        using var activity = ActivitySource.StartActivity($"{nameof(RestAdminClient)}.{operation}", ActivityKind.Internal);
        if (tag is { } attribute)
            activity?.SetTag(attribute.Name, attribute.Value);

        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            activity.RecordException(failure);
            throw;
        }
    }

    private static async Task<IReadOnlyList<TItem>> FetchAllPagesAsync<TResponse, TItem>(
        string operation,
        Func<string, Task<TResponse>> fetchPage,
        Func<TResponse, string?> readNextPageToken,
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
            var nextPageToken = readNextPageToken(response) ?? string.Empty;

            if (nextPageToken.Length > 0 && !seenPageTokens.Add(nextPageToken))
            {
                throw new InvalidOperationException(
                    $"{operation} pagination is not progressing: the server returned the page token '{nextPageToken}' that was already used earlier in this call.");
            }

            items.AddRange(readItems(response));
            pageToken = nextPageToken;

            if (pageToken.Length > 0 && fetchedPages >= MaxPagesPerPaginatedCall)
            {
                throw new InvalidOperationException(
                    $"{operation} pagination did not complete after {MaxPagesPerPaginatedCall} pages; aborting instead of following an unbounded page-token stream.");
            }
        } while (pageToken.Length > 0);

        return items;
    }

    private static async Task<T?> NullWhenNotFoundAsync<T>(Task<T?> call)
        where T : class
    {
        try
        {
            return await call.ConfigureAwait(false);
        }
        catch (LedgerOperationException rejection) when (IsNotFound(rejection))
        {
            return null;
        }
    }

    private static bool IsNotFound(LedgerOperationException rejection) =>
        rejection.Category is DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing
        || rejection.Status is TransportStatus.Http { StatusCode: HttpStatusCode.NotFound };

    private static RestCall Read(string path, string subject) =>
        new(HttpMethod.Get, path, Body: null, MissingBody(subject), MalformedBody(subject));

    private static RestCall Mutate(HttpMethod method, string path, object body, string subject) =>
        new(method, path, body, MissingBody(subject), MalformedBody(subject), Replayable: false);

    private static string MissingBody(string subject) =>
        $"Server returned a successful response but no body was present for the {subject}.";

    private static string MalformedBody(string subject) =>
        $"Server returned a malformed {subject} response body: ";

    private static string PagedPath(string path, string pageToken) =>
        pageToken.Length == 0
            ? $"{path}?pageSize={PageSize}"
            : $"{path}?pageSize={PageSize}&pageToken={Uri.EscapeDataString(pageToken)}";

    private static string UserPath(string userId) => $"{UsersPath}/{Uri.EscapeDataString(userId)}";

    private static string UserRightsPath(string userId) => $"{UserPath(userId)}/rights";

    private static Task<bool> IgnoreBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        Task.FromResult(true);

    private static void ThrowIfNullOrEmpty(byte[] darFile)
    {
        ArgumentNullException.ThrowIfNull(darFile);
        if (darFile.Length == 0)
            throw new ArgumentException("DAR file must not be empty.", nameof(darFile));
    }

    private static PartyDetails FromWire(Raw.PartyDetails details) =>
        new(new Party(details.Party), details.IsLocal ?? false);

    private static UserDetails FromWire(Raw.User user) =>
        new(user.Id, string.IsNullOrEmpty(user.PrimaryParty) ? null : new Party(user.PrimaryParty));

    private static IEnumerable<VettedPackage> FromWire(Raw.VettedPackages group) =>
        (group.Packages ?? []).Select(package => new VettedPackage(
            package.PackageId,
            package.PackageName,
            package.PackageVersion,
            group.ParticipantId,
            new SynchronizerId(group.SynchronizerId)));

    private static UserRight FromWire(Raw.Right right) => right.Kind switch
    {
        { CanActAs: { } actAs } => new UserRight.ActAs(new Party(actAs.Party)),
        { CanReadAs: { } readAs } => new UserRight.ReadAs(new Party(readAs.Party)),
        { ParticipantAdmin: not null } => new UserRight.ParticipantAdmin(),
        { IdentityProviderAdmin: not null } => new UserRight.IdentityProviderAdmin(),
        { CanReadAsAnyParty: not null } => new UserRight.ReadAsAnyParty(),
        { CanExecuteAs: { } executeAs } => new UserRight.ExecuteAs(new Party(executeAs.Party)),
        { CanExecuteAsAnyParty: not null } => new UserRight.ExecuteAsAnyParty(),
        _ => throw new NotSupportedException("Unknown right kind served by the participant."),
    };

    private static Raw.Right ToWire(UserRight right) => new()
    {
        Kind = right switch
        {
            UserRight.ActAs actAs => new Raw.RightKind { CanActAs = new Raw.Right_CanActAs { Party = actAs.Party.Value } },
            UserRight.ReadAs readAs => new Raw.RightKind { CanReadAs = new Raw.Right_CanReadAs { Party = readAs.Party.Value } },
            UserRight.ParticipantAdmin => new Raw.RightKind { ParticipantAdmin = new Raw.Right_ParticipantAdmin() },
            UserRight.IdentityProviderAdmin => new Raw.RightKind { IdentityProviderAdmin = new Raw.Right_IdentityProviderAdmin() },
            UserRight.ReadAsAnyParty => new Raw.RightKind { CanReadAsAnyParty = new Raw.Right_CanReadAsAnyParty() },
            UserRight.ExecuteAs executeAs => new Raw.RightKind { CanExecuteAs = new Raw.Right_CanExecuteAs { Party = executeAs.Party.Value } },
            UserRight.ExecuteAsAnyParty => new Raw.RightKind { CanExecuteAsAnyParty = new Raw.Right_CanExecuteAsAnyParty() },
            _ => throw new NotSupportedException($"Unknown right type: {right.GetType().Name}"),
        },
    };

    [LoggerMessage(Level = LogLevel.Debug, Message = "Allocating party with hint: {PartyIdHint}")]
    private static partial void LogAllocatingParty(ILogger logger, string partyIdHint);

    [LoggerMessage(Level = LogLevel.Information, Message = "Party allocated: {PartyId}")]
    private static partial void LogPartyAllocated(ILogger logger, string partyId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Creating user: {UserId}")]
    private static partial void LogCreatingUser(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User created: {UserId}")]
    private static partial void LogUserCreated(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rights granted to user {UserId}")]
    private static partial void LogRightsGranted(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rights revoked from user {UserId}")]
    private static partial void LogRightsRevoked(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Uploading DAR file ({DarSize} bytes)")]
    private static partial void LogUploadingDar(ILogger logger, int darSize);

    [LoggerMessage(Level = LogLevel.Information, Message = "DAR file uploaded ({DarSize} bytes)")]
    private static partial void LogDarUploaded(ILogger logger, int darSize);

    [LoggerMessage(Level = LogLevel.Information, Message = "DAR file validated ({DarSize} bytes)")]
    private static partial void LogDarValidated(ILogger logger, int darSize);
}
