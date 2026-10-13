// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;

namespace Canton.Ledger.Kernel.Authentication.TokenGeneration;

/// <summary>
/// How <see cref="ClientCredentialsProvider"/> pauses fetching after a failed token fetch. While a cooldown
/// window is open, calls fail at once with the failure that opened it instead of reaching the
/// identity provider again, so a struggling identity provider is not hammered by every call the
/// application keeps making. The first failure opens a window of <see cref="InitialDelay"/>, each
/// further consecutive failure doubles it up to <see cref="MaxDelay"/>, and a successful fetch
/// resets it. Enabled by default; set <see cref="Enabled"/> to <see langword="false"/> to fetch on
/// every call that finds no cached token. It never re-sends a ledger call or a token request: it only makes calls fail fast for the window after a failed token fetch.
/// </summary>
public sealed record TokenFailureCooldownOptions : IValidatableObject
{
    /// <summary>Whether a failed token fetch opens a cooldown window. Default: <see langword="true"/>.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Length of the window after the first failure. Must be positive. Default: 500 milliseconds.</summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Longest a window grows to. Must not be below <see cref="InitialDelay"/>. Default: 30 seconds.</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (InitialDelay <= TimeSpan.Zero)
            yield return new ValidationResult(
                "TokenFailureCooldownOptions.InitialDelay must be positive.",
                [nameof(InitialDelay)]);

        if (MaxDelay < InitialDelay)
            yield return new ValidationResult(
                "TokenFailureCooldownOptions.MaxDelay must not be below InitialDelay.",
                [nameof(MaxDelay)]);
    }
}
