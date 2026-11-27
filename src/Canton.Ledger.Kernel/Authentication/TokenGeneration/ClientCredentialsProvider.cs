// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Canton.Ledger.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Canton.Ledger.Kernel.Authentication.TokenGeneration;

/// <summary>
/// OAuth2 client-credentials token provider with TTL-based caching.
/// Thread-safe: concurrent callers share a single in-flight refresh request and observe its one
/// outcome, success or failure, and cached tokens are reused until <c>expires_in</c> minus
/// <see cref="ClientCredentialsOptions.SafetyMargin"/>, with that margin capped at half of
/// <c>expires_in</c>. After a failed fetch, calls fail fast with that failure for a cooldown window
/// instead of reaching the identity provider, as <see cref="ClientCredentialsOptions.TokenFailureCooldown"/>
/// configures. Each window is shortened by a random amount of up to half its length, so clients that
/// failed together do not all fetch again at the same moment.
/// </summary>
public sealed partial class ClientCredentialsProvider : ITokenProvider, IDisposable
{
    private const int MaxResponseBodyBytes = 64 * 1024;
    private const int MaxLoggedOAuthErrorLength = 256;
    private static readonly TimeSpan LifetimeWithoutExpiresIn = TimeSpan.FromSeconds(60);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ClientCredentialsOptions _options;
    private readonly Uri _tokenEndpoint;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClientCredentialsProvider> _logger;
    private readonly Func<double> _cooldownJitterSample;
    private readonly object _refreshGate = new();
    private bool _disposed;
    private readonly CancellationTokenSource _disposal = new();

    private Task<string>? _refreshInFlight;
    private CachedToken? _cached;
    private Task<string>? _cooldownFailure;
    private long _cooldownEndsAtTicks;
    private TimeSpan _cooldownWindow;

    /// <summary>
    /// Creates a new <see cref="ClientCredentialsProvider"/>.
    /// </summary>
    /// <remarks>
    /// When registered via <c>AddCantonAuth</c>, options validation surfaces the same
    /// misconfiguration as an <see cref="OptionsValidationException"/> before this
    /// constructor runs.
    /// </remarks>
    /// <param name="options">The client-credentials configuration.</param>
    /// <param name="httpClientFactory">Factory used to create the <c>CantonAuth</c> named client.</param>
    /// <param name="timeProvider">Time source used to track token expiry.</param>
    /// <param name="logger">Optional logger; logs are discarded when omitted.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/>, <paramref name="httpClientFactory"/>, or
    /// <paramref name="timeProvider"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The options resolve to no usable token endpoint:
    /// <see cref="ClientCredentialsOptions.TokenEndpoint"/> is set but is not an absolute
    /// http/https URI, <see cref="ClientCredentialsOptions.Domain"/> ends with the
    /// <c>/oauth/token</c> path, neither is configured, or the endpoint uses plaintext
    /// <c>http</c> without <see cref="ClientCredentialsOptions.AllowInsecureTokenEndpoint"/>.
    /// </exception>
    public ClientCredentialsProvider(
        IOptions<ClientCredentialsOptions> options,
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        ILogger<ClientCredentialsProvider>? logger = null)
        : this(options, httpClientFactory, timeProvider, Random.Shared.NextDouble, logger)
    {
    }

    internal ClientCredentialsProvider(
        IOptions<ClientCredentialsOptions> options,
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        Func<double> cooldownJitterSample,
        ILogger<ClientCredentialsProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _cooldownJitterSample = cooldownJitterSample;
        _options = options.Value;
        _tokenEndpoint = _options.TokenGenerationEndpoint;
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
        _logger = logger ?? NullLogger<ClientCredentialsProvider>.Instance;

        if (_tokenEndpoint.Scheme == Uri.UriSchemeHttp)
            LogInsecureTokenEndpoint(_logger, _tokenEndpoint);
    }

