// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;

namespace Canton.Ledger.Kernel.Authentication;

/// <summary>
/// Shared token-resolution helpers so every transport applies the same
/// unauthenticated-skip and empty-token guards to an <see cref="ITokenProvider"/>.
/// </summary>
internal static class TokenProviderExtensions
{
    /// <summary>
    /// Resolves the bearer token to send on an outgoing request, applying the guards
    /// shared by every transport: <see cref="ITokenProvider.None"/> (and a <see langword="null"/>
    /// provider) resolve to <see langword="null"/> so callers skip the Authorization header,
    /// and a provider that yields a blank token fails loudly instead of sending an
    /// unauthenticated request.
    /// </summary>
    /// <param name="tokenProvider">
    /// The provider of bearer tokens; <see cref="ITokenProvider.None"/> or <see langword="null"/> disables the header.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the token acquisition.</param>
    /// <returns>The bearer token, or <see langword="null"/> when no Authorization header should be sent.</returns>
    /// <exception cref="TokenAcquisitionException">
    /// The provider raised anything but the caller's own cancellation, or returned a null, empty, or whitespace token.
    /// </exception>
    public static Task<string?> ResolveBearerTokenAsync(
        this ITokenProvider? tokenProvider,
        CancellationToken cancellationToken = default) =>
        tokenProvider.ResolveBearerTokenAsync(cancellationToken, callerCancellation: cancellationToken);

    /// <summary>
    /// Resolves the bearer token like <see cref="ResolveBearerTokenAsync(ITokenProvider?, CancellationToken)"/>
    /// for a call whose <paramref name="cancellationToken"/> also carries a deadline the caller did not
    /// raise: only a cancellation of <paramref name="callerCancellation"/> propagates as cancellation, while
    /// the deadline cutting the fetch short is a <see cref="TokenAcquisitionException"/>.
    /// </summary>
    /// <param name="tokenProvider">
    /// The provider of bearer tokens; <see cref="ITokenProvider.None"/> or <see langword="null"/> disables the header.
    /// </param>
    /// <param name="cancellationToken">A token, including any call deadline, that cancels the token acquisition.</param>
    /// <param name="callerCancellation">The caller's own token, whose cancellation is not a token failure.</param>
    /// <returns>The bearer token, or <see langword="null"/> when no Authorization header should be sent.</returns>
    /// <exception cref="TokenAcquisitionException">
    /// The provider raised anything but the caller's own cancellation, or returned a null, empty, or whitespace token.
    /// </exception>
    public static async Task<string?> ResolveBearerTokenAsync(
        this ITokenProvider? tokenProvider,
        CancellationToken cancellationToken,
        CancellationToken callerCancellation)
    {
        if (tokenProvider is null || ReferenceEquals(tokenProvider, ITokenProvider.None))
            return null;

        try
        {
            var token = await tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(token)
                ? throw new InvalidOperationException(
                    $"Token provider {tokenProvider.GetType().Name} returned an empty token.")
                : token;
        }
        catch (Exception failure) when (IsTokenFailure(failure, callerCancellation))
        {
            throw new TokenAcquisitionException(
                $"Could not acquire a bearer token from {tokenProvider.GetType().Name}: {failure.Message}",
                failure);
        }
    }

    private static bool IsTokenFailure(Exception failure, CancellationToken callerCancellation) => failure switch
    {
        TokenAcquisitionException => false,
        OperationCanceledException => !callerCancellation.IsCancellationRequested,
        _ => true,
    };
}
