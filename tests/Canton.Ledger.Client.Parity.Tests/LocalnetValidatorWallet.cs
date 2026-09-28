// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// The a-validator-1 operator's Splice wallet, reached through the validator app's wallet API.
/// Splice only installs a wallet for the validator's wallet admin user, not for the
/// client-credentials service user the ledger lanes authenticate as, so every wallet call
/// authenticates as the wallet admin through LocalNet's resource-owner-password client.
/// </summary>
internal sealed class LocalnetValidatorWallet
{
    private const string ValidatorApiUrlEnv = "CANTON_LOCALNET_A_VALIDATOR_1_VALIDATOR_API_URL";
    private const string DefaultValidatorApiUrl = "http://localhost:11903";
    private const string WalletTapPath = "api/validator/v0/wallet/tap";

    private readonly Uri tokenEndpoint;

    private LocalnetValidatorWallet(Uri validatorApiBaseUri, Uri tokenEndpoint)
    {
        TapUri = new Uri(validatorApiBaseUri, WalletTapPath);
        this.tokenEndpoint = tokenEndpoint;
    }

    /// <summary>
    /// The form fields of the OAuth2 password grant that authenticates as the a-validator-1
    /// wallet admin, using the LocalNet Keycloak realm's development credentials.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> WalletAdminPasswordGrant { get; } =
        new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "a-validator-1-unsafe",
            ["username"] = "a-validator-1",
            ["password"] = "abc123",
        };

    /// <summary>The validator wallet API endpoint that mints DevNet Amulet to the wallet owner.</summary>
    internal Uri TapUri { get; }

    /// <summary>
    /// The a-validator-1 wallet, reached on the Splice validator admin API port LocalNet publishes
    /// unless <c>CANTON_LOCALNET_A_VALIDATOR_1_VALIDATOR_API_URL</c> overrides it, and
    /// authenticated through <paramref name="tokenEndpoint"/>, the a-validator-1 realm's
    /// token endpoint.
    /// </summary>
    internal static LocalnetValidatorWallet ForAValidator1(Uri tokenEndpoint, Func<string, string?> getEnvironmentVariable)
        => new(new Uri(getEnvironmentVariable(ValidatorApiUrlEnv) ?? DefaultValidatorApiUrl), tokenEndpoint);

    /// <summary>Mints <paramref name="amount"/> DevNet Amulet to the validator operator party.</summary>
    internal async Task TapAsync(string amount, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        var accessToken = await GetWalletAdminAccessTokenAsync(httpClient, cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Post, TapUri)
        {
            Content = JsonContent.Create(new { amount }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> GetWalletAdminAccessTokenAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(WalletAdminPasswordGrant);
        using var response = await httpClient.PostAsync(tokenEndpoint, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return body.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException($"the token endpoint {tokenEndpoint} returned a null access_token");
    }
}