    /// <inheritdoc />
    /// <exception cref="HttpRequestException">The token endpoint returned a non-success status code or was unreachable.</exception>
    /// <exception cref="System.Text.Json.JsonException">The token endpoint returned a body that is not valid JSON.</exception>
    /// <exception cref="InvalidOperationException">The token endpoint returned a malformed response: <see langword="null"/> after deserialization, missing <c>access_token</c>, or an <c>expires_in</c> that is not positive or exceeds <see cref="int.MaxValue"/> seconds. A response with no <c>expires_in</c> is cached for 60 seconds.</exception>
    /// <exception cref="TimeoutException">The token fetch exceeded <see cref="ClientCredentialsOptions.TokenAcquisitionTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled via <paramref name="cancellationToken"/>, or the provider was disposed while the fetch was in flight.</exception>
    /// <exception cref="ObjectDisposedException">The provider was disposed before the call.</exception>
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);

        if (TryGetUnexpiredCachedToken(out var cachedToken))
            return cachedToken;

        return await JoinOrStartRefresh().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task<string> JoinOrStartRefresh()
    {
        lock (_refreshGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (TryGetUnexpiredCachedToken(out var cachedToken))
                return Task.FromResult(cachedToken);

            if (_refreshInFlight is { } inFlight)
                return inFlight;

            if (_cooldownFailure is { } failure && _timeProvider.GetUtcNow().Ticks < _cooldownEndsAtTicks)
                return failure;

            var disposalToken = _disposal.Token;
            return _refreshInFlight = Task.Run(() => RefreshAsync(disposalToken));
        }
    }

    private async Task<string> RefreshAsync(CancellationToken disposalToken)
    {
        try
        {
            var token = await RequestTokenAsync(disposalToken).ConfigureAwait(false);
            EndCooldown();
            return token;
        }
        catch (Exception ex)
        {
            LogTokenRefreshFailed(_logger, _tokenEndpoint, ex);
            if (!disposalToken.IsCancellationRequested)
                BeginOrExtendCooldown(ex);
            throw;
        }
        finally
        {
            lock (_refreshGate)
                _refreshInFlight = null;
        }
    }

    private void EndCooldown()
    {
        lock (_refreshGate)
        {
            _cooldownFailure = null;
            _cooldownWindow = TimeSpan.Zero;
        }
    }

    private void BeginOrExtendCooldown(Exception failure)
    {
        var cooldown = _options.TokenFailureCooldown;
        if (!cooldown.Enabled)
            return;

        lock (_refreshGate)
        {
            _cooldownWindow = NextCooldownWindow(cooldown, _cooldownWindow);
            _cooldownFailure = ObservedFailure(failure);
            _cooldownEndsAtTicks = (_timeProvider.GetUtcNow() + Jittered(_cooldownWindow)).Ticks;
        }
    }

    private TimeSpan Jittered(TimeSpan window) => window - window / 2 * _cooldownJitterSample();

    private static Task<string> ObservedFailure(Exception failure)
    {
        var faulted = Task.FromException<string>(failure);
        _ = faulted.Exception;
        return faulted;
    }

    private static TimeSpan NextCooldownWindow(TokenFailureCooldownOptions cooldown, TimeSpan previousWindow)
    {
        if (previousWindow == TimeSpan.Zero)
            return cooldown.InitialDelay;

        return previousWindow > cooldown.MaxDelay / 2 ? cooldown.MaxDelay : previousWindow * 2;
    }

    private bool TryGetUnexpiredCachedToken([NotNullWhen(true)] out string? cachedToken)
    {
        var cached = Volatile.Read(ref _cached);
        cachedToken = cached?.Value;
        return cached is not null && _timeProvider.GetUtcNow().Ticks < cached.RefreshAtTicks;
    }

    private sealed record CachedToken(string Value, long RefreshAtTicks);

    private async Task<string> RequestTokenAsync(CancellationToken disposalToken)
    {
        var formData = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "client_credentials"),
            new("client_id", _options.ClientId),
            new("client_secret", _options.ClientSecret)
        };

        if (_options.Audience is not null)
            formData.Add(new("audience", _options.Audience));

        var httpClient = _httpClientFactory.CreateClient("CantonAuth");
        using var content = new FormUrlEncodedContent(formData);

        using var timeoutCts = new CancellationTokenSource(_options.TokenAcquisitionTimeout, _timeProvider);
        using var fetchCts = CancellationTokenSource.CreateLinkedTokenSource(disposalToken, timeoutCts.Token);
        var fetchToken = fetchCts.Token;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _tokenEndpoint) { Content = content };
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, fetchToken)
                .ConfigureAwait(false);
            var body = await BoundedResponseBody.ReadAsync(response.Content, MaxResponseBodyBytes, fetchToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var (error, errorDescription) = OAuthError(body.Bytes);
                LogTokenAcquisitionFailed(_logger, _tokenEndpoint, (int)response.StatusCode, error, errorDescription);
                response.EnsureSuccessStatusCode();
            }

            if (body.ExceededLimit)
                throw new InvalidOperationException(
                    $"Token endpoint {_tokenEndpoint} returned a response larger than {MaxResponseBodyBytes} bytes.");

            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(body.Bytes, JsonSerializerOptions.Web)
                ?? throw new InvalidOperationException("Token endpoint returned null response.");

            if (string.IsNullOrEmpty(tokenResponse.AccessToken))
                throw new InvalidOperationException(
                    $"Token endpoint {_tokenEndpoint} returned a response with no access_token.");

            Volatile.Write(
                ref _cached,
                new CachedToken(tokenResponse.AccessToken, RefreshAtTicks(TokenLifetime(tokenResponse.ExpiresIn))));

            LogTokenAcquired(_logger, _tokenEndpoint);

            return tokenResponse.AccessToken;
        }
        catch (OperationCanceledException ex) when (!disposalToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Token acquisition from {_tokenEndpoint} timed out after {_options.TokenAcquisitionTimeout.TotalSeconds:0.###}s.",
                ex);
        }
    }

    private static (string Error, string Description) OAuthError(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return (OAuthErrorField(document.RootElement, "error"), OAuthErrorField(document.RootElement, "error_description"));
        }
        catch (JsonException)
        {
            return (string.Empty, string.Empty);
        }
    }

    private static string OAuthErrorField(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(name, out var field)
            || field.ValueKind != JsonValueKind.String)
            return string.Empty;

        var value = field.GetString()!;
        return value.Length > MaxLoggedOAuthErrorLength
            ? string.Concat(value.AsSpan(0, MaxLoggedOAuthErrorLength), "… (truncated)")
            : value;
    }

    private TimeSpan TokenLifetime(double? expiresIn)
    {
        if (expiresIn is not { } seconds)
        {
            LogExpiresInAbsent(_logger, _tokenEndpoint, LifetimeWithoutExpiresIn.TotalSeconds);
            return LifetimeWithoutExpiresIn;
        }

        if (!(seconds > 0 && seconds <= int.MaxValue))
            throw new InvalidOperationException(
                $"Token endpoint {_tokenEndpoint} returned an invalid expires_in value '{seconds}'. A positive number of seconds no greater than {int.MaxValue} is required.");

        return TimeSpan.FromSeconds(seconds);
    }

    private long RefreshAtTicks(TimeSpan lifetime)
    {
        var margin = TimeSpan.FromTicks(Math.Min(_options.SafetyMargin.Ticks, lifetime.Ticks / 2));
        return (_timeProvider.GetUtcNow() + lifetime - margin).Ticks;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Token acquired from {Endpoint}")]
    private static partial void LogTokenAcquired(ILogger logger, Uri endpoint);

    [LoggerMessage(Level = LogLevel.Error, Message = "Token acquisition failed from {Endpoint}: HTTP {StatusCode}, error '{OAuthError}', description '{OAuthErrorDescription}'")]
    private static partial void LogTokenAcquisitionFailed(ILogger logger, Uri endpoint, int statusCode, string oAuthError, string oAuthErrorDescription);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Token endpoint {Endpoint} returned no expires_in; the token is cached for {LifetimeSeconds} seconds")]
    private static partial void LogExpiresInAbsent(ILogger logger, Uri endpoint, double lifetimeSeconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Token refresh failed from {Endpoint}")]
    private static partial void LogTokenRefreshFailed(ILogger logger, Uri endpoint, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AllowInsecureTokenEndpoint is enabled: the OAuth client secret will be sent over plaintext http to {Endpoint}, where anyone on the network path can read it. Use an https token endpoint for anything beyond local development.")]
    private static partial void LogInsecureTokenEndpoint(ILogger logger, Uri endpoint);

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_refreshGate)
        {
            if (_disposed)
                return;

            Volatile.Write(ref _disposed, true);
        }

        _disposal.Cancel();
        _disposal.Dispose();
    }
}
