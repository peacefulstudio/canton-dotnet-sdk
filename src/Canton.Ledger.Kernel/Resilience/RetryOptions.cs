// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;

namespace Canton.Ledger.Kernel.Resilience;

/// <summary>
/// Configuration for the kernel's opt-in retry pipeline. <see cref="Enabled"/>
/// defaults to <see langword="false"/>, so a transport that does not explicitly opt in
/// sees no retry behavior — <see cref="RetryPipelineFactory.Create"/> hands it a genuine
/// no-op pipeline.
/// </summary>
/// <remarks>
/// Implements <see cref="IValidatableObject"/> so a host wiring these options through
/// <c>ValidateDataAnnotations().ValidateOnStart()</c> fails fast on a misconfiguration —
/// a negative <see cref="MaxRetryAttempts"/> (which Polly would treat as effectively
/// unbounded retries) or a <see cref="Delay"/> that is negative or longer than one day (which
/// would throw deep inside the Polly pipeline build) — instead of at the first RPC.
/// </remarks>
public sealed record RetryOptions : IValidatableObject
{
    private static readonly TimeSpan MaxDelay = TimeSpan.FromDays(1);

    /// <summary>Whether the retry pipeline is active. Default: <see langword="false"/>.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Maximum number of retry attempts once <see cref="Enabled"/> is <see langword="true"/>. Zero means
    /// each call is attempted once. Default: 3.
    /// </summary>
    [Range(0, int.MaxValue, ErrorMessage = "RetryOptions.MaxRetryAttempts must be zero or greater.")]
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>
    /// Base delay between attempts once <see cref="Enabled"/> is <see langword="true"/>, at most one day.
    /// Default: 200ms.
    /// </summary>
    public TimeSpan Delay { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Delay < TimeSpan.Zero)
            yield return new ValidationResult(
                "RetryOptions.Delay must be zero or greater.",
                [nameof(Delay)]);

        if (Delay > MaxDelay)
            yield return new ValidationResult(
                "RetryOptions.Delay must be at most one day.",
                [nameof(Delay)]);
    }
}
